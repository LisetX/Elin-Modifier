using System;
using System.Globalization;
using HarmonyLib;

internal sealed class PlantHarvestMultiplierModule
{
    [ThreadStatic] private static int _cropHarvestDepth;
    [ThreadStatic] private static int _seedReapingDepth;

    internal bool Enabled { get; private set; }
    internal float CropHarvestMultiplier { get; private set; } = 1f;
    internal float SeedReapingMultiplier { get; private set; } = 1f;
    internal string CropHarvestMultiplierText { get; set; } = "1";
    internal string SeedReapingMultiplierText { get; set; } = "1";

    internal void Load(bool enabled, float cropHarvestMultiplier, float seedReapingMultiplier)
    {
        Enabled = enabled;
        CropHarvestMultiplier = ClampMultiplier(cropHarvestMultiplier);
        SeedReapingMultiplier = ClampMultiplier(seedReapingMultiplier);
        SyncTextFields();
    }

    internal void Reset()
    {
        Enabled = false;
        CropHarvestMultiplier = 1f;
        SeedReapingMultiplier = 1f;
        _cropHarvestDepth = 0;
        _seedReapingDepth = 0;
        SyncTextFields();
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        return true;
    }

    internal bool TryApplyMultiplierTextFields()
    {
        if (!TryParseMultiplier(CropHarvestMultiplierText, out var cropHarvestMultiplier) ||
            !TryParseMultiplier(SeedReapingMultiplierText, out var seedReapingMultiplier))
            return false;

        CropHarvestMultiplier = cropHarvestMultiplier;
        SeedReapingMultiplier = seedReapingMultiplier;
        SyncTextFields();
        return true;
    }

    internal void SyncTextFields()
    {
        CropHarvestMultiplierText =
            CropHarvestMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
        SeedReapingMultiplierText =
            SeedReapingMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal bool TryEnterCropHarvest()
    {
        if (!Enabled)
            return false;
        _cropHarvestDepth++;
        return true;
    }

    internal void ExitCropHarvest(bool entered)
    {
        if (entered && _cropHarvestDepth > 0)
            _cropHarvestDepth--;
    }

    internal bool TryEnterSeedReaping(Task? task)
    {
        if (!Enabled || task is not TaskHarvest harvest || !harvest.IsReapSeed)
            return false;
        _seedReapingDepth++;
        return true;
    }

    internal void ExitSeedReaping(bool entered)
    {
        if (entered && _seedReapingDepth > 0)
            _seedReapingDepth--;
    }

    internal bool ScaleCropHarvest(Thing? thing)
    {
        if (!Enabled || _cropHarvestDepth <= 0 || thing == null)
            return true;
        var amount = ScalePositiveValue(thing.Num, CropHarvestMultiplier);
        if (amount <= 0)
        {
            thing.Destroy();
            return false;
        }
        thing.SetNum(amount);
        return true;
    }

    internal bool ScaleReapedSeed(Thing? thing)
    {
        if (!Enabled || _seedReapingDepth <= 0 || thing?.trait is not TraitSeed)
            return true;
        var amount = ScalePositiveValue(thing.Num, SeedReapingMultiplier);
        if (amount <= 0)
        {
            thing.Destroy();
            return false;
        }
        thing.SetNum(amount);
        return true;
    }

    internal static bool TryParseMultiplier(string? text, out float value)
    {
        if (!float.TryParse(
                (text ?? "").Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) ||
            float.IsNaN(value) ||
            float.IsInfinity(value) ||
            value < 0f)
        {
            value = 1f;
            return false;
        }

        value = ClampMultiplier(value);
        return true;
    }

    internal static int ScalePositiveValue(int value, float multiplier)
    {
        if (value <= 0)
            return value;
        if (multiplier <= 0f)
            return 0;

        var scaled = Math.Round(value * (double)multiplier, MidpointRounding.AwayFromZero);
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    private static float ClampMultiplier(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 1f;
        return Math.Max(0f, Math.Min(1000000f, value));
    }
}

internal static class PlantHarvestMultiplierPatchContext
{
    internal static PlantHarvestMultiplierModule? Current =>
        ElinModifierPlugin.ActiveModules?.PlantHarvestMultiplier;
}

[HarmonyPatch(
    typeof(GrowSystem),
    "PopHarvest",
    new[] { typeof(Chara), typeof(Thing), typeof(int) })]
internal static class GrowSystemPopHarvestMultiplierPatch
{
    private static void Prefix(out bool __state)
    {
        __state = PlantHarvestMultiplierPatchContext.Current?.TryEnterCropHarvest() == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        PlantHarvestMultiplierPatchContext.Current?.ExitCropHarvest(__state);
        return __exception;
    }
}

[HarmonyPatch(
    typeof(GrowSystem),
    "TryPick",
    new[] { typeof(Cell), typeof(Thing), typeof(Chara), typeof(bool) })]
internal static class GrowSystemTryPickHarvestMultiplierPatch
{
    private static bool Prefix(Thing __1)
    {
        return PlantHarvestMultiplierPatchContext.Current?.ScaleCropHarvest(__1) ?? true;
    }
}

[HarmonyPatch(
    typeof(Map),
    "MineObj",
    new[] { typeof(Point), typeof(Task), typeof(Chara) })]
internal static class MapMineObjSeedReapingMultiplierPatch
{
    private static void Prefix(Task __1, out bool __state)
    {
        __state = PlantHarvestMultiplierPatchContext.Current?.TryEnterSeedReaping(__1) == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        PlantHarvestMultiplierPatchContext.Current?.ExitSeedReaping(__state);
        return __exception;
    }
}

[HarmonyPatch(
    typeof(Chara),
    "PickOrDrop",
    new[] { typeof(Point), typeof(Thing), typeof(bool) })]
internal static class CharaPickOrDropSeedReapingMultiplierPatch
{
    private static bool Prefix(Thing __1)
    {
        return PlantHarvestMultiplierPatchContext.Current?.ScaleReapedSeed(__1) ?? true;
    }
}
