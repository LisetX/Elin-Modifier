using System;
using HarmonyLib;

internal static class YieldMultiplierPatchContext
{
    internal static YieldMultiplierModule? Current =>
        ElinModifierPlugin.ActiveModules?.YieldMultiplier;
}

[HarmonyPatch(
    typeof(Map),
    "TrySmoothPick",
    new[] { typeof(Point), typeof(Thing), typeof(Chara) })]
internal static class MapTrySmoothPickYieldMultiplierPatch
{
    private static bool Prefix(Thing __1)
    {
        return YieldMultiplierPatchContext.Current?.ScaleGathering(__1) ?? true;
    }
}

[HarmonyPatch(typeof(Card), "EjectSockets")]
internal static class CardEjectSocketsYieldMultiplierSuppressPatch
{
    private static void Prefix(out bool __state)
    {
        __state = YieldMultiplierPatchContext.Current?.TryEnterGatheringSuppress() == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        YieldMultiplierPatchContext.Current?.ExitGatheringSuppress(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(DramaOutcome), "get_scratch")]
internal static class DramaOutcomeScratchYieldMultiplierSuppressPatch
{
    private static void Prefix(out bool __state)
    {
        __state = YieldMultiplierPatchContext.Current?.TryEnterGatheringSuppress() == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        YieldMultiplierPatchContext.Current?.ExitGatheringSuppress(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(ZonePreEnterBoutWin), "Execute")]
internal static class ZonePreEnterBoutWinYieldMultiplierSuppressPatch
{
    private static void Prefix(out bool __state)
    {
        __state = YieldMultiplierPatchContext.Current?.TryEnterGatheringSuppress() == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        YieldMultiplierPatchContext.Current?.ExitGatheringSuppress(__state);
        return __exception;
    }
}

[HarmonyPatch(
    typeof(ThingGen),
    "CreateTreasureContent",
    new[] { typeof(Thing), typeof(int), typeof(TreasureType), typeof(bool) })]
internal static class ThingGenCreateTreasureContentYieldMultiplierPatch
{
    private static void Prefix(out bool __state)
    {
        __state = YieldMultiplierPatchContext.Current?.TryEnterContainer() == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        YieldMultiplierPatchContext.Current?.ExitContainer(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(TraitBaseContainer), "Prespawn", new[] { typeof(int) })]
internal static class TraitBaseContainerPrespawnYieldMultiplierPatch
{
    private static void Prefix(out bool __state)
    {
        __state = YieldMultiplierPatchContext.Current?.TryEnterContainer() == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        YieldMultiplierPatchContext.Current?.ExitContainer(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Trait), "OnBarter", new[] { typeof(bool) })]
internal static class TraitOnBarterYieldMultiplierSuppressPatch
{
    private static void Prefix(out bool __state)
    {
        __state = YieldMultiplierPatchContext.Current?.TryEnterContainerSuppress() == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        YieldMultiplierPatchContext.Current?.ExitContainerSuppress(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Card), "Add", new[] { typeof(string), typeof(int), typeof(int) })]
internal static class CardAddYieldMultiplierPatch
{
    private static void Prefix(ref int __1)
    {
        YieldMultiplierPatchContext.Current?.ScaleContainerAmount(ref __1);
    }
}

[HarmonyPatch(typeof(AI_Fish), "Makefish", new[] { typeof(Chara) })]
internal static class AiFishMakefishYieldMultiplierPatch
{
    private static void Postfix(Thing __result)
    {
        YieldMultiplierPatchContext.Current?.ScaleFishing(__result);
    }
}

[HarmonyPatch(typeof(AI_Shear), "GetFur", new[] { typeof(Chara), typeof(int) })]
internal static class AiShearGetFurYieldMultiplierPatch
{
    private static void Postfix(Thing __result)
    {
        YieldMultiplierPatchContext.Current?.ScaleFur(__result);
    }
}

[HarmonyPatch(typeof(FactionBranch), "DailyOutcome", new[] { typeof(VirtualDate) })]
internal static class FactionBranchDailyOutcomeYieldMultiplierPatch
{
    private static void Prefix(out bool __state)
    {
        __state = YieldMultiplierPatchContext.Current?.TryEnterHomeYield() == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        YieldMultiplierPatchContext.Current?.ExitHomeYield(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Card), "MakeEgg", new[]
{
    typeof(bool), typeof(int), typeof(bool), typeof(int), typeof(BlessedState?)
})]
internal static class CardMakeEggYieldMultiplierPatch
{
    private static void Postfix(Thing __result)
    {
        YieldMultiplierPatchContext.Current?.ScaleHomeYield(__result);
    }
}

[HarmonyPatch(typeof(Card), "MakeMilk", new[]
{
    typeof(bool), typeof(int), typeof(bool), typeof(BlessedState?)
})]
internal static class CardMakeMilkYieldMultiplierPatch
{
    private static void Postfix(Thing __result)
    {
        YieldMultiplierPatchContext.Current?.ScaleHomeYield(__result);
    }
}

[HarmonyPatch(typeof(AI_PlayMusic), "ThrowReward", new[] { typeof(Chara), typeof(bool) })]
internal static class AiPlayMusicThrowRewardYieldMultiplierPatch
{
    private static void Prefix(AI_PlayMusic __instance, bool __1, out bool __state)
    {
        Chara? owner = null;
        try { owner = __instance?.owner; }
        catch { }
        __state = YieldMultiplierPatchContext.Current?.TryEnterPerformance(owner, __1) == true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        YieldMultiplierPatchContext.Current?.ExitPerformance(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(ActThrow), "Throw", new[]
{
    typeof(Card), typeof(Point), typeof(Thing), typeof(ThrowMethod), typeof(float)
})]
internal static class ActThrowYieldMultiplierPatch
{
    private static void Prefix(Thing __2)
    {
        YieldMultiplierPatchContext.Current?.ScalePerformanceReward(__2);
    }
}
