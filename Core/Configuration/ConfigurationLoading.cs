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
    private void LoadConfig()
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path))
            {
                ApplyDefaultConfigValues();
                LoadAutomationConfig("");
                SaveConfig();
                ApplySavedEmpPluginStates(false);
                return;
            }

            var json = _modules.ConfigurationStorage.ReadAllText(path, Encoding.UTF8);
            var wasUnlockFrameRate = _unlockFrameRate;
            var hasLegacyPluginManagerVisible = HasJsonValue(json, "pluginManagerVisible");
            var hasLegacyItemMoreInfoBasicInfo = HasJsonValue(json, "showItemMoreInfoValue") ||
                                                  HasJsonValue(json, "itemMoreInfoValueColor");
            var hasLegacyNpcMoreInfoAdditionalIdentity = HasJsonValue(json, "showNpcMoreInfoRelationFaith") ||
                                                          HasJsonValue(json, "showNpcMoreInfoRelationExtraFontSize") ||
                                                          HasJsonValue(json, "npcMoreInfoRelationColor");
            var hasLegacyAutomationConfig = HasLegacyAutomationConfigFields(json);
            var hasLegacyWatermarkUiScale = HasJsonValue(json, "elinModifierWatermarkScale");
            var hasLegacyUnusedMoongateApis =
                HasJsonValue(json, "moongateCloudStatusApi") ||
                HasJsonValue(json, "moongateCloudCnCompatibilityApi") ||
                HasJsonValue(json, "moongateCloudJpCompatibilityApi") ||
                HasJsonValue(json, "moongateCloudEnCompatibilityApi");
            var shouldRewriteConfig = !HasOptimizedConfigOrder(json) ||
                                      hasLegacyPluginManagerVisible ||
                                      hasLegacyItemMoreInfoBasicInfo ||
                                      hasLegacyNpcMoreInfoAdditionalIdentity ||
                                      hasLegacyAutomationConfig ||
                                      hasLegacyWatermarkUiScale ||
                                      hasLegacyUnusedMoongateApis ||
                                      !HasJsonValue(json, "mainMenuInfoDefaultOnMigrated") ||
                                      !HasJsonValue(json, "elinModifierWatermarkDefaultOnMigrated") ||
                                      !HasJsonValue(json, "showMainMenuInfo") ||
                                      !HasJsonValue(json, "showElinModifierWatermark") ||
                                      !HasJsonValue(json, "elinModifierWatermarkPositionLocked") ||
                                      !HasJsonValue(json, "elinModifierWatermarkGameErrorNotification") ||
                                      !HasJsonValue(json, "elinModifierWatermarkSuppressWarningNotification") ||
                                      !HasJsonValue(json, "elinModifierWatermarkPositionX") ||
                                      !HasJsonValue(json, "elinModifierWatermarkPositionY") ||
                                      !HasJsonValue(json, "disableCwlErrorNotification") ||
                                      !HasJsonValue(json, "forceGameUnfocus") ||
                                      !HasJsonValue(json, "uiRoundedCorners") ||
                                      !HasJsonValue(json, "adaptiveUiScale") ||
                                      !HasJsonValue(json, "customUiScale") ||
                                      !HasJsonValue(json, "uiFontSize") ||
                                      !HasJsonValue(json, "openKey") ||
                                      !HasJsonValue(json, "lowPerformanceMode") ||
                                      !HasJsonValue(json, "unlockFrameRate") ||
                                      !HasJsonValue(json, "invincibleMode") ||
                                      !HasJsonValue(json, "invincibleModeIncludeParty") ||
                                      !HasJsonValue(json, "ignoreBuffEffects") ||
                                      !HasJsonValue(json, "ignoreBuffEffectsDebuff") ||
                                      !HasJsonValue(json, "ignoreBuffEffectsBuff") ||
                                       !HasJsonValue(json, "ignoreBuffEffectsIncludeParty") ||
                                       !HasJsonValue(json, "hostileThreatMarker") ||
                                       !HasJsonValue(json, "hostileThreatBehaviorPrediction") ||
                                       !HasJsonValue(json, "hostileThreatPredecisionLock") ||
                                       !HasJsonValue(json, "showNpcMoreInfo") ||
                                       !HasJsonValue(json, "showNpcMoreInfoOrder") ||
                                       !HasJsonValue(json, "showNpcMoreInfoAdditionalIdentityPerLine") ||
                                       !HasJsonValue(json, "showNpcMoreInfoVitalsPerLine") ||
                                       !HasJsonValue(json, "showNpcMoreInfoAttributesPerLine") ||
                                      !HasJsonValue(json, "showNpcMoreInfoBuffsPerLine") ||
                                      !HasJsonValue(json, "showNpcMoreInfoResistsPerLine") ||
                                      !HasJsonValue(json, "showNpcMoreInfoSkillsPerLine") ||
                                      !HasJsonValue(json, "showNpcMoreInfoAbilitiesPerLine") ||
                                      !HasJsonValue(json, "showNpcMoreInfoFeatsPerLine") ||
                                      !HasJsonValue(json, "showItemMoreInfo") ||
                                      !HasJsonValue(json, "showBuffSpecificValues") ||
                                      !HasJsonValue(json, "showBuffSpecificValuesIconFontSizeOffset") ||
                                      !HasJsonValue(json, "showBuffSpecificValuesTextFontSizeOffset") ||
                                      !HasJsonValue(json, "showItemPanelEnchantLevels") ||
                                      !HasJsonValue(json, "showItemPanelItemValue") ||
                                      !HasJsonValue(json, "showMainAbilityExperience") ||
                                      !HasJsonValue(json, "showMainAbilityExperienceInSkillTracker") ||
                                      !HasJsonValue(json, "equipmentComparison") ||
                                      !HasJsonValue(json, "ignoreFriendlyFire") ||
                                      !HasJsonValue(json, "workbenchIngredientReadingOptimization") ||
                                      !HasJsonValue(json, "experienceMultiplierEnabled") ||
                                      !HasJsonValue(json, "experienceMultiplierIncludePcFaction") ||
                                      !HasJsonValue(json, "characterLevelExperienceMultiplier") ||
                                      !HasJsonValue(json, "skillExperienceMultiplier") ||
                                      !HasJsonValue(json, "magicExperienceMultiplier") ||
                                      !HasJsonValue(json, "foodPotentialGainMultiplier") ||
                                      !HasJsonValue(json, "trainingPotentialGainMultiplier") ||
                                      !HasJsonValue(json, "plantHarvestMultiplierEnabled") ||
                                      !HasJsonValue(json, "cropHarvestMultiplier") ||
                                      !HasJsonValue(json, "seedReapingMultiplier") ||
                                      !HasJsonValue(json, "ignoreCropGrowthConditions") ||
                                      !HasJsonValue(json, "foodRestoresSpEnabled") ||
                                      !HasJsonValue(json, "foodRestoresSpPercent") ||
                                      !HasJsonValue(json, "optimizeMeleeHitChance") ||
                                      !HasJsonValue(json, "optimizeMeleeHitChanceIncludeParty") ||
                                      !HasJsonValue(json, "pcFactionTrainerAllSkills") ||
                                      !HasJsonValue(json, "unlimitedHomeResidentCap") ||
                                      !HasJsonValue(json, "unlimitedPartyMemberCap") ||
                                      !HasJsonValue(json, "unlimitedOfferingFaithPoints") ||
                                      !HasJsonValue(json, "ignoreGodArtifactFaithRequirement") ||
                                      !HasJsonValue(json, "shrineEffectSelection") ||
                                      !HasJsonValue(json, "infiniteChargeAndAmmo") ||
                                      !HasJsonValue(json, "chargeStacking") ||
                                      !HasJsonValue(json, "rightClickInterruptOperation") ||
                                      !HasJsonValue(json, "stealHandNoTargetLimit") ||
                                      !HasJsonValue(json, "stealHandUndetectable") ||
                                      !HasJsonValue(json, "aiInstructionAutoCombatBySave") ||
                                      !HasJsonValue(json, "merchantAlwaysStocksMonsterBall") ||
                                      !HasJsonValue(json, "merchantMonsterBallLevelOptimization") ||
                                      !HasJsonValue(json, "ignoreSpecialNpcHatchRestriction") ||
                                      !HasJsonValue(json, "ignoreSpecialNpcCaptureRestriction") ||
                                      !HasJsonValue(json, "affinityOnlyIncrease") ||
                                      !HasJsonValue(json, "karmaOnlyIncrease") ||
                                      !HasJsonValue(json, "attackCannotBeInterrupted") ||
                                      !HasJsonValue(json, "attackCannotBeInterruptedIncludeParty") ||
                                      !HasJsonValue(json, "fishingNoWait") ||
                                      !HasJsonValue(json, "geneSynthesisNoWait") ||
                                      !HasJsonValue(json, "sleepWithoutSleepiness") ||
                                      !HasJsonValue(json, "allPurposeWorkbench") ||
                                      !HasJsonValue(json, "abilityCustomAttributes") ||
                                      !HasJsonValue(json, "probabilityModule") ||
                                      !HasJsonValue(json, "showItemMoreInfoBasicInfo") ||
                                      !HasJsonValue(json, "showItemMoreInfoGatheringThreshold") ||
                                      !HasJsonValue(json, "showItemMoreInfoWeaponStats") ||
                                      !HasJsonValue(json, "showItemMoreInfoEnchantments") ||
                                      !HasJsonValue(json, "showItemMoreInfoPlantStats") ||
                                      !HasJsonValue(json, "showItemMoreInfoPlantStatsExtended") ||
                                      !HasJsonValue(json, "showItemMoreInfoFontSizeOffset") ||
                                      !HasJsonValue(json, "itemMoreInfoBasicInfoColor") ||
                                      !HasJsonValue(json, "itemMoreInfoGatheringToolColor") ||
                                      !HasJsonValue(json, "itemMoreInfoGatheringThresholdColor") ||
                                      !HasJsonValue(json, "itemMoreInfoRarityCrudeColor") ||
                                      !HasJsonValue(json, "itemMoreInfoRarityNormalColor") ||
                                      !HasJsonValue(json, "itemMoreInfoRaritySuperiorColor") ||
                                      !HasJsonValue(json, "itemMoreInfoRarityLegendaryColor") ||
                                      !HasJsonValue(json, "itemMoreInfoRarityMythicalColor") ||
                                      !HasJsonValue(json, "itemMoreInfoRarityArtifactColor") ||
                                      !HasJsonValue(json, "itemMoreInfoWeaponStatsColor") ||
                                      !HasJsonValue(json, "itemMoreInfoEnchantColor") ||
                                      !HasJsonValue(json, "itemMoreInfoPlantStatsColor") ||
                                      !HasJsonValue(json, "showNpcMoreInfoLevel") ||
                                      !HasJsonValue(json, "showNpcMoreInfoIdentity") ||
                                      !HasJsonValue(json, "showNpcMoreInfoAdditionalIdentity") ||
                                      !HasJsonValue(json, "showNpcMoreInfoVitals") ||
                                      !HasJsonValue(json, "showNpcMoreInfoAttributes") ||
                                      !HasJsonValue(json, "showNpcMoreInfoBuffs") ||
                                      !HasJsonValue(json, "showNpcMoreInfoResists") ||
                                      !HasJsonValue(json, "showNpcMoreInfoSkills") ||
                                      !HasJsonValue(json, "showNpcMoreInfoAbilities") ||
                                      !HasJsonValue(json, "showNpcMoreInfoFeats") ||
                                      !HasJsonValue(json, "showNpcMoreInfoCombatSimulation") ||
                                      !HasJsonValue(json, "showNpcMoreInfoFontSizeOffset") ||
                                      !HasJsonValue(json, "showNpcMoreInfoLevelExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoIdentityExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoAdditionalIdentityExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoVitalsExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoAttributesExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoBuffsExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoResistsExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoSkillsExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoAbilitiesExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoFeatsExtraFontSize") ||
                                      !HasJsonValue(json, "showNpcMoreInfoCombatExtraFontSize") ||
                                      !HasJsonValue(json, "npcMoreInfoLevelColor") ||
                                      !HasJsonValue(json, "npcMoreInfoIdentityColor") ||
                                      !HasJsonValue(json, "npcMoreInfoAdditionalIdentityColor") ||
                                      !HasJsonValue(json, "npcMoreInfoHpColor") ||
                                      !HasJsonValue(json, "npcMoreInfoMpColor") ||
                                      !HasJsonValue(json, "npcMoreInfoSpColor") ||
                                      !HasJsonValue(json, "npcMoreInfoExpColor") ||
                                      !HasJsonValue(json, "npcMoreInfoSpeedColor") ||
                                      !HasJsonValue(json, "npcMoreInfoDvColor") ||
                                      !HasJsonValue(json, "npcMoreInfoPvColor") ||
                                      !HasJsonValue(json, "npcMoreInfoSkillColor") ||
                                      !HasJsonValue(json, "npcMoreInfoAbilityColor") ||
                                      !HasJsonValue(json, "npcMoreInfoFeatColor") ||
                                      !HasJsonValue(json, "npcMoreInfoCombatColor") ||
                                      !HasJsonValue(json, "npcMoreInfoResistColor") ||
                                      !HasJsonValue(json, "npcMoreInfoAttributeColor") ||
                                      !HasJsonValue(json, "npcMoreInfoBuffColor") ||
                                      !HasJsonValue(json, "infinitePlayerSight") ||
                                      !HasJsonValue(json, "showFoodRot") ||
                                      !HasJsonValue(json, "ignoreFoodDecay") ||
                                      !HasJsonValue(json, "noCraftMaterials") ||
                                      !HasJsonValue(json, "unlockAllCraftMaterials") ||
                                      !HasJsonValue(json, "unlockAllCraftRecipes") ||
                                      !HasJsonValue(json, "customItemAmount") ||
                                      !HasJsonValue(json, "customItemEditor") ||
                                      !HasJsonValue(json, "customFoodEditor") ||
                                      !HasJsonValue(json, "customWeaponEditor") ||
                                      !HasJsonValue(json, "customGeneEditor") ||
                                      !HasJsonValue(json, "stethoscopeNoTargetLimit") ||
                                      !HasJsonValue(json, "ignoreTerrainMovement") ||
                                      !HasJsonValue(json, "optimizeDungeonVoidScaling") ||
                                       !HasJsonValue(json, "noTalkInterestLoss") ||
                                       !HasJsonValue(json, "killGrowth") ||
                                       !HasJsonValue(json, "expBySave") ||
                                       !HasJsonValue(json, "automation") ||
                                       !HasJsonValue(json, "selectedScript") ||
                                       !HasJsonValue(json, "moongateLandholderPrivileges") ||
                                       !HasJsonValue(json, "moongateUploadUpdateKeys") ||
                                       HasJsonValue(json, "moongateCloudHistoryIndexApi") ||
                                       HasJsonValue(json, "moongateCloudRevisionHistoryApi") ||
                                       (_modules.Nightly != null && !HasJsonValue(json, "nightlyAllowCurrencyGifts")) ||
                                       !HasJsonValue(json, "aiApiBase") ||
                                      !HasJsonValue(json, "aiApiKey") ||
                                      !HasJsonValue(json, "aiModelName") ||
                                      !HasJsonValue(json, "aiReasoningEffort") ||
                                      !HasJsonValue(json, "aiUseContext") ||
                                      !HasJsonValue(json, "aiAutoCompressContext") ||
                                      !HasJsonValue(json, "aiUseStreaming") ||
                                      !HasJsonValue(json, "aiUseToolStreaming") ||
                                      !HasJsonValue(json, "aiHttpTimeoutSeconds") ||
                                      !HasJsonValue(json, "aiContextCompressThreshold") ||
                                      !HasJsonValue(json, "empPlugins");
            _language = NormalizeLanguage(ExtractString(json, "language", _language));
            _uiStyleIndex = Clamp(ExtractInt(json, "uiStyleIndex", _uiStyleIndex), 0, UiStyleNamesZh.Length - 1);
            _uiAlpha = Clamp(ExtractFloat(json, "uiAlpha", _uiAlpha), 0.2f, 1f);
            SetUiFontSize(ExtractInt(json, "uiFontSize", UiFontSizeDefault));
            var fontColorMode = ExtractString(json, "uiFontColorMode", "style");
            _uiTextColorFollowsStyle = !string.Equals(fontColorMode, "custom", StringComparison.OrdinalIgnoreCase);
            _uiTextColor = GetDefaultUiTextColor();
            Color fontColor;
            if (TryParseHexColor(ExtractString(json, "uiFontColorHex", ""), out fontColor))
                _uiTextColor = fontColor;

            var keyName = ExtractString(json, "openKey", GetKeyLabel(DefaultOpenKey));
            KeyCode key;
            if (TryParseKeyCode(keyName, out key))
            {
                _openKey = key;
            }
            _forceGameUnfocus = ExtractBool(json, "forceGameUnfocus", _forceGameUnfocus);
            _uiRoundedCorners = ExtractBool(json, "uiRoundedCorners", true);
            _modules.MainMenuInfo.SetEnabled(HasJsonValue(json, "mainMenuInfoDefaultOnMigrated")
                ? ExtractBool(json, "showMainMenuInfo", true)
                : true);
            var watermarkDefaultMigrated = HasJsonValue(json, "elinModifierWatermarkDefaultOnMigrated");
            _modules.Watermark.LoadSettings(
                watermarkDefaultMigrated
                    ? ExtractBool(json, "showElinModifierWatermark", true)
                    : true,
                ExtractBool(json, "elinModifierWatermarkPositionLocked", false),
                watermarkDefaultMigrated
                    ? ExtractBool(json, "elinModifierWatermarkGameErrorNotification", true)
                    : true,
                ExtractBool(json, "elinModifierWatermarkSuppressWarningNotification", true),
                ExtractFloat(json, "elinModifierWatermarkPositionX", 0f),
                ExtractFloat(json, "elinModifierWatermarkPositionY", -10f));
            SetDisableCwlErrorNotification(ExtractBool(json, "disableCwlErrorNotification", false));
            RefreshMainMenuInfoButton();
            if (ShowMainMenuInfo)
                ScheduleMainMenuInfoAutoOpen();
            _adaptiveUiScale = ExtractBool(json, "adaptiveUiScale", _adaptiveUiScale);
            SetCustomUiScale(ExtractFloat(json, "customUiScale", 0f));
            var oldLowPerformanceMode = _lowPerformanceMode;
            _lowPerformanceMode = ExtractBool(json, "lowPerformanceMode", _lowPerformanceMode);
            if (oldLowPerformanceMode != _lowPerformanceMode)
                ClearLowPerformanceCaches();
            _unlockFrameRate = ExtractBool(json, "unlockFrameRate", _unlockFrameRate);
            _invincibleMode = ExtractBool(json, "invincibleMode", _invincibleMode);
            _invincibleModeIncludeParty = ExtractBool(json, "invincibleModeIncludeParty", _invincibleModeIncludeParty);
            _ignoreBuffEffects = ExtractBool(json, "ignoreBuffEffects", _ignoreBuffEffects);
            _ignoreBuffEffectsDebuff = ExtractBool(json, "ignoreBuffEffectsDebuff", _ignoreBuffEffectsDebuff);
            _ignoreBuffEffectsBuff = ExtractBool(json, "ignoreBuffEffectsBuff", _ignoreBuffEffectsBuff);
            _ignoreBuffEffectsIncludeParty = ExtractBool(json, "ignoreBuffEffectsIncludeParty", _ignoreBuffEffectsIncludeParty);
            _hostileThreatMarker = ExtractBool(json, "hostileThreatMarker", _hostileThreatMarker);
            _hostileThreatBehaviorPrediction = ExtractBool(json, "hostileThreatBehaviorPrediction", true);
            _hostileThreatPredecisionLock = ExtractBool(json, "hostileThreatPredecisionLock", false);
            if (!_hostileThreatMarker || !_hostileThreatBehaviorPrediction || !_hostileThreatPredecisionLock)
                _modules.ThreatOverlay.ClearLockedDecisions();
            _showNpcMoreInfo = ExtractBool(json, "showNpcMoreInfo", _showNpcMoreInfo);
            _showItemMoreInfo = ExtractBool(json, "showItemMoreInfo", _showItemMoreInfo);
            _showBuffSpecificValues = ExtractBool(json, "showBuffSpecificValues", _showBuffSpecificValues);
            var legacyBuffFontSizeOffset = Clamp(ExtractInt(json, "showBuffSpecificValuesFontSizeOffset", 0), -8, 8);
            _showBuffSpecificValuesIconFontSizeOffset = Clamp(ExtractInt(json, "showBuffSpecificValuesIconFontSizeOffset", legacyBuffFontSizeOffset), -8, 8);
            _showBuffSpecificValuesTextFontSizeOffset = Clamp(ExtractInt(json, "showBuffSpecificValuesTextFontSizeOffset", legacyBuffFontSizeOffset), -8, 8);
            _showItemPanelEnchantLevels = ExtractBool(json, "showItemPanelEnchantLevels", _showItemPanelEnchantLevels);
            _showItemPanelItemValue = ExtractBool(json, "showItemPanelItemValue", _showItemPanelItemValue);
            _showMainAbilityExperience = ExtractBool(json, "showMainAbilityExperience",
                ExtractBool(json, "showCharacterPanelExperience", _showMainAbilityExperience));
            _showMainAbilityExperienceInSkillTracker = ExtractBool(json, "showMainAbilityExperienceInSkillTracker", true);
            RefreshMainAbilityExperienceTracker(_showMainAbilityExperience && _showMainAbilityExperienceInSkillTracker);
            _equipmentComparison = ExtractBool(json, "equipmentComparison", false);
            if (!_equipmentComparison)
                DestroyEquipmentComparisonTooltip();
            _workbenchIngredientReadingOptimization = ExtractBool(json, "workbenchIngredientReadingOptimization", false);
            NonStandardCrafterIngredientOptimizer.Clear();
            if (!_workbenchIngredientReadingOptimization)
            {
                CraftIngredientPickerPager.CloseActivePickers();
                NonStandardCrafterIngredientPager.DisableAndRestoreActive();
            }
            _modules.Progression.ExperienceMultiplierEnabled = ExtractBool(json, "experienceMultiplierEnabled", false);
            _modules.Progression.ExperienceMultiplierIncludePcFaction = ExtractBool(json, "experienceMultiplierIncludePcFaction", true);
            _modules.Progression.CharacterLevelExperienceMultiplier = Mathf.Clamp(ExtractFloat(json, "characterLevelExperienceMultiplier", 1f), 0f, 1000000f);
            _modules.Progression.SkillExperienceMultiplier = Mathf.Clamp(ExtractFloat(json, "skillExperienceMultiplier", 1f), 0f, 1000000f);
            _modules.Progression.MagicExperienceMultiplier = Mathf.Clamp(ExtractFloat(json, "magicExperienceMultiplier", 1f), 0f, 1000000f);
            _modules.Progression.FoodPotentialGainMultiplier = Mathf.Clamp(ExtractFloat(json, "foodPotentialGainMultiplier", 1f), 0f, 1000000f);
            _modules.Progression.TrainingPotentialGainMultiplier = Mathf.Clamp(ExtractFloat(json, "trainingPotentialGainMultiplier", 1f), 0f, 1000000f);
            SyncExperienceMultiplierTextFields();
            _modules.PlantHarvestMultiplier.Load(
                ExtractBool(json, "plantHarvestMultiplierEnabled", false),
                ExtractFloat(json, "cropHarvestMultiplier", 1f),
                ExtractFloat(json, "seedReapingMultiplier", 1f));
            _modules.IgnoreCropGrowthConditions.Load(
                ExtractBool(json, "ignoreCropGrowthConditions", false));
            _modules.Progression.FoodRestoresSpEnabled = ExtractBool(json, "foodRestoresSpEnabled", false);
            _modules.Progression.FoodRestoresSpPercent = Clamp(ExtractInt(json, "foodRestoresSpPercent", 10), 1, 100);
            _modules.GuaranteedGatheringRewards.Load(
                ExtractBool(json, "dismantleAlwaysReturnsMaterials", false),
                ExtractBool(json, "dismantlingAlwaysLearnsRecipe", false));
            _modules.Progression.OptimizeMeleeHitChance = ExtractBool(json, "optimizeMeleeHitChance", false);
            _modules.Progression.OptimizeMeleeHitChanceIncludeParty = ExtractBool(json, "optimizeMeleeHitChanceIncludeParty", true);
            _modules.Progression.PcFactionTrainerAllSkills = ExtractBool(json, "pcFactionTrainerAllSkills", false);
            _unlimitedHomeResidentCap = ExtractBool(json, "unlimitedHomeResidentCap", _unlimitedHomeResidentCap);
            _unlimitedPartyMemberCap = ExtractBool(json, "unlimitedPartyMemberCap", _unlimitedPartyMemberCap);
            _unlimitedOfferingFaithPoints = ExtractBool(json, "unlimitedOfferingFaithPoints", _unlimitedOfferingFaithPoints);
            _ignoreGodArtifactFaithRequirement = ExtractBool(json, "ignoreGodArtifactFaithRequirement", _ignoreGodArtifactFaithRequirement);
            _shrineEffectSelection = ExtractBool(json, "shrineEffectSelection", _shrineEffectSelection);
            _infiniteChargeAndAmmo = ExtractBool(json, "infiniteChargeAndAmmo", _infiniteChargeAndAmmo);
            _rodStacking = ExtractBool(json, "chargeStacking", _rodStacking);
            _modules.RightClickInterrupt.Load(
                ExtractBool(json, "rightClickInterruptOperation", false));
            _stealHandNoTargetLimit = ExtractBool(json, "stealHandNoTargetLimit", false);
            _stealHandUndetectable = ExtractBool(json, "stealHandUndetectable", false);
            _modules.MerchantRefreshNoCost.Load(
                ExtractBool(json, "merchantRefreshNoCost", false));
            _modules.AiInstruction.Load(
                ExtractBool(json, "aiInstruction", false),
                json);
            _modules.MerchantMonsterBall.Load(
                ExtractBool(json, "merchantAlwaysStocksMonsterBall", false),
                ExtractBool(json, "merchantMonsterBallLevelOptimization", false));
            _modules.SpecialNpcHatch.Load(
                ExtractBool(json, "ignoreSpecialNpcHatchRestriction", false));
            _modules.SpecialNpcCapture.Load(
                ExtractBool(json, "ignoreSpecialNpcCaptureRestriction", false));
            _modules.CharacterProtection.Load(
                ExtractBool(json, "ignoreFriendlyFire", false),
                ExtractBool(json, "affinityOnlyIncrease", false),
                ExtractBool(json, "karmaOnlyIncrease", false),
                ExtractBool(json, "attackCannotBeInterrupted", false),
                ExtractBool(json, "attackCannotBeInterruptedIncludeParty", true));
            _modules.FishingNoWait.Load(ExtractBool(json, "fishingNoWait", false));
            _modules.GeneSynthesisNoWait.Load(ExtractBool(json, "geneSynthesisNoWait", false));
            _modules.SleepWithoutSleepiness.Load(ExtractBool(json, "sleepWithoutSleepiness", false));
            _modules.AllPurposeWorkbench.Load(
                ExtractBool(json, "allPurposeWorkbench", false),
                ExtractString(json, "allPurposeWorkbenchDefaultTabType", "itemCategory"));
            _modules.Probability.SetStoredConfigurationJson(
                ConfigurationValueDocument.For(json).GetRawJson("probabilityModule"));
            _showItemMoreInfoBasicInfo = ExtractBool(json, "showItemMoreInfoBasicInfo",
                ExtractBool(json, "showItemMoreInfoValue", _showItemMoreInfoBasicInfo));
            _showItemMoreInfoGatheringThreshold = ExtractBool(json, "showItemMoreInfoGatheringThreshold", true);
            _showItemMoreInfoWeaponStats = ExtractBool(json, "showItemMoreInfoWeaponStats", _showItemMoreInfoWeaponStats);
            _showItemMoreInfoEnchantments = ExtractBool(json, "showItemMoreInfoEnchantments", _showItemMoreInfoEnchantments);
            _showItemMoreInfoPlantStats = ExtractBool(json, "showItemMoreInfoPlantStats", _showItemMoreInfoPlantStats);
            _showItemMoreInfoPlantStatsExtended = ExtractBool(json, "showItemMoreInfoPlantStatsExtended", _showItemMoreInfoPlantStatsExtended);
            _showItemMoreInfoFontSizeOffset = Clamp(ExtractInt(json, "showItemMoreInfoFontSizeOffset", 0), -8, 8);
            _itemMoreInfoBasicInfoColor = NormalizeHoverInfoColor(
                ExtractString(json, "itemMoreInfoBasicInfoColor", ExtractString(json, "itemMoreInfoValueColor", DefaultItemMoreInfoBasicInfoColor)),
                DefaultItemMoreInfoBasicInfoColor);
            _itemMoreInfoGatheringToolColor = NormalizeHoverInfoColor(
                ExtractString(json, "itemMoreInfoGatheringToolColor", DefaultItemMoreInfoGatheringToolColor),
                DefaultItemMoreInfoGatheringToolColor);
            _itemMoreInfoGatheringThresholdColor = NormalizeHoverInfoColor(
                ExtractString(json, "itemMoreInfoGatheringThresholdColor", DefaultItemMoreInfoGatheringThresholdColor),
                DefaultItemMoreInfoGatheringThresholdColor);
            _itemMoreInfoWeaponStatsColor = NormalizeHoverInfoColor(
                ExtractString(json, "itemMoreInfoWeaponStatsColor", DefaultItemMoreInfoWeaponStatsColor),
                DefaultItemMoreInfoWeaponStatsColor);
            _itemMoreInfoEnchantColor = NormalizeHoverInfoColor(ExtractString(json, "itemMoreInfoEnchantColor", DefaultItemMoreInfoEnchantColor), DefaultItemMoreInfoEnchantColor);
            _itemMoreInfoPlantStatsColor = NormalizeHoverInfoColor(ExtractString(json, "itemMoreInfoPlantStatsColor", DefaultItemMoreInfoPlantStatsColor), DefaultItemMoreInfoPlantStatsColor);
            _itemMoreInfoRarityCrudeColor = NormalizeHoverInfoColor(ExtractString(json, "itemMoreInfoRarityCrudeColor", DefaultItemMoreInfoRarityCrudeColor), DefaultItemMoreInfoRarityCrudeColor);
            _itemMoreInfoRarityNormalColor = NormalizeHoverInfoColor(ExtractString(json, "itemMoreInfoRarityNormalColor", DefaultItemMoreInfoRarityNormalColor), DefaultItemMoreInfoRarityNormalColor);
            _itemMoreInfoRaritySuperiorColor = NormalizeHoverInfoColor(ExtractString(json, "itemMoreInfoRaritySuperiorColor", DefaultItemMoreInfoRaritySuperiorColor), DefaultItemMoreInfoRaritySuperiorColor);
            _itemMoreInfoRarityLegendaryColor = NormalizeHoverInfoColor(ExtractString(json, "itemMoreInfoRarityLegendaryColor", DefaultItemMoreInfoRarityLegendaryColor), DefaultItemMoreInfoRarityLegendaryColor);
            _itemMoreInfoRarityMythicalColor = NormalizeHoverInfoColor(ExtractString(json, "itemMoreInfoRarityMythicalColor", DefaultItemMoreInfoRarityMythicalColor), DefaultItemMoreInfoRarityMythicalColor);
            _itemMoreInfoRarityArtifactColor = NormalizeHoverInfoColor(ExtractString(json, "itemMoreInfoRarityArtifactColor", DefaultItemMoreInfoRarityArtifactColor), DefaultItemMoreInfoRarityArtifactColor);
            _showNpcMoreInfoLevel = ExtractBool(json, "showNpcMoreInfoLevel", _showNpcMoreInfoLevel);
            _showNpcMoreInfoIdentity = ExtractBool(json, "showNpcMoreInfoIdentity", _showNpcMoreInfoIdentity);
            _showNpcMoreInfoRelationFaith = ExtractBool(json, "showNpcMoreInfoAdditionalIdentity",
                ExtractBool(json, "showNpcMoreInfoRelationFaith", _showNpcMoreInfoRelationFaith));
            _showNpcMoreInfoVitals = ExtractBool(json, "showNpcMoreInfoVitals", _showNpcMoreInfoVitals);
            _showNpcMoreInfoAttributes = ExtractBool(json, "showNpcMoreInfoAttributes", _showNpcMoreInfoAttributes);
            _showNpcMoreInfoBuffs = ExtractBool(json, "showNpcMoreInfoBuffs", _showNpcMoreInfoBuffs);
            _showNpcMoreInfoResists = ExtractBool(json, "showNpcMoreInfoResists", _showNpcMoreInfoResists);
            _showNpcMoreInfoSkills = ExtractBool(json, "showNpcMoreInfoSkills", _showNpcMoreInfoSkills);
            _showNpcMoreInfoAbilities = ExtractBool(json, "showNpcMoreInfoAbilities", _showNpcMoreInfoAbilities);
            _showNpcMoreInfoFeats = ExtractBool(json, "showNpcMoreInfoFeats", _showNpcMoreInfoFeats);
            _showNpcMoreInfoCombatSimulation = ExtractBool(json, "showNpcMoreInfoCombatSimulation", _showNpcMoreInfoCombatSimulation);
            _showNpcMoreInfoOrder = NormalizeNpcMoreInfoOrder(ExtractString(json, "showNpcMoreInfoOrder", DefaultNpcMoreInfoOrder));
            _showNpcMoreInfoRelationPerLine = Clamp(ExtractInt(json, "showNpcMoreInfoAdditionalIdentityPerLine", 3), 1, 99);
            _showNpcMoreInfoVitalsPerLine = Clamp(ExtractInt(json, "showNpcMoreInfoVitalsPerLine", 4), 1, 99);
            _showNpcMoreInfoAttributesPerLine = Clamp(ExtractInt(json, "showNpcMoreInfoAttributesPerLine", 4), 1, 99);
            _showNpcMoreInfoBuffsPerLine = Clamp(ExtractInt(json, "showNpcMoreInfoBuffsPerLine", 5), 1, 99);
            _showNpcMoreInfoResistsPerLine = Clamp(ExtractInt(json, "showNpcMoreInfoResistsPerLine", 5), 1, 99);
            _showNpcMoreInfoSkillsPerLine = Clamp(ExtractInt(json, "showNpcMoreInfoSkillsPerLine", 5), 1, 99);
            _showNpcMoreInfoAbilitiesPerLine = Clamp(ExtractInt(json, "showNpcMoreInfoAbilitiesPerLine", 5), 1, 99);
            _showNpcMoreInfoFeatsPerLine = Clamp(ExtractInt(json, "showNpcMoreInfoFeatsPerLine", 5), 1, 99);
            _showNpcMoreInfoFontSizeOffset = Clamp(ExtractInt(json, "showNpcMoreInfoFontSizeOffset", 0), -8, 8);
            _showNpcMoreInfoLevelExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoLevelExtraFontSize", 0), -8, 8);
            _showNpcMoreInfoIdentityExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoIdentityExtraFontSize", 0), -8, 8);
            _showNpcMoreInfoRelationExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoAdditionalIdentityExtraFontSize",
                ExtractInt(json, "showNpcMoreInfoRelationExtraFontSize", 0)), -8, 8);
            _showNpcMoreInfoVitalsExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoVitalsExtraFontSize", 4), -8, 8);
            _showNpcMoreInfoAttributesExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoAttributesExtraFontSize", 0), -8, 8);
            _showNpcMoreInfoBuffsExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoBuffsExtraFontSize", 0), -8, 8);
            _showNpcMoreInfoResistsExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoResistsExtraFontSize", 0), -8, 8);
            _showNpcMoreInfoSkillsExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoSkillsExtraFontSize", 0), -8, 8);
            _showNpcMoreInfoAbilitiesExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoAbilitiesExtraFontSize", 0), -8, 8);
            _showNpcMoreInfoFeatsExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoFeatsExtraFontSize", 0), -8, 8);
            _showNpcMoreInfoCombatExtraFontSize = Clamp(ExtractInt(json, "showNpcMoreInfoCombatExtraFontSize", 0), -8, 8);
            _npcMoreInfoLevelColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoLevelColor", DefaultNpcMoreInfoLevelColor), DefaultNpcMoreInfoLevelColor);
            _npcMoreInfoIdentityColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoIdentityColor", DefaultNpcMoreInfoIdentityColor), DefaultNpcMoreInfoIdentityColor);
            _npcMoreInfoRelationColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoAdditionalIdentityColor",
                ExtractString(json, "npcMoreInfoRelationColor", DefaultNpcMoreInfoRelationColor)), DefaultNpcMoreInfoRelationColor);
            _npcMoreInfoHpColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoHpColor", DefaultNpcMoreInfoHpColor), DefaultNpcMoreInfoHpColor);
            _npcMoreInfoMpColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoMpColor", DefaultNpcMoreInfoMpColor), DefaultNpcMoreInfoMpColor);
            _npcMoreInfoSpColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoSpColor", DefaultNpcMoreInfoSpColor), DefaultNpcMoreInfoSpColor);
            _npcMoreInfoExpColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoExpColor", DefaultNpcMoreInfoExpColor), DefaultNpcMoreInfoExpColor);
            _npcMoreInfoSpeedColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoSpeedColor", DefaultNpcMoreInfoSpeedColor), DefaultNpcMoreInfoSpeedColor);
            _npcMoreInfoDvColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoDvColor", DefaultNpcMoreInfoDvColor), DefaultNpcMoreInfoDvColor);
            _npcMoreInfoPvColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoPvColor", DefaultNpcMoreInfoPvColor), DefaultNpcMoreInfoPvColor);
            _npcMoreInfoSkillColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoSkillColor", DefaultNpcMoreInfoSkillColor), DefaultNpcMoreInfoSkillColor);
            _npcMoreInfoAbilityColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoAbilityColor", DefaultNpcMoreInfoAbilityColor), DefaultNpcMoreInfoAbilityColor);
            _npcMoreInfoFeatColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoFeatColor", DefaultNpcMoreInfoFeatColor), DefaultNpcMoreInfoFeatColor);
            _npcMoreInfoCombatColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoCombatColor", DefaultNpcMoreInfoCombatColor), DefaultNpcMoreInfoCombatColor);
            _npcMoreInfoResistColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoResistColor", DefaultNpcMoreInfoResistColor), DefaultNpcMoreInfoResistColor);
            _npcMoreInfoAttributeColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoAttributeColor", DefaultNpcMoreInfoAttributeColor), DefaultNpcMoreInfoAttributeColor);
            _npcMoreInfoBuffColor = NormalizeHoverInfoColor(ExtractString(json, "npcMoreInfoBuffColor", DefaultNpcMoreInfoBuffColor), DefaultNpcMoreInfoBuffColor);
            SyncNpcMoreInfoColorInputs();
            SyncItemMoreInfoColorInputs();
            InvalidateNpcMoreInfoCaches();
            InvalidateItemMoreInfoCache();
            _infinitePlayerSight = ExtractBool(json, "infinitePlayerSight", _infinitePlayerSight);
            _showFoodRot = ExtractBool(json, "showFoodRot", _showFoodRot);
            _ignoreFoodDecay = ExtractBool(json, "ignoreFoodDecay", _ignoreFoodDecay);
            _noCraftMaterials = ExtractBool(json, "noCraftMaterials", _noCraftMaterials);
            _unlockAllCraftMaterials = ExtractBool(json, "unlockAllCraftMaterials", _unlockAllCraftMaterials);
            _unlockAllCraftRecipes = ExtractBool(json, "unlockAllCraftRecipes", _unlockAllCraftRecipes);
            _customItemAmount = ExtractBool(json, "customItemAmount", _customItemAmount);
            _customItemEditor = ExtractBool(json, "customItemEditor", _customItemEditor);
            _customFoodEditor = ExtractBool(json, "customFoodEditor", _customFoodEditor);
            _customWeaponEditor = ExtractBool(json, "customWeaponEditor", _customWeaponEditor);
            _customGeneEditor = ExtractBool(json, "customGeneEditor", _customGeneEditor);
            _stethoscopeNoTargetLimit = ExtractBool(json, "stethoscopeNoTargetLimit", _stethoscopeNoTargetLimit);
            _ignoreTerrainMovement = ExtractBool(json, "ignoreTerrainMovement", _ignoreTerrainMovement);
            _optimizeDungeonVoidScaling = ExtractBool(json, "optimizeDungeonVoidScaling", _optimizeDungeonVoidScaling);
            _noTalkInterestLoss = ExtractBool(json, "noTalkInterestLoss", _noTalkInterestLoss);
            LoadAbilityCustomAttributesConfig(json);
            LoadKillGrowthConfig(json);
            LoadAutomationConfig(json);
            _modules.Moongate.LandholderPrivilegesEnabled =
                ExtractBool(json, "moongateLandholderPrivileges", true);
            _modules.Moongate.LoadUploadUpdateKeys(
                ConfigurationValueDocument.For(json).GetRawJson("moongateUploadUpdateKeys"));
            if (_modules.Nightly != null)
            {
                _modules.Nightly.AllowCurrencyGifts = ExtractBool(json, "nightlyAllowCurrencyGifts", false);
                _nightlyConfigPassthroughJson = _modules.Nightly.AllowCurrencyGifts ? "true" : "false";
            }
            else
            {
                _nightlyConfigPassthroughJson = ConfigurationValueDocument.For(json)
                    .GetRawJson("nightlyAllowCurrencyGifts");
            }
            _aiApiBase = ExtractString(json, "aiApiBase", _aiApiBase);
            _aiApiKey = ExtractString(json, "aiApiKey", _aiApiKey);
            _aiModelName = ExtractString(json, "aiModelName", _aiModelName);
            var hasOldReasoningEnabled = HasJsonValue(json, "aiReasoningEnabled");
            var oldReasoningEnabled = ExtractBool(json, "aiReasoningEnabled", false);
            var savedReasoningEffort = ExtractString(json, "aiReasoningEffort", hasOldReasoningEnabled ? (oldReasoningEnabled ? "medium" : "off") : "high");
            _aiReasoningEffortIndex = GetAiReasoningEffortIndex(savedReasoningEffort);
            _aiUseContext = ExtractBool(json, "aiUseContext", _aiUseContext);
            _aiAutoCompressContext = ExtractBool(json, "aiAutoCompressContext", _aiAutoCompressContext);
            _aiUseStreaming = ExtractBool(json, "aiUseStreaming", false);
            _aiUseToolStreaming = ExtractBool(json, "aiUseToolStreaming", false);
            _aiHttpTimeoutSeconds = Clamp(ExtractInt(json, "aiHttpTimeoutSeconds", AiHttpTimeoutDefaultSeconds), AiHttpTimeoutMinSeconds, AiHttpTimeoutMaxSeconds);
            _aiHttpTimeoutSecondsText = _aiHttpTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            _aiContextCompressThreshold = Clamp(ExtractInt(json, "aiContextCompressThreshold", _aiContextCompressThreshold), AiContextCompressionMinThreshold, AiContextCompressionMaxThreshold);
            _aiContextCompressThresholdText = _aiContextCompressThreshold.ToString(CultureInfo.InvariantCulture);
            LoadEmpPluginStatesFromConfig(json);

            SyncConfigTextFields();
            if (wasUnlockFrameRate && !_unlockFrameRate)
                RestoreFrameRateLimit();
            if (_unlockFrameRate)
                ApplyUnlockFrameRate();
            if (shouldRewriteConfig)
                SaveConfig();
            else
                _configLog = T("配置已读取", "Config loaded");
            ApplySavedEmpPluginStates(false);
        }
        catch (Exception ex)
        {
            SyncConfigTextFields();
            _configLog = T("读取配置失败: ", "Failed to load config: ") + ex.Message;
        }
    }
    private void ApplyDefaultConfigValues()
    {
        _language = NormalizeLanguage(DetectWindowsLanguage());
        _uiStyleIndex = 4;
        _uiAlpha = 0.9f;
        _uiFontSize = UiFontSizeDefault;
        _uiFontSizeText = UiFontSizeDefault.ToString(CultureInfo.InvariantCulture);
        UseStyleUiTextColor();
        _openKey = DefaultOpenKey;
        _forceGameUnfocus = true;
        _uiRoundedCorners = true;
        _modules.MainMenuInfo.SetEnabled(true);
        _modules.Watermark.ResetSettings();
        SetDisableCwlErrorNotification(false);
        _adaptiveUiScale = true;
        _customUiScale = 0f;
        _lowPerformanceMode = false;
        _unlockFrameRate = false;
        _invincibleMode = false;
        _invincibleModeIncludeParty = false;
        _ignoreBuffEffects = false;
        _ignoreBuffEffectsDebuff = true;
        _ignoreBuffEffectsBuff = false;
        _ignoreBuffEffectsIncludeParty = false;
        _ignoreBuffEffectsTrackedPartyMembers.Clear();
        _hostileThreatMarker = false;
        _hostileThreatBehaviorPrediction = true;
        _hostileThreatPredecisionLock = false;
        _modules.ThreatOverlay.ClearLockedDecisions();
        _showNpcMoreInfo = false;
        _showItemMoreInfo = false;
        _showBuffSpecificValues = false;
        _showBuffSpecificValuesIconFontSizeOffset = 0;
        _showBuffSpecificValuesTextFontSizeOffset = 0;
        _showItemPanelEnchantLevels = false;
        _showItemPanelItemValue = false;
        _showMainAbilityExperience = false;
        _showMainAbilityExperienceInSkillTracker = true;
        RefreshMainAbilityExperienceTracker(false);
        _equipmentComparison = false;
        DestroyEquipmentComparisonTooltip();
        _workbenchIngredientReadingOptimization = false;
        NonStandardCrafterIngredientOptimizer.Clear();
        CraftIngredientPickerPager.CloseActivePickers();
        NonStandardCrafterIngredientPager.DisableAndRestoreActive();
        _modules.Progression.ExperienceMultiplierEnabled = false;
        _modules.Progression.ExperienceMultiplierIncludePcFaction = true;
        _modules.Progression.CharacterLevelExperienceMultiplier = 1f;
        _modules.Progression.SkillExperienceMultiplier = 1f;
        _modules.Progression.MagicExperienceMultiplier = 1f;
        _modules.Progression.FoodPotentialGainMultiplier = 1f;
        _modules.Progression.TrainingPotentialGainMultiplier = 1f;
        SyncExperienceMultiplierTextFields();
        _modules.PlantHarvestMultiplier.Reset();
        _modules.IgnoreCropGrowthConditions.Reset();
        _modules.Progression.FoodRestoresSpEnabled = false;
        _modules.Progression.FoodRestoresSpPercent = 10;
        _modules.GuaranteedGatheringRewards.Reset();
        _modules.Progression.OptimizeMeleeHitChance = false;
        _modules.Progression.OptimizeMeleeHitChanceIncludeParty = true;
        _modules.Progression.PcFactionTrainerAllSkills = false;
        _unlimitedHomeResidentCap = false;
        _unlimitedPartyMemberCap = false;
        _unlimitedOfferingFaithPoints = false;
        _ignoreGodArtifactFaithRequirement = false;
        _shrineEffectSelection = false;
        _infiniteChargeAndAmmo = false;
        _rodStacking = false;
        _modules.RightClickInterrupt.Reset();
        _stealHandNoTargetLimit = false;
        _stealHandUndetectable = false;
        _modules.MerchantRefreshNoCost.Reset();
        _modules.AiInstruction.Reset();
        _modules.MerchantMonsterBall.Reset();
        _modules.SpecialNpcHatch.Reset();
        _modules.SpecialNpcCapture.Reset();
        _modules.FishingNoWait.Reset();
        _modules.GeneSynthesisNoWait.Reset();
        _modules.SleepWithoutSleepiness.Reset();
        _modules.AllPurposeWorkbench.Reset();
        _modules.Probability.ResetStoredConfiguration();
        if (_modules.Nightly != null)
        {
            _modules.Nightly.AllowCurrencyGifts = false;
            _nightlyConfigPassthroughJson = "false";
        }
        else
        {
            _nightlyConfigPassthroughJson = "";
        }
        _modules.CharacterProtection.Reset();
        _rodStackingTarget = null;
        _rodStackingSource = null;
        _rodStackingCandidatePage = 0;
        _showItemMoreInfoBasicInfo = true;
        _showItemMoreInfoGatheringThreshold = true;
        _showItemMoreInfoWeaponStats = true;
        _showItemMoreInfoEnchantments = true;
        _showItemMoreInfoPlantStats = true;
        _showItemMoreInfoPlantStatsExtended = true;
        _showItemMoreInfoFontSizeOffset = 0;
        ResetItemMoreInfoColors(false);
        _showNpcMoreInfoLevel = true;
        _showNpcMoreInfoIdentity = true;
        _showNpcMoreInfoRelationFaith = true;
        _showNpcMoreInfoVitals = true;
        _showNpcMoreInfoAttributes = true;
        _showNpcMoreInfoBuffs = true;
        _showNpcMoreInfoResists = true;
        _showNpcMoreInfoSkills = true;
        _showNpcMoreInfoAbilities = true;
        _showNpcMoreInfoFeats = true;
        _showNpcMoreInfoCombatSimulation = true;
        _showNpcMoreInfoOrder = DefaultNpcMoreInfoOrder;
        _showNpcMoreInfoRelationPerLine = 3;
        _showNpcMoreInfoVitalsPerLine = 4;
        _showNpcMoreInfoAttributesPerLine = 4;
        _showNpcMoreInfoBuffsPerLine = 5;
        _showNpcMoreInfoResistsPerLine = 5;
        _showNpcMoreInfoSkillsPerLine = 5;
        _showNpcMoreInfoAbilitiesPerLine = 5;
        _showNpcMoreInfoFeatsPerLine = 5;
        _showNpcMoreInfoFontSizeOffset = 0;
        _showNpcMoreInfoLevelExtraFontSize = 0;
        _showNpcMoreInfoIdentityExtraFontSize = 0;
        _showNpcMoreInfoRelationExtraFontSize = 0;
        _showNpcMoreInfoVitalsExtraFontSize = 4;
        _showNpcMoreInfoAttributesExtraFontSize = 0;
        _showNpcMoreInfoBuffsExtraFontSize = 0;
        _showNpcMoreInfoResistsExtraFontSize = 0;
        _showNpcMoreInfoSkillsExtraFontSize = 0;
        _showNpcMoreInfoAbilitiesExtraFontSize = 0;
        _showNpcMoreInfoFeatsExtraFontSize = 0;
        _showNpcMoreInfoCombatExtraFontSize = 0;
        ResetNpcMoreInfoColors(false);
        _infinitePlayerSight = false;
        _showFoodRot = false;
        _ignoreFoodDecay = false;
        _noCraftMaterials = false;
        _unlockAllCraftMaterials = false;
        _unlockAllCraftRecipes = false;
        _customItemAmount = false;
        _customItemEditor = false;
        _customFoodEditor = false;
        _customWeaponEditor = false;
        _customGeneEditor = false;
        _stethoscopeNoTargetLimit = false;
        _ignoreTerrainMovement = false;
        _optimizeDungeonVoidScaling = false;
        _noTalkInterestLoss = false;
        _abilityChanceOverrides.Clear();
        _abilityPowerOverrides.Clear();
        _abilityCostOverrides.Clear();
        ResetKillGrowthConfig();
        ResetAutomationConfig();
        _modules.Moongate.ResetCloudApiSettings();
        _aiApiBase = "https://api.openai.com/v1";
        _aiApiKey = "";
        _aiModelName = "";
        _aiReasoningEffortIndex = 3;
        _aiUseContext = true;
        _aiAutoCompressContext = true;
        _aiUseStreaming = false;
        _aiUseToolStreaming = false;
        _aiHttpTimeoutSeconds = AiHttpTimeoutDefaultSeconds;
        _aiHttpTimeoutSecondsText = _aiHttpTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _aiContextCompressThreshold = AiContextCompressionDefaultThreshold;
        _aiContextCompressThresholdText = _aiContextCompressThreshold.ToString(CultureInfo.InvariantCulture);
        SyncConfigTextFields();
    }
    private static string DetectWindowsLanguage()
    {
        try
        {
            var name = CultureInfo.InstalledUICulture.Name;
            if (!string.IsNullOrEmpty(name) && name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "zh";
            if (!string.IsNullOrEmpty(name) && name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return "ja";
            if (!string.IsNullOrEmpty(name) && name.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                return "ru";
        }
        catch
        {
        }
        return "en";
    }
    private string NormalizeLanguage(string language)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            return "en";
        if (string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language, "jp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language, "japanese", StringComparison.OrdinalIgnoreCase))
            return "ja";
        if (string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language, "rus", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language, "russian", StringComparison.OrdinalIgnoreCase))
            return "ru";
        return "zh";
    }
}
