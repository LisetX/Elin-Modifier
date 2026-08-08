using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Chara), "Tick")]
internal static class ThreatPredecisionTickPatch
{
    private static void Prefix(Chara __instance)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.PrepareLockedDecision(__instance);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(AM_Adv), "RefreshArrow")]
internal static class ThreatPredecisionMouseDirectionPatch
{
    private static readonly AccessTools.FieldRef<AM_Adv, Vector2> ArrowRef =
        AccessTools.FieldRefAccess<AM_Adv, Vector2>("vArrow");

    private static void Postfix(AM_Adv __instance)
    {
        try
        {
            var arrow = ArrowRef(__instance);
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordMouseThreatDirection(
                Mathf.RoundToInt(arrow.x),
                Mathf.RoundToInt(arrow.y));
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(GoalCombat),
    "TryUseAbility",
    new[] { typeof(int), typeof(bool) })]
internal static class ThreatPredecisionAbilityPatch
{
    private static bool Prefix(GoalCombat __instance, ref bool __result)
    {
        try
        {
            var module = ElinModifierPlugin.ActiveModules?.ThreatOverlay;
            if (module != null && module.TryExecuteLockedDecision(__instance, out var result))
            {
                __result = result;
                return false;
            }
        }
        catch
        {
        }
        return true;
    }
}

[HarmonyPatch(
    typeof(GoalCombat),
    "TryMove",
    new[] { typeof(int) })]
internal static class ThreatPredecisionMovePatch
{
    private static bool Prefix(GoalCombat __instance, ref bool __result)
    {
        try
        {
            var module = ElinModifierPlugin.ActiveModules?.ThreatOverlay;
            if (module != null && module.TryExecuteLockedDecision(__instance, out var result))
            {
                __result = result;
                return false;
            }
        }
        catch
        {
        }
        return true;
    }
}
