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
    private string AiToolSpawnItem(string args)
    {
        EnsureItemRows();
        var id = AiArgString(args, "item_id");
        var item = FindAiItem(id);
        if (item == null)
            return "failed: item not found: " + id;
        var oldCount = _itemCount;
        var oldLv = _itemLv;
        var oldMat = _itemMat;
        _itemCount = Math.Max(1, AiArgInt(args, "count", 1)).ToString(CultureInfo.InvariantCulture);
        _itemLv = Math.Max(1, AiArgInt(args, "level", 1)).ToString(CultureInfo.InvariantCulture);
        _itemMat = AiArgInt(args, "material_id", -1).ToString(CultureInfo.InvariantCulture);
        SpawnItem(item);
        var log = _itemLog;
        _itemCount = oldCount;
        _itemLv = oldLv;
        _itemMat = oldMat;
        return (log.StartsWith(T("生成失败：", "Spawn failed:"), StringComparison.Ordinal) ? "failed: " : "ok: ") + log;
    }
    private string AiToolSpawnNpc(string args)
    {
        EnsureNpcRows();
        var id = AiArgString(args, "npc_id");
        var npc = FindAiNpc(id);
        var npcId = npc == null ? id : npc.Id;
        if (string.IsNullOrWhiteSpace(npcId))
            return "failed: npc_id is empty";
        var oldId = _npcSpawnId;
        var oldLv = _npcSpawnLv;
        var oldAffinity = _npcSpawnAffinity;
        var oldRelation = _npcSpawnHostilityIndex;
        _npcSpawnId = npcId;
        _npcSpawnLv = AiArgInt(args, "level", -1).ToString(CultureInfo.InvariantCulture);
        _npcSpawnAffinity = AiArgInt(args, "affinity", 0).ToString(CultureInfo.InvariantCulture);
        _npcSpawnHostilityIndex = GetRelationshipIndex(AiArgRelationship(args, "relationship", Hostility.Friend));
        SpawnNpc();
        var log = _npcLog;
        _npcSpawnId = oldId;
        _npcSpawnLv = oldLv;
        _npcSpawnAffinity = oldAffinity;
        _npcSpawnHostilityIndex = oldRelation;
        return (log.StartsWith(T("NPC生成失败：", "NPC spawn failed:"), StringComparison.Ordinal) ? "failed: " : "ok: ") + log;
    }
    private IEnumerable<Thing> EnumerateAiInventoryThings()
    {
        var result = new List<Thing>();
        var seen = new HashSet<int>();
        void AddThing(Thing? thing)
        {
            if (!CanCustomizeItemAmount(thing))
                return;
            var uid = GetAiCardUid(thing!);
            if (!seen.Add(uid))
                return;
            result.Add(thing!);
        }

        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            if (pc != null)
            {
                EnumerateThingObjects(GetMemberValue(pc, "things"), AddThing, new HashSet<object>(ReferenceObjectComparer.Instance), 0);
                EnumerateThingObjects(GetMemberValue(pc, "inventory"), AddThing, new HashSet<object>(ReferenceObjectComparer.Instance), 0);
                EnumerateThingObjects(GetMemberValue(pc, "items"), AddThing, new HashSet<object>(ReferenceObjectComparer.Instance), 0);
            }
        }
        catch { }

        try
        {
            var owner = InvOwner.Main;
            EnumerateThingObjects(owner, AddThing, new HashSet<object>(ReferenceObjectComparer.Instance), 0);
        }
        catch { }

        return result;
    }
    private static void EnumerateThingObjects(object? value, Action<Thing> add, HashSet<object> visited, int depth)
    {
        if (value == null || add == null || depth > 4)
            return;
        if (value is string)
            return;
        if (value is Thing thing)
        {
            add(thing);
            return;
        }
        if (value is Card card)
        {
            if (card is Thing cardThing)
                add(cardThing);
            return;
        }
        if (!value.GetType().IsValueType && !visited.Add(value))
            return;

        if (value is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                EnumerateThingObjects(entry.Key, add, visited, depth + 1);
                EnumerateThingObjects(entry.Value, add, visited, depth + 1);
            }
            return;
        }
        if (value is IEnumerable items)
        {
            foreach (var item in items)
                EnumerateThingObjects(item, add, visited, depth + 1);
            return;
        }

        var type = value.GetType();
        if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal))
            return;

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(string) || field.FieldType.IsPrimitive || field.FieldType.IsEnum)
                continue;
            if (!typeof(IEnumerable).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(Thing) && field.FieldType != typeof(Card) && !field.FieldType.Name.Contains("Thing"))
                continue;
            try { EnumerateThingObjects(field.GetValue(value), add, visited, depth + 1); }
            catch { }
        }
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                continue;
            if (property.PropertyType == typeof(string) || property.PropertyType.IsPrimitive || property.PropertyType.IsEnum)
                continue;
            if (!typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(Thing) && property.PropertyType != typeof(Card) && !property.PropertyType.Name.Contains("Thing"))
                continue;
            try { EnumerateThingObjects(property.GetValue(value, null), add, visited, depth + 1); }
            catch { }
        }
    }
    private static bool AiInventoryThingMatchesFilter(Thing thing, string filter)
    {
        if (thing == null)
            return false;
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        var needle = NormalizeAiKey(filter);
        foreach (var value in GetAiInventoryThingSearchValues(thing))
        {
            var key = NormalizeAiKey(value);
            if (!string.IsNullOrEmpty(key) && (key.Contains(needle) || needle.Contains(key)))
                return true;
        }
        return false;
    }
    private static IEnumerable<string> GetAiInventoryThingSearchValues(Thing thing)
    {
        yield return SafeThingName(thing);
        yield return SafeText(() => thing.Name, "");
        yield return SafeText(() => thing.id, "");
        yield return SafeText(() => thing.uid.ToString(CultureInfo.InvariantCulture), "");
        yield return SafeText(() => thing.idMaterial.ToString(CultureInfo.InvariantCulture), "");
        yield return SafeText(() => thing.trait == null ? "" : thing.trait.ToString(), "");
    }
    private List<Thing> FindAiInventoryThingMatches(string text, string matchMode)
    {
        var things = EnumerateAiInventoryThings().ToList();
        var needle = NormalizeAiKey(text);
        var mode = NormalizeAiKey(matchMode);
        var exact = new List<Thing>();
        var contains = new List<Thing>();
        int uid;
        var numeric = int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uid);

        foreach (var thing in things)
        {
            if (mode == "uid")
            {
                if (numeric && GetAiCardUid(thing) == uid)
                    exact.Add(thing);
                continue;
            }
            if (mode == "id")
            {
                var idKey = NormalizeAiKey(SafeText(() => thing.id, ""));
                if (idKey == needle)
                    exact.Add(thing);
                else if (!string.IsNullOrEmpty(idKey) && idKey.Contains(needle))
                    contains.Add(thing);
                continue;
            }

            if (mode == "auto" || string.IsNullOrEmpty(mode))
            {
                if (numeric && GetAiCardUid(thing) == uid)
                {
                    exact.Add(thing);
                    continue;
                }
                var idKey = NormalizeAiKey(SafeText(() => thing.id, ""));
                if (idKey == needle)
                {
                    exact.Add(thing);
                    continue;
                }
            }

            foreach (var value in GetAiInventoryThingSearchValues(thing))
            {
                var key = NormalizeAiKey(value);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (key == needle)
                {
                    exact.Add(thing);
                    break;
                }
                if ((mode == "name" || mode == "auto" || string.IsNullOrEmpty(mode)) && (key.Contains(needle) || needle.Contains(key)))
                {
                    if (!contains.Contains(thing))
                        contains.Add(thing);
                }
            }
        }

        return exact.Count > 0 ? exact.Distinct().ToList() : contains.Distinct().ToList();
    }
    private Thing? ResolveAiInventoryThingFromArgs(string args, out string error)
    {
        error = "";
        var itemText = AiArgString(args, "item");
        var matchMode = NormalizeAiKey(AiArgString(args, "match_mode", "auto"));
        if (string.IsNullOrWhiteSpace(itemText))
        {
            error = "failed: item is empty";
            return null;
        }

        var matches = FindAiInventoryThingMatches(itemText, matchMode);
        if (matches.Count == 0)
        {
            error = "failed: inventory item not found: " + itemText + "\n" + AiToolListInventoryItems("{\"filter\":\"" + EscapeJson(itemText) + "\",\"limit\":20}");
            return null;
        }
        if (matches.Count > 1)
        {
            var sb = new StringBuilder();
            sb.Append("failed: ambiguous inventory item: ").Append(itemText).AppendLine();
            sb.AppendLine("Use UID from these candidates:");
            for (var i = 0; i < Math.Min(matches.Count, 20); i++)
                AppendAiInventoryThingLine(sb, matches[i], i + 1);
            if (matches.Count > 20)
                sb.AppendLine("truncated: " + matches.Count.ToString(CultureInfo.InvariantCulture) + " candidates");
            error = sb.ToString().TrimEnd();
            return null;
        }
        return matches[0];
    }
    private List<Thing> ResolveAiDeleteInventoryTargets(string args, out string error)
    {
        error = "";
        var scope = NormalizeAiKey(AiArgString(args, "scope"));
        var itemText = AiArgString(args, "item");
        var filter = AiArgString(args, "filter");
        var matchMode = NormalizeAiKey(AiArgString(args, "match_mode", "auto"));

        if (string.IsNullOrEmpty(scope))
        {
            if (!string.IsNullOrWhiteSpace(itemText)) scope = "uid";
            else if (!string.IsNullOrWhiteSpace(filter)) scope = "matching";
            else scope = "all";
        }

        if (scope == "all" || scope == "全部" || scope == "清空")
        {
            var targets = EnumerateAiInventoryThings().ToList();
            if (targets.Count == 0)
                error = "failed: inventory is empty or no editable real items were found";
            return targets;
        }

        if (scope == "matching" || scope == "match" || scope == "filter" || scope == "匹配")
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                error = "failed: filter is required for matching delete";
                return new List<Thing>();
            }

            var targets = EnumerateAiInventoryThings()
                .Where(thing => AiInventoryThingMatchesFilter(thing, filter))
                .ToList();
            if (targets.Count == 0)
                error = "failed: no inventory items matched filter: " + filter;
            return targets;
        }

        if (scope == "uid" || scope == "item" || scope == "single" || scope == "单个")
        {
            if (string.IsNullOrWhiteSpace(itemText))
            {
                error = "failed: item is required for uid/single delete";
                return new List<Thing>();
            }

            var matches = FindAiInventoryThingMatches(itemText, matchMode);
            if (matches.Count == 0)
            {
                error = "failed: inventory item not found: " + itemText + "\n" + AiToolListInventoryItems("{\"filter\":\"" + EscapeJson(itemText) + "\",\"limit\":20}");
                return new List<Thing>();
            }
            if (matches.Count > 1)
            {
                var sb = new StringBuilder();
                sb.Append("failed: ambiguous inventory item: ").Append(itemText).AppendLine();
                sb.AppendLine("Use UID from these candidates:");
                for (var i = 0; i < Math.Min(matches.Count, 20); i++)
                    AppendAiInventoryThingLine(sb, matches[i], i + 1);
                if (matches.Count > 20)
                    sb.AppendLine("truncated: " + matches.Count.ToString(CultureInfo.InvariantCulture) + " candidates");
                error = sb.ToString().TrimEnd();
                return new List<Thing>();
            }
            return matches;
        }

        error = "failed: unsupported delete scope " + scope;
        return new List<Thing>();
    }
    private string ValidateAiDeleteInventoryItemsArguments(string args)
    {
        string error;
        var targets = ResolveAiDeleteInventoryTargets(args, out error);
        if (!string.IsNullOrEmpty(error))
            return error;
        if (targets.Count == 0)
            return "failed: no inventory items matched";
        return "";
    }
    private string AiToolDeleteInventoryItemsNow(string args)
    {
        string error;
        var targets = ResolveAiDeleteInventoryTargets(args, out error);
        if (!string.IsNullOrEmpty(error))
            return error;
        if (targets.Count == 0)
            return "failed: no inventory items matched";

        var deleted = new List<string>();
        var failed = new List<string>();
        foreach (var target in targets.Distinct().ToList())
        {
            if (!CanCustomizeItemAmount(target))
                continue;
            var label = FormatAiInventoryThing(target);
            try
            {
                target.Destroy();
                deleted.Add(label);
            }
            catch (Exception ex)
            {
                failed.Add(label + " | " + ex.Message);
            }
        }

        RefreshInventoryUi();
        var sb = new StringBuilder();
        sb.Append("ok: deleted inventory items=").Append(deleted.Count.ToString(CultureInfo.InvariantCulture));
        if (failed.Count > 0)
            sb.Append(" failed=").Append(failed.Count.ToString(CultureInfo.InvariantCulture));
        var preview = Math.Min(deleted.Count, 20);
        for (var i = 0; i < preview; i++)
            sb.AppendLine().Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(deleted[i]);
        if (deleted.Count > preview)
            sb.AppendLine().Append("truncated: ").Append((deleted.Count - preview).ToString(CultureInfo.InvariantCulture)).Append(" more deleted");
        if (failed.Count > 0)
        {
            sb.AppendLine().Append("Failed:");
            for (var i = 0; i < Math.Min(failed.Count, 10); i++)
                sb.AppendLine().Append(failed[i]);
        }
        return sb.ToString().TrimEnd();
    }
    private static void ApplyAiOptionalIntText(string args, string name, Action<string> setter)
    {
        if (AiHasArg(args, name))
            setter(AiArgInt(args, name, 0).ToString(CultureInfo.InvariantCulture));
    }
    private static void ApplyAiOptionalBool(string args, string name, Action<bool> setter)
    {
        if (AiHasArg(args, name))
            setter(AiArgBool(args, name, false));
    }
    private static bool TryApplyAiValuePairs(string text, List<GeneValueInput> target, out string error)
    {
        error = "";
        if (target == null)
        {
            error = "failed: target value list is null";
            return false;
        }
        target.Clear();
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var separators = new[] { ',', ';', '\n', '\r' };
        var parts = text.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<int>();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (string.IsNullOrEmpty(part))
                continue;
            var split = part.Split(new[] { '=', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length < 2)
            {
                error = "failed: invalid value pair '" + part + "'. Use elementId=value.";
                return false;
            }
            int id;
            int value;
            if (!int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ||
                !int.TryParse(split[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = "failed: invalid numeric value pair '" + part + "'";
                return false;
            }
            if (id <= 0)
            {
                error = "failed: element ID must be greater than 0";
                return false;
            }
            if (!seen.Add(id))
            {
                error = "failed: duplicate element ID " + id.ToString(CultureInfo.InvariantCulture);
                return false;
            }
            target.Add(new GeneValueInput(id.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture)));
        }
        return true;
    }
    private static int GetAiCardUid(Card card)
    {
        try { return card == null ? 0 : card.uid; }
        catch { return RuntimeHelpers.GetHashCode(card); }
    }
    private static string FormatAiInventoryThing(Thing thing)
    {
        var name = SafeThingName(thing);
        var id = SafeText(() => thing.id, "");
        var uid = SafeText(() => thing.uid.ToString(CultureInfo.InvariantCulture), "0");
        var count = SafeText(() => thing.Num.ToString(CultureInfo.InvariantCulture), "0");
        var level = SafeText(() => thing.LV.ToString(CultureInfo.InvariantCulture), "0");
        var material = SafeText(() => thing.idMaterial.ToString(CultureInfo.InvariantCulture), "0");
        var skin = SafeText(() => thing.idSkin.ToString(CultureInfo.InvariantCulture), "0");
        var rarity = SafeText(() => thing.rarityLv.ToString(CultureInfo.InvariantCulture), "0");
        var blessed = SafeText(() => ((int)thing.blessedState).ToString(CultureInfo.InvariantCulture), "0");
        return "uid=" + uid + " | name=" + name + " | id=" + id + " | count=" + count + " | lv=" + level + " | material=" + material + " | skin=" + skin + " | blessed=" + blessed + " | rarity=" + rarity;
    }
    private string FormatAiInventoryItemData(Thing thing)
    {
        var sb = new StringBuilder();
        sb.AppendLine(FormatAiInventoryThing(thing));
        sb.Append("editable=item");
        if (CanEditFoodData(thing)) sb.Append(",food");
        if (CanEditWeaponData(thing)) sb.Append(",weapon");
        if (CanEditGene(thing)) sb.Append(",gene");
        sb.AppendLine();
        sb.AppendLine("base: level=" + SafeText(() => thing.LV.ToString(CultureInfo.InvariantCulture), "0") +
                      " | enhance=" + SafeText(() => thing.encLV.ToString(CultureInfo.InvariantCulture), "0") +
                      " | material_id=" + SafeText(() => thing.idMaterial.ToString(CultureInfo.InvariantCulture), "0") +
                      " | weight=" + SafeText(() => thing.SelfWeight.ToString(CultureInfo.InvariantCulture), "0") +
                      " | variant_id=" + SafeText(() => thing.idSkin.ToString(CultureInfo.InvariantCulture), "0") +
                      " | blessed_state=" + SafeText(() => ((int)thing.blessedState).ToString(CultureInfo.InvariantCulture), "0") +
                      " | rarity=" + SafeText(() => thing.rarityLv.ToString(CultureInfo.InvariantCulture), "0"));
        sb.AppendLine("flags: " + FormatAiThingFlags(thing));
        sb.AppendLine("item: fixed_price=" + SafeText(() => thing.c_priceFix.ToString(CultureInfo.InvariantCulture), "0") +
                      " | value=" + SafeText(() => thing.c_fixedValue.ToString(CultureInfo.InvariantCulture), "0") +
                      " | value_bonus=" + SafeText(() => thing.c_priceAdd.ToString(CultureInfo.InvariantCulture), "0"));
        if (CanEditFoodData(thing))
        {
            sb.AppendLine("food: rot=" + SafeText(() => Clamp(GetRawFoodDecay(thing), 0, Math.Max(1, thing.MaxDecay)).ToString(CultureInfo.InvariantCulture), "0") +
                          " | max_rot=" + SafeText(() => thing.MaxDecay.ToString(CultureInfo.InvariantCulture), "0"));
        }
        if (CanEditWeaponData(thing))
        {
            sb.AppendLine("weapon: damage_dice_sides=" + SafeText(() => thing.c_diceDim.ToString(CultureInfo.InvariantCulture), "0") +
                          " | hit=" + GetThingElementBase(thing, 66).ToString(CultureInfo.InvariantCulture) +
                          " | damage_bonus=" + GetThingElementBase(thing, 67).ToString(CultureInfo.InvariantCulture) +
                          " | dv=" + GetThingElementBase(thing, 64).ToString(CultureInfo.InvariantCulture) +
                          " | pv=" + GetThingElementBase(thing, 65).ToString(CultureInfo.InvariantCulture) +
                          " | charges=" + SafeText(() => thing.c_charges.ToString(CultureInfo.InvariantCulture), "0") +
                          " | ammo=" + SafeText(() => thing.c_ammo.ToString(CultureInfo.InvariantCulture), "0") +
                          " | range=" + SafeIntText(() => thing.range) +
                          " | penetration=" + SafeIntText(() => thing.Penetration) +
                          " | modification_slots=" + GetWeaponModificationSlotCount(thing).ToString(CultureInfo.InvariantCulture));
        }
        if (CanEditGene(thing) && EnsureEditableGeneDna(thing))
        {
            var dna = thing.c_DNA;
            sb.AppendLine("gene: source_id=" + (dna.id ?? "") +
                          " | level=" + dna.lv.ToString(CultureInfo.InvariantCulture) +
                          " | seed=" + dna.seed.ToString(CultureInfo.InvariantCulture) +
                          " | cost=" + dna.cost.ToString(CultureInfo.InvariantCulture) +
                          " | slots=" + dna.slot.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("gene_effects: " + FormatAiDnaValues(dna));
        }
        sb.AppendLine("elements: " + FormatAiThingElements(thing));
        return sb.ToString().TrimEnd();
    }
    private static string FormatAiThingFlags(Thing thing)
    {
        try
        {
            var parts = new[]
            {
                "is_stolen=" + thing.isStolen.ToString(),
                "is_crafted=" + thing.isCrafted.ToString(),
                "is_gifted=" + thing.isGifted.ToString(),
                "is_replica=" + thing.isReplica.ToString(),
                "is_copy=" + thing.isCopy.ToString(),
                "is_fireproof=" + thing.isFireproof.ToString(),
                "is_acidproof=" + thing.isAcidproof.ToString(),
                "is_broken=" + thing.isBroken.ToString(),
                "no_sell=" + thing.noSell.ToString(),
                "is_lost_property=" + thing.isLostProperty.ToString()
            };
            return string.Join(", ", parts);
        }
        catch
        {
            return "unavailable";
        }
    }
    private static string FormatAiThingElements(Thing thing)
    {
        try
        {
            var parts = new List<string>();
            var rows = new List<Element>();
            foreach (var element in thing.elements.dict.Values)
                if (element != null && element.id > 0)
                    rows.Add(element);
            rows.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var element in rows)
                parts.Add(element.id.ToString(CultureInfo.InvariantCulture) + "=" + GetThingElementEditorValue(thing, element).ToString(CultureInfo.InvariantCulture) + "(" + GetGeneEffectNameStatic(element.id) + ")");
            return parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }
        catch
        {
            return "unavailable";
        }
    }
    private static string FormatAiDnaValues(DNA dna)
    {
        try
        {
            if (dna == null || dna.vals == null || dna.vals.Count == 0)
                return "none";
            var parts = new List<string>();
            for (var i = 0; i + 1 < dna.vals.Count; i += 2)
                parts.Add(dna.vals[i].ToString(CultureInfo.InvariantCulture) + "=" + dna.vals[i + 1].ToString(CultureInfo.InvariantCulture) + "(" + GetGeneEffectNameStatic(dna.vals[i]) + ")");
            return parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }
        catch
        {
            return "unavailable";
        }
    }
    internal static string GetGeneEffectNameStatic(int id)
    {
        try
        {
            var row = FindSourceElementRowById(id);
            if (row == null)
                return id.ToString(CultureInfo.InvariantCulture);
            var name = GetElementDisplayName(row);
            if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                name = GetString(row, "alias");
            return string.IsNullOrEmpty(name) ? id.ToString(CultureInfo.InvariantCulture) : CleanDisplayName(name);
        }
        catch
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
