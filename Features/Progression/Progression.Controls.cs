using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private void SetExperienceMultiplierEnabled(bool enabled)
    {
        _modules.Progression.ExperienceMultiplierEnabled = enabled;
        _log = enabled
            ? T("经验倍率修改已开启", "Experience multiplier modifier enabled")
            : T("经验倍率修改已关闭", "Experience multiplier modifier disabled");
    }
    private void SetPlantHarvestMultiplierEnabled(bool enabled)
    {
        if (!_modules.PlantHarvestMultiplier.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("种植收获倍率已开启", "Plant harvest multiplier enabled")
            : T("种植收获倍率已关闭", "Plant harvest multiplier disabled");
    }
    private void SetIgnoreCropGrowthConditions(bool enabled)
    {
        if (!_modules.IgnoreCropGrowthConditions.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("无视作物生长条件已开启", "Ignore crop growth conditions enabled")
            : T("无视作物生长条件已关闭", "Ignore crop growth conditions disabled");
    }
    private void SetIgnoreEncumbrance(bool enabled)
    {
        if (!_modules.IgnoreEncumbrance.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("无视负重已开启", "Ignore encumbrance enabled")
            : T("无视负重已关闭", "Ignore encumbrance disabled");
    }
    private void SetAllFeatsLearnable(bool enabled)
    {
        if (!_modules.AllFeatsLearnable.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("全部专长可学习已开启", "All feats learnable enabled")
            : T("全部专长可学习已关闭", "All feats learnable disabled");
    }
    private void SetCharacterPanelGenes(bool enabled)
    {
        if (!_modules.CharacterPanelGenes.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("人物面板显示基因已开启", "Character panel gene display enabled")
            : T("人物面板显示基因已关闭", "Character panel gene display disabled");
    }
    private void SetAllowPcGeneImplant(bool enabled)
    {
        if (!_modules.AllowPcGeneImplant.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("允许PC植入基因已开启", "PC gene implantation enabled")
            : T("允许PC植入基因已关闭", "PC gene implantation disabled");
    }
    private void SetPredationGeneSelection(bool enabled)
    {
        if (!_modules.PredationGeneSelection.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("捕食技能自选基因已开启", "Devour gene selection enabled")
            : T("捕食技能自选基因已关闭", "Devour gene selection disabled");
    }
    private void SetAllowCurrencyGifts(bool enabled)
    {
        if (!_modules.AllowCurrencyGifts.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("允许货币赠礼已开启", "Currency gifts enabled")
            : T("允许货币赠礼已关闭", "Currency gifts disabled");
    }
    private bool TryApplyPlantHarvestMultiplierSettings(out string status)
    {
        if (!_modules.PlantHarvestMultiplier.TryApplyMultiplierTextFields())
        {
            status = T("倍率输入无效", "Invalid multiplier value");
            return false;
        }

        status = "";
        return true;
    }
    private void SetExperienceMultiplierIncludePcFaction(bool enabled)
    {
        _modules.Progression.ExperienceMultiplierIncludePcFaction = enabled;
    }
    private void SetFoodRestoresSpEnabled(bool enabled)
    {
        _modules.Progression.FoodRestoresSpEnabled = enabled;
        _log = enabled
            ? T("食用食物恢复SP已开启", "SP recovery from food enabled")
            : T("食用食物恢复SP已关闭", "SP recovery from food disabled");
    }
    private void SetFoodRestoresSpPercent(int value)
    {
        _modules.Progression.FoodRestoresSpPercent = Clamp(value, 1, 100);
    }
    private void SetDismantleAlwaysReturnsMaterials(bool enabled)
    {
        if (!_modules.GuaranteedGatheringRewards.SetDismantleAlwaysReturnsMaterials(enabled))
            return;
        _log = enabled
            ? T("分解必返还材料已开启", "Guaranteed dismantling material returns enabled")
            : T("分解必返还材料已关闭", "Guaranteed dismantling material returns disabled");
    }
    private void SetUseVanillaDismantleMechanism(bool enabled)
    {
        _modules.GuaranteedGatheringRewards.SetUseVanillaDismantleMechanism(enabled);
    }
    private void SetDismantlingAlwaysLearnsRecipe(bool enabled)
    {
        if (!_modules.GuaranteedGatheringRewards.SetDismantlingAlwaysLearnsRecipe(enabled))
            return;
        _log = enabled
            ? T("分解物品必获配方已开启", "Guaranteed dismantled-item recipe learning enabled")
            : T("分解物品必获配方已关闭", "Guaranteed dismantled-item recipe learning disabled");
    }
    private void SetOptimizeMeleeHitChance(bool enabled)
    {
        _modules.Progression.OptimizeMeleeHitChance = enabled;
        _log = enabled
            ? T("优化近战命中率逻辑已开启", "Optimized melee hit chance logic enabled")
            : T("优化近战命中率逻辑已关闭", "Optimized melee hit chance logic disabled");
    }
    private void SetOptimizeMeleeHitChanceIncludeParty(bool enabled)
    {
        _modules.Progression.OptimizeMeleeHitChanceIncludeParty = enabled;
    }
    private void SetPcFactionTrainerAllSkills(bool enabled)
    {
        _modules.Progression.PcFactionTrainerAllSkills = enabled;
        _log = enabled
            ? T("PC阵营训练师可训练全技能已开启", "PC-faction trainers now teach all skills")
            : T("PC阵营训练师可训练全技能已关闭", "PC-faction trainers restored to their original skill lists");
    }
    private bool TryApplyExperienceMultiplierSettings(out string status)
    {
        if (!_modules.Progression.TryApplyMultiplierTextFields())
        {
            status = T("??????", "Invalid multiplier value");
            return false;
        }

        status = "";
        return true;
    }
    private static bool TryParseExperienceMultiplier(string text, out float value) =>
        ProgressionModule.TryParseMultiplier(text, out value);
    private void SyncExperienceMultiplierTextFields() =>
        _modules.Progression.SyncTextFields();
    private static int ScalePositiveExperienceValue(int value, float multiplier) =>
        ProgressionModule.ScalePositiveValue(value, multiplier);
    private static bool IsExperienceMultiplierTarget(Card card) =>
        ActiveModules?.Progression.IsExperienceTarget(card) == true;
    private static bool IsMagicExperienceElement(Element element) =>
        ProgressionModule.IsMagicExperienceElement(element);
}
