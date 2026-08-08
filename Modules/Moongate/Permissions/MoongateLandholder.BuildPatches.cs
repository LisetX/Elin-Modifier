using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

[HarmonyPatch(typeof(Zone), "CanEnterBuildModeAnywhere", MethodType.Getter)]
internal static class MoongateCanEnterBuildModeAnywherePatch
{
    private static void Postfix(Zone __instance, ref bool __result)
    {
        if (!__result && MoongateLandholderPrivilegeContext.IsActive(__instance))
            __result = true;
    }
}

[HarmonyPatch(typeof(Zone), "IsCrime", new[] { typeof(Chara), typeof(Act) })]
internal static class MoongateLandholderCrimePatch
{
    private static void Postfix(Zone __instance, Chara c, ref bool __result)
    {
        if (__result && c?.IsPC == true && MoongateLandholderPrivilegeContext.IsActive(__instance))
            __result = false;
    }
}

[HarmonyPatch(typeof(HotbarManager), "ResetHotbar", new[] { typeof(int) })]
internal static class MoongateLandholderHotbarPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceBranchChecks(instructions);
    }
}

[HarmonyPatch(typeof(AM_Picker), "CanActivate", MethodType.Getter)]
internal static class MoongateLandholderPickerPatch
{
    private static void Postfix(AM_Picker __instance, ref bool __result)
    {
        if (__result || __instance.IsActive || !MoongateLandholderPrivilegeContext.IsActive(GameAccess.World.CurrentZone))
            return;

        try
        {
            __result = GameAccess.Runtime.Debug.godBuild || GameAccess.Characters.PlayerCharacter?.homeBranch?.elements?.Has(4005) == true;
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(TaskBuild), "CanRotateBlock")]
internal static class MoongateLandholderBuildRotationPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

[HarmonyPatch(typeof(TaskBuild), "GetHitResult")]
internal static class MoongateLandholderBuildPlacementPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

[HarmonyPatch(typeof(TaskDig), "GetHitResult")]
internal static class MoongateLandholderDigPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

[HarmonyPatch]
internal static class MoongateLandholderMiningAndDestructionResourcePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var methodName in new[]
                 {
                     "DropBlockComponent",
                     "MineBlock",
                     "MineObj"
                 })
        {
            foreach (var method in MoongateLandholderPrivilegeContext
                         .EnumerateMethodAndGeneratedMethods(typeof(Map), methodName))
                yield return method;
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceUserMapBenefitChecks(instructions);
    }
}

[HarmonyPatch]
internal static class MoongateLandholderGrowthResourcePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var methodName in new[]
                 {
                     "PopMineObj",
                     "TryPopSeed"
                 })
        {
            foreach (var method in MoongateLandholderPrivilegeContext
                         .EnumerateMethodAndGeneratedMethods(typeof(GrowSystem), methodName))
                yield return method;
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceUserMapBenefitChecks(instructions);
    }
}

[HarmonyPatch(typeof(TaskChopWood), "GetHitResult")]
internal static class MoongateLandholderChopWoodPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceNpcPropertyChecks(
            MoongateLandholderPrivilegeContext.ReplaceUserMapBenefitChecks(instructions));
    }
}

[HarmonyPatch]
internal static class MoongateLandholderHarvestPermissionPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var hostileGetter = AccessTools.PropertyGetter(
            typeof(TaskHarvest),
            "IsHostileAct");
        if (hostileGetter != null)
            yield return hostileGetter;

        var harvestThing = AccessTools.Method(
            typeof(TaskHarvest),
            "HarvestThing");
        if (harvestThing != null)
            yield return harvestThing;

        foreach (var method in MoongateLandholderPrivilegeContext
                     .EnumerateMethodAndGeneratedMethods(
                         typeof(TaskHarvest),
                         "OnCreateProgress"))
            yield return method;
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceNpcPropertyChecks(
            MoongateLandholderPrivilegeContext.ReplaceUserMapBenefitChecks(instructions));
    }
}

[HarmonyPatch(typeof(TaskDump), "TryPerform")]
internal static class MoongateLandholderDumpPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

[HarmonyPatch(typeof(InvOwnerCraft), "AllowStockIngredients", MethodType.Getter)]
internal static class MoongateLandholderCraftStockPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

