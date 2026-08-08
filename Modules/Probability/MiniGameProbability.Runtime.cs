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
    private void EnsureSlotProbabilityPatch()
    {
        if (_harmony == null)
            return;

        if (!_gambleChestProbabilityPatchInstalled)
        {
            try
            {
                var runMethod = AccessTools.Method(typeof(AI_OpenGambleChest), "Run");
                var moveNext = runMethod == null ? null : AccessTools.EnumeratorMoveNext(runMethod);
                var transpiler = AccessTools.Method(typeof(ProbabilityModule), nameof(GambleChestProbabilityTranspiler));
                if (moveNext != null && transpiler != null)
                {
                    _harmony.Patch(moveNext, transpiler: new HarmonyMethod(transpiler));
                    _gambleChestProbabilityPatchInstalled = true;
                }
            }
            catch { }
        }

        if (_slotProbabilityPatchInstalled)
            return;
        try
        {
            var slotType = FindLoadedTypeExact("CSFramework.CustomSlot");
            if (slotType == null)
                return;
            var startSpin = AccessTools.Method(slotType, "StartSpin", new[] { typeof(float) });
            var prefix = AccessTools.Method(typeof(ProbabilityModule), nameof(SlotStartSpinPrefix));
            if (startSpin == null || prefix == null)
                return;
            _harmony.Patch(startSpin, prefix: new HarmonyMethod(prefix));
            _slotProbabilityPatchInstalled = true;
        }
        catch { }
    }
    private static Type? FindLoadedTypeExact(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                    continue;
                try
                {
                    var type = assembly.GetType(fullName, false, false);
                    if (type != null)
                        return type;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }
    private static void SlotStartSpinPrefix(object __instance)
    {
        var module = ElinModifierPlugin.ActiveModules?.Probability;
        if (module == null || __instance == null)
            return;
        var chance = Mathf.Clamp(module._miniGameProbability.SlotForcedWinPercent, 0, 100);
        if (chance <= 0 || (chance < 100 && UnityEngine.Random.Range(0f, 100f) >= chance))
            return;
        module.TryForceSlotWin(__instance);
    }
    private void TryForceSlotWin(object slot)
    {
        try
        {
            var slotType = slot.GetType();
            var symbolManager = AccessTools.Field(slotType, "symbolManager")?.GetValue(slot);
            if (symbolManager == null)
                return;
            var symbols = AccessTools.Field(symbolManager.GetType(), "symbols")?.GetValue(symbolManager) as Array;
            if (symbols == null || symbols.Length == 0)
                return;
            var reels = AccessTools.Field(slotType, "reels")?.GetValue(slot) as Array;
            var reelCount = Math.Max(3, reels?.Length ?? 3);

            object? bestSymbol = null;
            var bestPay = int.MinValue;
            for (var i = 0; i < symbols.Length; i++)
            {
                var symbol = symbols.GetValue(i);
                if (symbol == null)
                    continue;
                var symbolType = symbol.GetType();
                var matchType = AccessTools.Field(symbolType, "matchType")?.GetValue(symbol)?.ToString() ?? "";
                if (string.Equals(matchType, "Scatter", StringComparison.OrdinalIgnoreCase))
                    continue;
                var pays = AccessTools.Field(symbolType, "pays")?.GetValue(symbol) as int[];
                if (pays == null || pays.Length == 0)
                    continue;
                var payIndex = Math.Min(reelCount, pays.Length) - 1;
                var pay = payIndex >= 0 ? pays[payIndex] : 0;
                if (pay <= 0)
                {
                    for (var payCursor = pays.Length - 1; payCursor >= 0; payCursor--)
                    {
                        if (pays[payCursor] <= 0)
                            continue;
                        pay = pays[payCursor];
                        break;
                    }
                }
                if (pay <= bestPay)
                    continue;
                bestPay = pay;
                bestSymbol = symbol;
            }
            if (bestSymbol == null)
                return;

            var selectedSymbolType = bestSymbol.GetType();
            var setManipulation = slotType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "SetManipulation", StringComparison.Ordinal))
                        return false;
                    var parameters = method.GetParameters();
                    return parameters.Length == 2 && parameters[0].ParameterType == typeof(int) &&
                           !parameters[1].ParameterType.IsArray && parameters[1].ParameterType.IsAssignableFrom(selectedSymbolType);
                });
            setManipulation?.Invoke(slot, new[] { (object)0, bestSymbol });
        }
        catch { }
    }
    private static int GetScratchMedalDenominator()
    {
        return ClampProbabilityDenominator(ElinModifierPlugin.ActiveModules?.Probability._miniGameProbability.ScratchMedalDenominator ?? 20);
    }
    private static int GetScratchPlatinumDenominator()
    {
        return ClampProbabilityDenominator(ElinModifierPlugin.ActiveModules?.Probability._miniGameProbability.ScratchPlatinumDenominator ?? 10);
    }
    private static int GetScratchFurnitureDenominator()
    {
        return ClampProbabilityDenominator(ElinModifierPlugin.ActiveModules?.Probability._miniGameProbability.ScratchFurnitureDenominator ?? 10);
    }
    private static int GetScratchModelBoxDenominator()
    {
        return ClampProbabilityDenominator(ElinModifierPlugin.ActiveModules?.Probability._miniGameProbability.ScratchModelBoxDenominator ?? 4);
    }
    private static int GetScratchFoodDenominator()
    {
        return ClampProbabilityDenominator(ElinModifierPlugin.ActiveModules?.Probability._miniGameProbability.ScratchFoodDenominator ?? 4);
    }
    private static int GetGambleChestForcedSuccessDenominator()
    {
        return ClampProbabilityDenominator(ElinModifierPlugin.ActiveModules?.Probability._miniGameProbability.GambleChestForcedSuccessDenominator ?? 20);
    }
    private static int GetGambleChestForcedFailureDenominator()
    {
        return ClampProbabilityDenominator(ElinModifierPlugin.ActiveModules?.Probability._miniGameProbability.GambleChestForcedFailureDenominator ?? 20);
    }
    private static int GetGambleChestJackpotRange()
    {
        return ClampProbabilityDenominator(ElinModifierPlugin.ActiveModules?.Probability._miniGameProbability.GambleChestJackpotRange ?? 10000);
    }
}
