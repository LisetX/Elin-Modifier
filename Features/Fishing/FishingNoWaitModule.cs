using HarmonyLib;

internal sealed class FishingNoWaitModule
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

internal static class FishingNoWaitPatchContext
{
    internal static FishingNoWaitModule? Current =>
        ElinModifierPlugin.ActiveModules?.FishingNoWait;
}

[HarmonyPatch(typeof(AI_Fish.ProgressFish), "OnProgress")]
internal static class FishingNoWaitProgressPatch
{
    private static void Prefix(AI_Fish.ProgressFish __instance, ref int ___hit)
    {
        var module = FishingNoWaitPatchContext.Current;
        if (module?.Enabled != true || __instance.owner?.IsPC != true)
            return;

        if (___hit < 0)
            ___hit = 0;
    }
}
