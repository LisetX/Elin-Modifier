using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;

internal sealed class AllowCurrencyGiftsModule
{
    private static AllowCurrencyGiftsModule? _active;
    private bool _initialized;

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

    internal void Initialize(HarmonyPatchModule harmonyModule, ManualLogSource logger)
    {
        if (_initialized)
            return;

        Harmony? harmony = null;
        try
        {
            var canAcceptGift = AccessTools.Method(
                typeof(Chara),
                "CanAcceptGift",
                new[] { typeof(Chara), typeof(Card) });
            var canAcceptGiftPostfix = AccessTools.Method(
                typeof(AllowCurrencyGiftsModule),
                nameof(CanAcceptGiftPostfix));
            var onGift = AccessTools.Method(
                typeof(Affinity),
                "OnGift",
                new[] { typeof(Thing) });
            var onGiftTranspiler = AccessTools.Method(
                typeof(AllowCurrencyGiftsModule),
                nameof(CurrencyGiftAffinityTranspiler));
            if (canAcceptGift == null || canAcceptGiftPostfix == null)
                throw new MissingMethodException("Chara.CanAcceptGift currency gift patch target was not found.");
            if (onGift == null || onGiftTranspiler == null)
                throw new MissingMethodException("Affinity.OnGift currency gift patch target was not found.");

            harmony = harmonyModule.GetGroupHarmony("allow-currency-gifts");
            harmony.Patch(
                onGift,
                transpiler: new HarmonyMethod(onGiftTranspiler));
            harmony.Patch(
                canAcceptGift,
                postfix: new HarmonyMethod(canAcceptGiftPostfix));
            _active = this;
            _initialized = true;
        }
        catch (Exception ex)
        {
            _active = null;
            _initialized = false;
            try
            {
                harmony?.UnpatchSelf();
            }
            catch
            {
            }
            logger.LogError("Currency gift patch failed: " + ex);
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
            typeof(AllowCurrencyGiftsModule),
            nameof(ResolveCurrencyGiftAffinity));
        if (stringEquality == null || resolver == null)
            throw new MissingMethodException("Currency gift affinity helper was not found.");

        for (var i = 0; i + 4 < codes.Count; i++)
        {
            if (codes[i].opcode != OpCodes.Ldstr ||
                !string.Equals(codes[i].operand as string, "currency", StringComparison.Ordinal) ||
                !codes[i + 1].Calls(stringEquality) ||
                (codes[i + 2].opcode != OpCodes.Brfalse &&
                 codes[i + 2].opcode != OpCodes.Brfalse_S) ||
                codes[i + 3].opcode != OpCodes.Ldc_I4_0 ||
                codes[i + 4].opcode != OpCodes.Stloc_0)
                continue;

            codes[i + 3].opcode = OpCodes.Ldloc_0;
            codes[i + 3].operand = null;
            codes.Insert(i + 4, new CodeInstruction(OpCodes.Call, resolver));
            return codes;
        }

        throw new InvalidOperationException(
            "Affinity.OnGift currency affinity reset was not found.");
    }

    private static int ResolveCurrencyGiftAffinity(int stableAffinity)
    {
        var module = _active;
        return module != null && module.Enabled ? stableAffinity : 0;
    }

    private static void CanAcceptGiftPostfix(Chara __instance, Card __1, ref bool __result)
    {
        var module = _active;
        if (__result || module == null || !module.Enabled || __instance == null || __1 == null)
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
