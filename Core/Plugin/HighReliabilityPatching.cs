using System;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private bool IsHighReliabilityStartupMode => _highReliabilityStartupModeActive;

    private bool ShouldInstallHighReliabilityStartupPatch(Type patchType)
    {
        return IsUiSettingsPatchType(patchType) ||
               IsMoongatePrivilegePatchType(patchType) ||
               IsEnabledIndependentPatchType(patchType);
    }

    private bool IsEnabledIndependentPatchType(Type patchType)
    {
        foreach (LGuiFeatureId id in Enum.GetValues(typeof(LGuiFeatureId)))
        {
            if (GetLGuiFeatureValue(id) && PatchTypeMatchesIndependentFeature(patchType, id))
                return true;
        }
        return false;
    }

    private void EnsureIndependentFeatureHarmonyPatches(LGuiFeatureId id)
    {
        if (!IsHighReliabilityStartupMode)
            return;
        _modules.Harmony.Install(
            typeof(ElinModifierPlugin).Assembly,
            Logger,
            patchType => PatchTypeMatchesIndependentFeature(patchType, id));
        if (id == LGuiFeatureId.PredationGeneSelection)
            _modules.PredationGeneSelection.Initialize(_modules.Harmony, Logger);
        else if (id == LGuiFeatureId.AllowCurrencyGifts)
            _modules.AllowCurrencyGifts.Initialize(_modules.Harmony, Logger);
    }

    private void EnsureEnabledIndependentHarmonyPatches()
    {
        if (!IsHighReliabilityStartupMode)
            return;
        _modules.Harmony.Install(
            typeof(ElinModifierPlugin).Assembly,
            Logger,
            IsEnabledIndependentPatchType);
        if (_modules.PredationGeneSelection.Enabled)
            _modules.PredationGeneSelection.Initialize(_modules.Harmony, Logger);
        if (_modules.AllowCurrencyGifts.Enabled)
            _modules.AllowCurrencyGifts.Initialize(_modules.Harmony, Logger);
    }

    private void MaintainHighReliabilityHarmonyPatches()
    {
        if (!IsHighReliabilityStartupMode || Time.unscaledTime < _nextHighReliabilityPatchCheckAt)
            return;
        _nextHighReliabilityPatchCheckAt = Time.unscaledTime + 0.75f;
        EnsureEnabledIndependentHarmonyPatches();
        if (_modules.Nightly?.FixSelfTalkBug == true)
            _modules.Nightly.Initialize(_modules.Harmony, Logger);
    }

    private void EnsureLGuiPageHarmonyPatches(LGuiPage page)
    {
        if (!IsHighReliabilityStartupMode)
            return;
        _modules.Harmony.Install(
            typeof(ElinModifierPlugin).Assembly,
            Logger,
            patchType => PatchTypeMatchesPage(patchType, page));
    }

    internal void InitializeModuleNightlyPatches()
    {
        if (!IsHighReliabilityStartupMode || _modules.Nightly?.FixSelfTalkBug == true)
            _modules.Nightly?.Initialize(_modules.Harmony, Logger);
    }

    private void EnsureNightlyFeatureHarmonyPatches()
    {
        if (IsHighReliabilityStartupMode)
            _modules.Nightly?.Initialize(_modules.Harmony, Logger);
    }

    internal void InitializePredationGeneSelectionPatches()
    {
        if (!IsHighReliabilityStartupMode || _modules.PredationGeneSelection.Enabled)
            _modules.PredationGeneSelection.Initialize(_modules.Harmony, Logger);
    }

    internal void InitializeAllowCurrencyGiftsPatches()
    {
        if (!IsHighReliabilityStartupMode || _modules.AllowCurrencyGifts.Enabled)
            _modules.AllowCurrencyGifts.Initialize(_modules.Harmony, Logger);
    }

    private static bool IsUiSettingsPatchType(Type patchType)
    {
        return patchType.Name.StartsWith("MainMenuInfo", StringComparison.Ordinal);
    }

    private static bool IsMoongatePrivilegePatchType(Type patchType)
    {
        var name = patchType.Name;
        return name.StartsWith("MoongateLandholder", StringComparison.Ordinal) ||
               name.StartsWith("MoongateCanEnterBuildMode", StringComparison.Ordinal);
    }

    private static bool PatchTypeMatchesPage(Type patchType, LGuiPage page)
    {
        var name = patchType.Name;
        switch (page)
        {
            case LGuiPage.Character:
                return name == "AbilityGetPowerPatch" ||
                       name == "CharaCalcCastingChancePatch" ||
                       name == "ElementGetCostPatch" ||
                       name == "CharaUseAbilityPatch";
            case LGuiPage.PlayerInfo:
                return name.EndsWith("PlayerInfoPatch", StringComparison.Ordinal);
            case LGuiPage.Probability:
                return name == "DropMultiplierSpawnLootPatch" ||
                       name == "DropMultiplierZoneAddPatch" ||
                       name == "ScratchProbabilityPatch";
            case LGuiPage.Automation:
                return name == "CardDamageHpAutomationRetaliationPatch";
            case LGuiPage.Moongate:
                return name.StartsWith("Moongate", StringComparison.Ordinal);
            case LGuiPage.NpcInfo:
                return name.IndexOf("NpcCompendiumQuickLookup", StringComparison.Ordinal) >= 0;
            case LGuiPage.Settings:
                return IsUiSettingsPatchType(patchType);
            default:
                return false;
        }
    }

    private static bool PatchTypeMatchesIndependentFeature(Type patchType, LGuiFeatureId id)
    {
        var name = patchType.Name;
        switch (id)
        {
            case LGuiFeatureId.AiInstruction:
                return name.IndexOf("AiInstruction", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.InvincibleMode:
                return name.IndexOf("InvincibleMode", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.IgnoreBuffEffects:
                return name.IndexOf("IgnoreBuffEffects", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.HostileThreatMarker:
                return name.StartsWith("ThreatOverlay", StringComparison.Ordinal) ||
                       name.StartsWith("ThreatPredecision", StringComparison.Ordinal);
            case LGuiFeatureId.ShowNpcMoreInfo:
                return name.IndexOf("NpcMoreInfo", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.ShowItemMoreInfo:
                return name.IndexOf("ItemMoreInfo", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.ShowBuffSpecificValues:
                return name.IndexOf("SpecificValues", StringComparison.Ordinal) >= 0 ||
                       name == "BaseNotificationBuffSpecificInfoPositionPatch";
            case LGuiFeatureId.ShowItemPanelEnchantLevels:
                return name.IndexOf("ItemPanelEnchantLevel", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.ShowItemPanelItemValue:
                return name == "ThingWriteNotePatch" ||
                       name == "UINoteAddHeaderCardItemPanelValuePatch";
            case LGuiFeatureId.ShowItemPanelMilkBonus:
                return name == "ThingWriteNotePatch";
            case LGuiFeatureId.ShowMainAbilityExperience:
                return name.IndexOf("MainAbilityExperience", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.OneClickQuestCompletion:
                return name.IndexOf("OneClickCompletion", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.EquipmentComparison:
                return name.IndexOf("EquipmentComparison", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.IgnoreFriendlyFire:
                return name.IndexOf("FriendlyFire", StringComparison.Ordinal) >= 0 ||
                       name == "CharaDoHostileActionLovePotionAffinityProtectionPatch";
            case LGuiFeatureId.WorkbenchIngredientReadingOptimization:
                return name.StartsWith("CraftIngredientPicker", StringComparison.Ordinal) ||
                       name.StartsWith("NonStandardCrafterIngredient", StringComparison.Ordinal);
            case LGuiFeatureId.ExperienceMultiplier:
                return name.IndexOf("ExperienceMultiplier", StringComparison.Ordinal) >= 0 ||
                       name.IndexOf("PotentialMultiplier", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.PlantHarvestMultiplier:
                return name.IndexOf("HarvestMultiplier", StringComparison.Ordinal) >= 0 ||
                       name.IndexOf("ReapingMultiplier", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.IgnoreCropGrowthConditions:
                return name.IndexOf("IgnoreCropConditions", StringComparison.Ordinal) >= 0 ||
                       name.IndexOf("IgnoreFertilizer", StringComparison.Ordinal) >= 0 ||
                       name.IndexOf("IgnoreWater", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.AllFeatsLearnable:
                return name.IndexOf("AllFeatsLearnable", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.CharacterPanelGenes:
                return name.IndexOf("CharacterPanelGenes", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.AllowPcGeneImplant:
                return name.IndexOf("PcGene", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.FoodRestoresSp:
                return name == "FoodEffectRestorePlayerSpPatch";
            case LGuiFeatureId.DismantleAlwaysReturnsMaterials:
                return name == "TaskHarvestMaterialRollScopePatch" ||
                       name == "SuppressOriginalDismantledOutputPatch" ||
                       name == "DismantleMaterialRandomRollPatch";
            case LGuiFeatureId.DismantlingAlwaysLearnsRecipe:
                return name.StartsWith("DismantlingRecipe", StringComparison.Ordinal);
            case LGuiFeatureId.OptimizeMeleeHitChance:
                return name.IndexOf("OptimizedMeleeHitChance", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.PcFactionTrainerAllSkills:
                return name.IndexOf("PcFactionTrainer", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.UnlimitedHomeResidentCap:
                return name == "FactionBranchMaxPopulationUnlimitedPatch";
            case LGuiFeatureId.UnlimitedPartyMemberCap:
                return name == "PlayerMaxAllyUnlimitedPatch";
            case LGuiFeatureId.UnlimitedOfferingFaithPoints:
                return name.IndexOf("UnlimitedOfferingFaithPoints", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.IgnoreGodArtifactFaithRequirement:
                return name.IndexOf("IgnoreGodArtifactFaith", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.ShrineEffectSelection:
                return name.IndexOf("ShrineEffectSelection", StringComparison.Ordinal) >= 0 ||
                       name == "TraitShrineSelectedOutcomePatch";
            case LGuiFeatureId.InfiniteChargeAndAmmo:
                return name == "CardInfiniteChargePatch" || name == "CardInfiniteAmmoPatch";
            case LGuiFeatureId.RodStacking:
                return name == "InvOwnerListInteractionsPatch";
            case LGuiFeatureId.StealHandNoTargetLimit:
                return name != "StealHandDiscoveryPatch" && name.StartsWith("StealHand", StringComparison.Ordinal);
            case LGuiFeatureId.StealHandUndetectable:
                return name == "StealHandDiscoveryPatch";
            case LGuiFeatureId.MerchantRefreshNoCost:
                return name.IndexOf("MerchantRefreshNoCost", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.MerchantAlwaysStocksMonsterBall:
            case LGuiFeatureId.MerchantMonsterBallLevelOptimization:
                return name.IndexOf("MerchantMonsterBall", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.IgnoreSpecialNpcHatchRestriction:
                return name.IndexOf("SpecialNpcHatch", StringComparison.Ordinal) >= 0 ||
                       name == "CardMakeEggSpecialNpcPatch" ||
                       name == "TraitFoodEggFertilizedIncubateSpecialNpcPatch";
            case LGuiFeatureId.IgnoreSpecialNpcCaptureRestriction:
                return name.IndexOf("SpecialNpcCapture", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.AffinityOnlyIncrease:
                return name.IndexOf("Affinity", StringComparison.Ordinal) >= 0 ||
                       name.IndexOf("LovePotion", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.KarmaOnlyIncrease:
                return name.IndexOf("KarmaOnlyIncrease", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.AttackCannotBeInterrupted:
                return name.IndexOf("AttackCannotBeInterrupted", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.FishingNoWait:
                return name.IndexOf("FishingNoWait", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.GeneSynthesisNoWait:
                return name.IndexOf("GeneSynthesisNoWait", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.SleepWithoutSleepiness:
                return name.IndexOf("SleepWithoutSleepiness", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.AllPurposeWorkbench:
                return name.IndexOf("AllPurposeWorkbench", StringComparison.Ordinal) >= 0 ||
                       name == "RecipeManagerListSourcesPatch";
            case LGuiFeatureId.InfiniteSight:
                return name.IndexOf("InfinitePlayerSight", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.ShowFoodRot:
                return name.IndexOf("FoodRotOverlay", StringComparison.Ordinal) >= 0 ||
                       name == "ButtonGridSetCardPatch" ||
                       name == "CardSetDecayPatch" ||
                       name == "ThingWriteNotePatch";
            case LGuiFeatureId.IgnoreFoodDecay:
                return name == "CardDecayNaturalPatch" ||
                       name == "CardDecayPatch" ||
                       name == "CardIsDecayedPatch" ||
                       name == "CardIsRottingPatch" ||
                       name == "CardIsFresnPatch";
            case LGuiFeatureId.NoCraftMaterials:
                return name == "PropsListThingStackPatch" ||
                       name == "DropdownGridBuildIngredientsPatch" ||
                       name == "RecipeIngredientSetThingPatch" ||
                       name == "RecipeIsCraftablePatch" ||
                       name == "RecipeGetMaxCountPatch" ||
                       name == "LayerCraftOnClickCraftPatch" ||
                       name == "LayerCraftGetTargetsPatch" ||
                       name == "TaskCraftIsIngredientsValidPatch" ||
                       name == "CardSplitPatch" ||
                       name == "AIUseCrafterOnEndPatch";
            case LGuiFeatureId.UnlockCraftRecipes:
                return name == "RecipeManagerIsKnownPatch" ||
                       name == "RecipeManagerGetRecipeLearnStatePatch" ||
                       name == "RecipeManagerListSourcesPatch";
            case LGuiFeatureId.CustomItemAmount:
            case LGuiFeatureId.CustomItemData:
            case LGuiFeatureId.CustomFoodData:
            case LGuiFeatureId.CustomGeneData:
                return name == "InvOwnerListInteractionsPatch";
            case LGuiFeatureId.CustomWeaponData:
                return name == "InvOwnerListInteractionsPatch" ||
                       name == "ThingRangeOverridePatch" ||
                       name == "ThingPenetrationOverridePatch";
            case LGuiFeatureId.StethoscopeNoLimit:
                return name == "TraitStethoscopeTrySetHeldActPatch";
            case LGuiFeatureId.IgnoreTerrain:
                return name == "CharaGetFirstStepPatch" ||
                       name == "CharaTryMoveTowardsPatch" ||
                       name == "AIGotoTryGoToPatch" ||
                       name == "CharaCanMoveToPatch" ||
                       name == "CharaMovePatch";
            case LGuiFeatureId.OptimizeVoid:
                return name == "ZoneSpawnMobDebugTracePatch" ||
                       name == "ZoneSpawnMobVoidScalingPatch";
            case LGuiFeatureId.NoTalkInterestLoss:
                return name.IndexOf("NoInterestLoss", StringComparison.Ordinal) >= 0;
            case LGuiFeatureId.KillGrowth:
                return name.IndexOf("KillGrowth", StringComparison.Ordinal) >= 0;
            default:
                return false;
        }
    }
}
