using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using Steamworks;

internal sealed class NightlyModule
{
    private static NightlyModule? _active;
    private bool _initialized;

    private NightlyModule(string branchName)
    {
        BranchName = branchName;
    }

    internal string BranchName { get; }
    internal bool AllowCurrencyGifts { get; set; }
    internal string Log { get; set; } = "Ready";

    internal static NightlyModule? TryCreate()
    {
        try
        {
            string branchName;
            if (!SteamApps.GetCurrentBetaName(out branchName, 128))
                return null;
            if (!string.Equals(branchName, "nightly", StringComparison.OrdinalIgnoreCase))
                return null;
            return new NightlyModule(branchName);
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
            if (canAcceptGift == null || canAcceptGiftPostfix == null)
                throw new MissingMethodException("Chara.CanAcceptGift Nightly patch target was not found.");
            if (onGift == null || onGiftTranspiler == null)
                throw new MissingMethodException("Affinity.OnGift Nightly patch target was not found.");

            var harmony = harmonyModule.GetGroupHarmony("nightly");
            harmony.Patch(
                onGift,
                transpiler: new HarmonyMethod(onGiftTranspiler));
            harmony.Patch(
                canAcceptGift,
                postfix: new HarmonyMethod(canAcceptGiftPostfix));
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
