using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

[HarmonyPatch(typeof(InvOwnerRefuel), "AllowStockIngredients", MethodType.Getter)]
internal static class MoongateLandholderRefuelStockPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

[HarmonyPatch]
internal static class MoongateLandholderStockSearchLocalPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var nestedTypes = typeof(Props).GetNestedTypes(
            BindingFlags.Public | BindingFlags.NonPublic);
        for (var i = 0; i < nestedTypes.Length; i++)
        {
            var methods = nestedTypes[i].GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            for (var j = 0; j < methods.Length; j++)
            {
                var name = methods[j].Name;
                if (name.IndexOf("<ListThingStack>g__Find|", StringComparison.Ordinal) >= 0 ||
                    name.IndexOf("<ListThingStack>g__FindCat|", StringComparison.Ordinal) >= 0)
                    yield return methods[j];
            }
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

[HarmonyPatch(typeof(Chara), "HoldCard", new[] { typeof(Card), typeof(int) })]
internal static class MoongateLandholderHoldCardPatch
{
    private static void Prefix(Chara __instance, Card t)
    {
        if (__instance?.IsPC == true && t != null &&
            MoongateLandholderPrivilegeContext.IsActive(GameAccess.World.CurrentZone))
            t.isNPCProperty = false;
    }
}

[HarmonyPatch(
    typeof(Chara),
    "Pick",
    new[] { typeof(Thing), typeof(bool), typeof(bool) })]
internal static class MoongateLandholderPickThingPatch
{
    private static void Prefix(Chara __instance, Thing t)
    {
        if (__instance?.IsPC == true && t != null && t.ExistsOnMap &&
            MoongateLandholderPrivilegeContext.IsActive(GameAccess.World.CurrentZone))
            t.isNPCProperty = false;
    }
}

[HarmonyPatch]
internal static class MoongateLandholderPickupActionPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return MoongateLandholderPrivilegeContext.EnumerateMethodAndGeneratedMethods(
            typeof(HotItemNoItem),
            "_TrySetAct");
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceNpcPropertyChecks(
            MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions));
    }
}

[HarmonyPatch(typeof(TileTypeDoor), "CanBeHeld", MethodType.Getter)]
internal static class MoongateLandholderDoorPickupPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

[HarmonyPatch(typeof(TraitTrap), "CanBeHeld", MethodType.Getter)]
internal static class MoongateLandholderTrapPickupPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }
}

[HarmonyPatch(typeof(TraitContainer), "TrySetAct")]
internal static class MoongateLandholderContainerActionPatch
{
    private static bool Prefix(TraitContainer __instance, ActPlan p)
    {
        var owner = __instance?.owner;
        if (owner == null || !owner.ExistsOnMap ||
            !MoongateLandholderPrivilegeContext.IsActive(GameAccess.World.CurrentZone))
            return true;

        if (owner.c_lockLv > 0)
            return true;

        p.TrySetAct(
            "actContainer",
            delegate
            {
                __instance.Open();
                return false;
            },
            owner,
            CursorSystem.Container,
            1,
            false);
        return false;
    }
}

