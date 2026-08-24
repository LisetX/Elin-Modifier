using System;
using BepInEx.Logging;
using HarmonyLib;
using Steamworks;

internal sealed class NightlyModule
{
    private static NightlyModule? _active;
    private readonly IBoundGameValue<Card> _actTarget;
    private bool _initialized;

    private NightlyModule(string branchName, IGameMemberBinder binder)
    {
        BranchName = branchName;
        _actTarget = binder.BindStaticValue<Card>(
            typeof(Act),
            GameValueAccess.ReadWrite,
            "TC");
    }

    internal string BranchName { get; }
    internal bool FixSelfTalkBug { get; set; }
    internal string Log { get; set; } = "Ready";

    internal static NightlyModule? TryCreate(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        try
        {
            string branchName;
            if (!SteamApps.GetCurrentBetaName(out branchName, 128))
                return null;
            if (!string.Equals(branchName, "nightly", StringComparison.OrdinalIgnoreCase))
                return null;
            return new NightlyModule(branchName, binder);
        }
        catch
        {
            return null;
        }
    }

    internal void Initialize(HarmonyPatchModule harmonyModule, ManualLogSource logger)
    {
        if (_initialized)
            return;

        try
        {
            var canPerform = AccessTools.Method(
                typeof(Act),
                "CanPerform",
                new[] { typeof(Chara), typeof(Card), typeof(Point) });
            var canPerformPrefix = AccessTools.Method(
                typeof(NightlyModule),
                nameof(ChatTargetPrefix));
            var canPerformPostfix = AccessTools.Method(
                typeof(NightlyModule),
                nameof(ChatTargetPostfix));
            if (canPerform == null || canPerformPrefix == null || canPerformPostfix == null)
                throw new MissingMethodException("Act.CanPerform Nightly patch target was not found.");
            if (!_actTarget.IsBound)
                throw new MissingMemberException("Act.TC Nightly patch target was not found.");

            var harmony = harmonyModule.GetGroupHarmony("nightly");
            harmony.Patch(
                canPerform,
                prefix: new HarmonyMethod(canPerformPrefix),
                postfix: new HarmonyMethod(canPerformPostfix));
            _active = this;
            _initialized = true;
            Log = "Nightly patches ready";
        }
        catch (Exception ex)
        {
            _active = null;
            _initialized = false;
            Log = "Nightly patch failed: " + ex.Message;
            logger.LogError(Log);
        }
    }

    internal void Shutdown()
    {
        if (ReferenceEquals(_active, this))
            _active = null;
        _initialized = false;
    }

    private static void ChatTargetPrefix(Act __instance, Card __1, out Card? __state)
    {
        var module = _active;
        __state = module != null &&
                  module.FixSelfTalkBug &&
                  __instance is ActChat &&
                  __1 is Chara
            ? __1
            : null;
    }

    private static void ChatTargetPostfix(Act __instance, Card? __state, bool __result)
    {
        var module = _active;
        if (module == null ||
            !module.FixSelfTalkBug ||
            __instance is not ActChat ||
            __state == null ||
            !__result)
            return;

        module._actTarget.TrySet(null, __state);
    }
}
