using System;
using System.Globalization;

internal sealed class YieldMultiplierModule
{
    [ThreadStatic] private static int _gatheringSuppressDepth;
    [ThreadStatic] private static int _containerDepth;
    [ThreadStatic] private static int _containerSuppressDepth;
    [ThreadStatic] private static int _homeYieldDepth;
    [ThreadStatic] private static int _performanceDepth;

    internal bool Enabled { get; private set; }

    internal float GatheringMultiplier { get; private set; } = 1f;
    internal float ContainerMultiplier { get; private set; } = 1f;
    internal float FishingMultiplier { get; private set; } = 1f;
    internal float FurMultiplier { get; private set; } = 1f;
    internal float HomeYieldMultiplier { get; private set; } = 1f;
    internal float PerformanceRewardMultiplier { get; private set; } = 1f;

    internal string GatheringMultiplierText { get; set; } = "1";
    internal string ContainerMultiplierText { get; set; } = "1";
    internal string FishingMultiplierText { get; set; } = "1";
    internal string FurMultiplierText { get; set; } = "1";
    internal string HomeYieldMultiplierText { get; set; } = "1";
    internal string PerformanceRewardMultiplierText { get; set; } = "1";

    internal void Load(
        bool enabled,
        float gathering,
        float container,
        float fishing,
        float fur,
        float homeOutput,
        float performanceReward)
    {
        Enabled = enabled;
        GatheringMultiplier = ClampMultiplier(gathering);
        ContainerMultiplier = ClampMultiplier(container);
        FishingMultiplier = ClampMultiplier(fishing);
        FurMultiplier = ClampMultiplier(fur);
        HomeYieldMultiplier = ClampMultiplier(homeOutput);
        PerformanceRewardMultiplier = ClampMultiplier(performanceReward);
        SyncTextFields();
    }

    internal void Reset()
    {
        Enabled = false;
        GatheringMultiplier = 1f;
        ContainerMultiplier = 1f;
        FishingMultiplier = 1f;
        FurMultiplier = 1f;
        HomeYieldMultiplier = 1f;
        PerformanceRewardMultiplier = 1f;
        _gatheringSuppressDepth = 0;
        _containerDepth = 0;
        _containerSuppressDepth = 0;
        _homeYieldDepth = 0;
        _performanceDepth = 0;
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
        if (!TryParseMultiplier(GatheringMultiplierText, out var gathering) ||
            !TryParseMultiplier(ContainerMultiplierText, out var container) ||
            !TryParseMultiplier(FishingMultiplierText, out var fishing) ||
            !TryParseMultiplier(FurMultiplierText, out var fur) ||
            !TryParseMultiplier(HomeYieldMultiplierText, out var homeOutput) ||
            !TryParseMultiplier(PerformanceRewardMultiplierText, out var performanceReward))
            return false;

        GatheringMultiplier = gathering;
        ContainerMultiplier = container;
        FishingMultiplier = fishing;
        FurMultiplier = fur;
        HomeYieldMultiplier = homeOutput;
        PerformanceRewardMultiplier = performanceReward;
        SyncTextFields();
        return true;
    }

    internal void SyncTextFields()
    {
        GatheringMultiplierText = Format(GatheringMultiplier);
        ContainerMultiplierText = Format(ContainerMultiplier);
        FishingMultiplierText = Format(FishingMultiplier);
        FurMultiplierText = Format(FurMultiplier);
        HomeYieldMultiplierText = Format(HomeYieldMultiplier);
        PerformanceRewardMultiplierText = Format(PerformanceRewardMultiplier);
    }

    internal bool TryEnterGatheringSuppress()
    {
        if (!Enabled)
            return false;
        _gatheringSuppressDepth++;
        return true;
    }

    internal void ExitGatheringSuppress(bool entered)
    {
        if (entered && _gatheringSuppressDepth > 0)
            _gatheringSuppressDepth--;
    }

    internal bool TryEnterContainer()
    {
        if (!Enabled)
            return false;
        _containerDepth++;
        return true;
    }

    internal void ExitContainer(bool entered)
    {
        if (entered && _containerDepth > 0)
            _containerDepth--;
    }

    internal bool TryEnterContainerSuppress()
    {
        if (!Enabled)
            return false;
        _containerSuppressDepth++;
        return true;
    }

    internal void ExitContainerSuppress(bool entered)
    {
        if (entered && _containerSuppressDepth > 0)
            _containerSuppressDepth--;
    }

    internal bool TryEnterHomeYield()
    {
        if (!Enabled)
            return false;
        _homeYieldDepth++;
        return true;
    }

    internal void ExitHomeYield(bool entered)
    {
        if (entered && _homeYieldDepth > 0)
            _homeYieldDepth--;
    }

    internal bool TryEnterPerformance(Chara? owner, bool punish)
    {
        if (!Enabled || punish || owner == null)
            return false;
        try
        {
            if (!owner.IsPCParty)
                return false;
        }
        catch
        {
            return false;
        }
        _performanceDepth++;
        return true;
    }

    internal void ExitPerformance(bool entered)
    {
        if (entered && _performanceDepth > 0)
            _performanceDepth--;
    }

    internal bool ScaleGathering(Thing? thing)
    {
        if (!Enabled || thing == null ||
            _gatheringSuppressDepth > 0 ||
            PlantHarvestMultiplierModule.IsCropHarvestScopeActive ||
            PlantHarvestMultiplierModule.IsSeedReapingScopeActive ||
            IsNeutralMultiplier(GatheringMultiplier))
            return true;

        var amount = ScalePositiveValue(thing.Num, GatheringMultiplier);
        if (amount <= 0)
        {
            thing.Destroy();
            return false;
        }
        thing.SetNum(amount);
        return true;
    }

    internal void ScaleContainerAmount(ref int num)
    {
        if (!Enabled || num <= 0 ||
            _containerDepth <= 0 ||
            _containerSuppressDepth > 0 ||
            IsNeutralMultiplier(ContainerMultiplier))
            return;
        num = Math.Max(1, ScalePositiveValue(num, ContainerMultiplier));
    }

    internal void ScaleFishing(Thing? result)
    {
        if (!Enabled || IsNeutralMultiplier(FishingMultiplier))
            return;
        ApplyResultMultiplier(result, FishingMultiplier);
    }

    internal void ScaleFur(Thing? result)
    {
        if (!Enabled || IsNeutralMultiplier(FurMultiplier))
            return;
        ApplyResultMultiplier(result, FurMultiplier);
    }

    internal void ScaleHomeYield(Thing? result)
    {
        if (!Enabled || _homeYieldDepth <= 0 || IsNeutralMultiplier(HomeYieldMultiplier))
            return;
        ApplyResultMultiplier(result, HomeYieldMultiplier);
    }

    internal bool ScalePerformanceReward(Thing? thing)
    {
        if (!Enabled || thing == null ||
            _performanceDepth <= 0 ||
            IsNeutralMultiplier(PerformanceRewardMultiplier))
            return true;

        var amount = ScalePositiveValue(thing.Num, PerformanceRewardMultiplier);
        if (amount <= 0)
            amount = 1;
        thing.SetNum(amount);
        return true;
    }

    private static void ApplyResultMultiplier(Thing? result, float multiplier)
    {
        if (result == null)
            return;
        try
        {
            if (result.Num <= 0)
                return;
            var amount = ScalePositiveValue(result.Num, multiplier);
            result.SetNum(Math.Max(1, amount));
        }
        catch
        {
        }
    }

    private static string Format(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool IsNeutralMultiplier(float value)
    {
        return Math.Abs(value - 1f) <= 0.0001f;
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
