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
    private static string EscapeJson(string value)
    {
        if (value == null)
            return "";
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }
    private static string ExtractString(string json, string name, string fallback)
    {
        return ConfigurationValueDocument.For(json).GetString(name, fallback);
    }
    private static bool HasJsonValue(string json, string name)
    {
        return ConfigurationValueDocument.For(json).Contains(name);
    }
    private static bool HasOptimizedConfigOrder(string json)
    {
        if (string.IsNullOrEmpty(json))
            return false;

        var acceptedTermsVersion = json.IndexOf("\"acceptedTermsVersion\"", StringComparison.Ordinal);
        var watermarkDefaultMigrated = json.IndexOf("\"elinModifierWatermarkDefaultOnMigrated\"", StringComparison.Ordinal);
        var showMainMenuInfo = json.IndexOf("\"showMainMenuInfo\"", StringComparison.Ordinal);
        var showElinModifierWatermark = json.IndexOf("\"showElinModifierWatermark\"", StringComparison.Ordinal);
        var watermarkPositionLocked = json.IndexOf("\"elinModifierWatermarkPositionLocked\"", StringComparison.Ordinal);
        var watermarkGameErrorNotification = json.IndexOf("\"elinModifierWatermarkGameErrorNotification\"", StringComparison.Ordinal);
        var watermarkSuppressWarningNotification = json.IndexOf("\"elinModifierWatermarkSuppressWarningNotification\"", StringComparison.Ordinal);
        var watermarkPositionX = json.IndexOf("\"elinModifierWatermarkPositionX\"", StringComparison.Ordinal);
        var watermarkPositionY = json.IndexOf("\"elinModifierWatermarkPositionY\"", StringComparison.Ordinal);
        var disableCwlErrorNotification = json.IndexOf("\"disableCwlErrorNotification\"", StringComparison.Ordinal);
        var language = json.IndexOf("\"language\"", StringComparison.Ordinal);
        var lowPerformance = json.IndexOf("\"lowPerformanceMode\"", StringComparison.Ordinal);
        var npcMain = json.IndexOf("\"showNpcMoreInfo\"", StringComparison.Ordinal);
        var npcLast = json.IndexOf("\"npcMoreInfoBuffColor\"", StringComparison.Ordinal);
        var itemMain = json.IndexOf("\"showItemMoreInfo\"", StringComparison.Ordinal);
        var itemLast = json.IndexOf("\"itemMoreInfoPlantStatsColor\"", StringComparison.Ordinal);
        var buffDetails = json.IndexOf("\"showBuffSpecificValues\"", StringComparison.Ordinal);
        var itemPanelValue = json.IndexOf("\"showItemPanelItemValue\"", StringComparison.Ordinal);
        var mainAbilityExperience = json.IndexOf("\"showMainAbilityExperience\"", StringComparison.Ordinal);
        var mainAbilityExperienceInSkillTracker = json.IndexOf("\"showMainAbilityExperienceInSkillTracker\"", StringComparison.Ordinal);
        var equipmentComparison = json.IndexOf("\"equipmentComparison\"", StringComparison.Ordinal);
        var ignoreFriendlyFire = json.IndexOf("\"ignoreFriendlyFire\"", StringComparison.Ordinal);
        var workbenchIngredientReadingOptimization = json.IndexOf("\"workbenchIngredientReadingOptimization\"", StringComparison.Ordinal);
        var experienceMultiplier = json.IndexOf("\"experienceMultiplierEnabled\"", StringComparison.Ordinal);
        var experienceMultiplierLast = json.IndexOf("\"trainingPotentialGainMultiplier\"", StringComparison.Ordinal);
        var plantHarvestMultiplier = json.IndexOf("\"plantHarvestMultiplierEnabled\"", StringComparison.Ordinal);
        var plantHarvestMultiplierLast = json.IndexOf("\"seedReapingMultiplier\"", StringComparison.Ordinal);
        var foodRestoresSp = json.IndexOf("\"foodRestoresSpEnabled\"", StringComparison.Ordinal);
        var foodRestoresSpLast = json.IndexOf("\"foodRestoresSpPercent\"", StringComparison.Ordinal);
        var optimizeMeleeHitChance = json.IndexOf("\"optimizeMeleeHitChance\"", StringComparison.Ordinal);
        var optimizeMeleeHitChanceIncludeParty = json.IndexOf("\"optimizeMeleeHitChanceIncludeParty\"", StringComparison.Ordinal);
        var pcFactionTrainerAllSkills = json.IndexOf("\"pcFactionTrainerAllSkills\"", StringComparison.Ordinal);
        var unlimitedHomeResidentCap = json.IndexOf("\"unlimitedHomeResidentCap\"", StringComparison.Ordinal);
        var unlimitedPartyMemberCap = json.IndexOf("\"unlimitedPartyMemberCap\"", StringComparison.Ordinal);
        var unlimitedOfferingFaithPoints = json.IndexOf("\"unlimitedOfferingFaithPoints\"", StringComparison.Ordinal);
        var ignoreGodArtifactFaithRequirement = json.IndexOf("\"ignoreGodArtifactFaithRequirement\"", StringComparison.Ordinal);
        var shrineEffectSelection = json.IndexOf("\"shrineEffectSelection\"", StringComparison.Ordinal);
        var infiniteChargeAndAmmo = json.IndexOf("\"infiniteChargeAndAmmo\"", StringComparison.Ordinal);
        var chargeStacking = json.IndexOf("\"chargeStacking\"", StringComparison.Ordinal);
        var rightClickInterruptOperation = json.IndexOf("\"rightClickInterruptOperation\"", StringComparison.Ordinal);
        var stealHandNoTargetLimit = json.IndexOf("\"stealHandNoTargetLimit\"", StringComparison.Ordinal);
        var stealHandUndetectable = json.IndexOf("\"stealHandUndetectable\"", StringComparison.Ordinal);
        var merchantAlwaysStocksMonsterBall = json.IndexOf("\"merchantAlwaysStocksMonsterBall\"", StringComparison.Ordinal);
        var merchantMonsterBallLevelOptimization = json.IndexOf("\"merchantMonsterBallLevelOptimization\"", StringComparison.Ordinal);
        var ignoreSpecialNpcHatchRestriction = json.IndexOf("\"ignoreSpecialNpcHatchRestriction\"", StringComparison.Ordinal);
        var ignoreSpecialNpcCaptureRestriction = json.IndexOf("\"ignoreSpecialNpcCaptureRestriction\"", StringComparison.Ordinal);
        var affinityOnlyIncrease = json.IndexOf("\"affinityOnlyIncrease\"", StringComparison.Ordinal);
        var karmaOnlyIncrease = json.IndexOf("\"karmaOnlyIncrease\"", StringComparison.Ordinal);
        var attackCannotBeInterrupted = json.IndexOf("\"attackCannotBeInterrupted\"", StringComparison.Ordinal);
        var attackCannotBeInterruptedIncludeParty = json.IndexOf("\"attackCannotBeInterruptedIncludeParty\"", StringComparison.Ordinal);
        var fishingNoWait = json.IndexOf("\"fishingNoWait\"", StringComparison.Ordinal);
        var geneSynthesisNoWait = json.IndexOf("\"geneSynthesisNoWait\"", StringComparison.Ordinal);
        var sleepWithoutSleepiness = json.IndexOf("\"sleepWithoutSleepiness\"", StringComparison.Ordinal);
        var allPurposeWorkbench = json.IndexOf("\"allPurposeWorkbench\"", StringComparison.Ordinal);
        var abilityCustomAttributes = json.IndexOf("\"abilityCustomAttributes\"", StringComparison.Ordinal);
        var probabilityModule = json.IndexOf("\"probabilityModule\"", StringComparison.Ordinal);
        var killGrowth = json.IndexOf("\"killGrowth\"", StringComparison.Ordinal);
        var automation = json.IndexOf("\"automation\"", StringComparison.Ordinal);
        var moongateLandholderPrivileges = json.IndexOf("\"moongateLandholderPrivileges\"", StringComparison.Ordinal);
        var moongateUploadUpdateKeys = json.IndexOf("\"moongateUploadUpdateKeys\"", StringComparison.Ordinal);
        var ai = json.IndexOf("\"aiApiBase\"", StringComparison.Ordinal);
        var emp = json.IndexOf("\"empPlugins\"", StringComparison.Ordinal);

        return acceptedTermsVersion >= 0 &&
               acceptedTermsVersion < watermarkDefaultMigrated &&
               watermarkDefaultMigrated >= 0 &&
               watermarkDefaultMigrated < showMainMenuInfo &&
               showMainMenuInfo < showElinModifierWatermark &&
               showElinModifierWatermark < watermarkPositionLocked &&
               watermarkPositionLocked < watermarkGameErrorNotification &&
               watermarkGameErrorNotification < watermarkSuppressWarningNotification &&
               watermarkSuppressWarningNotification < watermarkPositionX &&
               watermarkPositionX < watermarkPositionY &&
               watermarkPositionY < disableCwlErrorNotification &&
               disableCwlErrorNotification < language &&
               language < lowPerformance &&
               lowPerformance < npcMain &&
               npcMain < npcLast &&
               npcLast < itemMain &&
               itemMain < itemLast &&
               itemLast < buffDetails &&
               buffDetails < itemPanelValue &&
               itemPanelValue < mainAbilityExperience &&
               mainAbilityExperience < mainAbilityExperienceInSkillTracker &&
               mainAbilityExperienceInSkillTracker < equipmentComparison &&
               equipmentComparison < ignoreFriendlyFire &&
               ignoreFriendlyFire < workbenchIngredientReadingOptimization &&
               workbenchIngredientReadingOptimization < experienceMultiplier &&
               experienceMultiplier < experienceMultiplierLast &&
               experienceMultiplierLast < plantHarvestMultiplier &&
               plantHarvestMultiplier < plantHarvestMultiplierLast &&
               plantHarvestMultiplierLast < foodRestoresSp &&
               foodRestoresSp < foodRestoresSpLast &&
               foodRestoresSpLast < optimizeMeleeHitChance &&
               optimizeMeleeHitChance < optimizeMeleeHitChanceIncludeParty &&
               optimizeMeleeHitChanceIncludeParty < pcFactionTrainerAllSkills &&
               pcFactionTrainerAllSkills < unlimitedHomeResidentCap &&
               unlimitedHomeResidentCap < unlimitedPartyMemberCap &&
               unlimitedPartyMemberCap < unlimitedOfferingFaithPoints &&
               unlimitedOfferingFaithPoints < ignoreGodArtifactFaithRequirement &&
               ignoreGodArtifactFaithRequirement < shrineEffectSelection &&
               shrineEffectSelection < infiniteChargeAndAmmo &&
               infiniteChargeAndAmmo < chargeStacking &&
               chargeStacking < rightClickInterruptOperation &&
               rightClickInterruptOperation < stealHandNoTargetLimit &&
               stealHandNoTargetLimit < stealHandUndetectable &&
               stealHandUndetectable < merchantAlwaysStocksMonsterBall &&
               merchantAlwaysStocksMonsterBall < merchantMonsterBallLevelOptimization &&
               merchantMonsterBallLevelOptimization < ignoreSpecialNpcHatchRestriction &&
               ignoreSpecialNpcHatchRestriction < ignoreSpecialNpcCaptureRestriction &&
               ignoreSpecialNpcCaptureRestriction < affinityOnlyIncrease &&
               affinityOnlyIncrease < karmaOnlyIncrease &&
               karmaOnlyIncrease < attackCannotBeInterrupted &&
               attackCannotBeInterrupted < attackCannotBeInterruptedIncludeParty &&
               attackCannotBeInterruptedIncludeParty < fishingNoWait &&
               fishingNoWait < geneSynthesisNoWait &&
               geneSynthesisNoWait < sleepWithoutSleepiness &&
               sleepWithoutSleepiness < allPurposeWorkbench &&
               allPurposeWorkbench < abilityCustomAttributes &&
               abilityCustomAttributes < probabilityModule &&
               probabilityModule < killGrowth &&
               killGrowth < automation &&
               automation < moongateLandholderPrivileges &&
               moongateLandholderPrivileges < moongateUploadUpdateKeys &&
               moongateUploadUpdateKeys < ai &&
               ai < emp;
    }
    private static int ExtractInt(string json, string name, int fallback)
    {
        return ConfigurationValueDocument.For(json).GetInt(name, fallback);
    }
    private static float ExtractFloat(string json, string name, float fallback)
    {
        return ConfigurationValueDocument.For(json).GetFloat(name, fallback);
    }
    private static bool ExtractBool(string json, string name, bool fallback)
    {
        return ConfigurationValueDocument.For(json).GetBool(name, fallback);
    }
    private static string ExtractScalarToken(string json, string name)
    {
        return ConfigurationValueDocument.For(json).GetScalar(name);
    }
    internal static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
    internal static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
