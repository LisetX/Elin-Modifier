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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    internal void EnsureGameRows()
    {
        if (_skillRows != null) return;
        _skillRows = new List<RowDef>();
        _traitRows = new List<RowDef>();
        _featRows = new List<RowDef>();
        var seen = new HashSet<int>();

        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            if (id <= 0 || !seen.Add(id)) continue;

            var alias = GetString(row, "alias");
            var name = GetElementDisplayName(row);
            if (string.IsNullOrEmpty(name) || name == alias || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) continue;

            var type = GetString(row, "type");
            var group = GetString(row, "group");
            var category = GetString(row, "category");
            var categorySub = GetString(row, "categorySub");
            var tags = string.Join(",", GetStringArray(row, "tag"));
            var isSkill = IsCharacterSkillRow(row, id, type, group, category, categorySub, tags);
            var isTrait = GetBool(row, "isTrait") || TextHas(type, "trait") || TextHas(group, "trait");
            var isFeat = IsFeatRow(row, alias, type, group, category, categorySub, tags);

            var def = new RowDef(id.ToString(), name, isFeat ? RowKind.Feat : RowKind.Element)
            {
                Alias = alias,
                Category = string.IsNullOrEmpty(category) ? group : category
            };

            if (isFeat) _featRows.Add(def);
            else if (isTrait) _traitRows.Add(def);
            else if (isSkill) _skillRows.Add(def);
        }

        RemoveDuplicateLabels(_skillRows);
        RemoveDuplicateLabels(_traitRows);
        RemoveDuplicateLabels(_featRows);
        _skillRows.Sort(CompareRows);
        _traitRows.Sort(CompareRows);
        _featRows.Sort(CompareRows);
        _log = T("已读取游戏数据：技能 ", "Loaded game data: skills ") + _skillRows.Count + T("，特质 ", ", traits ") + _traitRows.Count + T("，专长 ", ", feats ") + _featRows.Count;
    }
    private static bool IsCharacterSkillRow(object row, int id, string type, string group, string category, string categorySub, string tags)
    {
        if (id < 100 || id >= 400)
            return false;
        if (TextHas(tags, "unused") || TextHas(tags, "hidden"))
            return false;
        if (GetBool(row, "isSpell") || GetBool(row, "isTrait") || GetBool(row, "isAttribute"))
            return false;
        if (IsNonCharacterSkillCategory(category) || IsNonCharacterSkillCategory(categorySub))
            return false;

        if (string.Equals(category, "skill", StringComparison.OrdinalIgnoreCase))
            return IsCharacterSkillSubCategory(categorySub);

        var hasSkillType = string.Equals(type, "Skill", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(group, "SKILL", StringComparison.OrdinalIgnoreCase);
        if (!hasSkillType)
            return false;

        return IsCharacterSkillSubCategory(categorySub) || IsCharacterSkillSubCategory(category);
    }
    private static bool IsFeatRow(object row, string alias, string type, string group, string category, string categorySub, string tags)
    {
        var isFeat = string.Equals(category, "feat", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(group, "FEAT", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(type, "Feat", StringComparison.OrdinalIgnoreCase) ||
                     TextHas(tags, "feat");
        if (!isFeat)
            return false;
        if (GetBool(row, "isSpell") || GetBool(row, "isSkill") || GetBool(row, "isAttribute"))
            return false;
        if (TextHas(category, "enchant") || TextHas(categorySub, "enchant") ||
            string.Equals(category, "landfeat", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
    private static bool IsCharacterSkillSubCategory(string category)
    {
        return string.Equals(category, "general", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "labor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "mind", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "stealth", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "combat", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "craft", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "weapon", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "armor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "survival", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsNonCharacterSkillCategory(string category)
    {
        return string.Equals(category, "attribute", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "resist", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "enchant", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "ability", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "spell", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "feat", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "landfeat", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "tech", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "policy", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "faction", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "ether", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "mutation", StringComparison.OrdinalIgnoreCase);
    }
    private void EnsureEtherDiseaseRows()
    {
        if (_etherDiseaseRows != null) return;
        _etherDiseaseRows = new List<RowDef>();
        var seen = new HashSet<int>();

        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            if (id <= 0 || !seen.Add(id)) continue;
            var category = GetString(row, "category");
            if (!string.Equals(category, "ether", StringComparison.OrdinalIgnoreCase)) continue;

            var alias = GetString(row, "alias");
            var name = GetElementDisplayName(row);
            if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                name = string.IsNullOrEmpty(alias) ? id.ToString(CultureInfo.InvariantCulture) : alias;

            _etherDiseaseRows.Add(new RowDef(id.ToString(CultureInfo.InvariantCulture), name, RowKind.Feat)
            {
                Alias = alias,
                Category = category,
                Max = Math.Max(1, GetInt(row, "max"))
            });
        }

        RemoveDuplicateRowsByKey(_etherDiseaseRows);
        _etherDiseaseRows.Sort(CompareRows);
    }
    private void EnsureAbilityRows()
    {
        if (_abilityRows != null) return;
        _abilityRows = new List<AbilityDef>();
        var seen = new HashSet<int>();

        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            if (id <= 0 || !seen.Add(id)) continue;

            var alias = GetString(row, "alias");
            var name = GetDisplayName(row);
            if (string.IsNullOrEmpty(name) || name == alias || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) continue;

            var type = GetString(row, "type");
            var group = GetString(row, "group");
            var category = GetString(row, "category");
            var categorySub = GetString(row, "categorySub");
            var tags = string.Join(",", GetStringArray(row, "tag"));
            var abilityTypes = GetStringArray(row, "abilityType");
            var isSkill = GetBool(row, "isSkill") || type == "Skill";
            var isTrait = GetBool(row, "isTrait") || TextHas(type, "trait") || TextHas(group, "trait");
            var isFeat = TextHas(alias, "feat") || TextHas(type, "feat") || TextHas(group, "feat") ||
                         TextHas(category, "feat") || TextHas(categorySub, "feat") || TextHas(tags, "feat");
            var isSpellLike = GetBool(row, "isSpell") || string.Equals(categorySub, "spell", StringComparison.OrdinalIgnoreCase) ||
                              TextHas(type, "spell") || TextHas(group, "spell") ||
                              TextHas(category, "spell") || TextHas(categorySub, "spell");
            var isAbility = abilityTypes.Length > 0 || isSpellLike ||
                            TextHas(type, "ability") || TextHas(group, "ability") ||
                            TextHas(category, "ability") || TextHas(categorySub, "ability") ||
                            TextHas(type, "act") || TextHas(group, "act") ||
                            TextHas(category, "act") || TextHas(categorySub, "act");

            if (!isAbility || (isSkill && !isSpellLike) || isTrait || isFeat) continue;
            _abilityRows.Add(new AbilityDef(id, name, row)
            {
                Alias = alias,
                Category = string.IsNullOrEmpty(categorySub) ? (string.IsNullOrEmpty(category) ? group : category) : categorySub
            });
        }

        MarkDuplicateAbilityNames(_abilityRows);
        _abilityRows.Sort(CompareAbilities);
        _log = T("已读取能力数据：", "Loaded ability data: ") + _abilityRows.Count;
    }
    private static void MarkDuplicateAbilityNames(List<AbilityDef> rows)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = row.Name.Trim();
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        foreach (var row in rows)
        {
            if (!counts.TryGetValue(row.Name.Trim(), out var count) || count <= 1)
            {
                row.DisplayName = row.Name;
                continue;
            }

            var qualifier = GetAbilityNameQualifier(row);
            if (!string.IsNullOrEmpty(qualifier) && row.Name.IndexOf(qualifier, StringComparison.OrdinalIgnoreCase) < 0)
                row.DisplayName = row.Name + " (" + qualifier + ")";
            else if (!string.IsNullOrEmpty(row.Alias))
                row.DisplayName = row.Name + " [" + row.Alias + "]";
            else
                row.DisplayName = row.Name + " (" + row.Id + ")";
        }
    }
    private static int CompareAbilities(AbilityDef a, AbilityDef b)
    {
        var ca = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
        return ca != 0 ? ca : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
    }
    private static HashSet<int> GetExistingCharacterValueIds(Chara target)
    {
        var result = new HashSet<int>();
        if (target == null)
            return result;

        try
        {
            foreach (var pair in GetElements(target).dict)
            {
                if (pair.Key > 0 && pair.Value != null)
                    result.Add(pair.Key);
            }
        }
        catch { }

        try
        {
            var list = target._listAbility;
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var id = list[i];
                    if (id < 0 && id != int.MinValue)
                        id = -id;
                    if (id > 0)
                        result.Add(id);
                }
            }
        }
        catch { }

        try
        {
            var items = target.ability?.list?.items;
            if (items != null)
            {
                foreach (var item in items)
                {
                    var id = item?.act?.id ?? 0;
                    if (id > 0)
                        result.Add(id);
                }
            }
        }
        catch { }
        return result;
    }
    private static int CompareRowsExistingFirst(RowDef a, RowDef b, HashSet<int> existingValueIds)
    {
        var aExists = int.TryParse(a.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aId) && existingValueIds.Contains(aId);
        var bExists = int.TryParse(b.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bId) && existingValueIds.Contains(bId);
        var existingOrder = bExists.CompareTo(aExists);
        return existingOrder != 0 ? existingOrder : CompareRows(a, b);
    }
    private static int CompareAbilitiesExistingFirst(AbilityDef a, AbilityDef b, HashSet<int> existingValueIds)
    {
        var existingOrder = existingValueIds.Contains(b.Id).CompareTo(existingValueIds.Contains(a.Id));
        return existingOrder != 0 ? existingOrder : CompareAbilities(a, b);
    }
    private int GetAbilityLevel(Chara target, AbilityDef ability)
    {
        var element = GetAbilityElement(target, ability);
        return element == null ? 0 : element.ValueWithoutLink;
    }
    private int GetAbilityStock(Chara target, AbilityDef ability)
    {
        var element = GetAbilityElement(target, ability);
        return element == null ? 0 : element.vPotential;
    }
    private static int GetAbilityChance(AbilityDef ability)
    {
        return GetInt(ability.Source, "chance");
    }
    private static int GetAbilityPower(AbilityDef ability)
    {
        return GetInt(ability.Source, "value");
    }
    private int GetAbilityDisplayChance(Chara target, AbilityDef ability)
    {
        if (_abilityChanceOverrides.TryGetValue(ability.Id, out var customChance))
            return customChance;
        var element = GetAbilityElement(target, ability);
        if (element != null)
        {
            try { return Clamp(target.CalcCastingChance(element, 0), 0, 100); }
            catch { }
        }
        return GetAbilityChance(ability);
    }
    private int GetAbilityDisplayPower(Chara target, AbilityDef ability)
    {
        if (_abilityPowerOverrides.TryGetValue(ability.Id, out var customPower))
            return customPower;
        var element = GetAbilityElement(target, ability);
        if (element != null)
        {
            try { return Math.Max(0, element.GetPower(target)); }
            catch { }
        }
        return GetAbilityPower(ability);
    }
    private int GetAbilityCost(Chara target, AbilityDef ability, int index)
    {
        if (_abilityCostOverrides.TryGetValue(ability.Id, out var customCost))
        {
            if (index == 0) return customCost.Hp;
            if (index == 1) return customCost.Mp;
            if (index == 2) return customCost.Sp;
        }

        var cost = GetField(ability.Source, "cost") as int[];
        var nativeCost = cost != null && cost.Length > 0 ? cost[0] : 0;
        if (nativeCost <= 0) return 0;
        if (IsAbilitySpell(target, ability))
            return index == 1 ? nativeCost : 0;
        return index == 2 ? nativeCost : 0;
    }
    private bool IsAbilityCustomAttributesEnabled(int abilityId)
    {
        return _abilityChanceOverrides.ContainsKey(abilityId) &&
               _abilityPowerOverrides.ContainsKey(abilityId) &&
               _abilityCostOverrides.ContainsKey(abilityId);
    }
    private void EnableAbilityCustomAttributes(Chara target, AbilityDef ability)
    {
        if (IsAbilityCustomAttributesEnabled(ability.Id))
            return;

        SetAbilityCustomAttributes(ability.Id, -1, -1, -1, -1, -1);
    }
    private void SetAbilityCustomAttributes(
        int abilityId,
        int chance,
        int power,
        int hpCost,
        int mpCost,
        int spCost)
    {
        _abilityChanceOverrides[abilityId] = NormalizeAbilityChanceOverrideValue(chance);
        _abilityPowerOverrides[abilityId] = NormalizeAbilityCustomValue(power);
        _abilityCostOverrides[abilityId] = new AbilityCostOverride(hpCost, mpCost, spCost);
    }
    private static int NormalizeAbilityChanceOverrideValue(int value)
    {
        return value < 0 ? -1 : Clamp(value, 0, 100);
    }
    private static int NormalizeAbilityCustomValue(int value)
    {
        return value < 0 ? -1 : value;
    }
    private void DisableAbilityCustomAttributes(int abilityId)
    {
        _abilityChanceOverrides.Remove(abilityId);
        _abilityPowerOverrides.Remove(abilityId);
        _abilityCostOverrides.Remove(abilityId);
    }
    private static Element GetAbilityElement(Chara target, AbilityDef ability)
    {
        try { return GetElements(target).GetElement(ability.Id); }
        catch { return null; }
    }
    private void ApplyAbilityValues(Chara target, AbilityDef ability, string levelKey, string chanceKey, string powerKey, string hpCostKey, string mpCostKey, string spCostKey, string stockKey)
    {
        ApplyAbilityValues(
            target,
            ability,
            levelKey,
            chanceKey,
            powerKey,
            hpCostKey,
            mpCostKey,
            spCostKey,
            stockKey,
            true,
            false);
    }
    private void ApplyAbilityValues(
        Chara target,
        AbilityDef ability,
        string levelKey,
        string chanceKey,
        string powerKey,
        string hpCostKey,
        string mpCostKey,
        string spCostKey,
        string stockKey,
        bool customAttributes,
        bool persistCustomAttributes)
    {
        try
        {
            if (!TryGetInputInt(levelKey, out var level) ||
                !TryGetInputInt(stockKey, out var stock))
            {
                _log = T("能力输入不是数字", "Ability input is not a number");
                return;
            }

            var chance = 0;
            var power = 0;
            var hpCost = 0;
            var mpCost = 0;
            var spCost = 0;
            if (customAttributes &&
                (!TryGetInputInt(chanceKey, out chance) ||
                 !TryGetInputInt(powerKey, out power) ||
                 !TryGetInputInt(hpCostKey, out hpCost) ||
                 !TryGetInputInt(mpCostKey, out mpCost) ||
                 !TryGetInputInt(spCostKey, out spCost)))
            {
                _log = T("能力输入不是数字", "Ability input is not a number");
                return;
            }

            var elements = GetElements(target);
            var element = elements.SetBase(ability.Id, level, stock);
            if (element != null)
                element.vPotential = stock;
            SyncNpcAbilityListAfterApply(target, ability.Id, level);

            if (customAttributes)
                SetAbilityCustomAttributes(ability.Id, chance, power, hpCost, mpCost, spCost);
            else
                DisableAbilityCustomAttributes(ability.Id);

            if (persistCustomAttributes)
                SaveConfig(false, false);

            target.Refresh(false);
            TryRedrawAbilityLayer();
            _log = T("能力已设置: ", "Ability set: ") + ability.DisplayName;
        }
        catch (Exception ex)
        {
            _log = T("能力设置失败: ", "Ability set failed: ") + ex.Message;
        }
    }
    private static void SyncNpcAbilityListAfterApply(Chara target, int abilityId, int level)
    {
        try
        {
            if (target == null || target.IsPC || abilityId <= 0)
                return;

            if (level <= 0)
                RemoveNpcAbilityFromList(target, abilityId);
            else
                EnsureNpcAbilityInList(target, abilityId);

            RefreshNpcAbilityList(target);
        }
        catch { }
    }
    private static void EnsureNpcAbilityInList(Chara target, int abilityId)
    {
        try
        {
            if (target.ability != null && target.ability.Has(abilityId))
                return;
        }
        catch { }

        if (target._listAbility == null)
            target._listAbility = new List<int>();
        for (var i = 0; i < target._listAbility.Count; i++)
        {
            var existing = target._listAbility[i];
            if (existing == abilityId || existing == -abilityId)
                return;
        }
        target._listAbility.Add(abilityId);
    }
    private static void RemoveNpcAbilityFromList(Chara target, int abilityId)
    {
        var list = target._listAbility;
        if (list == null)
            return;

        for (var i = list.Count - 1; i >= 0; i--)
        {
            var existing = list[i];
            if (existing == abilityId || existing == -abilityId)
                list.RemoveAt(i);
        }
        if (list.Count == 0)
            target._listAbility = null;
    }
    private static void RefreshNpcAbilityList(Chara target)
    {
        try { target.ability.Refresh(); } catch { }
        try
        {
            var combat = target.ai as GoalCombat;
            if (combat == null)
                return;
            if (combat.abilities == null)
                combat.abilities = new List<GoalCombat.ItemAbility>();
            combat.BuildAbilityList();
        }
        catch { }
    }
    private bool TryGetInputInt(string key, out int value)
    {
        value = 0;
        return _inputs.TryGetValue(key, out var text) && int.TryParse(text, out value);
    }
    private static bool IsAbilitySpell(Chara target, AbilityDef ability)
    {
        try
        {
            var element = GetElements(target).GetElement(ability.Id);
            if (element is Spell) return true;
        }
        catch { }

        var type = GetString(ability.Source, "type");
        var group = GetString(ability.Source, "group");
        var category = GetString(ability.Source, "category");
        var categorySub = GetString(ability.Source, "categorySub");
        return GetBool(ability.Source, "isSpell") ||
               TextHas(type, "spell") || TextHas(group, "spell") ||
               TextHas(category, "spell") || TextHas(categorySub, "spell");
    }
    private static void TryRedrawAbilityLayer()
    {
        try { LayerAbility.Redraw(); }
        catch { }
    }
}
