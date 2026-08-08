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
    private void CaptureMiniGameProbabilityDefaults()
    {
        _miniGameProbability.SlotForcedWinPercent = 0;
        _miniGameProbability.ScratchMedalDenominator = 20;
        _miniGameProbability.ScratchPlatinumDenominator = 10;
        _miniGameProbability.ScratchFurnitureDenominator = 10;
        _miniGameProbability.ScratchModelBoxDenominator = 4;
        _miniGameProbability.ScratchFoodDenominator = 4;
        _miniGameProbability.GambleChestForcedSuccessDenominator = 20;
        _miniGameProbability.GambleChestForcedFailureDenominator = 20;
        _miniGameProbability.GambleChestJackpotRange = 10000;
        try
        {
            var chances = FortuneRollData.chances;
            if (chances != null && chances.Length >= 4)
            {
                _miniGameProbability.FortuneGrade1Denominator = Math.Max(1, chances[1]);
                _miniGameProbability.FortuneGrade2Denominator = Math.Max(1, chances[2]);
                _miniGameProbability.FortuneGrade3Denominator = Math.Max(1, chances[3]);
            }
        }
        catch
        {
            _miniGameProbability.FortuneGrade1Denominator = 8;
            _miniGameProbability.FortuneGrade2Denominator = 25;
            _miniGameProbability.FortuneGrade3Denominator = 60;
        }
    }
    private void AddMiniGameProbabilityEntries(ref int errorCount)
    {
        AddMiniGameProbabilityEntry("minigame.slot_extra_forced_win", "老虎机：额外强制中奖", "SlotForcedWinPercent", ProbabilityMemberLabelKind.Percent, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.scratch_medal_reward", "刮刮乐：奖章奖励", "ScratchMedalDenominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.scratch_platinum_reward", "刮刮乐：白金币奖励", "ScratchPlatinumDenominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.scratch_furniture_reward", "刮刮乐：家具奖励", "ScratchFurnitureDenominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.scratch_model_box_reward", "刮刮乐：塑像盒奖励", "ScratchModelBoxDenominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.scratch_food_reward", "刮刮乐：食物奖励", "ScratchFoodDenominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.fortune_first_prize", "幸运转盘：一等奖", "FortuneGrade3Denominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.fortune_second_prize", "幸运转盘：二等奖", "FortuneGrade2Denominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.fortune_third_prize", "幸运转盘：三等奖", "FortuneGrade1Denominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.gamble_chest_forced_success", "赌博宝箱：强制成功", "GambleChestForcedSuccessDenominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.gamble_chest_forced_failure", "赌博宝箱：强制失败", "GambleChestForcedFailureDenominator", ProbabilityMemberLabelKind.Denominator, ref errorCount);
        AddMiniGameProbabilityEntry("minigame.gamble_chest_jackpot_range", "赌博宝箱：大奖随机范围", "GambleChestJackpotRange", ProbabilityMemberLabelKind.RandomRange, ref errorCount);
    }
    private void AddMiniGameProbabilityEntry(
        string persistentId,
        string displayName,
        string fieldName,
        ProbabilityMemberLabelKind memberLabelKind,
        ref int errorCount)
    {
        try
        {
            var field = typeof(MiniGameProbabilityState).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                errorCount++;
                return;
            }
            var value = field.GetValue(_miniGameProbability);
            if (value == null)
            {
                errorCount++;
                return;
            }
            _probabilityEntries.Add(new ProbabilityEntry(
                "minigame",
                "MiniGame",
                "",
                T(displayName, GetMiniGameProbabilityEnglishName(fieldName)),
                _miniGameProbability,
                field,
                value,
                memberLabelKind,
                persistentId));
        }
        catch
        {
            errorCount++;
        }
    }
    private static string GetMiniGameProbabilityEnglishName(string fieldName)
    {
        switch (fieldName)
        {
            case "SlotForcedWinPercent": return "Slot machine: extra forced win";
            case "ScratchMedalDenominator": return "Scratch card: medal reward";
            case "ScratchPlatinumDenominator": return "Scratch card: platinum coin reward";
            case "ScratchFurnitureDenominator": return "Scratch card: furniture reward";
            case "ScratchModelBoxDenominator": return "Scratch card: model box reward";
            case "ScratchFoodDenominator": return "Scratch card: food reward";
            case "FortuneGrade3Denominator": return "Fortune roll: first prize";
            case "FortuneGrade2Denominator": return "Fortune roll: second prize";
            case "FortuneGrade1Denominator": return "Fortune roll: third prize";
            case "GambleChestForcedSuccessDenominator": return "Gamble chest: forced success";
            case "GambleChestForcedFailureDenominator": return "Gamble chest: forced failure";
            case "GambleChestJackpotRange": return "Gamble chest: jackpot random range";
            default: return fieldName;
        }
    }
    private void CaptureDropMultiplierDefaults()
    {
        _dropMultiplier.QualityMultiplier = 1f;
        _dropMultiplier.QuantityMultiplier = 1f;
    }
    private void AddDropMultiplierEntries(ref int errorCount)
    {
        AddDropMultiplierEntry(
            "drop.quality_multiplier",
            "品质倍率",
            "Quality multiplier",
            "QualityMultiplier",
            ref errorCount);
        AddDropMultiplierEntry(
            "drop.quantity_multiplier",
            "数量倍率",
            "Quantity multiplier",
            "QuantityMultiplier",
            ref errorCount);
    }
    private void AddDropMultiplierEntry(
        string persistentId,
        string displayName,
        string englishName,
        string fieldName,
        ref int errorCount)
    {
        try
        {
            var field = typeof(DropMultiplierState).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            var value = field?.GetValue(_dropMultiplier);
            if (field == null || value == null)
            {
                errorCount++;
                return;
            }
            _probabilityEntries.Add(new ProbabilityEntry(
                "drop",
                "DropMultiplier",
                "",
                T(displayName, englishName),
                _dropMultiplier,
                field,
                value,
                ProbabilityMemberLabelKind.Multiplier,
                persistentId));
        }
        catch
        {
            errorCount++;
        }
    }
}
