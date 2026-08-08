using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class ProbabilityModule
{
    private static IEnumerable<object> EnumerateProbabilitySourceRows(object source)
    {
        object? rows = null;
        var type = source.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            var property = type.GetProperty("rows", flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                rows = property.GetValue(source, null);
        }
        catch { }

        if (rows == null)
        {
            try
            {
                var field = FindProbabilityField(type, "rows") ?? FindProbabilityField(type, "map");
                if (field != null)
                    rows = field.GetValue(source);
            }
            catch { }
        }

        if (rows is IDictionary dictionary)
        {
            foreach (DictionaryEntry pair in dictionary)
                if (pair.Value != null)
                    yield return pair.Value;
            yield break;
        }

        if (rows is IEnumerable enumerable && !(rows is string))
        {
            foreach (var row in enumerable)
                if (row != null)
                    yield return row;
        }
    }
    private static FieldInfo? FindProbabilityField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }
    private void AddProbabilityMembers(string sourceName, object row, ref int errorCount)
    {
        FieldInfo[] fields;
        try { fields = row.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public); }
        catch { errorCount++; return; }

        var rowId = ReadProbabilityRowText(row, "id");
        if (rowId.Length == 0)
            rowId = ReadProbabilityRowText(row, "_id");
        var displayName = GetProbabilityRowDisplayName(row, rowId);
        var category = GetProbabilityCategory(sourceName);
        if (category.Length == 0)
            return;

        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral ||
                !IsProbabilityNumericType(field.FieldType) ||
                !IsProbabilityMemberName(field.Name))
                continue;

            try
            {
                var value = field.GetValue(row);
                if (value == null)
                    continue;
                _probabilityEntries.Add(new ProbabilityEntry(
                    category,
                    sourceName,
                    rowId,
                    displayName,
                    row,
                    field,
                    value,
                    persistentId: BuildSourcePersistentId(category, sourceName, rowId, displayName, field.Name)));
            }
            catch
            {
                errorCount++;
            }
        }
    }
    private static bool IsProbabilityMemberName(string name)
    {
        var normalized = (name ?? "").Replace("_", "").ToLowerInvariant();
        if (normalized.Length == 0 || normalized == "tempchance")
            return false;
        if (normalized == "chance" || normalized.EndsWith("chance", StringComparison.Ordinal) ||
            normalized.Contains("probability") || normalized.EndsWith("prob", StringComparison.Ordinal))
            return true;
        if (normalized == "critrange" || normalized == "fumblerange")
            return true;
        if (!normalized.Contains("rate"))
            return false;
        return normalized.Contains("spawn") || normalized.Contains("drop") || normalized.Contains("loot") ||
               normalized.Contains("encounter") || normalized.Contains("rare") || normalized.Contains("critical") ||
               normalized.Contains("crit") || normalized.Contains("dodge") || normalized.Contains("evade") ||
               normalized.Contains("block") || normalized.Contains("enchant") || normalized.Contains("bless") ||
               normalized.Contains("curse") || normalized.Contains("mutation") || normalized.Contains("reward");
    }
    private static bool IsProbabilityNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
            default:
                return false;
        }
    }
    private static string ReadProbabilityRowText(object row, string memberName)
    {
        try
        {
            var field = row.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            var value = field?.GetValue(row);
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
        catch { return ""; }
    }
    private static string GetProbabilityRowDisplayName(object row, string rowId)
    {
        try
        {
            var method = row.GetType().GetMethod("GetName", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (method != null && method.ReturnType == typeof(string))
            {
                var value = method.Invoke(row, null) as string;
                if (!string.IsNullOrWhiteSpace(value) && value != "*r")
                    return value.Trim();
            }
        }
        catch { }

        var name = ReadProbabilityRowText(row, "name");
        if (!string.IsNullOrWhiteSpace(name) && name != "*r")
            return name.Trim();
        if (!string.IsNullOrWhiteSpace(rowId))
            return rowId;
        return row.GetType().Name;
    }
    private static string GetProbabilityCategory(string sourceName)
    {
        switch ((sourceName ?? "").ToLowerInvariant())
        {
            case "charas":
            case "persons":
            case "races":
            case "jobs":
            case "tactics":
                return "character";
            case "things":
            case "thingv":
            case "categories":
            case "recipes":
                return "item";
            case "foods":
                return "food";
            case "materials":
                return "material";
            case "elements":
            case "stats":
                return "element";
            case "zones":
            case "areas":
            case "zoneaffixes":
            case "spawnlists":
            case "blocks":
            case "floors":
            case "decos":
            case "celleffects":
            case "objs":
            case "globaltiles":
                return "zone";
            case "quests":
            case "researches":
            case "homeresources":
            case "collectibles":
            case "hobbies":
            case "religions":
            case "factions":
                return "quest";
            case "checks":
            case "calc":
                return "";
            default:
                return "other";
        }
    }
    private static int GetProbabilityCategoryIndex(string categoryKey)
    {
        for (var i = 0; i < ProbabilityCategoryOrder.Length; i++)
            if (string.Equals(ProbabilityCategoryOrder[i], categoryKey, StringComparison.Ordinal))
                return i;
        return ProbabilityCategoryOrder.Length;
    }
    private string GetProbabilityCategoryLabel(string categoryKey)
    {
        switch (categoryKey)
        {
            case "character": return T("NPC生成概率 (随机池权重)", "NPC spawn chance (random-pool weight)");
            case "item": return T("物品生成概率 (随机池权重)", "Item spawn chance (random-pool weight)");
            case "food": return T("食品生成", "Food spawning");
            case "material": return T("材质生成 (随机池权重)", "Material generation (random-pool weight)");
            case "element": return T("元素附魔 (随机池权重)", "Element enchantments (random-pool weight)");
            case "zone": return T("地图生成 (随机池权重)", "Map generation (random-pool weight)");
            case "quest": return T("任务、事件、家园 (随机池权重)", "Quests, events, homes (random-pool weight)");
            case "minigame": return T("小游戏", "Mini-games");
            case "drop": return T("掉落倍率", "Drop multipliers");
            default: return T("其他概率", "Other probabilities");
        }
    }
    private string GetProbabilityMemberLabel(string memberName)
    {
        switch ((memberName ?? "").ToLowerInvariant())
        {
            case "chance": return T("概率", "Chance");
            case "critrange": return T("暴击范围", "Critical range");
            case "fumblerange": return T("大失败范围", "Fumble range");
            case "slotforcedwinpercent": return T("概率 (%)", "Chance (%)");
            case "gamblechestjackpotrange": return T("随机范围 N", "Random range N");
            case "scratchmedaldenominator":
            case "scratchplatinumdenominator":
            case "scratchfurnituredenominator":
            case "scratchmodelboxdenominator":
            case "scratchfooddenominator":
            case "fortunegrade1denominator":
            case "fortunegrade2denominator":
            case "fortunegrade3denominator":
            case "gamblechestforcedsuccessdenominator":
            case "gamblechestforcedfailuredenominator":
                return T("概率分母 (1/N)", "Chance denominator (1/N)");
            default: return memberName ?? "";
        }
    }
    private string GetProbabilityMemberLabel(ProbabilityEntry entry)
    {
        switch (entry.MemberLabelKind)
        {
            case ProbabilityMemberLabelKind.Percent:
                return T("概率 (%)", "Chance (%)");
            case ProbabilityMemberLabelKind.Denominator:
                return T("概率分母 (1/N)", "Chance denominator (1/N)");
            case ProbabilityMemberLabelKind.RandomRange:
                return T("随机范围 N", "Random range N");
            case ProbabilityMemberLabelKind.Multiplier:
                return T("倍率 (X)", "Multiplier (X)");
            default:
                return GetProbabilityMemberLabel(entry.Field.Name);
        }
    }
    private void RebuildProbabilityRows()
    {
        if (_probabilityList == null)
            return;

        _probabilityRows.Clear();
        var filter = (_probabilityFilter ?? "").Trim();
        var filterActive = filter.Length > 0;

        for (var categoryIndex = 0; categoryIndex < ProbabilityCategoryOrder.Length; categoryIndex++)
        {
            var categoryKey = ProbabilityCategoryOrder[categoryIndex];
            var categoryEntries = _probabilityEntries
                .Where(entry => string.Equals(entry.CategoryKey, categoryKey, StringComparison.Ordinal) && ProbabilityEntryMatchesFilter(entry, filter))
                .ToList();
            if (categoryEntries.Count == 0)
                continue;

            if (!_probabilityCategoryExpanded.TryGetValue(categoryKey, out var expanded))
                expanded = false;
            var effectiveExpanded = filterActive || expanded;
            var modified = categoryEntries.Count(entry => entry.HasOriginal);
            _probabilityRows.Add(new ProbabilityRow(categoryKey, categoryEntries.Count, modified, effectiveExpanded));
            if (effectiveExpanded)
                for (var i = 0; i < categoryEntries.Count; i++)
                    _probabilityRows.Add(new ProbabilityRow(categoryEntries[i]));
        }

        if (_probabilityRows.Count == 0)
            _probabilityRows.Add(new ProbabilityRow(filterActive
                ? T("没有符合过滤条件的概率项", "No probability values match the filter")
                : T("未发现可修改的概率项", "No editable probability values were found")));

        _probabilityList.SetItems(_probabilityRows);
        UpdateProbabilitySummary();
    }
    private bool ProbabilityEntryMatchesFilter(ProbabilityEntry entry, string filter)
    {
        if (filter.Length == 0)
            return true;
        return LGuiFilterMatches(
            entry.DisplayName,
            entry.RowId,
            (entry.MemberLabelKind == ProbabilityMemberLabelKind.Auto
                ? entry.SourceName + "." + entry.Field.Name
                : entry.SourceName) + " " + GetProbabilityCategoryLabel(entry.CategoryKey) + " " + GetProbabilityMemberLabel(entry),
            filter);
    }
    private void UpdateProbabilitySummary()
    {
        if (_probabilitySummaryText == null)
            return;
        _probabilitySummaryText.text = T("数值: ", "Values: ") + _probabilityEntries.Count.ToString(CultureInfo.InvariantCulture) +
                                              "  " + T("已修改: ", "Modified: ") + _probabilityModifiedCount.ToString(CultureInfo.InvariantCulture);
    }
}
