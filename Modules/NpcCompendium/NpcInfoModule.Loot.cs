using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

internal sealed partial class NpcInfoModule
{

    private List<NpcLootEntry> BuildLootEntries(NpcRecord npc)
    {
        var result = new List<NpcLootEntry>();
        AddEncodedLoot(result, npc.Row.loot, T("NPC固有", "NPC intrinsic"));
        SourceRace.Row? race = null;
        try { race = npc.Row.race_row; }
        catch { }
        var isMachine = false;
        var isAnimal = false;
        try
        {
            isMachine = npc.Row.HasTag(CTAG.machine);
            isAnimal = npc.Row.HasTag(CTAG.animal);
        }
        catch
        {
        }
        if (race != null)
        {
            AddEncodedLoot(result, race.loot, T("种族固有", "Race intrinsic"));
            if (race.corpse != null && race.corpse.Length >= 2 && !string.IsNullOrWhiteSpace(race.corpse[0]))
            {
                var corpseValue = ParseInt(race.corpse[1]);
                AddLootEntry(
                    result,
                    T("尸体", "Corpse"),
                    GetCardName(race.corpse[0]) + " [" + race.corpse[0] + "]",
                    corpseValue.ToString(CultureInfo.InvariantCulture) + "/1500 (" +
                    FormatProbability(Math.Max(0d, Math.Min(1d, corpseValue / 1500d))) + ")",
                    T("基础1；屠宰时动态", "base 1; dynamic when slaughtered"),
                    T("强敌和指定种族可强制掉落；屠宰、解剖学、阵营、用户区及物种专属分支会改写概率或数量",
                        "Powerful enemies and selected races may force it; slaughter, anatomy, faction, user-zone, and species rules can alter chance or quantity"));
            }
            try
            {
                isMachine = isMachine || race.IsMachine;
                isAnimal = isAnimal || race.IsAnimal;
            }
            catch
            {
            }
        }
        if (isMachine)
        {
            AddBaseChanceLoot(result, T("机械通用", "Machine common"), "memory_chip", 200,
                T("机械种族或机器标签，且非PC阵营、非用户区；法典掉落等级与随从状态会调整最终概率", "Machine race or tag, non-PC faction, and outside user zones; compendium drop level and minion state adjust the final chance"));
            AddBaseChanceLoot(result, T("机械通用", "Machine common"), "microchip", 20,
                T("非PC阵营、非用户区且无1248元素；有该元素则改掉废料；法典与随从状态会调整概率", "Non-PC faction, outside user zones, and no element 1248; drops scrap when present; compendium progress and minion state adjust the chance"));
            AddBaseChanceLoot(result, T("机械通用", "Machine common"), "scrap", 20,
                T("非PC阵营、非用户区且有1248元素，和微芯片互斥；法典与随从状态会调整概率", "Non-PC faction, outside user zones, and element 1248 present; mutually exclusive with microchip; compendium progress and minion state adjust the chance"));
            AddBaseChanceLoot(result, T("机械通用", "Machine common"), "battery", 15,
                T("非PC阵营、非用户区且无1248元素；有该元素则改掉螺栓；法典与随从状态会调整概率", "Non-PC faction, outside user zones, and no element 1248; drops bolt when present; compendium progress and minion state adjust the chance"));
            AddBaseChanceLoot(result, T("机械通用", "Machine common"), "bolt", 15,
                T("非PC阵营、非用户区且有1248元素，和电池互斥；法典与随从状态会调整概率", "Non-PC faction, outside user zones, and element 1248 present; mutually exclusive with battery; compendium progress and minion state adjust the chance"));
        }
        else if (isAnimal)
        {
            AddBaseChanceLoot(result, T("动物通用", "Animal common"), "fang", 15,
                T("动物种族或动物标签，且非PC阵营、非用户区；法典掉落等级与随从状态会调整最终概率", "Animal race or tag, non-PC faction, and outside user zones; compendium drop level and minion state adjust the final chance"));
            AddBaseChanceLoot(result, T("动物通用", "Animal common"), "skin", 10,
                T("动物种族或动物标签，且非PC阵营、非用户区；法典掉落等级与随从状态会调整最终概率", "Animal race or tag, non-PC faction, and outside user zones; compendium drop level and minion state adjust the final chance"));
        }

        AddBaseChanceLoot(result, T("全体通用", "All NPCs"), "offal", 20, GetCommonDropConditions());
        AddBaseChanceLoot(result, T("全体通用", "All NPCs"), "heart", 20, GetCommonDropConditions());
        AddLootEntry(result, T("全体通用", "All NPCs"), T("基因", "Gene"), FormatOneIn(200), "1",
            T("非PC阵营；用户区仅在禁用用户图收益时阻止；法典掉落等级与随从状态会调整最终概率", "Non-PC faction; user zones block it only when user-map benefits are disabled; compendium drop level and minion state adjust the final chance"));
        AddLootEntry(result, T("模型", "Figure"), GetCardName("figure") + " [figure]", FormatOneIn(500), "1",
            T("非固定角色且允许掉落时；稀有度、法典奖励和赞助配置可调整概率", "When not a fixed actor and loot is allowed; rarity, compendium rewards, and backer settings may adjust the chance"));
        AddLootEntry(result, T("模型", "Figure"), GetCardName("figure3") + " [figure3]", FormatOneIn(500), "1",
            T("非固定角色且允许掉落时；稀有度、法典奖励和赞助配置可调整概率", "When not a fixed actor and loot is allowed; rarity, compendium rewards, and backer settings may adjust the chance"));

        AddSpecialLoot(result, npc.Id);
        if (!string.IsNullOrWhiteSpace(npc.Equipment))
        {
            AddLootEntry(
                result,
                T("实例装备", "Instance equipment"),
                npc.Equipment,
                T("实例生成", "Instance-generated"),
                T("0-N", "0-N"),
                T("死亡时再按装备稀有度、物品Trait、强制掉落标记与全局掉率判定",
                    "Checked on death using equipment rarity, item traits, forced-drop flags, and global drop settings"));
        }
        AddLootEntry(
            result,
            T("实例携带物", "Carried items"),
            T("装备、背包物品、赠品与库存", "Equipment, inventory, gifts, and stock"),
            T("实例/条件", "Instance/conditional"),
            T("0-N", "0-N"),
            T("由NPC实例决定；旅行商库存、稀有装备、Trait及强制掉落标记分别走原版条件分支",
                "Determined per NPC instance; traveling stock, rare gear, traits, and forced-drop flags use separate original-game branches"));
        AddLootEntry(
            result,
            T("稀有度补偿", "Rarity compensation"),
            T("随机装备或奖章", "Random gear or medals"),
            T("条件触发", "Conditional"),
            T("动态", "Dynamic"),
            T("由死亡时的实例稀有度及原版补偿条件决定，不要求该NPC成为区域Boss",
                "Determined by instance rarity and original compensation rules on death; the NPC need not be a zone boss"));
        AddLootEntry(
            result,
            T("Boss奖励", "Boss reward"),
            T("Boss宝箱及区域奖励", "Boss chest and zone rewards"),
            T("条件触发", "Conditional"),
            T("动态", "Dynamic"),
            T("仅当该NPC实例被选为相应区域Boss并满足区域奖励条件时生成，并非该NPC基础掉落",
                "Generated only when this NPC instance is selected as the relevant zone boss and meets zone-reward rules; not a base NPC drop"));
        return result;
    }

    private void AddEncodedLoot(ICollection<NpcLootEntry> result, string[]? entries, string source)
    {
        if (entries == null)
            return;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            var parts = entry.Split('/');
            if (parts.Length < 2)
            {
                AddLootEntry(result, source, entry, "-", "-", T("数据格式无法解析", "Unrecognized data format"));
                continue;
            }
            var encoded = ParseInt(parts[1]);
            var decoded = NpcInfoProbabilityMath.DecodeLootValue(encoded);
            string probability;
            string quantity;
            if (decoded.Probability < 1d)
            {
                probability = FormatProbability(decoded.Probability);
                quantity = "1";
            }
            else if (decoded.MinimumQuantity == decoded.MaximumQuantity)
            {
                probability = T("必定", "Guaranteed");
                quantity = decoded.MinimumQuantity.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                probability = T("必定", "Guaranteed");
                quantity = decoded.MinimumQuantity.ToString(CultureInfo.InvariantCulture) + "-" +
                           decoded.MaximumQuantity.ToString(CultureInfo.InvariantCulture) + " (" +
                           T("期望", "expected") + " " + decoded.ExpectedQuantity.ToString("0.###", CultureInfo.InvariantCulture) + ")";
            }
            AddLootEntry(
                result,
                source,
                GetCardName(parts[0]) + " [" + parts[0] + "]",
                probability,
                quantity,
                T("数据表固定掉落；实例、阵营与区域规则仍可阻止或改写结果",
                    "Fixed data-table drop; instance, faction, and zone rules may still block or alter the result"));
        }
    }

    private void AddBaseChanceLoot(
        ICollection<NpcLootEntry> result,
        string source,
        string itemId,
        int denominator,
        string conditions)
    {
        AddLootEntry(result, source, GetCardName(itemId) + " [" + itemId + "]", FormatOneIn(denominator), "1", conditions);
    }

    private static void AddLootEntry(
        ICollection<NpcLootEntry> result,
        string source,
        string item,
        string probability,
        string quantity,
        string conditions)
    {
        result.Add(new NpcLootEntry
        {
            Source = source,
            Item = item,
            Probability = probability,
            Quantity = quantity,
            Conditions = conditions
        });
    }

    private string FormatOneIn(int denominator) =>
        "1/" + denominator.ToString(CultureInfo.InvariantCulture) + " (" + FormatProbability(1d / denominator) + ")";

    private string GetCommonDropConditions() =>
        T("原版基础分母；法典掉落等级会缩小分母，随从会将分母×5，PC阵营或用户区等分支可阻止掉落",
            "Original base denominator; compendium drop level reduces it, minions multiply it by 5, and PC-faction or user-zone branches may block the drop");

    private void AddSpecialLoot(ICollection<NpcLootEntry> result, string id)
    {
        switch (id)
        {
            case "bubble_pudding":
                AddLootEntry(result, T("专属", "Special"), GetCardName("milk_custard") + " [milk_custard]",
                    T("尸体分支成功后必定", "Guaranteed after corpse-branch success"), T("继承尸体数量", "inherits corpse quantity"),
                    GetSpecialDropConditions(T("仅布丁波球的尸体分支", "Only the pudding-bubble corpse branch")));
                break;
            case "marshmallow_monster":
                AddLootEntry(result, T("专属", "Special"), GetCardName("marshmallow_nama") + " [marshmallow_nama]",
                    T("尸体分支成功后必定", "Guaranteed after corpse-branch success"), T("继承尸体数量", "inherits corpse quantity"),
                    GetSpecialDropConditions(T("仅棉花糖怪的尸体分支", "Only the marshmallow-monster corpse branch")));
                break;
            case "marshmallow_king":
                AddLootEntry(result, T("专属", "Special"), GetCardName("marshmallow_nama") + " [marshmallow_nama]",
                    T("尸体分支成功后必定", "Guaranteed after corpse-branch success"), T("尸体数量×8", "corpse quantity ×8"),
                    GetSpecialDropConditions(T("仅棉花糖王的尸体分支", "Only the marshmallow-king corpse branch")));
                break;
            case "pumpkin":
                AddLootEntry(result, T("专属", "Special"), T("蛋糕/曲奇类别随机成品", "Random cake/cookie-category product"),
                    FormatOneIn(3), "1", GetSpecialDropConditions(T("成功后从对应食物类别生成实际物品", "On success, generates the actual item from the corresponding food category")));
                break;
            case "isca":
                AddLootEntry(result, T("专属", "Special"), GetCardName("blood_angel") + " [blood_angel]",
                    T("分支内必定", "Guaranteed within branch"), "1", GetSpecialDropConditions(T("仅该NPC专属分支", "NPC-specific branch only")));
                break;
            case "golem_wood":
                AddLootEntry(result, T("专属", "Special"), GetCardName("crystal_earth") + " [crystal_earth]",
                    FormatOneIn(30), "1", GetSpecialDropConditions(T("仅木魔像专属分支", "Wood-golem branch only")));
                break;
            case "golem_fish":
            case "golem_stone":
                AddLootEntry(result, T("专属", "Special"), GetCardName("crystal_sun") + " [crystal_sun]",
                    FormatOneIn(30), "1", GetSpecialDropConditions(T("仅对应魔像专属分支", "Matching golem branch only")));
                break;
            case "golem_steel":
                AddLootEntry(result, T("专属", "Special"), GetCardName("crystal_mana") + " [crystal_mana]",
                    FormatOneIn(30), "1", GetSpecialDropConditions(T("仅钢魔像专属分支", "Steel-golem branch only")));
                break;
            case "golem_gold":
                AddLootEntry(result, T("专属", "Special"), GetCardName("money2") + " [money2]",
                    T("分支内必定", "Guaranteed within branch"), "1", GetSpecialDropConditions(T("仅金魔像专属分支", "Gold-golem branch only")));
                break;
        }
    }

    private string GetSpecialDropConditions(string specific) =>
        specific + T("；仍受外层阵营、用户区、特殊区域、法典掉落等级与随从状态规则限制",
            "; still subject to outer faction, user-zone, special-area, compendium drop-level, and minion-state rules");
}
