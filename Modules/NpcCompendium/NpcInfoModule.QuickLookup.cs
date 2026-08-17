using HarmonyLib;

internal sealed partial class NpcInfoModule
{
    private const string QuickLookupActionId = "ElinModifier.NpcCompendium.QuickLookup";

    internal void AddQuickLookupInteraction(ActPlan? plan, PointTarget? pointTarget)
    {
        if (!QuickLookupEnabled || plan == null || pointTarget == null || plan.input != ActInput.AllAction)
            return;

        Chara? target;
        try
        {
            target = pointTarget.TargetChara;
        }
        catch
        {
            return;
        }

        if (target == null || ReferenceEquals(target, GameAccess.Characters.PlayerCharacter))
            return;

        var npcId = GameAccess.Characters.GetId(target);
        if (string.IsNullOrWhiteSpace(npcId) || !HasNpc(npcId))
            return;

        var previousIgnoreCondition = plan.ignoreAddCondition;
        try
        {
            plan.ignoreAddCondition = true;
            plan.TrySetAct(
                QuickLookupActionId,
                () => OpenQuickLookup(npcId),
                target,
                null,
                -1,
                false,
                false,
                false);
        }
        catch
        {
        }
        finally
        {
            plan.ignoreAddCondition = previousIgnoreCondition;
        }
    }

    internal bool TryGetQuickLookupActionText(DynamicAct? act, out string text)
    {
        text = "";
        if (!QuickLookupEnabled || act == null || act.id != QuickLookupActionId)
            return false;
        text = T("查看NPC图鉴", "View NPC Compendium");
        return true;
    }

    private bool OpenQuickLookup(string npcId)
    {
        _host.OpenLGuiNpcInformationFromInteraction(npcId);
        return false;
    }
}

[HarmonyPatch(typeof(ActPlan), "_Update")]
internal static class ActPlanNpcCompendiumQuickLookupPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ActPlan __instance, PointTarget __0)
    {
        ElinModifierPlugin.ActiveModules?.NpcInfo.AddQuickLookupInteraction(__instance, __0);
    }
}

[HarmonyPatch(typeof(DynamicAct), "GetText")]
internal static class DynamicActNpcCompendiumQuickLookupTextPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(DynamicAct __instance, ref string __result)
    {
        if (ElinModifierPlugin.ActiveModules?.NpcInfo.TryGetQuickLookupActionText(__instance, out var text) == true)
            __result = text;
    }
}
