using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private bool GetLGuiFeatureValue(LGuiFeatureId id)
    {
        switch (id)
        {
            case LGuiFeatureId.AiInstruction: return _modules.AiInstruction.Enabled;
            case LGuiFeatureId.LowPerformance: return _lowPerformanceMode;
            case LGuiFeatureId.UnlockFrameRate: return _unlockFrameRate;
            case LGuiFeatureId.InvincibleMode: return _invincibleMode;
            case LGuiFeatureId.IgnoreBuffEffects: return _ignoreBuffEffects;
            case LGuiFeatureId.HostileThreatMarker: return _hostileThreatMarker;
            case LGuiFeatureId.ShowNpcMoreInfo: return _showNpcMoreInfo;
            case LGuiFeatureId.ShowItemMoreInfo: return _showItemMoreInfo;
            case LGuiFeatureId.ShowBuffSpecificValues: return _showBuffSpecificValues;
            case LGuiFeatureId.ShowItemPanelEnchantLevels: return _showItemPanelEnchantLevels;
            case LGuiFeatureId.ShowItemPanelItemValue: return _showItemPanelItemValue;
            case LGuiFeatureId.ShowItemPanelMilkBonus: return _showItemPanelMilkBonus;
            case LGuiFeatureId.ShowMainAbilityExperience: return _showMainAbilityExperience;
            case LGuiFeatureId.OneClickQuestCompletion: return _modules.OneClickQuestCompletion.Enabled;
            case LGuiFeatureId.EquipmentComparison: return _equipmentComparison;
            case LGuiFeatureId.IgnoreFriendlyFire: return _modules.CharacterProtection.IgnoreFriendlyFire;
            case LGuiFeatureId.WorkbenchIngredientReadingOptimization: return _workbenchIngredientReadingOptimization;
            case LGuiFeatureId.ExperienceMultiplier: return _modules.Progression.ExperienceMultiplierEnabled;
            case LGuiFeatureId.PlantHarvestMultiplier: return _modules.PlantHarvestMultiplier.Enabled;
            case LGuiFeatureId.IgnoreCropGrowthConditions: return _modules.IgnoreCropGrowthConditions.Enabled;
            case LGuiFeatureId.AllFeatsLearnable: return _modules.AllFeatsLearnable.Enabled;
            case LGuiFeatureId.CharacterPanelGenes: return _modules.CharacterPanelGenes.Enabled;
            case LGuiFeatureId.AllowPcGeneImplant: return _modules.AllowPcGeneImplant.Enabled;
            case LGuiFeatureId.PredationGeneSelection: return _modules.PredationGeneSelection.Enabled;
            case LGuiFeatureId.AllowCurrencyGifts: return _modules.AllowCurrencyGifts.Enabled;
            case LGuiFeatureId.FoodRestoresSp: return _modules.Progression.FoodRestoresSpEnabled;
            case LGuiFeatureId.DismantleAlwaysReturnsMaterials: return _modules.GuaranteedGatheringRewards.DismantleAlwaysReturnsMaterials;
            case LGuiFeatureId.DismantlingAlwaysLearnsRecipe: return _modules.GuaranteedGatheringRewards.DismantlingAlwaysLearnsRecipe;
            case LGuiFeatureId.OptimizeMeleeHitChance: return _modules.Progression.OptimizeMeleeHitChance;
            case LGuiFeatureId.PcFactionTrainerAllSkills: return _modules.Progression.PcFactionTrainerAllSkills;
            case LGuiFeatureId.UnlimitedHomeResidentCap: return _unlimitedHomeResidentCap;
            case LGuiFeatureId.UnlimitedPartyMemberCap: return _unlimitedPartyMemberCap;
            case LGuiFeatureId.UnlimitedOfferingFaithPoints: return _unlimitedOfferingFaithPoints;
            case LGuiFeatureId.IgnoreGodArtifactFaithRequirement: return _ignoreGodArtifactFaithRequirement;
            case LGuiFeatureId.ShrineEffectSelection: return _shrineEffectSelection;
            case LGuiFeatureId.InfiniteChargeAndAmmo: return _infiniteChargeAndAmmo;
            case LGuiFeatureId.RodStacking: return _rodStacking;
            case LGuiFeatureId.RightClickInterruptOperation: return _modules.RightClickInterrupt.Enabled;
            case LGuiFeatureId.StealHandNoTargetLimit: return _stealHandNoTargetLimit;
            case LGuiFeatureId.StealHandUndetectable: return _stealHandUndetectable;
            case LGuiFeatureId.MerchantRefreshNoCost: return _modules.MerchantRefreshNoCost.Enabled;
            case LGuiFeatureId.MerchantAlwaysStocksMonsterBall: return _modules.MerchantMonsterBall.Enabled;
            case LGuiFeatureId.MerchantMonsterBallLevelOptimization: return _modules.MerchantMonsterBall.LevelOptimizationEnabled;
            case LGuiFeatureId.IgnoreSpecialNpcHatchRestriction: return _modules.SpecialNpcHatch.IgnoreRestriction;
            case LGuiFeatureId.IgnoreSpecialNpcCaptureRestriction: return _modules.SpecialNpcCapture.IgnoreRestriction;
            case LGuiFeatureId.AffinityOnlyIncrease: return _modules.CharacterProtection.AffinityOnlyIncrease;
            case LGuiFeatureId.KarmaOnlyIncrease: return _modules.CharacterProtection.KarmaOnlyIncrease;
            case LGuiFeatureId.AttackCannotBeInterrupted: return _modules.CharacterProtection.AttackCannotBeInterrupted;
            case LGuiFeatureId.FishingNoWait: return _modules.FishingNoWait.Enabled;
            case LGuiFeatureId.GeneSynthesisNoWait: return _modules.GeneSynthesisNoWait.Enabled;
            case LGuiFeatureId.SleepWithoutSleepiness: return _modules.SleepWithoutSleepiness.Enabled;
            case LGuiFeatureId.AllPurposeWorkbench: return _modules.AllPurposeWorkbench.Enabled;
            case LGuiFeatureId.InfiniteSight: return _infinitePlayerSight;
            case LGuiFeatureId.ShowFoodRot: return _showFoodRot;
            case LGuiFeatureId.IgnoreFoodDecay: return _ignoreFoodDecay;
            case LGuiFeatureId.NoCraftMaterials: return _noCraftMaterials;
            case LGuiFeatureId.UnlockCraftMaterials: return _unlockAllCraftMaterials;
            case LGuiFeatureId.UnlockCraftRecipes: return _unlockAllCraftRecipes;
            case LGuiFeatureId.CustomItemAmount: return _customItemAmount;
            case LGuiFeatureId.CustomItemData: return _customItemEditor;
            case LGuiFeatureId.CustomFoodData: return _customFoodEditor;
            case LGuiFeatureId.CustomWeaponData: return _customWeaponEditor;
            case LGuiFeatureId.CustomGeneData: return _customGeneEditor;
            case LGuiFeatureId.StethoscopeNoLimit: return _stethoscopeNoTargetLimit;
            case LGuiFeatureId.IgnoreTerrain: return _ignoreTerrainMovement;
            case LGuiFeatureId.OptimizeVoid: return _optimizeDungeonVoidScaling;
            case LGuiFeatureId.NoTalkInterestLoss: return _noTalkInterestLoss;
            case LGuiFeatureId.KillGrowth: return _killGrowthEnabled;
            default: return false;
        }
    }
    private static bool CanConfigureLGuiFeature(LGuiFeatureId id)
    {
        return id == LGuiFeatureId.InvincibleMode || id == LGuiFeatureId.IgnoreBuffEffects ||
               id == LGuiFeatureId.HostileThreatMarker ||
               id == LGuiFeatureId.ShowNpcMoreInfo || id == LGuiFeatureId.ShowItemMoreInfo ||
               id == LGuiFeatureId.ShowBuffSpecificValues || id == LGuiFeatureId.ShowMainAbilityExperience ||
               id == LGuiFeatureId.ExperienceMultiplier ||
               id == LGuiFeatureId.PlantHarvestMultiplier ||
               id == LGuiFeatureId.FoodRestoresSp ||
               id == LGuiFeatureId.DismantleAlwaysReturnsMaterials ||
               id == LGuiFeatureId.OptimizeMeleeHitChance ||
               id == LGuiFeatureId.AttackCannotBeInterrupted || id == LGuiFeatureId.AllPurposeWorkbench ||
               id == LGuiFeatureId.KillGrowth;
    }
    private void OpenLGuiFeatureConfiguration(LGuiFeatureId id)
    {
        var titleText = id == LGuiFeatureId.InvincibleMode
            ? T("无敌模式", "Invincible mode")
            : id == LGuiFeatureId.KillGrowth
            ? T("击杀成长", "Kill growth")
            : id == LGuiFeatureId.ShowItemMoreInfo
                ? T("显示物品更多信息", "Show more item info")
                : T("显示NPC更多信息", "Show more NPC info");
        if (id == LGuiFeatureId.IgnoreBuffEffects)
            titleText = T("无视Buff效果", "Ignore Buff effects");
        if (id == LGuiFeatureId.HostileThreatMarker)
            titleText = T("敌对威胁标记", "Hostile threat marker");
        if (id == LGuiFeatureId.ShowBuffSpecificValues)
            titleText = T("显示Buff具体信息", "Show detailed Buff information");
        if (id == LGuiFeatureId.ShowMainAbilityExperience)
            titleText = T("显示主能力经验值", "Show main ability experience");
        if (id == LGuiFeatureId.ExperienceMultiplier)
            titleText = T("经验倍率修改", "Experience multiplier modifier");
        if (id == LGuiFeatureId.PlantHarvestMultiplier)
            titleText = T("种植收获倍率", "Plant harvest multiplier");
        if (id == LGuiFeatureId.FoodRestoresSp)
            titleText = T("食用食物恢复SP", "Restore SP by eating food");
        if (id == LGuiFeatureId.DismantleAlwaysReturnsMaterials)
            titleText = T("分解必返还材料", "Dismantling always returns materials");
        if (id == LGuiFeatureId.OptimizeMeleeHitChance)
            titleText = T("优化近战命中率逻辑", "Optimize melee hit chance logic");
        if (id == LGuiFeatureId.AttackCannotBeInterrupted)
            titleText = T("攻击不会被打断", "Attacks cannot be interrupted");
        if (id == LGuiFeatureId.AllPurposeWorkbench)
            titleText = T("全能制作台", "All-purpose workbench");
        var modalHeight = id == LGuiFeatureId.InvincibleMode ? 300f :
            id == LGuiFeatureId.HostileThreatMarker ? 300f :
            id == LGuiFeatureId.ShowBuffSpecificValues ? 300f :
            id == LGuiFeatureId.ShowMainAbilityExperience ? 300f :
            id == LGuiFeatureId.FoodRestoresSp ? 300f :
            id == LGuiFeatureId.DismantleAlwaysReturnsMaterials ? 300f :
            id == LGuiFeatureId.OptimizeMeleeHitChance ? 300f :
            id == LGuiFeatureId.AttackCannotBeInterrupted ? 300f :
            id == LGuiFeatureId.AllPurposeWorkbench ? 300f :
            id == LGuiFeatureId.PlantHarvestMultiplier ? 360f :
            id == LGuiFeatureId.ExperienceMultiplier ? 648f :
            id == LGuiFeatureId.IgnoreBuffEffects ? 430f :
            id == LGuiFeatureId.KillGrowth ? 900f : id == LGuiFeatureId.ShowItemMoreInfo ? 760f : 900f;
        var modal = CreateLGuiCompleteModal("RuntimeFeatureConfiguration", titleText, out var content, 1260f, modalHeight);
        if (modal == null)
            return;
        _lGuiModalRestoreMainOnClose = true;

        if (id == LGuiFeatureId.DismantleAlwaysReturnsMaterials)
        {
            CreateLGuiToggleControl(
                content,
                T(
                    "原版分解机制(适配万物炼金)",
                    "Vanilla dismantling mechanism (Everything Alchemy compatibility)"),
                _modules.GuaranteedGatheringRewards.UseVanillaDismantleMechanism,
                10f,
                SetUseVanillaDismantleMechanism);
            content.sizeDelta = new Vector2(0f, 90f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.ShowMainAbilityExperience)
        {
            CreateLGuiToggleControl(
                content,
                T("是否在技能追踪器显示", "Show in skill tracker"),
                _showMainAbilityExperienceInSkillTracker,
                10f,
                SetShowMainAbilityExperienceInSkillTracker);
            content.sizeDelta = new Vector2(0f, 90f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.HostileThreatMarker)
        {
            CreateLGuiToggleControl(
                content,
                T("行为预测", "Behavior prediction"),
                _hostileThreatBehaviorPrediction,
                10f,
                SetHostileThreatBehaviorPrediction);
            CreateLGuiToggleControl(
                content,
                T(
                    "预决策锁定(牺牲AI自主行动灵活性,大幅提高行动可预测性)",
                    "Pre-decision lock (reduces AI flexibility; greatly improves action predictability)"),
                _hostileThreatPredecisionLock,
                68f,
                SetHostileThreatPredecisionLock);
            content.sizeDelta = new Vector2(0f, 148f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.InvincibleMode)
        {
            CreateLGuiToggleControl(content, T("是否对队伍内队友使用", "Apply to party members"), _invincibleModeIncludeParty, 10f, SetInvincibleModeIncludeParty);
            content.sizeDelta = new Vector2(0f, 90f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.IgnoreBuffEffects)
        {
            CreateLGuiToggleControl(content, T("影响范围:Debuff", "Scope: Debuff"), _ignoreBuffEffectsDebuff, 10f, SetIgnoreBuffEffectsDebuff);
            CreateLGuiToggleControl(content, T("影响范围:Buff", "Scope: Buff"), _ignoreBuffEffectsBuff, 68f, SetIgnoreBuffEffectsBuff);
            CreateLGuiToggleControl(content, T("是否对队伍内队友生效", "Apply to party members"), _ignoreBuffEffectsIncludeParty, 126f, SetIgnoreBuffEffectsIncludeParty);
            content.sizeDelta = new Vector2(0f, 206f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.ShowBuffSpecificValues)
        {
            var iconFontSizeLabel = CreateLGuiText(content, "BuffSpecificInfoIconFontSizeLabel", T("图标型字体大小", "Icon font size"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(iconFontSizeLabel.rectTransform, 0f, 10f, 180f, 48f);
            var iconFontSizeValue = CreateLGuiText(content, "BuffSpecificInfoIconFontSizeValue", _showBuffSpecificValuesIconFontSizeOffset.ToString(CultureInfo.InvariantCulture), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(iconFontSizeValue.rectTransform, 510f, 10f, 90f, 48f);
            var iconFontSizeSlider = CreateLGuiSlider(content, "BuffSpecificInfoIconFontSizeSlider", 190f, 21f, 300f, 26f, -8f, 8f, _showBuffSpecificValuesIconFontSizeOffset);
            iconFontSizeSlider.wholeNumbers = true;
            iconFontSizeSlider.onValueChanged.AddListener(value =>
            {
                SetShowBuffSpecificValuesIconFontSizeOffset(Mathf.RoundToInt(value));
                iconFontSizeValue.text = _showBuffSpecificValuesIconFontSizeOffset.ToString(CultureInfo.InvariantCulture);
            });

            var textFontSizeLabel = CreateLGuiText(content, "BuffSpecificInfoTextFontSizeLabel", T("文字型字体大小", "Text font size"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(textFontSizeLabel.rectTransform, 0f, 68f, 180f, 48f);
            var textFontSizeValue = CreateLGuiText(content, "BuffSpecificInfoTextFontSizeValue", _showBuffSpecificValuesTextFontSizeOffset.ToString(CultureInfo.InvariantCulture), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(textFontSizeValue.rectTransform, 510f, 68f, 90f, 48f);
            var textFontSizeSlider = CreateLGuiSlider(content, "BuffSpecificInfoTextFontSizeSlider", 190f, 79f, 300f, 26f, -8f, 8f, _showBuffSpecificValuesTextFontSizeOffset);
            textFontSizeSlider.wholeNumbers = true;
            textFontSizeSlider.onValueChanged.AddListener(value =>
            {
                SetShowBuffSpecificValuesTextFontSizeOffset(Mathf.RoundToInt(value));
                textFontSizeValue.text = _showBuffSpecificValuesTextFontSizeOffset.ToString(CultureInfo.InvariantCulture);
            });
            content.sizeDelta = new Vector2(0f, 148f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.ExperienceMultiplier)
        {
            CreateLGuiToggleControl(content, T("是否对队友生效", "Apply to PC-faction allies"),
                _modules.Progression.ExperienceMultiplierIncludePcFaction, 10f, SetExperienceMultiplierIncludePcFaction);
            AddLGuiBoundInput(content, T("角色等级经验倍率", "Character level EXP multiplier"),
                () => _modules.Progression.CharacterLevelExperienceMultiplierText,
                value => _modules.Progression.CharacterLevelExperienceMultiplierText = value ?? "1", 68f, 360f);
            AddLGuiBoundInput(content, T("主能力经验倍率", "Main ability EXP multiplier"),
                () => _modules.Progression.MainAbilityExperienceMultiplierText,
                value => _modules.Progression.MainAbilityExperienceMultiplierText = value ?? "1", 126f, 360f);
            AddLGuiBoundInput(content, T("技能经验倍率", "Skill EXP multiplier"),
                () => _modules.Progression.SkillExperienceMultiplierText,
                value => _modules.Progression.SkillExperienceMultiplierText = value ?? "1", 184f, 360f);
            AddLGuiBoundInput(content, T("魔法经验倍率", "Magic EXP multiplier"),
                () => _modules.Progression.MagicExperienceMultiplierText,
                value => _modules.Progression.MagicExperienceMultiplierText = value ?? "1", 242f, 360f);
            AddLGuiBoundInput(content, T("潜力获取倍率(食物来源)", "Potential gain multiplier (food)"),
                () => _modules.Progression.FoodPotentialGainMultiplierText,
                value => _modules.Progression.FoodPotentialGainMultiplierText = value ?? "1", 300f, 360f);
            AddLGuiBoundInput(content, T("潜力获取倍率(训练来源)", "Potential gain multiplier (training)"),
                () => _modules.Progression.TrainingPotentialGainMultiplierText,
                value => _modules.Progression.TrainingPotentialGainMultiplierText = value ?? "1", 358f, 360f);
            var statusText = CreateLGuiText(content, "ExperienceMultiplierStatus", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(statusText.rectTransform, 180f, 426f, 760f, 48f);
            CreateLGuiButton(content, "ApplyExperienceMultipliers", T("应用", "Apply"), 0f, 426f, 150f, 48f, () =>
            {
                string status;
                TryApplyExperienceMultiplierSettings(out status);
                statusText.text = status;
            });
            content.sizeDelta = new Vector2(0f, 496f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.PlantHarvestMultiplier)
        {
            AddLGuiBoundInput(content, T("作物收获倍率", "Crop harvest multiplier"),
                () => _modules.PlantHarvestMultiplier.CropHarvestMultiplierText,
                value => _modules.PlantHarvestMultiplier.CropHarvestMultiplierText = value ?? "1",
                10f,
                360f);
            AddLGuiBoundInput(content, T("种子收割倍率", "Seed reaping multiplier"),
                () => _modules.PlantHarvestMultiplier.SeedReapingMultiplierText,
                value => _modules.PlantHarvestMultiplier.SeedReapingMultiplierText = value ?? "1",
                68f,
                360f);
            var statusText = CreateLGuiText(
                content,
                "PlantHarvestMultiplierStatus",
                "",
                16,
                TextAnchor.MiddleLeft,
                FontStyle.Normal);
            PlaceLGuiRect(statusText.rectTransform, 180f, 136f, 760f, 48f);
            CreateLGuiButton(
                content,
                "ApplyPlantHarvestMultipliers",
                T("应用", "Apply"),
                0f,
                136f,
                150f,
                48f,
                () =>
                {
                    string status;
                    TryApplyPlantHarvestMultiplierSettings(out status);
                    statusText.text = status;
                });
            content.sizeDelta = new Vector2(0f, 206f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.FoodRestoresSp)
        {
            var percentLabel = CreateLGuiText(content, "FoodRestoresSpPercentLabel",
                T("单个食物恢复SP百分比(%)", "SP restored per food (%)"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(percentLabel.rectTransform, 0f, 10f, 300f, 48f);
            var percentValue = CreateLGuiText(content, "FoodRestoresSpPercentValue",
                _modules.Progression.FoodRestoresSpPercent.ToString(CultureInfo.InvariantCulture), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(percentValue.rectTransform, 650f, 10f, 90f, 48f);
            var percentSlider = CreateLGuiSlider(content, "FoodRestoresSpPercentSlider",
                310f, 21f, 320f, 26f, 1f, 100f, _modules.Progression.FoodRestoresSpPercent);
            percentSlider.wholeNumbers = true;
            percentSlider.onValueChanged.AddListener(value =>
            {
                SetFoodRestoresSpPercent(Mathf.RoundToInt(value));
                percentValue.text = _modules.Progression.FoodRestoresSpPercent.ToString(CultureInfo.InvariantCulture);
            });
            content.sizeDelta = new Vector2(0f, 90f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.OptimizeMeleeHitChance)
        {
            CreateLGuiToggleControl(content, T("是否对队友生效", "Apply to party members"),
                _modules.Progression.OptimizeMeleeHitChanceIncludeParty, 10f, SetOptimizeMeleeHitChanceIncludeParty);
            content.sizeDelta = new Vector2(0f, 90f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.AttackCannotBeInterrupted)
        {
            CreateLGuiToggleControl(content, T("是否对队友生效", "Apply to party members"),
                _modules.CharacterProtection.AttackCannotBeInterruptedIncludeParty, 10f, SetAttackCannotBeInterruptedIncludeParty);
            content.sizeDelta = new Vector2(0f, 90f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.AllPurposeWorkbench)
        {
            var label = CreateLGuiText(
                content,
                "AllPurposeWorkbenchDefaultTabTypeLabel",
                T("默认全能工作台标签类型", "Default all-purpose workbench tab type"),
                18,
                TextAnchor.MiddleLeft,
                FontStyle.Normal);
            PlaceLGuiRect(label.rectTransform, 0f, 10f, 290f, 48f);
            CreateAutomationDropdown(
                content,
                "AllPurposeWorkbenchDefaultTabType",
                new[]
                {
                    T("物品分类", "Item category"),
                    T("工作台", "Workbench")
                },
                _modules.AllPurposeWorkbench.DefaultByWorkbench ? 1 : 0,
                300f,
                10f,
                420f,
                48f,
                optionIndex => _modules.AllPurposeWorkbench.SetDefaultByWorkbench(optionIndex == 1));
            content.sizeDelta = new Vector2(0f, 90f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.ShowItemMoreInfo)
        {
            SyncItemMoreInfoColorInputs();
            CreateLGuiToggleControl(content, T("基础信息", "Basic info"), _showItemMoreInfoBasicInfo, 10f, value => _showItemMoreInfoBasicInfo = value);
            CreateLGuiToggleControl(content, T("采集物采集门槛", "Gathering requirements"), _showItemMoreInfoGatheringThreshold, 68f, value => _showItemMoreInfoGatheringThreshold = value);
            CreateLGuiToggleControl(content, T("武器属性", "Weapon stats"), _showItemMoreInfoWeaponStats, 126f, value => _showItemMoreInfoWeaponStats = value);
            CreateLGuiToggleControl(content, T("附魔内容", "Enchantments"), _showItemMoreInfoEnchantments, 184f, value => _showItemMoreInfoEnchantments = value);
            CreateLGuiToggleControl(content, T("种植作物属性", "Planted crop stats"), _showItemMoreInfoPlantStats, 242f, value => _showItemMoreInfoPlantStats = value);
            CreateLGuiToggleControl(content, T("种植作物属性拓展", "Extended planted crop stats"), _showItemMoreInfoPlantStatsExtended, 300f, value => _showItemMoreInfoPlantStatsExtended = value);
            var fontSizeLabel = CreateLGuiText(content, "ItemMoreInfoFontSizeLabel", T("字体大小", "Font size"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(fontSizeLabel.rectTransform, 0f, 358f, 120f, 48f);
            var fontSizeValue = CreateLGuiText(content, "ItemMoreInfoFontSizeValue", _showItemMoreInfoFontSizeOffset.ToString(CultureInfo.InvariantCulture), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(fontSizeValue.rectTransform, 510f, 358f, 90f, 48f);
            var fontSizeSlider = CreateLGuiSlider(content, "ItemMoreInfoFontSizeSlider", 130f, 369f, 360f, 26f, -8f, 8f, _showItemMoreInfoFontSizeOffset);
            fontSizeSlider.wholeNumbers = true;
            fontSizeSlider.onValueChanged.AddListener(value =>
            {
                SetItemMoreInfoFontSizeOffset(Mathf.RoundToInt(value));
                fontSizeValue.text = _showItemMoreInfoFontSizeOffset.ToString(CultureInfo.InvariantCulture);
            });
            var colorLabel = CreateLGuiText(content, "ItemMoreInfoColorLabel", T("字体颜色", "Font color"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(colorLabel.rectTransform, 0f, 416f, 180f, 42f);
            AddLGuiInlineInput(content, T("基础信息", "Basic info"), () => _itemMoreInfoBasicInfoColorText, value => _itemMoreInfoBasicInfoColorText = value, 0f, 462f, 150f, 220f);
            AddLGuiInlineInput(content, T("武器属性", "Weapon stats"), () => _itemMoreInfoWeaponStatsColorText, value => _itemMoreInfoWeaponStatsColorText = value, 620f, 462f, 150f, 220f);
            AddLGuiInlineInput(content, T("采集工具", "Gathering tool"), () => _itemMoreInfoGatheringToolColorText, value => _itemMoreInfoGatheringToolColorText = value, 0f, 512f, 150f, 220f);
            AddLGuiInlineInput(content, T("采集门槛", "Gathering threshold"), () => _itemMoreInfoGatheringThresholdColorText, value => _itemMoreInfoGatheringThresholdColorText = value, 620f, 512f, 150f, 220f);
            AddLGuiInlineInput(content, T("附魔内容", "Enchantments"), () => _itemMoreInfoEnchantColorText, value => _itemMoreInfoEnchantColorText = value, 0f, 562f, 150f, 220f);
            AddLGuiInlineInput(content, T("种植作物属性", "Planted crop stats"), () => _itemMoreInfoPlantStatsColorText, value => _itemMoreInfoPlantStatsColorText = value, 620f, 562f, 150f, 220f);
            var rarityColorLabel = CreateLGuiText(content, "ItemMoreInfoRarityColorLabel", T("稀有度颜色", "Rarity colors"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(rarityColorLabel.rectTransform, 0f, 612f, 180f, 42f);
            AddLGuiInlineInput(content, T("低级", "Poor"), () => _itemMoreInfoRarityCrudeColorText, value => _itemMoreInfoRarityCrudeColorText = value, 0f, 658f, 150f, 220f);
            AddLGuiInlineInput(content, T("普通", "Standard"), () => _itemMoreInfoRarityNormalColorText, value => _itemMoreInfoRarityNormalColorText = value, 620f, 658f, 150f, 220f);
            AddLGuiInlineInput(content, T("高级", "Superior"), () => _itemMoreInfoRaritySuperiorColorText, value => _itemMoreInfoRaritySuperiorColorText = value, 0f, 708f, 150f, 220f);
            AddLGuiInlineInput(content, T("奇迹", "Miracle"), () => _itemMoreInfoRarityLegendaryColorText, value => _itemMoreInfoRarityLegendaryColorText = value, 620f, 708f, 150f, 220f);
            AddLGuiInlineInput(content, T("神器", "Godly"), () => _itemMoreInfoRarityMythicalColorText, value => _itemMoreInfoRarityMythicalColorText = value, 0f, 758f, 150f, 220f);
            AddLGuiInlineInput(content, T("古遗物", "Artifact"), () => _itemMoreInfoRarityArtifactColorText, value => _itemMoreInfoRarityArtifactColorText = value, 620f, 758f, 150f, 220f);
            var colorStatus = CreateLGuiText(content, "ItemMoreInfoColorStatus", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(colorStatus.rectTransform, 240f, 814f, 760f, 44f);
            CreateLGuiButton(content, "ApplyItemMoreInfoColors", T("应用", "Apply"), 0f, 814f, 105f, 44f, () =>
            {
                TryApplyItemMoreInfoColors(out var status);
                colorStatus.text = status;
            });
            CreateLGuiButton(content, "ResetItemMoreInfoColors", T("重置", "Reset"), 118f, 814f, 105f, 44f, () =>
            {
                ResetItemMoreInfoColors();
                OpenLGuiFeatureConfiguration(LGuiFeatureId.ShowItemMoreInfo);
            });
            content.sizeDelta = new Vector2(0f, 878f);
            ApplyLGuiVisualSettings();
            return;
        }

        if (id == LGuiFeatureId.ShowNpcMoreInfo)
        {
            SyncNpcMoreInfoColorInputs();
            var npcMoreInfoOrder = GetNpcMoreInfoOrder();
            for (var orderIndex = 0; orderIndex < npcMoreInfoOrder.Length; orderIndex++)
                CreateLGuiNpcMoreInfoSortableRow(content, npcMoreInfoOrder[orderIndex], orderIndex, 10f + orderIndex * 58f);
            var npcMoreInfoSettingsY = 10f + npcMoreInfoOrder.Length * 58f;
            var fontSizeLabel = CreateLGuiText(content, "NpcMoreInfoFontSizeLabel", T("字体大小", "Font size"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(fontSizeLabel.rectTransform, 0f, npcMoreInfoSettingsY, 120f, 48f);
            var fontSizeValue = CreateLGuiText(content, "NpcMoreInfoFontSizeValue", _showNpcMoreInfoFontSizeOffset.ToString(CultureInfo.InvariantCulture), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(fontSizeValue.rectTransform, 510f, npcMoreInfoSettingsY, 90f, 48f);
            var fontSizeSlider = CreateLGuiSlider(content, "NpcMoreInfoFontSizeSlider", 130f, npcMoreInfoSettingsY + 11f, 360f, 26f, -8f, 8f, _showNpcMoreInfoFontSizeOffset);
            fontSizeSlider.wholeNumbers = true;
            fontSizeSlider.onValueChanged.AddListener(value =>
            {
                SetNpcMoreInfoFontSizeOffset(Mathf.RoundToInt(value));
                fontSizeValue.text = _showNpcMoreInfoFontSizeOffset.ToString(CultureInfo.InvariantCulture);
            });
            var colorLabel = CreateLGuiText(content, "NpcMoreInfoColorLabel", T("字体颜色", "Font color"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(colorLabel.rectTransform, 0f, npcMoreInfoSettingsY + 58f, 180f, 42f);
            AddLGuiInlineInput(content, T("身份信息", "Identity"), () => _npcMoreInfoIdentityColorText, value => _npcMoreInfoIdentityColorText = value, 0f, npcMoreInfoSettingsY + 104f, 150f, 220f);
            AddLGuiInlineInput(content, T("更多身份信息", "Additional identity info"), () => _npcMoreInfoRelationColorText, value => _npcMoreInfoRelationColorText = value, 620f, npcMoreInfoSettingsY + 104f, 210f, 160f);
            AddLGuiInlineInput(content, T("等级", "Level"), () => _npcMoreInfoLevelColorText, value => _npcMoreInfoLevelColorText = value, 0f, npcMoreInfoSettingsY + 154f, 150f, 220f);
            AddLGuiInlineInput(content, "HP", () => _npcMoreInfoHpColorText, value => _npcMoreInfoHpColorText = value, 620f, npcMoreInfoSettingsY + 154f, 150f, 220f);
            AddLGuiInlineInput(content, "MP", () => _npcMoreInfoMpColorText, value => _npcMoreInfoMpColorText = value, 0f, npcMoreInfoSettingsY + 204f, 150f, 220f);
            AddLGuiInlineInput(content, "SP", () => _npcMoreInfoSpColorText, value => _npcMoreInfoSpColorText = value, 620f, npcMoreInfoSettingsY + 204f, 150f, 220f);
            AddLGuiInlineInput(content, "EXP", () => _npcMoreInfoExpColorText, value => _npcMoreInfoExpColorText = value, 0f, npcMoreInfoSettingsY + 254f, 150f, 220f);
            AddLGuiInlineInput(content, T("速度", "Speed"), () => _npcMoreInfoSpeedColorText, value => _npcMoreInfoSpeedColorText = value, 620f, npcMoreInfoSettingsY + 254f, 150f, 220f);
            AddLGuiInlineInput(content, "DV", () => _npcMoreInfoDvColorText, value => _npcMoreInfoDvColorText = value, 0f, npcMoreInfoSettingsY + 304f, 150f, 220f);
            AddLGuiInlineInput(content, "PV", () => _npcMoreInfoPvColorText, value => _npcMoreInfoPvColorText = value, 620f, npcMoreInfoSettingsY + 304f, 150f, 220f);
            AddLGuiInlineInput(content, T("技能", "Skills"), () => _npcMoreInfoSkillColorText, value => _npcMoreInfoSkillColorText = value, 0f, npcMoreInfoSettingsY + 354f, 150f, 220f);
            AddLGuiInlineInput(content, T("能力", "Abilities"), () => _npcMoreInfoAbilityColorText, value => _npcMoreInfoAbilityColorText = value, 620f, npcMoreInfoSettingsY + 354f, 150f, 220f);
            AddLGuiInlineInput(content, T("专长", "Feats"), () => _npcMoreInfoFeatColorText, value => _npcMoreInfoFeatColorText = value, 0f, npcMoreInfoSettingsY + 404f, 150f, 220f);
            AddLGuiInlineInput(content, T("交战推演", "Combat Simulation"), () => _npcMoreInfoCombatColorText, value => _npcMoreInfoCombatColorText = value, 620f, npcMoreInfoSettingsY + 404f, 150f, 220f);
            AddLGuiInlineInput(content, T("抗性", "Resistances"), () => _npcMoreInfoResistColorText, value => _npcMoreInfoResistColorText = value, 0f, npcMoreInfoSettingsY + 454f, 150f, 220f);
            AddLGuiInlineInput(content, T("主属性", "Main Attributes"), () => _npcMoreInfoAttributeColorText, value => _npcMoreInfoAttributeColorText = value, 620f, npcMoreInfoSettingsY + 454f, 150f, 220f);
            AddLGuiInlineInput(content, "Buff", () => _npcMoreInfoBuffColorText, value => _npcMoreInfoBuffColorText = value, 0f, npcMoreInfoSettingsY + 504f, 150f, 220f);
            var colorStatus = CreateLGuiText(content, "NpcMoreInfoColorStatus", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(colorStatus.rectTransform, 240f, npcMoreInfoSettingsY + 560f, 760f, 44f);
            CreateLGuiButton(content, "ApplyNpcMoreInfoColors", T("应用", "Apply"), 0f, npcMoreInfoSettingsY + 560f, 105f, 44f, () =>
            {
                TryApplyNpcMoreInfoColors(out var status);
                colorStatus.text = status;
            });
            CreateLGuiButton(content, "ResetNpcMoreInfoColors", T("重置", "Reset"), 118f, npcMoreInfoSettingsY + 560f, 105f, 44f, () =>
            {
                ResetNpcMoreInfoColors();
                OpenLGuiFeatureConfiguration(LGuiFeatureId.ShowNpcMoreInfo);
            });
            content.sizeDelta = new Vector2(0f, npcMoreInfoSettingsY + 624f);
            ApplyLGuiVisualSettings();
            return;
        }

        CreateLGuiToggleControl(content, T("共享经验", "Shared EXP"), _killGrowthSharedExperience, 10f, SetKillGrowthSharedExperience);
        AddLGuiBoundInput(content, T("每级所需经验", "EXP per level"), () => _killGrowthExpPerLevelText, value =>
        {
            _killGrowthExpPerLevelText = value;
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                _killGrowthExpPerLevel = ClampKillGrowthDecimal(parsed, 0.01m, 100000000m);
        }, 72f, 180f);
        AddLGuiBoundInput(content, T("单次击杀基础经验", "Base EXP per kill"), () => _killGrowthBaseExpText, value =>
        {
            _killGrowthBaseExpText = value;
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                _killGrowthBaseExp = ClampKillGrowthDecimal(parsed, 0m, 100000000m);
        }, 128f, 180f);
        var y = 198f;
        var attributeTexts = new[]
        {
            _killGrowthStrBonusText, _killGrowthEndBonusText, _killGrowthDexBonusText, _killGrowthPerBonusText,
            _killGrowthLeaBonusText, _killGrowthWilBonusText, _killGrowthMagBonusText, _killGrowthChaBonusText
        };
        for (var i = 0; i < 8; i++)
        {
            var elementId = 70 + i;
            var localIndex = i;
            AddLGuiBoundInput(content, GetKillGrowthAttributeName(elementId), () => attributeTexts[localIndex], value =>
            {
                attributeTexts[localIndex] = value;
            }, y + i * 54f, 160f);
        }
        CreateLGuiButton(content, "ApplyKillGrowth", T("应用", "Apply"), 220f, 650f, 150f, 48f, () =>
        {
            _killGrowthStrBonusText = attributeTexts[0];
            _killGrowthEndBonusText = attributeTexts[1];
            _killGrowthDexBonusText = attributeTexts[2];
            _killGrowthPerBonusText = attributeTexts[3];
            _killGrowthLeaBonusText = attributeTexts[4];
            _killGrowthWilBonusText = attributeTexts[5];
            _killGrowthMagBonusText = attributeTexts[6];
            _killGrowthChaBonusText = attributeTexts[7];
            for (var i = 0; i < attributeTexts.Length; i++)
                SetLGuiKillGrowthAttributeText(70 + i, attributeTexts[i]);
            RefreshKillGrowthAffectedCharacters();
            CloseLGuiEditorModal();
        });
        EnsureKillGrowthSaveContext(true);
        var growthDataY = 720f;
        var growthDataTitle = CreateLGuiText(content, "KillGrowthCurrentDataTitle",
            T("\u5f53\u524dPC/NPC\u51fb\u6740\u6210\u957f\u6570\u636e", "Current PC/NPC kill growth data"),
            18, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(growthDataTitle.rectTransform, 0f, growthDataY, 1080f, 42f);
        growthDataY += 46f;
        var growthCharacterCount = 0;
        foreach (var chara in EnumerateKillGrowthCharacters())
        {
            if (chara == null)
                continue;

            var exp = GetKillGrowthExperience(chara);
            var level = GetKillGrowthLevelFromExp(exp);
            var role = chara.IsPC ? "PC" : "NPC";
            var line = role + "  " + SafeName(chara) + "  " +
                       T("\u7b49\u7ea7", "Level") + ": " + level.ToString(CultureInfo.InvariantCulture) + "  " +
                       T("\u7ecf\u9a8c\u503c", "EXP") + ": " + FormatKillGrowthDecimal(exp);
            var row = CreateLGuiText(content, "KillGrowthCharacter_" + growthCharacterCount.ToString(CultureInfo.InvariantCulture),
                line, 17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(row.rectTransform, 20f, growthDataY, 1060f, 40f);
            growthDataY += 42f;
            growthCharacterCount++;
        }
        if (growthCharacterCount == 0)
        {
            var emptyRow = CreateLGuiText(content, "KillGrowthCharacterEmpty",
                T("\u5f53\u524d\u6ca1\u6709\u53ef\u663e\u793a\u7684PC\u6216\u961f\u4f0dNPC", "No PC or party NPC is currently available"),
                17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(emptyRow.rectTransform, 20f, growthDataY, 1060f, 40f);
            growthDataY += 42f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(780f, growthDataY + 20f));
        ApplyLGuiVisualSettings();
    }
    private void SetLGuiKillGrowthAttributeText(int elementId, string value)
    {
        if (int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            _killGrowthAttributeBonus[elementId] = Clamp(parsed, 0, 1000000);
    }
    private void OpenLGuiEmpValueEditor(LGuiEmpRow row)
    {
        CloseLGuiEditorModal(true);
        if (_lGuiWindow == null)
            return;
        var values = ReadEmpMultiValueState(row.Function, row.State);
        var parameterCount = row.Function.ValueParameters.Count;
        _lGuiEditorModal = new GameObject("RuntimeEmpValueEditor", typeof(RectTransform), typeof(Image));
        _lGuiEditorModal.transform.SetParent(_lGuiRoot!.transform, false);
        PrepareLGuiStandaloneModal(_lGuiEditorModal);
        var modal = (RectTransform)_lGuiEditorModal.transform;
        modal.anchorMin = new Vector2(0.5f, 0.5f);
        modal.anchorMax = new Vector2(0.5f, 0.5f);
        modal.pivot = new Vector2(0.5f, 0.5f);
        modal.sizeDelta = new Vector2(1260f, Math.Min(940f, 170f + parameterCount * 56f));
        modal.anchoredPosition = Vector2.zero;
        _lGuiEditorModal.GetComponent<Image>().color = GetLGuiRowColor(0, true);
        var title = CreateLGuiText(modal, "Title", SafeEmpText(row.Plugin.Name, row.Plugin.Id) + " / " + SafeEmpText(row.Function.Name, row.Function.Id), 22, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(title.rectTransform, 24f, 16f, 950f, 42f);
        EnableLGuiModalDragging(modal, title);
        CreateLGuiButton(modal, "Close", "×", 1180f, 14f, 54f, 44f, CloseLGuiEditorModal);
        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = row.Function.ValueParameters[i];
            if (parameter == null || string.IsNullOrWhiteSpace(parameter.Key))
                continue;
            var key = parameter.Key;
            var label = string.IsNullOrWhiteSpace(parameter.Label) ? key : parameter.Label;
            AddLGuiBoundInput(modal, label, () => values.TryGetValue(key, out var value) ? value : "", value => values[key] = value, 82f + i * 54f, 520f);
        }
        CreateLGuiButton(modal, "Apply", T("应用", "Apply"), 220f, 96f + parameterCount * 54f, 150f, 48f, () =>
        {
            row.State.Value = BuildEmpMultiValueState(values);
            row.State.PendingApply = true;
            row.State.Initialized = false;
            ApplyEmpFunctionStateNow(row.Plugin, row.Function, row.State, true);
            CloseLGuiEditorModal();
            _lGuiEmpList?.RefreshBoundRows();
        });
        ApplyLGuiVisualSettings();
    }
    private void SetLGuiFeatureValue(LGuiFeatureId id, bool value)
    {
        switch (id)
        {
            case LGuiFeatureId.AiInstruction: SetAiInstruction(value); break;
            case LGuiFeatureId.LowPerformance: SetLowPerformanceMode(value); break;
            case LGuiFeatureId.UnlockFrameRate: SetUnlockFrameRate(value); break;
            case LGuiFeatureId.InvincibleMode: SetInvincibleMode(value); break;
            case LGuiFeatureId.IgnoreBuffEffects: SetIgnoreBuffEffects(value); break;
            case LGuiFeatureId.HostileThreatMarker: SetHostileThreatMarker(value); InvalidateThreatData(); break;
            case LGuiFeatureId.ShowNpcMoreInfo: SetShowNpcMoreInfo(value); break;
            case LGuiFeatureId.ShowItemMoreInfo: SetShowItemMoreInfo(value); break;
            case LGuiFeatureId.ShowBuffSpecificValues: SetShowBuffSpecificValues(value); break;
            case LGuiFeatureId.ShowItemPanelEnchantLevels: SetShowItemPanelEnchantLevels(value); break;
            case LGuiFeatureId.ShowItemPanelItemValue: SetShowItemPanelItemValue(value); break;
            case LGuiFeatureId.ShowItemPanelMilkBonus: SetShowItemPanelMilkBonus(value); break;
            case LGuiFeatureId.ShowMainAbilityExperience: SetShowMainAbilityExperience(value); break;
            case LGuiFeatureId.OneClickQuestCompletion: SetOneClickQuestCompletion(value); break;
            case LGuiFeatureId.EquipmentComparison: SetEquipmentComparison(value); break;
            case LGuiFeatureId.IgnoreFriendlyFire: SetIgnoreFriendlyFire(value); break;
            case LGuiFeatureId.WorkbenchIngredientReadingOptimization: SetWorkbenchIngredientReadingOptimization(value); break;
            case LGuiFeatureId.ExperienceMultiplier: SetExperienceMultiplierEnabled(value); break;
            case LGuiFeatureId.PlantHarvestMultiplier: SetPlantHarvestMultiplierEnabled(value); break;
            case LGuiFeatureId.IgnoreCropGrowthConditions: SetIgnoreCropGrowthConditions(value); break;
            case LGuiFeatureId.AllFeatsLearnable: SetAllFeatsLearnable(value); break;
            case LGuiFeatureId.CharacterPanelGenes: SetCharacterPanelGenes(value); break;
            case LGuiFeatureId.AllowPcGeneImplant: SetAllowPcGeneImplant(value); break;
            case LGuiFeatureId.PredationGeneSelection: SetPredationGeneSelection(value); break;
            case LGuiFeatureId.AllowCurrencyGifts: SetAllowCurrencyGifts(value); break;
            case LGuiFeatureId.FoodRestoresSp: SetFoodRestoresSpEnabled(value); break;
            case LGuiFeatureId.DismantleAlwaysReturnsMaterials: SetDismantleAlwaysReturnsMaterials(value); break;
            case LGuiFeatureId.DismantlingAlwaysLearnsRecipe: SetDismantlingAlwaysLearnsRecipe(value); break;
            case LGuiFeatureId.OptimizeMeleeHitChance: SetOptimizeMeleeHitChance(value); break;
            case LGuiFeatureId.PcFactionTrainerAllSkills: SetPcFactionTrainerAllSkills(value); break;
            case LGuiFeatureId.UnlimitedHomeResidentCap: SetUnlimitedHomeResidentCap(value); break;
            case LGuiFeatureId.UnlimitedPartyMemberCap: SetUnlimitedPartyMemberCap(value); break;
            case LGuiFeatureId.UnlimitedOfferingFaithPoints: SetUnlimitedOfferingFaithPoints(value); break;
            case LGuiFeatureId.IgnoreGodArtifactFaithRequirement: SetIgnoreGodArtifactFaithRequirement(value); break;
            case LGuiFeatureId.ShrineEffectSelection: SetShrineEffectSelection(value); break;
            case LGuiFeatureId.InfiniteChargeAndAmmo: SetInfiniteChargeAndAmmo(value); break;
            case LGuiFeatureId.RodStacking: SetRodStacking(value); break;
            case LGuiFeatureId.RightClickInterruptOperation: SetRightClickInterruptOperation(value); break;
            case LGuiFeatureId.StealHandNoTargetLimit: SetStealHandNoTargetLimit(value); break;
            case LGuiFeatureId.StealHandUndetectable: SetStealHandUndetectable(value); break;
            case LGuiFeatureId.MerchantRefreshNoCost: SetMerchantRefreshNoCost(value); break;
            case LGuiFeatureId.MerchantAlwaysStocksMonsterBall: SetMerchantAlwaysStocksMonsterBall(value); break;
            case LGuiFeatureId.MerchantMonsterBallLevelOptimization: SetMerchantMonsterBallLevelOptimization(value); break;
            case LGuiFeatureId.IgnoreSpecialNpcHatchRestriction: SetIgnoreSpecialNpcHatchRestriction(value); break;
            case LGuiFeatureId.IgnoreSpecialNpcCaptureRestriction: SetIgnoreSpecialNpcCaptureRestriction(value); break;
            case LGuiFeatureId.AffinityOnlyIncrease: SetAffinityOnlyIncrease(value); break;
            case LGuiFeatureId.KarmaOnlyIncrease: SetKarmaOnlyIncrease(value); break;
            case LGuiFeatureId.AttackCannotBeInterrupted: SetAttackCannotBeInterrupted(value); break;
            case LGuiFeatureId.FishingNoWait: SetFishingNoWait(value); break;
            case LGuiFeatureId.GeneSynthesisNoWait: SetGeneSynthesisNoWait(value); break;
            case LGuiFeatureId.SleepWithoutSleepiness: SetSleepWithoutSleepiness(value); break;
            case LGuiFeatureId.AllPurposeWorkbench: SetAllPurposeWorkbench(value); break;
            case LGuiFeatureId.InfiniteSight: SetInfinitePlayerSight(value); break;
            case LGuiFeatureId.ShowFoodRot: SetShowFoodRot(value); break;
            case LGuiFeatureId.IgnoreFoodDecay: SetIgnoreFoodDecay(value); break;
            case LGuiFeatureId.NoCraftMaterials: SetNoCraftMaterials(value); break;
            case LGuiFeatureId.UnlockCraftMaterials: SetUnlockAllCraftMaterials(value); break;
            case LGuiFeatureId.UnlockCraftRecipes: SetUnlockAllCraftRecipes(value); break;
            case LGuiFeatureId.CustomItemAmount: SetCustomItemAmount(value); break;
            case LGuiFeatureId.CustomItemData: SetCustomItemEditor(value); break;
            case LGuiFeatureId.CustomFoodData: SetCustomFoodEditor(value); break;
            case LGuiFeatureId.CustomWeaponData: SetCustomWeaponEditor(value); break;
            case LGuiFeatureId.CustomGeneData: SetCustomGeneEditor(value); break;
            case LGuiFeatureId.StethoscopeNoLimit: SetStethoscopeNoTargetLimit(value); break;
            case LGuiFeatureId.IgnoreTerrain: SetIgnoreTerrainMovement(value); break;
            case LGuiFeatureId.OptimizeVoid: SetOptimizeDungeonVoidScaling(value); break;
            case LGuiFeatureId.NoTalkInterestLoss: SetNoTalkInterestLoss(value); break;
            case LGuiFeatureId.KillGrowth: SetKillGrowthEnabled(value); break;
        }
        NotifyLGuiDataDirty();
    }
}
