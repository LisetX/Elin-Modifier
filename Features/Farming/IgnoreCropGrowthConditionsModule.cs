using System;
using System.Collections.Generic;
using HarmonyLib;

internal sealed class IgnoreCropGrowthConditionsModule
{
    internal bool Enabled { get; private set; }

    internal void Load(bool enabled)
    {
        Enabled = enabled;
    }

    internal void Reset()
    {
        Enabled = false;
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        return true;
    }
}

internal static class IgnoreCropGrowthConditionsPatchContext
{
    private static readonly System.Reflection.FieldInfo? CurrentCellField =
        AccessTools.Field(typeof(GrowSystem), "cell");

    internal static IgnoreCropGrowthConditionsModule? Current =>
        ElinModifierPlugin.ActiveModules?.IgnoreCropGrowthConditions;

    internal static bool ShouldIgnoreCurrentCrop()
    {
        if (Current?.Enabled != true)
            return false;

        try
        {
            var cell = CurrentCellField?.GetValue(null) as Cell;
            var map = GameAccess.World.CurrentMap;
            return cell != null && map != null && map.TryGetPlant(cell) != null;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsEnabled => Current?.Enabled == true;

    internal sealed class FertilityState
    {
        internal readonly List<KeyValuePair<PlantData, int>> Entries =
            new List<KeyValuePair<PlantData, int>>();
    }

    internal sealed class WaterState
    {
        internal Cell Cell = null!;
        internal PlantData Plant = null!;
        internal bool WasWatered;
        internal int Water;
    }

    internal static FertilityState? BeginIgnoreFertilizer()
    {
        if (!IsEnabled)
            return null;

        try
        {
            var plants = GameAccess.World.CurrentMap?.plants;
            if (plants == null || plants.Count == 0)
                return null;

            FertilityState? state = null;
            foreach (var plant in plants.Values)
            {
                if (plant == null || plant.fert >= 0)
                    continue;

                state ??= new FertilityState();
                state.Entries.Add(new KeyValuePair<PlantData, int>(plant, plant.fert));
                plant.fert = 0;
            }

            return state;
        }
        catch
        {
            return null;
        }
    }

    internal static void EndIgnoreFertilizer(FertilityState? state)
    {
        if (state == null)
            return;

        foreach (var entry in state.Entries)
        {
            try
            {
                entry.Key.fert = entry.Value;
            }
            catch
            {
            }
        }
    }

    internal static WaterState? BeginIgnoreWater()
    {
        if (!IsEnabled)
            return null;

        try
        {
            var cell = CurrentCellField?.GetValue(null) as Cell;
            var plant = cell == null ? null : GameAccess.World.CurrentMap?.TryGetPlant(cell);
            if (cell == null || plant == null || cell.isWatered)
                return null;

            var state = new WaterState
            {
                Cell = cell,
                Plant = plant,
                WasWatered = cell.isWatered,
                Water = plant.water
            };
            cell.isWatered = true;
            return state;
        }
        catch
        {
            return null;
        }
    }

    internal static void EndIgnoreWater(WaterState? state)
    {
        if (state == null)
            return;

        try
        {
            state.Cell.isWatered = state.WasWatered;
            state.Plant.water = state.Water;
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(GrowSystem), "CanGrow", new[] { typeof(VirtualDate) })]
internal static class GrowSystemCanGrowIgnoreCropConditionsPatch
{
    private static void Postfix(ref bool __result)
    {
        if (!__result && IgnoreCropGrowthConditionsPatchContext.ShouldIgnoreCurrentCrop())
            __result = true;
    }
}

[HarmonyPatch(typeof(Zone), "GrowPlants", new[] { typeof(VirtualDate) })]
internal static class ZoneGrowPlantsIgnoreFertilizerPatch
{
    private static void Prefix(out IgnoreCropGrowthConditionsPatchContext.FertilityState? __state)
    {
        __state = IgnoreCropGrowthConditionsPatchContext.BeginIgnoreFertilizer();
    }

    private static Exception? Finalizer(
        Exception? __exception,
        IgnoreCropGrowthConditionsPatchContext.FertilityState? __state)
    {
        IgnoreCropGrowthConditionsPatchContext.EndIgnoreFertilizer(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(GrowSystem), "Grow", new[] { typeof(int) })]
internal static class GrowSystemGrowIgnoreWaterPatch
{
    private static void Prefix(out IgnoreCropGrowthConditionsPatchContext.WaterState? __state)
    {
        __state = IgnoreCropGrowthConditionsPatchContext.BeginIgnoreWater();
    }

    private static Exception? Finalizer(
        Exception? __exception,
        IgnoreCropGrowthConditionsPatchContext.WaterState? __state)
    {
        IgnoreCropGrowthConditionsPatchContext.EndIgnoreWater(__state);
        return __exception;
    }
}
