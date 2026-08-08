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
    private string AiToolSetFeature(string args)
    {
        var feature = NormalizeAiKey(AiArgString(args, "feature"));
        var enabled = AiArgBool(args, "enabled", false);
        switch (feature)
        {
            case "low_performance_mode":
            case "lowperformancemode":
            case "低性能模式": SetLowPerformanceMode(enabled); break;
            case "unlock_frame_rate":
            case "unlockframerate":
            case "解锁刷新率上限": SetUnlockFrameRate(enabled); break;
            case "invincible_mode":
            case "invinciblemode":
            case "无敌模式": SetInvincibleMode(enabled); break;
            case "invincible_mode_include_party":
            case "invinciblemodeincludeparty":
            case "是否对队伍内队友使用": SetInvincibleModeIncludeParty(enabled); break;
            case "ignore_buff_effects":
            case "ignorebuffeffects":
            case "无视buff效果": SetIgnoreBuffEffects(enabled); break;
            case "ignore_buff_effects_debuff":
            case "ignorebuffeffectsdebuff":
            case "影响范围:debuff": SetIgnoreBuffEffectsDebuff(enabled); break;
            case "ignore_buff_effects_buff":
            case "ignorebuffeffectsbuff":
            case "影响范围:buff": SetIgnoreBuffEffectsBuff(enabled); break;
            case "ignore_buff_effects_include_party":
            case "ignorebuffeffectsincludeparty":
            case "是否对队伍内队友生效": SetIgnoreBuffEffectsIncludeParty(enabled); break;
            case "hostile_threat_marker":
            case "hostilethreatmarker":
            case "敌对威胁标记": SetHostileThreatMarker(enabled); break;
            case "show_npc_more_info":
            case "shownpcmoreinfo":
            case "npc_more_info":
            case "npcmoreinfo":
            case "显示npc更多信息": SetShowNpcMoreInfo(enabled); break;
            case "show_item_more_info":
            case "showitemmoreinfo":
            case "item_more_info":
            case "itemmoreinfo":
            case "显示物品更多信息": SetShowItemMoreInfo(enabled); break;
            case "show_buff_specific_values":
            case "show_buff_specific_info":
            case "showbuffspecificvalues":
            case "showbuffspecificinfo":
            case "显示buff具体信息":
            case "显示buff具体数值": SetShowBuffSpecificValues(enabled); break;
            case "show_item_panel_enchant_levels":
            case "showitempanelenchantlevels":
            case "item_panel_enchant_levels":
            case "显示物品面板附魔等级": SetShowItemPanelEnchantLevels(enabled); break;
            case "show_item_panel_item_value":
            case "showitempanelitemvalue":
            case "item_panel_item_value":
            case "显示物品面板物品价值": SetShowItemPanelItemValue(enabled); break;
            case "show_main_ability_experience":
            case "showmainabilityexperience":
            case "main_ability_experience":
            case "显示主能力经验值": SetShowMainAbilityExperience(enabled); break;
            case "show_main_ability_experience_in_skill_tracker":
            case "showmainabilityexperienceinskilltracker":
            case "main_ability_experience_in_skill_tracker":
            case "是否在技能追踪器显示": SetShowMainAbilityExperienceInSkillTracker(enabled); break;
            case "equipment_comparison":
            case "equipmentcomparison":
            case "装备对比": SetEquipmentComparison(enabled); break;
            case "ignore_friendly_fire":
            case "ignorefriendlyfire":
            case "no_friendly_fire":
            case "无视友伤": SetIgnoreFriendlyFire(enabled); break;
            case "workbench_ingredient_reading_optimization":
            case "workbenchingredientreadingoptimization":
            case "workbench_material_loading_optimization":
            case "工作台素材读取优化": SetWorkbenchIngredientReadingOptimization(enabled); break;
            case "experience_multiplier":
            case "experiencemultiplier":
            case "experience_multiplier_modifier":
            case "经验倍率修改": SetExperienceMultiplierEnabled(enabled); break;
            case "plant_harvest_multiplier":
            case "plantharvestmultiplier":
            case "crop_harvest_multiplier":
            case "种植收获倍率": SetPlantHarvestMultiplierEnabled(enabled); break;
            case "ignore_crop_growth_conditions":
            case "ignorecropgrowthconditions":
            case "无视作物生长条件": SetIgnoreCropGrowthConditions(enabled); break;
            case "experience_multiplier_include_pc_faction":
            case "experience_multiplier_include_allies":
            case "是否对队友生效": SetExperienceMultiplierIncludePcFaction(enabled); break;
            case "food_restores_sp":
            case "restore_sp_by_eating":
            case "foodrestoressp":
            case "食用食物恢复sp": SetFoodRestoresSpEnabled(enabled); break;
            case "dismantle_always_returns_materials":
            case "dismantlealwaysreturnsmaterials":
            case "分解必返还材料": SetDismantleAlwaysReturnsMaterials(enabled); break;
            case "dismantling_always_learns_recipe":
            case "dismantlingalwayslearnsrecipe":
            case "分解物品必获配方": SetDismantlingAlwaysLearnsRecipe(enabled); break;
            case "optimize_melee_hit_chance":
            case "optimizemeleehitchance":
            case "optimized_melee_hit_chance":
            case "优化近战命中率逻辑": SetOptimizeMeleeHitChance(enabled); break;
            case "optimize_melee_hit_chance_include_party":
            case "optimizemeleehitchanceincludeparty":
            case "optimized_melee_hit_chance_include_party": SetOptimizeMeleeHitChanceIncludeParty(enabled); break;
            case "pc_faction_trainer_all_skills":
            case "pcfactiontrainerallskills":
            case "trainer_all_skills":
            case "pc阵营训练师可训练全技能": SetPcFactionTrainerAllSkills(enabled); break;
            case "unlimited_home_resident_cap":
            case "unlimited_home_population":
            case "unlimitedhomeresidentcap":
            case "home_population_9999":
            case "home_population_999":
            case "家园居民上限无限制": SetUnlimitedHomeResidentCap(enabled); break;
            case "unlimited_party_member_cap":
            case "unlimitedpartymembercap":
            case "party_member_cap_9999":
            case "队伍人数上限无限制": SetUnlimitedPartyMemberCap(enabled); break;
            case "unlimited_offering_faith_points":
            case "unlimitedofferingfaithpoints":
            case "unlimited_offering_piety":
            case "unlimited_offering_piety_gain":
            case "供奉提升虔诚度无上限": SetUnlimitedOfferingFaithPoints(enabled); break;
            case "ignore_god_artifact_faith_requirement":
            case "ignoregodartifactfaithrequirement":
            case "ignore_artifact_faith_requirement":
            case "无视神器信仰条件限制": SetIgnoreGodArtifactFaithRequirement(enabled); break;
            case "shrine_effect_selection":
            case "shrineeffectselection":
            case "select_shrine_effect":
            case "神龛自选效果": SetShrineEffectSelection(enabled); break;
            case "infinite_charge":
            case "infinitecharge":
            case "unlimited_charge":
            case "infinite_charge_and_ammo":
            case "infinitechargeandammo":
            case "unlimited_charge_and_ammo":
            case "无限充能":
            case "无限充能无限弹药": SetInfiniteChargeAndAmmo(enabled); break;
            case "rod_stacking":
            case "rodstacking":
            case "charge_stacking":
            case "chargestacking":
            case "法杖堆叠":
            case "充能堆叠": SetRodStacking(enabled); break;
            case "right_click_interrupt_operation":
            case "rightclickinterruptoperation":
            case "right_click_cancel_action":
            case "rightclickcancelaction":
            case "右键打断操作": SetRightClickInterruptOperation(enabled); break;
            case "steal_hand_no_target_limit":
            case "stealhandnotargetlimit":
            case "盗窃之手无对象限制": SetStealHandNoTargetLimit(enabled); break;
            case "steal_hand_undetectable":
            case "stealhandundetectable":
            case "盗窃之手不会被发现": SetStealHandUndetectable(enabled); break;
            case "merchant_always_stocks_monster_ball":
            case "merchantalwaysstocksmonsterball":
            case "goods_merchant_monster_ball":
            case "goodsmerchantmonsterball":
            case "道具商必刷精灵球": SetMerchantAlwaysStocksMonsterBall(enabled); break;
            case "merchant_monster_ball_level_optimization":
            case "merchantmonsterballleveloptimization":
            case "goods_merchant_monster_ball_level":
            case "goodsmerchantmonsterballlevel":
            case "道具商精灵球等级优化": SetMerchantMonsterBallLevelOptimization(enabled); break;
            case "ignore_special_npc_hatch_restriction":
            case "ignorespecialnpchatchrestriction":
            case "special_npc_hatch":
            case "无视特殊npc孵化限制": SetIgnoreSpecialNpcHatchRestriction(enabled); break;
            case "ignore_special_npc_capture_restriction":
            case "ignorespecialnpccapturerestriction":
            case "special_npc_capture":
            case "无视特殊npc捕获限制": SetIgnoreSpecialNpcCaptureRestriction(enabled); break;
            case "affinity_only_increase":
            case "affinityonlyincrease":
            case "no_affinity_loss":
            case "noaffinityloss":
            case "好感度只增不减": SetAffinityOnlyIncrease(enabled); break;
            case "karma_only_increase":
            case "karmaonlyincrease":
            case "no_karma_loss":
            case "nokarmaloss":
            case "善恶值只增不减": SetKarmaOnlyIncrease(enabled); break;
            case "attack_cannot_be_interrupted":
            case "attackcannotbeinterrupted":
            case "no_attack_interruption":
            case "noattackinterruption":
            case "攻击不会被打断": SetAttackCannotBeInterrupted(enabled); break;
            case "attack_cannot_be_interrupted_include_party":
            case "attackcannotbeinterruptedincludeparty":
            case "攻击不会被打断是否对队友生效": SetAttackCannotBeInterruptedIncludeParty(enabled); break;
            case "fishing_no_wait":
            case "fishingnowait":
            case "instant_fishing_bite":
            case "instantfishingbite":
            case "钓鱼无需等待": SetFishingNoWait(enabled); break;
            case "gene_synthesis_no_wait":
            case "genesynthesisnowait":
            case "instant_gene_synthesis":
            case "instantgenesynthesis":
            case "基因合成无需等待": SetGeneSynthesisNoWait(enabled); break;
            case "sleep_without_sleepiness":
            case "sleepwithoutsleepiness":
            case "sleep_without_tiredness":
            case "sleepwithouttiredness":
            case "睡觉无需困意": SetSleepWithoutSleepiness(enabled); break;
            case "all_purpose_workbench":
            case "allpurposeworkbench":
            case "universal_workbench":
            case "universalworkbench":
            case "全能制作台": SetAllPurposeWorkbench(enabled); break;
            case "infinite_sight":
            case "ignore_fog_infinite_sight":
            case "infiniteplayersight":
            case "无视迷雾无限视野":
            case "无视迷雾无限视距": SetInfinitePlayerSight(enabled); break;
            case "show_food_rot":
            case "showfoodrot":
            case "显示食物腐烂度": SetShowFoodRot(enabled); break;
            case "ignore_food_decay":
            case "ignore_food_rot":
            case "ignorefooddecay":
            case "无视食物腐烂": SetIgnoreFoodDecay(enabled); break;
            case "no_material_crafting":
            case "no_craft_materials":
            case "nocraftmaterials":
            case "制作无需材料":
            case "无需材料制作": SetNoCraftMaterials(enabled); break;
            case "unlock_all_crafting_materials":
            case "unlockallcraftmaterials":
            case "解锁全部制作材料": SetUnlockAllCraftMaterials(enabled); break;
            case "unlock_all_crafting_recipes":
            case "unlockallcraftrecipes":
            case "解锁全部制作配方": SetUnlockAllCraftRecipes(enabled); break;
            case "custom_item_amount":
            case "customitemamount":
            case "自定义物品持有数量": SetCustomItemAmount(enabled); break;
            case "custom_item_data":
            case "customitemeditor":
            case "自定义物品数据": SetCustomItemEditor(enabled); break;
            case "custom_food_data":
            case "customfoodeditor":
            case "自定义食物数据": SetCustomFoodEditor(enabled); break;
            case "custom_weapon_data":
            case "customweaponeditor":
            case "自定义武器数据": SetCustomWeaponEditor(enabled); break;
            case "custom_gene_editing":
            case "customgeneeditor":
            case "自定义基因编辑": SetCustomGeneEditor(enabled); break;
            case "stethoscope_no_target_limit":
            case "stethoscopenotargetlimit":
            case "听诊器无对象限制": SetStethoscopeNoTargetLimit(enabled); break;
            case "ignore_terrain_movement":
            case "ignoreterrainmovement":
            case "无视地形移动": SetIgnoreTerrainMovement(enabled); break;
            case "optimize_dungeon_void_scaling":
            case "optimizedungeonvoidscaling":
            case "optimize_void_scaling":
            case "optimizevoidscaling":
            case "优化地牢void缩放逻辑":
            case "优化地牢void缩放": SetOptimizeDungeonVoidScaling(enabled); break;
            case "no_talk_interest_loss":
            case "notalkinterestloss":
            case "dialogue_no_interest_loss":
            case "dialoguenointerestloss":
            case "对话不减兴趣": SetNoTalkInterestLoss(enabled); break;
            case "kill_growth":
            case "killgrowth":
            case "击杀成长": SetKillGrowthEnabled(enabled); break;
            case "kill_growth_shared_experience":
            case "killgrowthsharedexperience":
            case "shared_kill_growth":
            case "sharedkillgrowth":
            case "共享经验": SetKillGrowthSharedExperience(enabled); break;
            default:
                return "failed: unknown feature " + feature;
        }
        return "ok: " + feature + " = " + enabled.ToString(CultureInfo.InvariantCulture);
    }
    private string AiToolSetPlantHarvestMultiplierSettings(string args)
    {
        var cropMultiplier = ExtractFloat(
            args ?? "",
            "crop_multiplier",
            _modules.PlantHarvestMultiplier.CropHarvestMultiplier);
        var seedMultiplier = ExtractFloat(
            args ?? "",
            "seed_multiplier",
            _modules.PlantHarvestMultiplier.SeedReapingMultiplier);

        _modules.PlantHarvestMultiplier.CropHarvestMultiplierText =
            cropMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
        _modules.PlantHarvestMultiplier.SeedReapingMultiplierText =
            seedMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
        string status;
        if (!TryApplyPlantHarvestMultiplierSettings(out status))
            return "failed: invalid plant harvest multiplier value";

        return "ok: crop_multiplier=" +
               _modules.PlantHarvestMultiplier.CropHarvestMultiplier.ToString("0.###", CultureInfo.InvariantCulture) +
               " | seed_multiplier=" +
               _modules.PlantHarvestMultiplier.SeedReapingMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
    }
    private string AiToolListInventoryItems(string args)
    {
        var filter = AiArgString(args, "filter");
        var limit = Clamp(AiArgInt(args, "limit", 80), 0, 5000);
        var rows = EnumerateAiInventoryThings()
            .Where(thing => AiInventoryThingMatchesFilter(thing, filter))
            .OrderBy(thing => SafeThingName(thing), StringComparer.OrdinalIgnoreCase)
            .ThenBy(thing => SafeText(() => thing.uid.ToString(CultureInfo.InvariantCulture), "0"))
            .ToList();

        var total = rows.Count;
        if (limit > 0 && rows.Count > limit)
            rows = rows.Take(limit).ToList();

        var sb = new StringBuilder();
        sb.Append("ok: inventory items ").Append(rows.Count.ToString(CultureInfo.InvariantCulture))
            .Append("/").Append(total.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(filter))
            sb.Append(" filter=").Append(filter.Trim());
        if (total == 0)
            return sb.AppendLine().Append("empty").ToString().TrimEnd();

        for (var i = 0; i < rows.Count; i++)
        {
            sb.AppendLine();
            AppendAiInventoryThingLine(sb, rows[i], i + 1);
        }
        if (limit > 0 && total > rows.Count)
            sb.AppendLine().Append("truncated: use a higher limit or limit=0 for all rows");
        return sb.ToString().TrimEnd();
    }
    private string AiToolSetInventoryItemAmount(string args)
    {
        var itemText = AiArgString(args, "item");
        var count = AiArgInt(args, "count", 0);
        var matchMode = NormalizeAiKey(AiArgString(args, "match_mode", "auto"));
        if (string.IsNullOrWhiteSpace(itemText))
            return "failed: item is empty";
        if (count <= 0)
            return "failed: count must be greater than 0";

        var matches = FindAiInventoryThingMatches(itemText, matchMode);
        if (matches.Count == 0)
            return "failed: inventory item not found: " + itemText + "\n" + AiToolListInventoryItems("{\"filter\":\"" + EscapeJson(itemText) + "\",\"limit\":20}");
        if (matches.Count > 1)
        {
            var sb = new StringBuilder();
            sb.Append("failed: ambiguous inventory item: ").Append(itemText).AppendLine();
            sb.AppendLine("Use UID from these candidates:");
            for (var i = 0; i < Math.Min(matches.Count, 20); i++)
                AppendAiInventoryThingLine(sb, matches[i], i + 1);
            if (matches.Count > 20)
                sb.AppendLine("truncated: " + matches.Count.ToString(CultureInfo.InvariantCulture) + " candidates");
            return sb.ToString().TrimEnd();
        }

        var target = matches[0];
        if (!CanCustomizeItemAmount(target))
            return "failed: target item cannot be edited";
        SetCardNum(target, count);
        RefreshInventoryUi();
        RefreshFoodRotOverlayForCard(target);
        return "ok: inventory item amount set | " + FormatAiInventoryThing(target) + " | count=" + count.ToString(CultureInfo.InvariantCulture);
    }
    private string AiToolGetInventoryItemData(string args)
    {
        var target = ResolveAiInventoryThingFromArgs(args, out var error);
        if (target == null)
            return error;
        return "ok: inventory item data\n" + FormatAiInventoryItemData(target);
    }
    private string AiToolSetItemData(string args)
    {
        var target = ResolveAiInventoryThingFromArgs(args, out var error);
        if (target == null)
            return error;
        if (!CanEditItemData(target))
            return "failed: target item cannot be edited";

        OpenItemDataEditorWindow(target);
        ApplyAiOptionalIntText(args, "level", value => _itemDataEditorLv = value);
        ApplyAiOptionalIntText(args, "enhance", value => _itemDataEditorEncLv = value);
        ApplyAiOptionalIntText(args, "material_id", value => _itemDataEditorMaterialId = value);
        ApplyAiOptionalIntText(args, "weight", value => _itemDataEditorWeight = value);
        ApplyAiOptionalIntText(args, "variant_id", value => _itemDataEditorSkin = value);
        ApplyAiOptionalIntText(args, "fixed_price", value => _itemDataEditorPriceFix = value);
        ApplyAiOptionalIntText(args, "value", value => _itemDataEditorValue = value);
        ApplyAiOptionalIntText(args, "value_bonus", value => _itemDataEditorValueBonus = value);
        if (AiHasArg(args, "blessed_state"))
            _itemDataEditorBlessedStateValue = (int)ParseBlessedStateValue(ExtractScalarToken(args ?? "", "blessed_state"), _itemDataEditorBlessedStateValue);
        ApplyAiOptionalBool(args ?? "", "is_stolen", value => _itemDataEditorFlagStolen = value);
        ApplyAiOptionalBool(args ?? "", "is_crafted", value => _itemDataEditorFlagCrafted = value);
        ApplyAiOptionalBool(args ?? "", "is_gifted", value => _itemDataEditorFlagGifted = value);
        ApplyAiOptionalBool(args ?? "", "is_replica", value => _itemDataEditorFlagReplica = value);
        ApplyAiOptionalBool(args ?? "", "is_copy", value => _itemDataEditorFlagCopy = value);
        ApplyAiOptionalBool(args ?? "", "is_fireproof", value => _itemDataEditorFlagFireproof = value);
        ApplyAiOptionalBool(args ?? "", "is_acidproof", value => _itemDataEditorFlagAcidproof = value);
        ApplyAiOptionalBool(args ?? "", "is_broken", value => _itemDataEditorFlagBroken = value);
        ApplyAiOptionalBool(args ?? "", "no_sell", value => _itemDataEditorFlagNoSell = value);
        ApplyAiOptionalBool(args ?? "", "is_lost_property", value => _itemDataEditorFlagLostProperty = value);
        if (AiHasArg(args, "rarity"))
            _itemDataEditorRarityValue = AiArgInt(args, "rarity", _itemDataEditorRarityValue);
        if (AiHasArg(args, "enchantments"))
        {
            if (!TryApplyAiValuePairs(AiArgString(args, "enchantments"), _itemDataEditorEnchantments, out error))
                return error;
        }
        ApplyItemDataEditorChange();
        return (_log.IndexOf(T("失败", "failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _log + "\n" + FormatAiInventoryItemData(target);
    }
    private string AiToolSetFoodData(string args)
    {
        var target = ResolveAiInventoryThingFromArgs(args, out var error);
        if (target == null)
            return error;
        if (!CanEditFoodData(target))
            return "failed: target item is not editable food";

        OpenFoodEditorWindow(target);
        ApplyAiOptionalIntText(args, "level", value => _foodEditorLv = value);
        ApplyAiOptionalIntText(args, "enhance", value => _foodEditorEncLv = value);
        ApplyAiOptionalIntText(args, "material_id", value => _foodEditorMaterialId = value);
        ApplyAiOptionalIntText(args, "weight", value => _foodEditorWeight = value);
        ApplyAiOptionalIntText(args, "rot", value => _foodEditorDecay = value);
        if (AiHasArg(args, "blessed_state"))
            _foodEditorBlessedStateValue = (int)ParseBlessedStateValue(ExtractScalarToken(args ?? "", "blessed_state"), _foodEditorBlessedStateValue);
        ApplyAiOptionalBool(args ?? "", "is_stolen", value => _foodEditorFlagStolen = value);
        ApplyAiOptionalBool(args ?? "", "is_crafted", value => _foodEditorFlagCrafted = value);
        ApplyAiOptionalBool(args ?? "", "is_gifted", value => _foodEditorFlagGifted = value);
        ApplyAiOptionalBool(args ?? "", "is_replica", value => _foodEditorFlagReplica = value);
        ApplyAiOptionalBool(args ?? "", "is_copy", value => _foodEditorFlagCopy = value);
        ApplyAiOptionalBool(args ?? "", "is_fireproof", value => _foodEditorFlagFireproof = value);
        ApplyAiOptionalBool(args ?? "", "is_acidproof", value => _foodEditorFlagAcidproof = value);
        ApplyAiOptionalBool(args ?? "", "is_broken", value => _foodEditorFlagBroken = value);
        ApplyAiOptionalBool(args ?? "", "no_sell", value => _foodEditorFlagNoSell = value);
        ApplyAiOptionalBool(args ?? "", "is_lost_property", value => _foodEditorFlagLostProperty = value);
        if (AiHasArg(args, "rarity"))
            _foodEditorRarityValue = AiArgInt(args, "rarity", _foodEditorRarityValue);
        if (AiHasArg(args, "effects"))
        {
            if (!TryApplyAiValuePairs(AiArgString(args, "effects"), _foodEditorEffects, out error))
                return error;
        }
        ApplyFoodEditorChange();
        return (_log.IndexOf(T("失败", "failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _log + "\n" + FormatAiInventoryItemData(target);
    }
    private string AiToolSetWeaponData(string args)
    {
        var target = ResolveAiInventoryThingFromArgs(args, out var error);
        if (target == null)
            return error;
        if (!CanEditWeaponData(target))
            return "failed: target item is not editable weapon/tool";

        OpenWeaponEditorWindow(target);
        ApplyAiOptionalIntText(args, "level", value => _weaponEditorLv = value);
        ApplyAiOptionalIntText(args, "enhance", value => _weaponEditorEncLv = value);
        ApplyAiOptionalIntText(args, "material_id", value => _weaponEditorMaterialId = value);
        ApplyAiOptionalIntText(args, "damage_dice_sides", value => _weaponEditorDiceDim = value);
        ApplyAiOptionalIntText(args, "hit", value => _weaponEditorHit = value);
        ApplyAiOptionalIntText(args, "damage_bonus", value => _weaponEditorDamage = value);
        ApplyAiOptionalIntText(args, "dv", value => _weaponEditorDv = value);
        ApplyAiOptionalIntText(args, "pv", value => _weaponEditorPv = value);
        ApplyAiOptionalIntText(args, "weight", value => _weaponEditorWeight = value);
        ApplyAiOptionalIntText(args, "charges", value => _weaponEditorCharges = value);
        ApplyAiOptionalIntText(args, "ammo", value => _weaponEditorAmmo = value);
        ApplyAiOptionalIntText(args, "range", value => _weaponEditorRangeText = value);
        ApplyAiOptionalIntText(args, "penetration", value => _weaponEditorPenetrationText = value);
        ApplyAiOptionalIntText(args, "modification_slots", value => _weaponEditorModificationSlots = value);
        if (AiHasArg(args, "rarity"))
            _weaponEditorRarityValue = AiArgInt(args, "rarity", _weaponEditorRarityValue);
        if (AiHasArg(args, "blessed_state"))
            _weaponEditorBlessedStateValue = (int)ParseBlessedStateValue(ExtractScalarToken(args ?? "", "blessed_state"), _weaponEditorBlessedStateValue);
        ApplyAiOptionalBool(args ?? "", "is_stolen", value => _weaponEditorFlagStolen = value);
        ApplyAiOptionalBool(args ?? "", "is_crafted", value => _weaponEditorFlagCrafted = value);
        ApplyAiOptionalBool(args ?? "", "is_gifted", value => _weaponEditorFlagGifted = value);
        ApplyAiOptionalBool(args ?? "", "is_replica", value => _weaponEditorFlagReplica = value);
        ApplyAiOptionalBool(args ?? "", "is_copy", value => _weaponEditorFlagCopy = value);
        ApplyAiOptionalBool(args ?? "", "is_fireproof", value => _weaponEditorFlagFireproof = value);
        ApplyAiOptionalBool(args ?? "", "is_acidproof", value => _weaponEditorFlagAcidproof = value);
        ApplyAiOptionalBool(args ?? "", "is_broken", value => _weaponEditorFlagBroken = value);
        ApplyAiOptionalBool(args ?? "", "no_sell", value => _weaponEditorFlagNoSell = value);
        ApplyAiOptionalBool(args ?? "", "is_lost_property", value => _weaponEditorFlagLostProperty = value);
        if (AiHasArg(args, "enchantments"))
        {
            if (!TryApplyAiValuePairs(AiArgString(args, "enchantments"), _weaponEditorEnchantments, out error))
                return error;
        }
        ApplyWeaponEditorChange();
        return (_log.IndexOf(T("失败", "failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _log + "\n" + FormatAiInventoryItemData(target);
    }
    private string AiToolSetGeneData(string args)
    {
        var target = ResolveAiInventoryThingFromArgs(args, out var error);
        if (target == null)
            return error;
        if (!CanEditGene(target) || !EnsureEditableGeneDna(target))
            return "failed: target item is not editable gene";

        OpenGeneEditorWindow(target);
        ApplyAiOptionalString(args, "source_id", value => _geneEditorSourceId = value);
        ApplyAiOptionalIntText(args, "level", value => _geneEditorLv = value);
        ApplyAiOptionalIntText(args, "seed", value => _geneEditorSeed = value);
        ApplyAiOptionalIntText(args, "cost", value => _geneEditorCost = value);
        ApplyAiOptionalIntText(args, "slots", value => _geneEditorSlot = value);
        if (AiHasArg(args, "effects"))
        {
            if (!TryApplyAiValuePairs(AiArgString(args, "effects"), _geneEditorValues, out error))
                return error;
        }
        ApplyGeneEditorChange();
        return (_log.IndexOf(T("失败", "failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _log + "\n" + FormatAiInventoryItemData(target);
    }
    private string AiToolListGameNames(string args)
    {
        var category = NormalizeAiKey(AiArgString(args, "category", "all"));
        var filter = AiArgString(args, "filter");
        var limit = Clamp(AiArgInt(args, "limit", 80), 0, 5000);
        var sb = new StringBuilder("ok: game names");
        if (!string.IsNullOrWhiteSpace(filter))
            sb.Append(" filter=").Append(filter.Trim());

        var any = false;
        void AddSection(string key, IEnumerable<AiNameEntry> entries)
        {
            var rows = entries
                .Where(entry => AiNameEntryMatchesFilter(entry, filter))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var total = rows.Count;
            var visible = limit > 0 && rows.Count > limit ? rows.Take(limit).ToList() : rows;
            sb.AppendLine();
            sb.Append("[").Append(key).Append("] ").Append(visible.Count.ToString(CultureInfo.InvariantCulture))
                .Append("/").Append(total.ToString(CultureInfo.InvariantCulture));
            if (total == 0)
            {
                sb.AppendLine();
                sb.Append("empty");
            }
            else
            {
                foreach (var entry in visible)
                {
                    sb.AppendLine();
                    AppendAiNameEntryLine(sb, entry);
                }
                if (limit > 0 && total > visible.Count)
                    sb.AppendLine().Append("truncated: use a higher limit or limit=0 for all rows");
            }
            any = true;
        }

        switch (category)
        {
            case "all":
            case "":
                AddSection("enchantments", BuildAiEnchantNameEntries());
                AddSection("traits", BuildAiRowNameEntries("trait"));
                AddSection("feats", BuildAiRowNameEntries("feat"));
                AddSection("skills", BuildAiRowNameEntries("skill"));
                AddSection("spells", BuildAiAbilityNameEntries());
                AddSection("items", BuildAiItemNameEntries());
                AddSection("npcs", BuildAiNpcNameEntries());
                AddSection("religions", BuildAiFaithNameEntries());
                break;
            case "enchantment":
            case "enchantments":
            case "enchant":
            case "enchants":
            case "附魔":
                AddSection("enchantments", BuildAiEnchantNameEntries());
                break;
            case "trait":
            case "traits":
            case "特质":
                AddSection("traits", BuildAiRowNameEntries("trait"));
                break;
            case "feat":
            case "feats":
            case "专长":
                AddSection("feats", BuildAiRowNameEntries("feat"));
                break;
            case "skill":
            case "skills":
            case "技能":
                AddSection("skills", BuildAiRowNameEntries("skill"));
                break;
            case "spell":
            case "spells":
            case "ability":
            case "abilities":
            case "咒语":
            case "能力":
                AddSection("spells", BuildAiAbilityNameEntries());
                break;
            case "item":
            case "items":
            case "thing":
            case "things":
            case "物品":
                AddSection("items", BuildAiItemNameEntries());
                break;
            case "npc":
            case "npcs":
            case "chara":
            case "charas":
            case "角色":
                AddSection("npcs", BuildAiNpcNameEntries());
                break;
            case "religion":
            case "religions":
            case "faith":
            case "faiths":
            case "god":
            case "gods":
            case "信仰":
            case "宗教":
            case "神":
                AddSection("religions", BuildAiFaithNameEntries());
                break;
            default:
                return "failed: unknown category " + category;
        }

        if (!any)
            sb.AppendLine().Append("empty");
        return sb.ToString().TrimEnd();
    }
}
