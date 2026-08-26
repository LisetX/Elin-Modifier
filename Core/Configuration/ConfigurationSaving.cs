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
    private void SaveConfig(bool updateLog = true, bool saveAutomationScripts = true)
    {
        try
        {
            var path = GetConfigPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _language = NormalizeLanguage(_language);
            _uiStyleIndex = Clamp(_uiStyleIndex, 0, UiStyleNamesZh.Length - 1);
            _uiAlpha = Clamp(_uiAlpha, 0.2f, 1f);
            _uiFontSize = NormalizeUiFontSize(_uiFontSize);
            _uiTextColor.r = Clamp(_uiTextColor.r, 0f, 1f);
            _uiTextColor.g = Clamp(_uiTextColor.g, 0f, 1f);
            _uiTextColor.b = Clamp(_uiTextColor.b, 0f, 1f);
            _uiTextColor.a = 1f;
            ApplyAiHttpTimeoutSecondsText(false);
            ApplyKillGrowthConfigTexts();
            if (saveAutomationScripts)
                SaveAutomationScriptFiles();
            var fontColor = GetActiveUiTextColor();
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"acceptedTermsVersion\": \"" + EscapeJson(AcceptedTermsVersion) + "\",");
            sb.AppendLine("  \"mainMenuInfoDefaultOnMigrated\": true,");
            sb.AppendLine("  \"elinModifierWatermarkDefaultOnMigrated\": true,");
            sb.AppendLine("  \"showMainMenuInfo\": " + (ShowMainMenuInfo ? "true" : "false") + ",");
            sb.AppendLine("  \"showElinModifierWatermark\": " + (_modules.Watermark.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"elinModifierWatermarkPositionLocked\": " + (_modules.Watermark.PositionLocked ? "true" : "false") + ",");
            sb.AppendLine("  \"elinModifierWatermarkGameErrorNotification\": " + (_modules.Watermark.GameErrorNotificationEnabled ? "true" : "false") + ",");
            sb.AppendLine("  \"elinModifierWatermarkSuppressWarningNotification\": " + (_modules.Watermark.SuppressWarningNotificationEnabled ? "true" : "false") + ",");
            sb.AppendLine("  \"elinModifierWatermarkPositionX\": " + _modules.Watermark.PositionX.ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"elinModifierWatermarkPositionY\": " + _modules.Watermark.PositionY.ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"disableCwlErrorNotification\": " + (DisableCwlErrorNotification ? "true" : "false") + ",");
            sb.AppendLine("  \"adaptiveUiScale\": " + (_adaptiveUiScale ? "true" : "false") + ",");
            sb.AppendLine("  \"customUiScale\": " + NormalizeCustomUiScale(_customUiScale).ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"forceGameUnfocus\": " + (_forceGameUnfocus ? "true" : "false") + ",");
            sb.AppendLine("  \"uiRoundedCorners\": " + (_uiRoundedCorners ? "true" : "false") + ",");
            sb.AppendLine("  \"language\": \"" + EscapeJson(_language) + "\",");
            sb.AppendLine("  \"uiStyleIndex\": " + _uiStyleIndex + ",");
            sb.AppendLine("  \"uiStyleName\": \"" + EscapeJson(CurrentUiStyleName()) + "\",");
            sb.AppendLine("  \"uiAlpha\": " + _uiAlpha.ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"uiFontSize\": " + _uiFontSize.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"uiFontColorMode\": \"" + (_uiTextColorFollowsStyle ? "style" : "custom") + "\",");
            sb.AppendLine("  \"uiFontColorHex\": \"" + ColorToHex(fontColor) + "\",");
            sb.AppendLine("  \"openKey\": \"" + EscapeJson(GetKeyLabel(_openKey)) + "\",");
            sb.AppendLine();

            sb.AppendLine("  \"lowPerformanceMode\": " + (_lowPerformanceMode ? "true" : "false") + ",");
            sb.AppendLine("  \"unlockFrameRate\": " + (_unlockFrameRate ? "true" : "false") + ",");
            sb.AppendLine("  \"invincibleMode\": " + (_invincibleMode ? "true" : "false") + ",");
            sb.AppendLine("  \"invincibleModeIncludeParty\": " + (_invincibleModeIncludeParty ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreBuffEffects\": " + (_ignoreBuffEffects ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreBuffEffectsDebuff\": " + (_ignoreBuffEffectsDebuff ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreBuffEffectsBuff\": " + (_ignoreBuffEffectsBuff ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreBuffEffectsIncludeParty\": " + (_ignoreBuffEffectsIncludeParty ? "true" : "false") + ",");
            sb.AppendLine("  \"hostileThreatMarker\": " + (GetAutomationPersistedHostileThreatMarker() ? "true" : "false") + ",");
            sb.AppendLine("  \"hostileThreatBehaviorPrediction\": " + (_hostileThreatBehaviorPrediction ? "true" : "false") + ",");
            sb.AppendLine("  \"hostileThreatPredecisionLock\": " + (_hostileThreatPredecisionLock ? "true" : "false") + ",");
            sb.AppendLine();

            sb.AppendLine("  \"showNpcMoreInfo\": " + (_showNpcMoreInfo ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoLevel\": " + (_showNpcMoreInfoLevel ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoIdentity\": " + (_showNpcMoreInfoIdentity ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoAdditionalIdentity\": " + (_showNpcMoreInfoRelationFaith ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoVitals\": " + (_showNpcMoreInfoVitals ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoAttributes\": " + (_showNpcMoreInfoAttributes ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoBuffs\": " + (_showNpcMoreInfoBuffs ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoResists\": " + (_showNpcMoreInfoResists ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoSkills\": " + (_showNpcMoreInfoSkills ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoAbilities\": " + (_showNpcMoreInfoAbilities ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoFeats\": " + (_showNpcMoreInfoFeats ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoCombatSimulation\": " + (_showNpcMoreInfoCombatSimulation ? "true" : "false") + ",");
            sb.AppendLine("  \"showNpcMoreInfoOrder\": \"" + EscapeJson(NormalizeNpcMoreInfoOrder(_showNpcMoreInfoOrder)) + "\",");
            sb.AppendLine("  \"showNpcMoreInfoAdditionalIdentityPerLine\": " + Clamp(_showNpcMoreInfoRelationPerLine, 1, 99).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoVitalsPerLine\": " + Clamp(_showNpcMoreInfoVitalsPerLine, 1, 99).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoAttributesPerLine\": " + Clamp(_showNpcMoreInfoAttributesPerLine, 1, 99).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoBuffsPerLine\": " + Clamp(_showNpcMoreInfoBuffsPerLine, 1, 99).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoResistsPerLine\": " + Clamp(_showNpcMoreInfoResistsPerLine, 1, 99).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoSkillsPerLine\": " + Clamp(_showNpcMoreInfoSkillsPerLine, 1, 99).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoAbilitiesPerLine\": " + Clamp(_showNpcMoreInfoAbilitiesPerLine, 1, 99).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoFeatsPerLine\": " + Clamp(_showNpcMoreInfoFeatsPerLine, 1, 99).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoFontSizeOffset\": " + Clamp(_showNpcMoreInfoFontSizeOffset, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoLevelExtraFontSize\": " + Clamp(_showNpcMoreInfoLevelExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoIdentityExtraFontSize\": " + Clamp(_showNpcMoreInfoIdentityExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoAdditionalIdentityExtraFontSize\": " + Clamp(_showNpcMoreInfoRelationExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoVitalsExtraFontSize\": " + Clamp(_showNpcMoreInfoVitalsExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoAttributesExtraFontSize\": " + Clamp(_showNpcMoreInfoAttributesExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoBuffsExtraFontSize\": " + Clamp(_showNpcMoreInfoBuffsExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoResistsExtraFontSize\": " + Clamp(_showNpcMoreInfoResistsExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoSkillsExtraFontSize\": " + Clamp(_showNpcMoreInfoSkillsExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoAbilitiesExtraFontSize\": " + Clamp(_showNpcMoreInfoAbilitiesExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoFeatsExtraFontSize\": " + Clamp(_showNpcMoreInfoFeatsExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showNpcMoreInfoCombatExtraFontSize\": " + Clamp(_showNpcMoreInfoCombatExtraFontSize, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"npcMoreInfoLevelColor\": \"" + EscapeJson(_npcMoreInfoLevelColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoIdentityColor\": \"" + EscapeJson(_npcMoreInfoIdentityColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoAdditionalIdentityColor\": \"" + EscapeJson(_npcMoreInfoRelationColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoHpColor\": \"" + EscapeJson(_npcMoreInfoHpColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoMpColor\": \"" + EscapeJson(_npcMoreInfoMpColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoSpColor\": \"" + EscapeJson(_npcMoreInfoSpColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoExpColor\": \"" + EscapeJson(_npcMoreInfoExpColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoSpeedColor\": \"" + EscapeJson(_npcMoreInfoSpeedColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoDvColor\": \"" + EscapeJson(_npcMoreInfoDvColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoPvColor\": \"" + EscapeJson(_npcMoreInfoPvColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoSkillColor\": \"" + EscapeJson(_npcMoreInfoSkillColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoAbilityColor\": \"" + EscapeJson(_npcMoreInfoAbilityColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoFeatColor\": \"" + EscapeJson(_npcMoreInfoFeatColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoCombatColor\": \"" + EscapeJson(_npcMoreInfoCombatColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoResistColor\": \"" + EscapeJson(_npcMoreInfoResistColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoAttributeColor\": \"" + EscapeJson(_npcMoreInfoAttributeColor) + "\",");
            sb.AppendLine("  \"npcMoreInfoBuffColor\": \"" + EscapeJson(_npcMoreInfoBuffColor) + "\",");
            sb.AppendLine("  \"npcCompendiumQuickLookup\": " + (_modules.NpcInfo.QuickLookupEnabled ? "true" : "false") + ",");
            sb.AppendLine();

            sb.AppendLine("  \"showItemMoreInfo\": " + (_showItemMoreInfo ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemMoreInfoBasicInfo\": " + (_showItemMoreInfoBasicInfo ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemMoreInfoGatheringThreshold\": " + (_showItemMoreInfoGatheringThreshold ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemMoreInfoWeaponStats\": " + (_showItemMoreInfoWeaponStats ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemMoreInfoEnchantments\": " + (_showItemMoreInfoEnchantments ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemMoreInfoPlantStats\": " + (_showItemMoreInfoPlantStats ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemMoreInfoPlantStatsExtended\": " + (_showItemMoreInfoPlantStatsExtended ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemMoreInfoFontSizeOffset\": " + Clamp(_showItemMoreInfoFontSizeOffset, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"itemMoreInfoBasicInfoColor\": \"" + EscapeJson(_itemMoreInfoBasicInfoColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoGatheringToolColor\": \"" + EscapeJson(_itemMoreInfoGatheringToolColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoGatheringThresholdColor\": \"" + EscapeJson(_itemMoreInfoGatheringThresholdColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoRarityCrudeColor\": \"" + EscapeJson(_itemMoreInfoRarityCrudeColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoRarityNormalColor\": \"" + EscapeJson(_itemMoreInfoRarityNormalColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoRaritySuperiorColor\": \"" + EscapeJson(_itemMoreInfoRaritySuperiorColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoRarityLegendaryColor\": \"" + EscapeJson(_itemMoreInfoRarityLegendaryColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoRarityMythicalColor\": \"" + EscapeJson(_itemMoreInfoRarityMythicalColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoRarityArtifactColor\": \"" + EscapeJson(_itemMoreInfoRarityArtifactColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoWeaponStatsColor\": \"" + EscapeJson(_itemMoreInfoWeaponStatsColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoEnchantColor\": \"" + EscapeJson(_itemMoreInfoEnchantColor) + "\",");
            sb.AppendLine("  \"itemMoreInfoPlantStatsColor\": \"" + EscapeJson(_itemMoreInfoPlantStatsColor) + "\",");
            sb.AppendLine("  \"showBuffSpecificValues\": " + (_showBuffSpecificValues ? "true" : "false") + ",");
            sb.AppendLine("  \"showBuffSpecificValuesIconFontSizeOffset\": " + Clamp(_showBuffSpecificValuesIconFontSizeOffset, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showBuffSpecificValuesTextFontSizeOffset\": " + Clamp(_showBuffSpecificValuesTextFontSizeOffset, -8, 8).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"showItemPanelEnchantLevels\": " + (_showItemPanelEnchantLevels ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemPanelItemValue\": " + (_showItemPanelItemValue ? "true" : "false") + ",");
            sb.AppendLine("  \"showItemPanelMilkBonus\": " + (_showItemPanelMilkBonus ? "true" : "false") + ",");
            sb.AppendLine("  \"showMainAbilityExperience\": " + (_showMainAbilityExperience ? "true" : "false") + ",");
            sb.AppendLine("  \"showMainAbilityExperienceInSkillTracker\": " + (_showMainAbilityExperienceInSkillTracker ? "true" : "false") + ",");
            sb.AppendLine("  \"oneClickQuestCompletion\": " + (_modules.OneClickQuestCompletion.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"equipmentComparison\": " + (_equipmentComparison ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreFriendlyFire\": " + (_modules.CharacterProtection.IgnoreFriendlyFire ? "true" : "false") + ",");
            sb.AppendLine("  \"workbenchIngredientReadingOptimization\": " + (_workbenchIngredientReadingOptimization ? "true" : "false") + ",");
            sb.AppendLine("  \"experienceMultiplierEnabled\": " + (_modules.Progression.ExperienceMultiplierEnabled ? "true" : "false") + ",");
            sb.AppendLine("  \"experienceMultiplierIncludePcFaction\": " + (_modules.Progression.ExperienceMultiplierIncludePcFaction ? "true" : "false") + ",");
            sb.AppendLine("  \"characterLevelExperienceMultiplier\": " + _modules.Progression.CharacterLevelExperienceMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"mainAbilityExperienceMultiplier\": " + _modules.Progression.MainAbilityExperienceMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"skillExperienceMultiplier\": " + _modules.Progression.SkillExperienceMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"magicExperienceMultiplier\": " + _modules.Progression.MagicExperienceMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"foodPotentialGainMultiplier\": " + _modules.Progression.FoodPotentialGainMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"trainingPotentialGainMultiplier\": " + _modules.Progression.TrainingPotentialGainMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"plantHarvestMultiplierEnabled\": " + (_modules.PlantHarvestMultiplier.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"cropHarvestMultiplier\": " + _modules.PlantHarvestMultiplier.CropHarvestMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"seedReapingMultiplier\": " + _modules.PlantHarvestMultiplier.SeedReapingMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"ignoreCropGrowthConditions\": " + (_modules.IgnoreCropGrowthConditions.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"allFeatsLearnable\": " + (_modules.AllFeatsLearnable.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"characterPanelGenes\": " + (_modules.CharacterPanelGenes.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"allowPcGeneImplant\": " + (_modules.AllowPcGeneImplant.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"predationGeneSelection\": " + (_modules.PredationGeneSelection.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"allowCurrencyGifts\": " + (_modules.AllowCurrencyGifts.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"foodRestoresSpEnabled\": " + (_modules.Progression.FoodRestoresSpEnabled ? "true" : "false") + ",");
            sb.AppendLine("  \"foodRestoresSpPercent\": " + Clamp(_modules.Progression.FoodRestoresSpPercent, 1, 100).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"dismantleAlwaysReturnsMaterials\": " + (_modules.GuaranteedGatheringRewards.DismantleAlwaysReturnsMaterials ? "true" : "false") + ",");
            sb.AppendLine("  \"useVanillaDismantleMechanism\": " + (_modules.GuaranteedGatheringRewards.UseVanillaDismantleMechanism ? "true" : "false") + ",");
            sb.AppendLine("  \"dismantlingAlwaysLearnsRecipe\": " + (_modules.GuaranteedGatheringRewards.DismantlingAlwaysLearnsRecipe ? "true" : "false") + ",");
            sb.AppendLine("  \"optimizeMeleeHitChance\": " + (_modules.Progression.OptimizeMeleeHitChance ? "true" : "false") + ",");
            sb.AppendLine("  \"optimizeMeleeHitChanceIncludeParty\": " + (_modules.Progression.OptimizeMeleeHitChanceIncludeParty ? "true" : "false") + ",");
            sb.AppendLine("  \"pcFactionTrainerAllSkills\": " + (_modules.Progression.PcFactionTrainerAllSkills ? "true" : "false") + ",");
            sb.AppendLine("  \"unlimitedHomeResidentCap\": " + (_unlimitedHomeResidentCap ? "true" : "false") + ",");
            sb.AppendLine("  \"unlimitedPartyMemberCap\": " + (_unlimitedPartyMemberCap ? "true" : "false") + ",");
            sb.AppendLine("  \"unlimitedOfferingFaithPoints\": " + (_unlimitedOfferingFaithPoints ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreGodArtifactFaithRequirement\": " + (_ignoreGodArtifactFaithRequirement ? "true" : "false") + ",");
            sb.AppendLine("  \"shrineEffectSelection\": " + (_shrineEffectSelection ? "true" : "false") + ",");
            sb.AppendLine("  \"infiniteChargeAndAmmo\": " + (_infiniteChargeAndAmmo ? "true" : "false") + ",");
            sb.AppendLine("  \"chargeStacking\": " + (_rodStacking ? "true" : "false") + ",");
            sb.AppendLine("  \"rightClickInterruptOperation\": " + (_modules.RightClickInterrupt.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"stealHandNoTargetLimit\": " + (_stealHandNoTargetLimit ? "true" : "false") + ",");
            sb.AppendLine("  \"stealHandUndetectable\": " + (_stealHandUndetectable ? "true" : "false") + ",");
            sb.AppendLine("  \"merchantRefreshNoCost\": " + (_modules.MerchantRefreshNoCost.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"aiInstruction\": " + (_modules.AiInstruction.Enabled ? "true" : "false") + ",");
            _modules.AiInstruction.AppendAutoCombatConfiguration(sb);
            sb.AppendLine("  \"merchantAlwaysStocksMonsterBall\": " + (_modules.MerchantMonsterBall.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"merchantMonsterBallLevelOptimization\": " + (_modules.MerchantMonsterBall.LevelOptimizationEnabled ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreSpecialNpcHatchRestriction\": " + (_modules.SpecialNpcHatch.IgnoreRestriction ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreSpecialNpcCaptureRestriction\": " + (_modules.SpecialNpcCapture.IgnoreRestriction ? "true" : "false") + ",");
            sb.AppendLine("  \"affinityOnlyIncrease\": " + (_modules.CharacterProtection.AffinityOnlyIncrease ? "true" : "false") + ",");
            sb.AppendLine("  \"karmaOnlyIncrease\": " + (_modules.CharacterProtection.KarmaOnlyIncrease ? "true" : "false") + ",");
            sb.AppendLine("  \"attackCannotBeInterrupted\": " + (_modules.CharacterProtection.AttackCannotBeInterrupted ? "true" : "false") + ",");
            sb.AppendLine("  \"attackCannotBeInterruptedIncludeParty\": " + (_modules.CharacterProtection.AttackCannotBeInterruptedIncludeParty ? "true" : "false") + ",");
            sb.AppendLine("  \"fishingNoWait\": " + (_modules.FishingNoWait.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"geneSynthesisNoWait\": " + (_modules.GeneSynthesisNoWait.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"sleepWithoutSleepiness\": " + (_modules.SleepWithoutSleepiness.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"allPurposeWorkbench\": " + (_modules.AllPurposeWorkbench.Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"allPurposeWorkbenchDefaultTabType\": \"" + EscapeJson(_modules.AllPurposeWorkbench.DefaultTabTypeConfigValue) + "\",");
            sb.AppendLine();

            sb.AppendLine("  \"infinitePlayerSight\": " + (GetAutomationPersistedInfinitePlayerSight() ? "true" : "false") + ",");
            sb.AppendLine("  \"showFoodRot\": " + (_showFoodRot ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreFoodDecay\": " + (_ignoreFoodDecay ? "true" : "false") + ",");
            sb.AppendLine("  \"noCraftMaterials\": " + (_noCraftMaterials ? "true" : "false") + ",");
            sb.AppendLine("  \"unlockAllCraftMaterials\": " + (_unlockAllCraftMaterials ? "true" : "false") + ",");
            sb.AppendLine("  \"unlockAllCraftRecipes\": " + (_unlockAllCraftRecipes ? "true" : "false") + ",");
            sb.AppendLine("  \"customItemAmount\": " + (_customItemAmount ? "true" : "false") + ",");
            sb.AppendLine("  \"customItemEditor\": " + (_customItemEditor ? "true" : "false") + ",");
            sb.AppendLine("  \"customFoodEditor\": " + (_customFoodEditor ? "true" : "false") + ",");
            sb.AppendLine("  \"customWeaponEditor\": " + (_customWeaponEditor ? "true" : "false") + ",");
            sb.AppendLine("  \"customGeneEditor\": " + (_customGeneEditor ? "true" : "false") + ",");
            sb.AppendLine("  \"stethoscopeNoTargetLimit\": " + (_stethoscopeNoTargetLimit ? "true" : "false") + ",");
            sb.AppendLine("  \"ignoreTerrainMovement\": " + (_ignoreTerrainMovement ? "true" : "false") + ",");
            sb.AppendLine("  \"optimizeDungeonVoidScaling\": " + (_optimizeDungeonVoidScaling ? "true" : "false") + ",");
            sb.AppendLine("  \"noTalkInterestLoss\": " + (_noTalkInterestLoss ? "true" : "false") + ",");
            sb.AppendLine();
            AppendAbilityCustomAttributesConfigJson(sb);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("  \"probabilityModule\": " + _modules.Probability.StoredConfigurationJson + ",");
            sb.AppendLine();
            AppendKillGrowthConfigJson(sb);
            sb.AppendLine();
            AppendAutomationConfigJson(sb);
            sb.AppendLine();

            sb.AppendLine("  \"moongateLandholderPrivileges\": " + (_modules.Moongate.LandholderPrivilegesEnabled ? "true" : "false") + ",");
            sb.AppendLine("  \"moongateUploadUpdateKeys\": " + _modules.Moongate.BuildUploadUpdateKeysJson() + ",");
            sb.AppendLine();

            var nightlyFixSelfTalkBugConfigJson = _modules.Nightly != null
                ? (_modules.Nightly.FixSelfTalkBug ? "true" : "false")
                : _nightlyFixSelfTalkBugConfigPassthroughJson;
            if (!string.IsNullOrWhiteSpace(nightlyFixSelfTalkBugConfigJson))
                sb.AppendLine("  \"nightlyFixSelfTalkBug\": " + nightlyFixSelfTalkBugConfigJson + ",");
            if (!string.IsNullOrWhiteSpace(nightlyFixSelfTalkBugConfigJson))
                sb.AppendLine();

            sb.AppendLine("  \"aiApiBase\": \"" + EscapeJson(_aiApiBase) + "\",");
            sb.AppendLine("  \"aiApiKey\": \"" + EscapeJson(_aiApiKey) + "\",");
            sb.AppendLine("  \"aiModelName\": \"" + EscapeJson(_aiModelName) + "\",");
            sb.AppendLine("  \"aiReasoningEffort\": \"" + EscapeJson(GetAiReasoningEffort()) + "\",");
            sb.AppendLine("  \"aiUseContext\": " + (_aiUseContext ? "true" : "false") + ",");
            sb.AppendLine("  \"aiAutoCompressContext\": " + (_aiAutoCompressContext ? "true" : "false") + ",");
            sb.AppendLine("  \"aiUseStreaming\": " + (_aiUseStreaming ? "true" : "false") + ",");
            sb.AppendLine("  \"aiUseToolStreaming\": " + (_aiUseToolStreaming ? "true" : "false") + ",");
            sb.AppendLine("  \"aiHttpTimeoutSeconds\": " + _aiHttpTimeoutSeconds.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"aiContextCompressThreshold\": " + _aiContextCompressThreshold.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine();
            AppendEmpPluginConfigJson(sb);
            sb.AppendLine("}");
            _modules.ConfigurationStorage.WriteAllTextAtomic(path, sb.ToString(), Encoding.UTF8);
            SyncConfigTextFields();
            if (updateLog)
                _configLog = T("配置已保存", "Config saved");
        }
        catch (Exception ex)
        {
            if (updateLog)
                _configLog = T("保存配置失败: ", "Failed to save config: ") + ex.Message;
        }
    }
    private void SyncConfigTextFields()
    {
        _uiAlphaText = _uiAlpha.ToString("0.00", CultureInfo.InvariantCulture);
        _uiFontSizeText = GetEffectiveUiFontSize().ToString(CultureInfo.InvariantCulture);
        _uiTextColorHexText = ColorToHex(GetActiveUiTextColor());
        _openKeyText = GetKeyLabel(_openKey);
    }
}
