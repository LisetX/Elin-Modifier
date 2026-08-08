using HarmonyLib;

internal sealed class SleepWithoutSleepinessModule
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

internal static class SleepWithoutSleepinessPatchContext
{
    internal static SleepWithoutSleepinessModule? Current =>
        ElinModifierPlugin.ActiveModules?.SleepWithoutSleepiness;
}

[HarmonyPatch(typeof(Chara), "CanSleep")]
internal static class SleepWithoutSleepinessCanSleepPatch
{
    private static void Postfix(Chara __instance, ref bool __result)
    {
        if (__result || SleepWithoutSleepinessPatchContext.Current?.Enabled != true ||
            __instance?.IsPC != true)
            return;

        try
        {
            var zone = GameAccess.World.CurrentZone;
            if (zone != null && zone.events?.GetEvent<ZoneEventQuest>() == null)
                __result = true;
        }
        catch
        {
        }
    }
}
