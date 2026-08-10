using HarmonyLib;

[HarmonyPatch(
    typeof(Chara),
    "TryMove",
    new[] { typeof(Point), typeof(bool) })]
internal static class ThreatOverlayConfirmedMovePatch
{
    private static void Prefix(Chara __instance, out Point __state)
    {
        try
        {
            __state = __instance?.pos == null
                ? Point.Invalid
                : new Point(__instance.pos.x, __instance.pos.z);
        }
        catch
        {
            __state = Point.Invalid;
        }
    }

    private static void Postfix(
        Chara __instance,
        Point __0,
        Card.MoveResult __result,
        Point __state)
    {
        try
        {
            var module = ElinModifierPlugin.ActiveModules?.ThreatOverlay;
            module?.RecordResolvedPlayerMove(__instance, __state, __result);
            module?.RecordConfirmedMove(__instance, __0, __result);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(Act),
    "Perform",
    new[] { typeof(Chara), typeof(Card), typeof(Point) })]
internal static class ThreatOverlayConfirmedActPatch
{
    private static void Postfix(Act __instance, Chara __0, bool __result)
    {
        try
        {
            var module = ElinModifierPlugin.ActiveModules?.ThreatOverlay;
            module?.RecordResolvedPlayerAct(__0, __result);
            module?.RecordConfirmedAct(__0, __instance, __result);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(Chara),
    "UseAbility",
    new[] { typeof(Act), typeof(Card), typeof(Point), typeof(bool) })]
internal static class ThreatOverlayConfirmedAbilityPatch
{
    private static void Postfix(Chara __instance, Act __0, bool __result)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordConfirmedAbility(__instance, __0, __result);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(AIAct), "SetChild")]
internal static class ThreatOverlayAiChildSelectionPatch
{
    private static void Postfix(AIAct __instance, AIAct __0)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordSelectedAiAction(__instance, __0);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(Chara),
    "SetAI",
    new[] { typeof(AIAct) })]
internal static class ThreatOverlayAiGoalSelectionPatch
{
    private static void Postfix(Chara __instance, AIAct __0)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordAiGoalChanged(__instance, __0);
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
internal static class ThreatOverlayCombatEvaluationPatch
{
    private static void Postfix(GoalCombat __instance)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordCombatEvaluation(__instance);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(Chara),
    "SetEnemy",
    new[] { typeof(Chara) })]
internal static class ThreatOverlayTargetChangedPatch
{
    private static void Postfix(Chara __instance)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordThreatTargetChanged(__instance);
        }
        catch
        {
        }
    }
}

