using System;
using System.Collections.Generic;
using System.Reflection.Emit;
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
    internal bool AllowCurrencyGifts { get; set; }
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
            var canAcceptGift = AccessTools.Method(
                typeof(Chara),
                "CanAcceptGift",
                new[] { typeof(Chara), typeof(Card) });
            var canAcceptGiftPostfix = AccessTools.Method(
                typeof(NightlyModule),
                nameof(CanAcceptGiftPostfix));
            var onGift = AccessTools.Method(
                typeof(Affinity),
                "OnGift",
                new[] { typeof(Thing) });
            var onGiftTranspiler = AccessTools.Method(
                typeof(NightlyModule),
                nameof(CurrencyGiftAffinityTranspiler));
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
            if (canAcceptGift == null || canAcceptGiftPostfix == null)
                throw new MissingMethodException("Chara.CanAcceptGift Nightly patch target was not found.");
            if (onGift == null || onGiftTranspiler == null)
                throw new MissingMethodException("Affinity.OnGift Nightly patch target was not found.");
            if (canPerform == null || canPerformPrefix == null || canPerformPostfix == null)
                throw new MissingMethodException("Act.CanPerform Nightly patch target was not found.");
            if (!_actTarget.IsBound)
                throw new MissingMemberException("Act.TC Nightly patch target was not found.");

            var harmony = harmonyModule.GetGroupHarmony("nightly");
            harmony.Patch(
                onGift,
                transpiler: new HarmonyMethod(onGiftTranspiler));
            harmony.Patch(
                canAcceptGift,
                postfix: new HarmonyMethod(canAcceptGiftPostfix));
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

    private static IEnumerable<CodeInstruction> CurrencyGiftAffinityTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var stringEquality = AccessTools.Method(
            typeof(string),
            "op_Equality",
            new[] { typeof(string), typeof(string) });
        var resolver = AccessTools.Method(
            typeof(NightlyModule),
            nameof(ResolveCurrencyGiftAffinity));
        var patched = false;

        for (var i = 0; i + 4 < codes.Count; i++)
        {
            if (codes[i].opcode != OpCodes.Ldstr ||
                !string.Equals(codes[i].operand as string, "currency", StringComparison.Ordinal) ||
                stringEquality == null ||
                !codes[i + 1].Calls(stringEquality) ||
                (codes[i + 2].opcode != OpCodes.Brfalse &&
                 codes[i + 2].opcode != OpCodes.Brfalse_S) ||
                codes[i + 3].opcode != OpCodes.Ldc_I4_0 ||
                codes[i + 4].opcode != OpCodes.Stloc_0)
                continue;

            codes[i + 3].opcode = OpCodes.Ldloc_0;
            codes[i + 3].operand = null;
            codes.Insert(i + 4, new CodeInstruction(OpCodes.Call, resolver));
            patched = true;
            break;
        }

        if (!patched || resolver == null)
            throw new InvalidOperationException(
                "Affinity.OnGift Nightly currency affinity reset was not found.");

        return codes;
    }

    private static int ResolveCurrencyGiftAffinity(int stableAffinity)
    {
        var module = _active;
        return module != null && module.AllowCurrencyGifts ? stableAffinity : 0;
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

    private static void CanAcceptGiftPostfix(Chara __instance, Card __1, ref bool __result)
    {
        var module = _active;
        if (__result || module == null || !module.AllowCurrencyGifts || __instance == null || __1 == null)
            return;

        try
        {
            if (__1.category == null ||
                !string.Equals(__1.category.id, "currency", StringComparison.Ordinal) ||
                __instance.IsPCFaction ||
                __instance.things == null ||
                __instance.things.IsFull() ||
                __1.c_isImportant ||
                __1.trait == null ||
                __1.trait.CanOnlyCarry ||
                !__1.trait.CanBeDestroyed ||
                __1.trait.CanExtendBuild ||
                __1.rarity == Rarity.Artifact ||
                __1.IsContainer)
                return;

            __result = true;
        }
        catch
        {
        }
    }
}
