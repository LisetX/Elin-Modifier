using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class ProbabilityModule
{
    [HarmonyPatch(typeof(Card), "SpawnLoot", new[] { typeof(Card) })]
    private static class DropMultiplierSpawnLootPatch
    {
        private static void Prefix(out bool __state)
        {
            var module = ElinModifierPlugin.ActiveModules?.Probability;
            __state = module != null && module.HasActiveDropMultiplier();
            if (__state)
                _dropMultiplierDepth++;
        }

        private static Exception? Finalizer(Exception? __exception, bool __state)
        {
            if (__state && _dropMultiplierDepth > 0)
                _dropMultiplierDepth--;
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Zone), "AddCard", new[] { typeof(Card), typeof(Point) })]
    private static class DropMultiplierZoneAddPatch
    {
        private static bool Prefix(ref Card __0, ref Card __result, out DropZoneAddState __state)
        {
            var module = ElinModifierPlugin.ActiveModules?.Probability;
            __state = module == null ? default(DropZoneAddState) : module.PrepareDropThing(__0);
            if (!__state.Skip)
                return true;
            try { __0.Destroy(); } catch { }
            __result = __0;
            return false;
        }

        private static void Postfix(Zone __instance, Point __1, Card __result, DropZoneAddState __state)
        {
            if (!__state.Skip && __state.ExtraCopies > 0 && __result != null)
                AddDropExtraCopies(__instance, __1, __state, __result);
        }
    }
    [HarmonyPatch(typeof(TraitCrafter), "Craft", new[] { typeof(AI_UseCrafter) })]
    private static class ScratchProbabilityPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            ReplaceScratchPrizeDenominator(list, "medal", AccessTools.Method(typeof(ProbabilityModule), nameof(GetScratchMedalDenominator)));
            ReplaceScratchPrizeDenominator(list, "plat", AccessTools.Method(typeof(ProbabilityModule), nameof(GetScratchPlatinumDenominator)));
            ReplaceScratchPrizeDenominator(list, "furniture", AccessTools.Method(typeof(ProbabilityModule), nameof(GetScratchFurnitureDenominator)));
            ReplaceScratchPrizeDenominator(list, "plamo_box", AccessTools.Method(typeof(ProbabilityModule), nameof(GetScratchModelBoxDenominator)));
            ReplaceScratchPrizeDenominator(list, "food", AccessTools.Method(typeof(ProbabilityModule), nameof(GetScratchFoodDenominator)));
            return list;
        }
    }
    private static void ReplaceScratchPrizeDenominator(List<CodeInstruction> instructions, string rewardId, MethodInfo? getter)
    {
        if (getter == null)
            return;
        for (var i = 0; i + 1 < instructions.Count; i++)
        {
            if (!TryGetLdcI4(instructions[i], out _) || instructions[i + 1].opcode != OpCodes.Ldstr ||
                !string.Equals(instructions[i + 1].operand as string, rewardId, StringComparison.Ordinal))
                continue;
            instructions[i].opcode = OpCodes.Call;
            instructions[i].operand = getter;
            return;
        }
    }
    private static IEnumerable<CodeInstruction> GambleChestProbabilityTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        var rndMethod = AccessTools.Method(typeof(EClass), "rnd", new[] { typeof(int) });
        var forcedSuccessGetter = AccessTools.Method(typeof(ProbabilityModule), nameof(GetGambleChestForcedSuccessDenominator));
        var forcedFailureGetter = AccessTools.Method(typeof(ProbabilityModule), nameof(GetGambleChestForcedFailureDenominator));
        var jackpotRangeGetter = AccessTools.Method(typeof(ProbabilityModule), nameof(GetGambleChestJackpotRange));
        var twentyCount = 0;
        for (var i = 1; i < list.Count; i++)
        {
            if (rndMethod == null || !list[i].Calls(rndMethod) || !TryGetLdcI4(list[i - 1], out var constant))
                continue;
            MethodInfo? replacement = null;
            if (constant == 20)
            {
                replacement = twentyCount == 0 ? forcedSuccessGetter : twentyCount == 1 ? forcedFailureGetter : null;
                twentyCount++;
            }
            else if (constant == 10000)
            {
                replacement = jackpotRangeGetter;
            }
            if (replacement == null)
                continue;
            list[i - 1].opcode = OpCodes.Call;
            list[i - 1].operand = replacement;
        }
        return list;
    }
    private static bool TryGetLdcI4(CodeInstruction instruction, out int value)
    {
        value = 0;
        if (instruction.opcode == OpCodes.Ldc_I4_M1) value = -1;
        else if (instruction.opcode == OpCodes.Ldc_I4_0) value = 0;
        else if (instruction.opcode == OpCodes.Ldc_I4_1) value = 1;
        else if (instruction.opcode == OpCodes.Ldc_I4_2) value = 2;
        else if (instruction.opcode == OpCodes.Ldc_I4_3) value = 3;
        else if (instruction.opcode == OpCodes.Ldc_I4_4) value = 4;
        else if (instruction.opcode == OpCodes.Ldc_I4_5) value = 5;
        else if (instruction.opcode == OpCodes.Ldc_I4_6) value = 6;
        else if (instruction.opcode == OpCodes.Ldc_I4_7) value = 7;
        else if (instruction.opcode == OpCodes.Ldc_I4_8) value = 8;
        else if (instruction.opcode == OpCodes.Ldc_I4_S) value = Convert.ToInt32(instruction.operand, CultureInfo.InvariantCulture);
        else if (instruction.opcode == OpCodes.Ldc_I4) value = Convert.ToInt32(instruction.operand, CultureInfo.InvariantCulture);
        else return false;
        return true;
    }
    private static int GetSpawnListDepth(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return 0;
        var depth = 0;
        var current = id;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            while (!string.IsNullOrWhiteSpace(current) && seen.Add(current) && depth < 32 &&
                   GameAccess.Sources.SpawnLists?.map != null && GameAccess.Sources.SpawnLists.map.TryGetValue(current, out var row) &&
                   !string.IsNullOrWhiteSpace(row.parent))
            {
                depth++;
                current = row.parent;
            }
        }
        catch { }
        return depth;
    }
}
