using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

internal sealed class SpecialNpcHatchModule
{
    internal bool IgnoreRestriction { get; private set; }

    internal void Load(bool enabled)
    {
        IgnoreRestriction = enabled;
    }

    internal void Reset()
    {
        IgnoreRestriction = false;
    }

    internal bool SetEnabled(bool enabled)
    {
        if (IgnoreRestriction == enabled)
            return false;
        IgnoreRestriction = enabled;
        return true;
    }

    internal bool ShouldKeepReferencedNpc(CardRow row)
    {
        return IgnoreRestriction || row == null || row.quality != 4;
    }

    internal Card SelectLaidEggSource(Card producer, Card fallback)
    {
        return IgnoreRestriction && producer != null ? producer : fallback;
    }
}

internal static class SpecialNpcHatchPatchContext
{
    internal static SpecialNpcHatchModule? Current =>
        ElinModifierPlugin.ActiveModules?.SpecialNpcHatch;
}

[HarmonyPatch(
    typeof(TraitFoodEggFertilized),
    "Incubate",
    new[] { typeof(Thing), typeof(Point), typeof(Card) })]
internal static class TraitFoodEggFertilizedIncubateSpecialNpcPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var qualityField = AccessTools.Field(typeof(CardRow), "quality");
        var helper = AccessTools.Method(
            typeof(TraitFoodEggFertilizedIncubateSpecialNpcPatch),
            nameof(ShouldKeepReferencedNpc));
        var patched = false;

        for (var i = 0; i + 2 < codes.Count; i++)
        {
            if (codes[i].opcode != OpCodes.Ldfld ||
                !Equals(codes[i].operand, qualityField) ||
                codes[i + 1].opcode != OpCodes.Ldc_I4_4 ||
                (codes[i + 2].opcode != OpCodes.Bne_Un &&
                 codes[i + 2].opcode != OpCodes.Bne_Un_S))
                continue;

            codes[i].opcode = OpCodes.Call;
            codes[i].operand = helper;
            codes[i + 1].opcode = OpCodes.Nop;
            codes[i + 1].operand = null;
            codes[i + 2].opcode = codes[i + 2].opcode == OpCodes.Bne_Un_S
                ? OpCodes.Brtrue_S
                : OpCodes.Brtrue;
            patched = true;
            break;
        }

        if (!patched)
            throw new InvalidOperationException(
                "TraitFoodEggFertilized.Incubate special NPC restriction was not found.");

        return codes;
    }

    private static bool ShouldKeepReferencedNpc(CardRow row)
    {
        var module = SpecialNpcHatchPatchContext.Current;
        return module == null ? row == null || row.quality != 4 : module.ShouldKeepReferencedNpc(row);
    }
}

[HarmonyPatch(
    typeof(Card),
    "MakeEgg",
    new[]
    {
        typeof(bool), typeof(int), typeof(bool), typeof(int),
        typeof(Nullable<BlessedState>)
    })]
internal static class CardMakeEggSpecialNpcPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var makeFoodFrom = AccessTools.Method(
            typeof(Card),
            "MakeFoodFrom",
            new[] { typeof(Card), typeof(bool) });
        var helper = AccessTools.Method(
            typeof(CardMakeEggSpecialNpcPatch),
            nameof(MakeFoodFromSpecialNpc));
        var foundSpecialFallback = false;
        var patched = false;

        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldstr &&
                string.Equals(codes[i].operand as string, "caladrius", StringComparison.Ordinal))
            {
                foundSpecialFallback = true;
                continue;
            }

            if (!foundSpecialFallback ||
                codes[i].opcode != OpCodes.Callvirt ||
                !Equals(codes[i].operand, makeFoodFrom))
                continue;

            codes[i].opcode = OpCodes.Ldarg_0;
            codes[i].operand = null;
            codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, helper));
            patched = true;
            break;
        }

        if (!patched)
            throw new InvalidOperationException(
                "Card.MakeEgg special NPC egg source fallback was not found.");

        return codes;
    }

    private static Card MakeFoodFromSpecialNpc(
        Card egg,
        Card fallback,
        bool makeRef,
        Card producer)
    {
        var module = SpecialNpcHatchPatchContext.Current;
        var source = module == null
            ? fallback
            : module.SelectLaidEggSource(producer, fallback);
        return egg.MakeFoodFrom(source, makeRef);
    }
}
