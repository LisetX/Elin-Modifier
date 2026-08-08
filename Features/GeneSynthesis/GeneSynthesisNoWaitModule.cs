using HarmonyLib;

internal sealed class GeneSynthesisNoWaitModule
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

internal static class GeneSynthesisNoWaitPatchContext
{
    internal static GeneSynthesisNoWaitModule? Current =>
        ElinModifierPlugin.ActiveModules?.GeneSynthesisNoWait;

    internal static bool IsReady(TraitGeneMachine machine)
    {
        if (Current?.Enabled != true || machine == null)
            return false;
        try
        {
            return machine.IsTargetUsingGene();
        }
        catch
        {
            return false;
        }
    }
}

[HarmonyPatch(typeof(TraitGeneMachine), "GetProgress")]
internal static class GeneSynthesisNoWaitProgressPatch
{
    private static bool Prefix(TraitGeneMachine __instance, ref float __result)
    {
        if (!GeneSynthesisNoWaitPatchContext.IsReady(__instance))
            return true;

        __result = 1f;
        return false;
    }
}

[HarmonyPatch(typeof(TraitGeneMachine), "GetProgressText")]
internal static class GeneSynthesisNoWaitProgressTextPatch
{
    private static bool Prefix(TraitGeneMachine __instance, ref string __result)
    {
        if (!GeneSynthesisNoWaitPatchContext.IsReady(__instance))
            return true;

        __result = "progress_finish".lang();
        return false;
    }
}
