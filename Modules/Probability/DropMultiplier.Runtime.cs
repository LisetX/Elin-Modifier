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
    private static string GetDynamicCharaSpawnListParent(SpawnList list)
    {
        const string instancePrefix = "instance_";
        var id = list.id ?? "";
        if (id.StartsWith(instancePrefix, StringComparison.Ordinal) && id.Length > instancePrefix.Length)
        {
            var parent = id.Substring(instancePrefix.Length);
            try
            {
                if ((GameAccess.Sources.SpawnLists?.map != null && GameAccess.Sources.SpawnLists.map.ContainsKey(parent)) ||
                    (SpawnList.allList != null && SpawnList.allList.ContainsKey(parent)))
                    return parent;
            }
            catch { }
        }
        return "chara";
    }
    private static void RecalculateSpawnListTotal(SpawnList list)
    {
        var total = 0L;
        for (var rowIndex = 0; rowIndex < list.rows.Count; rowIndex++)
            total += list.rows[rowIndex]?.chance ?? 0;
        list.totalChance = total > int.MaxValue ? int.MaxValue : total < int.MinValue ? int.MinValue : (int)total;
    }
    private void ApplyMiniGameProbabilityValues()
    {
        _miniGameProbability.SlotForcedWinPercent = Mathf.Clamp(_miniGameProbability.SlotForcedWinPercent, 0, 100);
        _miniGameProbability.ScratchMedalDenominator = ClampProbabilityDenominator(_miniGameProbability.ScratchMedalDenominator);
        _miniGameProbability.ScratchPlatinumDenominator = ClampProbabilityDenominator(_miniGameProbability.ScratchPlatinumDenominator);
        _miniGameProbability.ScratchFurnitureDenominator = ClampProbabilityDenominator(_miniGameProbability.ScratchFurnitureDenominator);
        _miniGameProbability.ScratchModelBoxDenominator = ClampProbabilityDenominator(_miniGameProbability.ScratchModelBoxDenominator);
        _miniGameProbability.ScratchFoodDenominator = ClampProbabilityDenominator(_miniGameProbability.ScratchFoodDenominator);
        _miniGameProbability.FortuneGrade1Denominator = ClampProbabilityDenominator(_miniGameProbability.FortuneGrade1Denominator);
        _miniGameProbability.FortuneGrade2Denominator = ClampProbabilityDenominator(_miniGameProbability.FortuneGrade2Denominator);
        _miniGameProbability.FortuneGrade3Denominator = ClampProbabilityDenominator(_miniGameProbability.FortuneGrade3Denominator);
        _miniGameProbability.GambleChestForcedSuccessDenominator = ClampProbabilityDenominator(_miniGameProbability.GambleChestForcedSuccessDenominator);
        _miniGameProbability.GambleChestForcedFailureDenominator = ClampProbabilityDenominator(_miniGameProbability.GambleChestForcedFailureDenominator);
        _miniGameProbability.GambleChestJackpotRange = ClampProbabilityDenominator(_miniGameProbability.GambleChestJackpotRange);

        try
        {
            var chances = FortuneRollData.chances;
            if (chances != null && chances.Length >= 4)
            {
                chances[1] = _miniGameProbability.FortuneGrade1Denominator;
                chances[2] = _miniGameProbability.FortuneGrade2Denominator;
                chances[3] = _miniGameProbability.FortuneGrade3Denominator;
            }
        }
        catch { }

        if (_miniGameProbability.SlotForcedWinPercent > 0)
            EnsureSlotProbabilityPatch();
    }
    private void ApplyDropMultiplierValues()
    {
        _dropMultiplier.QualityMultiplier = NormalizeDropMultiplier(_dropMultiplier.QualityMultiplier);
        _dropMultiplier.QuantityMultiplier = NormalizeDropMultiplier(_dropMultiplier.QuantityMultiplier);
    }
    private static float NormalizeDropMultiplier(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 1f;
        return Mathf.Clamp(value, 0f, 100f);
    }
    private bool HasActiveDropMultiplier()
    {
        return Math.Abs(_dropMultiplier.QualityMultiplier - 1f) > 0.0001f ||
               Math.Abs(_dropMultiplier.QuantityMultiplier - 1f) > 0.0001f;
    }
    private static int StochasticRoundDropValue(double value)
    {
        if (double.IsNaN(value) || value <= 0d)
            return 0;
        if (double.IsPositiveInfinity(value) || value >= int.MaxValue)
            return int.MaxValue;
        var whole = (int)Math.Floor(value);
        var fraction = value - whole;
        if (fraction > 0d && UnityEngine.Random.value < fraction)
            whole++;
        return whole;
    }
    private DropZoneAddState PrepareDropThing(Card card)
    {
        var state = default(DropZoneAddState);
        if (_dropMultiplierDepth <= 0 || _dropMultiplierReplaying || !(card is Thing thing))
            return state;

        state.Source = thing;
        var qualityMultiplier = NormalizeDropMultiplier(_dropMultiplier.QualityMultiplier);
        if (Math.Abs(qualityMultiplier - 1f) > 0.0001f && !thing.IsUnique)
        {
            try
            {
                var rarity = Math.Max(0, thing.rarityLv);
                var scaledQuality = StochasticRoundDropValue((rarity + 1d) * qualityMultiplier) - 1;
                thing.rarityLv = Mathf.Clamp(scaledQuality, 0, (int)Rarity.Artifact);
            }
            catch { }
        }

        var quantityMultiplier = NormalizeDropMultiplier(_dropMultiplier.QuantityMultiplier);
        if (Math.Abs(quantityMultiplier - 1f) <= 0.0001f)
            return state;

        try
        {
            var originalCount = Math.Max(1, thing.Num);
            var targetCount = StochasticRoundDropValue(originalCount * (double)quantityMultiplier);
            if (targetCount <= 0)
            {
                state.Skip = true;
                return state;
            }

            var canStack = originalCount > 1 || (thing.trait != null && thing.trait.CanStack);
            if (canStack)
            {
                thing.SetNum(targetCount);
            }
            else if (!thing.IsUnique)
            {
                state.ExtraCopies = Math.Max(0, targetCount - 1);
            }
        }
        catch { }
        return state;
    }
    private static void AddDropExtraCopies(Zone zone, Point point, DropZoneAddState state, Card? addedCard)
    {
        if (state.ExtraCopies <= 0 || state.Source == null || state.Source.IsUnique)
            return;
        var source = addedCard as Thing ?? state.Source;
        try
        {
            _dropMultiplierReplaying = true;
            for (var i = 0; i < state.ExtraCopies; i++)
            {
                var copy = source.Duplicate(1);
                if (copy != null)
                    zone.AddCard(copy, point);
            }
        }
        catch { }
        finally
        {
            _dropMultiplierReplaying = false;
        }
    }
    private static int ClampProbabilityDenominator(int value)
    {
        return Math.Max(1, value);
    }
}
