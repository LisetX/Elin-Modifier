using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private string GetLGuiPageTitle(LGuiPage page)
    {
        switch (page)
        {
            case LGuiPage.Features: return T("独立功能", "Independent Features");
            case LGuiPage.Character: return T("游戏数据修改", "Character Data");
            case LGuiPage.Items: return T("物品生成", "Item Spawn");
            case LGuiPage.Npcs: return T("NPC生成", "NPC Spawn");
            case LGuiPage.PlayerInfo: return T("玩家信息", "Player Info");
            case LGuiPage.Home: return T("家园管理", "Home Management");
            case LGuiPage.Probability: return T("事件概率", "Event Probabilities");
            case LGuiPage.Automation: return AutomationText("自动化", "Automation", "自動化", "Автоматизация");
            case LGuiPage.Nightly: return "Nightly";
            case LGuiPage.Moongate: return T("月门", "Moongate");
            case LGuiPage.NpcInfo: return T("NPC图鉴", "NPC Compendium");
            case LGuiPage.Ai: return T("AI辅助", "AI Assistant");
            case LGuiPage.Debug: return T("调试模式", "Debug mode");
            case LGuiPage.Emp: return T("插件管理", "Plugin Manager");
            case LGuiPage.Settings: return T("UI设置", "UI Settings");
            default: return "";
        }
    }
    private void BuildLGuiFeaturesPage()
    {
        BuildLGuiFeatureRows();
        var scroll = CreateLGuiScroll(_lGuiPageHost!, "FeatureList", 0f);
        _lGuiFeatureList = new VirtualList<LGuiFeatureRow>(scroll, 58f, 18, CreateLGuiVirtualRow, BindLGuiFeatureRow);
        _lGuiFeatureList.SetItems(_lGuiFeatureRows);
    }
    private void BuildLGuiFeatureRows()
    {
        _lGuiFeatureRows.Clear();
        AddLGuiFeature(LGuiFeatureId.SimulateAdvance, T("模拟推进", "Simulated advance"));
        AddLGuiFeature(LGuiFeatureId.GenerateDungeon, T("生成地牢", "Generate dungeon"));
        AddLGuiFeature(LGuiFeatureId.AiInstruction, T("AI指示", "AI instructions"));
        AddLGuiFeature(LGuiFeatureId.LowPerformance, T("低性能模式", "Low performance mode"));
        AddLGuiFeature(LGuiFeatureId.UnlockFrameRate, T("解锁刷新率上限", "Unlock frame rate"));
        AddLGuiFeature(LGuiFeatureId.InvincibleMode, T("无敌模式", "Invincible mode"));
        AddLGuiFeature(LGuiFeatureId.IgnoreBuffEffects, T("无视Buff效果", "Ignore Buff effects"));
        AddLGuiFeature(LGuiFeatureId.HostileThreatMarker, T("敌对威胁标记", "Hostile threat marker"));
        AddLGuiFeature(LGuiFeatureId.ShowNpcMoreInfo, T("显示NPC更多信息", "Show more NPC info"));
        AddLGuiFeature(LGuiFeatureId.ShowItemMoreInfo, T("显示物品更多信息", "Show more item info"));
        AddLGuiFeature(LGuiFeatureId.ShowBuffSpecificValues, T("显示Buff具体信息", "Show detailed Buff information"));
        AddLGuiFeature(LGuiFeatureId.ShowItemPanelEnchantLevels, T("显示物品面板附魔等级", "Show item panel enchantment levels"));
        AddLGuiFeature(LGuiFeatureId.ShowItemPanelItemValue, T("显示物品面板物品价值", "Show item value in item panel"));
        AddLGuiFeature(LGuiFeatureId.ShowItemPanelMilkBonus, T("显示物品面板奶的加成", "Show milk bonus in item panel"));
        AddLGuiFeature(LGuiFeatureId.ShowMainAbilityExperience, T("显示主能力经验值", "Show main ability experience"));
        AddLGuiFeature(LGuiFeatureId.OneClickQuestCompletion, T("一键完成委托", "One-click quest completion"));
        AddLGuiFeature(LGuiFeatureId.EquipmentComparison, T("装备对比", "Equipment comparison"));
        AddLGuiFeature(LGuiFeatureId.IgnoreFriendlyFire, T("无视友伤", "Ignore friendly fire"));
        AddLGuiFeature(LGuiFeatureId.WorkbenchIngredientReadingOptimization, T("工作台素材读取优化", "Workbench ingredient loading optimization"));
        AddLGuiFeature(LGuiFeatureId.ExperienceMultiplier, T("经验倍率修改", "Experience multiplier modifier"));
        AddLGuiFeature(LGuiFeatureId.PlantHarvestMultiplier, T("种植收获倍率", "Plant harvest multiplier"));
        AddLGuiFeature(LGuiFeatureId.IgnoreCropGrowthConditions, T("无视作物生长条件", "Ignore crop growth conditions"));
        AddLGuiFeature(LGuiFeatureId.FoodRestoresSp, T("食用食物恢复SP", "Restore SP by eating food"));
        AddLGuiFeature(LGuiFeatureId.DismantleAlwaysReturnsMaterials, T("分解必返还材料", "Dismantling always returns materials"));
        AddLGuiFeature(LGuiFeatureId.DismantlingAlwaysLearnsRecipe, T("分解物品必获配方", "Always learn dismantled-item recipes"));
        AddLGuiFeature(LGuiFeatureId.OptimizeMeleeHitChance, T("优化近战命中率逻辑", "Optimize melee hit chance logic"));
        AddLGuiFeature(LGuiFeatureId.PcFactionTrainerAllSkills, T("PC阵营训练师可训练全技能", "PC-faction trainers teach all skills"));
        AddLGuiFeature(LGuiFeatureId.UnlimitedHomeResidentCap, T("家园居民上限无限制", "Unlimited home resident cap"));
        AddLGuiFeature(LGuiFeatureId.UnlimitedPartyMemberCap, T("队伍人数上限无限制", "Unlimited party member cap"));
        AddLGuiFeature(LGuiFeatureId.UnlimitedOfferingFaithPoints, T("供奉提升虔诚度无上限", "Unlimited piety gain per offering"));
        AddLGuiFeature(LGuiFeatureId.IgnoreGodArtifactFaithRequirement, T("无视神器信仰条件限制", "Ignore god artifact faith requirement"));
        AddLGuiFeature(LGuiFeatureId.ShrineEffectSelection, T("神龛自选效果", "Select shrine effect"));
        AddLGuiFeature(LGuiFeatureId.InfiniteChargeAndAmmo, T("无限充能&无限弹药", "Infinite charge & ammo"));
        AddLGuiFeature(LGuiFeatureId.RodStacking, T("充能堆叠", "Charge stacking"));
        AddLGuiFeature(LGuiFeatureId.RightClickInterruptOperation, T("右键打断操作", "Right-click to interrupt actions"));
        AddLGuiFeature(LGuiFeatureId.StealHandNoTargetLimit, T("盗窃之手无对象限制", "Steal hand without target restrictions"));
        AddLGuiFeature(LGuiFeatureId.StealHandUndetectable, T("盗窃之手不会被发现", "Undetectable steal hand"));
        AddLGuiFeature(LGuiFeatureId.MerchantRefreshNoCost, T("商人刷新商品无消耗", "Free merchant restocking"));
        AddLGuiFeature(LGuiFeatureId.MerchantAlwaysStocksMonsterBall, T("道具商必刷精灵球", "Goods merchant always stocks monster balls"));
        AddLGuiFeature(LGuiFeatureId.MerchantMonsterBallLevelOptimization, T("道具商精灵球等级优化", "Optimize goods merchant monster ball levels"));
        AddLGuiFeature(LGuiFeatureId.IgnoreSpecialNpcHatchRestriction, T("无视特殊NPC孵化限制", "Ignore special NPC hatching restriction"));
        AddLGuiFeature(LGuiFeatureId.IgnoreSpecialNpcCaptureRestriction, T("无视特殊NPC捕获限制", "Ignore special NPC capture restriction"));
        AddLGuiFeature(LGuiFeatureId.AffinityOnlyIncrease, T("好感度只增不减", "Affinity only increases"));
        AddLGuiFeature(LGuiFeatureId.KarmaOnlyIncrease, T("善恶值只增不减", "Karma only increases"));
        AddLGuiFeature(LGuiFeatureId.AttackCannotBeInterrupted, T("攻击不会被打断", "Attacks cannot be interrupted"));
        AddLGuiFeature(LGuiFeatureId.FishingNoWait, T("钓鱼无需等待", "Instant fishing bite"));
        AddLGuiFeature(LGuiFeatureId.GeneSynthesisNoWait, T("基因合成无需等待", "Instant gene synthesis"));
        AddLGuiFeature(LGuiFeatureId.SleepWithoutSleepiness, T("睡觉无需困意", "Sleep without sleepiness"));
        AddLGuiFeature(LGuiFeatureId.AllPurposeWorkbench, T("全能制作台", "All-purpose workbench"));
        AddLGuiFeature(LGuiFeatureId.InfiniteSight, T("无视迷雾+无限视野", "Ignore fog + infinite sight"));
        AddLGuiFeature(LGuiFeatureId.ShowFoodRot, T("显示食物腐烂度", "Show food decay"));
        AddLGuiFeature(LGuiFeatureId.IgnoreFoodDecay, T("无视食物腐烂", "Ignore food decay"));
        AddLGuiFeature(LGuiFeatureId.NoCraftMaterials, T("制作无需材料", "Craft without materials"));
        AddLGuiFeature(LGuiFeatureId.UnlockCraftMaterials, T("解锁全部制作材料", "Unlock all crafting materials"));
        AddLGuiFeature(LGuiFeatureId.UnlockCraftRecipes, T("解锁全部制作配方", "Unlock all crafting recipes"));
        AddLGuiFeature(LGuiFeatureId.CustomItemAmount, T("自定义物品持有数量", "Custom item amount"));
        AddLGuiFeature(LGuiFeatureId.CustomItemData, T("自定义物品数据", "Custom item data"));
        AddLGuiFeature(LGuiFeatureId.CustomFoodData, T("自定义食物数据", "Custom food data"));
        AddLGuiFeature(LGuiFeatureId.CustomWeaponData, T("自定义武器数据", "Custom weapon data"));
        AddLGuiFeature(LGuiFeatureId.CustomGeneData, T("自定义基因编辑", "Custom gene editing"));
        AddLGuiFeature(LGuiFeatureId.StethoscopeNoLimit, T("听诊器无对象限制", "Stethoscope no target limit"));
        AddLGuiFeature(LGuiFeatureId.IgnoreTerrain, T("无视地形移动", "Ignore terrain movement"));
        AddLGuiFeature(LGuiFeatureId.OptimizeVoid, T("优化地牢Void缩放逻辑", "Optimize dungeon Void scaling"));
        AddLGuiFeature(LGuiFeatureId.NoTalkInterestLoss, T("对话不减兴趣", "No talk interest loss"));
        AddLGuiFeature(LGuiFeatureId.KillGrowth, T("击杀成长", "Kill growth"));
    }
    private void AddLGuiFeature(LGuiFeatureId id, string label)
    {
        _lGuiFeatureRows.Add(new LGuiFeatureRow(id, label));
    }
}
