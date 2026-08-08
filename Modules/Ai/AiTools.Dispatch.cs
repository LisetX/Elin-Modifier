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
    private string ExecuteAiToolCalls(List<AiToolCall> toolCalls)
    {
        if (toolCalls == null || toolCalls.Count == 0)
            return "";

        var sb = new StringBuilder();
        var maxCalls = Math.Min(toolCalls.Count, 12);
        for (var i = 0; i < maxCalls; i++)
        {
            var call = toolCalls[i];
            try
            {
                var result = ExecuteAiToolCall(call.Name, call.Arguments);
                sb.Append("#").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                    .Append(" ").Append(call.Name).Append(": ").AppendLine(result);
            }
            catch (Exception ex)
            {
                sb.Append("#").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                    .Append(" ").Append(call.Name).Append(": failed: ").AppendLine(ex.Message);
            }
        }
        if (toolCalls.Count > maxCalls)
            sb.AppendLine("Some EMG calls were skipped because the per-message limit is 12.");
        return sb.ToString().TrimEnd();
    }
    private string ExecuteAiToolCall(string name, string arguments)
    {
        return ExecuteAiToolCall(name, arguments, true, true);
    }
    private string ExecuteAiToolCall(string name, string arguments, bool cacheAiPluginFeature)
    {
        return ExecuteAiToolCall(name, arguments, cacheAiPluginFeature, true);
    }
    private string ExecuteAiToolCall(string name, string arguments, bool cacheAiPluginFeature, bool allowDangerousQueue)
    {
        name = NormalizeAiKey(name);
        string result;
        switch (name)
        {
            case "set_feature_enabled": result = AiToolSetFeature(arguments); break;
            case "set_plant_harvest_multiplier_settings": result = AiToolSetPlantHarvestMultiplierSettings(arguments); break;
            case "list_inventory_items": result = AiToolListInventoryItems(arguments); break;
            case "set_inventory_item_amount": result = AiToolSetInventoryItemAmount(arguments); break;
            case "delete_inventory_items": result = allowDangerousQueue ? AiToolQueueDangerousAction(name, arguments) : AiToolDeleteInventoryItemsNow(arguments); break;
            case "get_inventory_item_data": result = AiToolGetInventoryItemData(arguments); break;
            case "set_item_data": result = AiToolSetItemData(arguments); break;
            case "set_food_data": result = AiToolSetFoodData(arguments); break;
            case "set_weapon_data": result = AiToolSetWeaponData(arguments); break;
            case "set_gene_data": result = AiToolSetGeneData(arguments); break;
            case "list_game_names": result = AiToolListGameNames(arguments); break;
            case "spawn_item": result = AiToolSpawnItem(arguments); break;
            case "spawn_npc": result = AiToolSpawnNpc(arguments); break;
            case "set_character_value": result = AiToolSetCharacterValue(arguments); break;
            case "set_character_potential": result = AiToolSetCharacterPotential(arguments); break;
            case "set_ability_values": result = AiToolSetAbilityValues(arguments); break;
            case "set_npc_relationship": result = AiToolSetNpcRelationship(arguments); break;
            case "teleport": result = AiToolTeleport(arguments); break;
            case "set_home_value": result = AiToolSetHomeValue(arguments); break;
            case "set_player_info": result = AiToolSetPlayerInfo(arguments); break;
            case "set_ui_option": result = AiToolSetUiOption(arguments); break;
            case "emp_list_plugins": result = AiToolEmpListPlugins(arguments); break;
            case "emp_set_function_state": result = AiToolEmpSetFunctionState(arguments); break;
            case "emp_reload_plugins": result = AiToolEmpReloadPlugins(arguments); break;
            case "runtime_reflect_get": result = AiToolReflectGet(arguments); break;
            case "runtime_list_assemblies": result = AiToolListAssemblies(arguments); break;
            case "runtime_search": result = AiToolSearch(arguments); break;
            case "runtime_list_type": result = AiToolListType(arguments); break;
            case "runtime_reflect_set": result = allowDangerousQueue ? AiToolQueueDangerousAction(name, arguments) : AiToolReflectSetNow(arguments); break;
            case "runtime_invoke_method": result = allowDangerousQueue ? AiToolQueueDangerousAction(name, arguments) : AiToolInvokeMethodNow(arguments); break;
            case "runtime_harmony_patch": result = allowDangerousQueue ? AiToolQueueDangerousAction(name, arguments) : AiToolHarmonyPatchNow(arguments); break;
            case "runtime_harmony_unpatch": result = allowDangerousQueue ? AiToolQueueDangerousAction(name, arguments) : AiToolHarmonyUnpatchNow(arguments); break;
            default: result = "failed: unsupported tool " + name; break;
        }

        if (cacheAiPluginFeature)
            MaybeCacheAiPluginFeature(name, arguments, result);
        return result;
    }
    private void MaybeCacheAiPluginFeature(string toolName, string arguments, string result)
    {
        toolName = NormalizeAiKey(toolName);
        if (!IsAiPluginCacheableTool(toolName) || !IsAiPluginCacheSuccess(result))
            return;

        var storedArguments = BuildAiPluginCacheStoredArguments(toolName, arguments, result);
        var signature = BuildAiPluginCacheSignature(toolName, storedArguments);
        if (string.IsNullOrWhiteSpace(signature))
            return;

        var displayTitle = BuildAiPluginCacheDisplayTitle(toolName, storedArguments, result);
        var displayKind = BuildAiPluginCacheDisplayKind(toolName, storedArguments);
        var summary = TruncateForLog((result ?? "").Trim(), 240);
        AiPluginCacheEntry entry;
        if (_aiPluginCacheBySignature.TryGetValue(signature, out entry))
        {
            entry.Arguments = storedArguments;
            entry.DisplayTitle = displayTitle;
            entry.DisplayKind = displayKind;
            entry.Summary = summary;
            entry.CachedUtc = DateTime.UtcNow;
            return;
        }

        entry = new AiPluginCacheEntry(signature, toolName, storedArguments, displayTitle, displayKind, summary, DateTime.UtcNow);
        _aiPluginCacheBySignature[signature] = entry;
        _aiPluginCacheEntries.Insert(0, entry);
        while (_aiPluginCacheEntries.Count > 64)
        {
            var last = _aiPluginCacheEntries[_aiPluginCacheEntries.Count - 1];
            _aiPluginCacheEntries.RemoveAt(_aiPluginCacheEntries.Count - 1);
            if (last != null)
                _aiPluginCacheBySignature.Remove(last.Signature);
        }
        _aiPluginCacheLog = T("已缓存 AI 功能: ", "Cached AI feature: ") + displayTitle;
    }
    private static bool IsAiPluginCacheableTool(string toolName)
    {
        switch (NormalizeAiKey(toolName))
        {
            case "set_feature_enabled":
            case "set_plant_harvest_multiplier_settings":
            case "set_inventory_item_amount":
            case "set_item_data":
            case "set_food_data":
            case "set_weapon_data":
            case "set_gene_data":
            case "spawn_item":
            case "spawn_npc":
            case "set_character_value":
            case "set_character_potential":
            case "set_ability_values":
            case "set_npc_relationship":
            case "teleport":
            case "set_home_value":
            case "set_player_info":
            case "set_ui_option":
            case "runtime_reflect_set":
            case "runtime_invoke_method":
            case "runtime_harmony_patch":
                return true;
            default:
                return false;
        }
    }
    private static bool IsAiPluginCacheSuccess(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return false;
        var text = result.TrimStart();
        return text.StartsWith("ok:", StringComparison.OrdinalIgnoreCase);
    }
    private static string BuildAiPluginCacheStoredArguments(string toolName, string arguments, string result)
    {
        if (NormalizeAiKey(toolName) != "runtime_harmony_patch")
            return string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments;

        Dictionary<string, string> values;
        if (!TryReadAiPluginCacheArguments(arguments, out values))
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!values.ContainsKey("patch_id"))
        {
            var patchId = ExtractAiPluginPatchId(result);
            if (!string.IsNullOrWhiteSpace(patchId))
                values["patch_id"] = patchId;
        }
        return BuildAiPluginCacheArgumentsJson(values);
    }
    private static string ExtractAiPluginPatchId(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return "";
        var match = Regex.Match(result, @"\bid=([^\s|]+)");
        return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : "";
    }
    private static string BuildAiPluginCacheSignature(string toolName, string arguments)
    {
        Dictionary<string, string> values;
        var sb = new StringBuilder();
        toolName = NormalizeAiKey(toolName);
        sb.Append(toolName);
        if (TryReadAiPluginCacheArguments(arguments, out values))
        {
            foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (toolName == "set_feature_enabled" && string.Equals(pair.Key, "enabled", StringComparison.OrdinalIgnoreCase))
                    continue;
                sb.Append('|').Append(NormalizeAiKey(pair.Key)).Append('=').Append(pair.Value ?? "");
            }
        }
        else
        {
            sb.Append('|').Append(arguments ?? "");
        }
        return sb.ToString();
    }
    private static string BuildAiPluginCacheDisplayKind(string toolName, string arguments)
    {
        Dictionary<string, string> values;
        Dictionary<string, string> operationArguments;
        List<string> valueKeys;
        if (TryReadAiPluginCacheArguments(arguments, out values) &&
            TryBuildAiPluginCacheValueOperationArguments(toolName, values, out operationArguments, out valueKeys))
            return "Value";

        switch (NormalizeAiKey(toolName))
        {
            case "set_feature_enabled": return "Toggle";
            case "runtime_harmony_patch": return "Patch";
            default: return "Button";
        }
    }
    private static string BuildAiPluginCacheDisplayTitle(string toolName, string arguments, string result)
    {
        Dictionary<string, string> values;
        TryReadAiPluginCacheArguments(arguments, out values);
        values = values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string value;
        switch (NormalizeAiKey(toolName))
        {
            case "set_feature_enabled":
                return "Feature: " + (values.TryGetValue("feature", out value) ? value : "?");
            case "set_inventory_item_amount":
                return "Set amount: " + FirstAiPluginCacheValue(values, "item", "uid", "id") + " => " + FirstAiPluginCacheValue(values, "count", "amount");
            case "spawn_item":
                return "Spawn item: " + FirstAiPluginCacheValue(values, "item_id", "id", "item") + " x" + FirstAiPluginCacheValue(values, "count", "amount");
            case "spawn_npc":
                return "Spawn NPC: " + FirstAiPluginCacheValue(values, "npc_id", "id", "npc");
            case "set_item_data":
                return "Edit item: " + FirstAiPluginCacheValue(values, "item", "uid", "id");
            case "set_food_data":
                return "Edit food: " + FirstAiPluginCacheValue(values, "item", "uid", "id");
            case "set_weapon_data":
                return "Edit weapon: " + FirstAiPluginCacheValue(values, "item", "uid", "id");
            case "set_gene_data":
                return "Edit gene: " + FirstAiPluginCacheValue(values, "item", "uid", "id");
            case "set_character_value":
                return "Set character value: " + FirstAiPluginCacheValue(values, "target") + "." + FirstAiPluginCacheValue(values, "field") + " = " + FirstAiPluginCacheValue(values, "value");
            case "set_character_potential":
                return "Set potential: " + FirstAiPluginCacheValue(values, "target") + "." + FirstAiPluginCacheValue(values, "field") + " = " + FirstAiPluginCacheValue(values, "value");
            case "set_ability_values":
                return "Set ability: " + FirstAiPluginCacheValue(values, "target") + "." + FirstAiPluginCacheValue(values, "ability");
            case "set_npc_relationship":
                return "NPC relationship: " + FirstAiPluginCacheValue(values, "target");
            case "teleport":
                return "Teleport: " + FirstAiPluginCacheValue(values, "mode", "landmark", "x");
            case "set_home_value":
                return "Set home value: " + FirstAiPluginCacheValue(values, "category") + "." + FirstAiPluginCacheValue(values, "field");
            case "set_player_info":
                return "Set player info: " + FirstAiPluginCacheValue(values, "field", "name", "title", "race_id", "job_id");
            case "set_ui_option":
                return "Set UI option: " + FirstAiPluginCacheValue(values, "option") + " = " + FirstAiPluginCacheValue(values, "value");
            case "runtime_reflect_set":
                return "Reflect set: " + FirstAiPluginCacheValue(values, "target");
            case "runtime_invoke_method":
                return "Invoke: " + FirstAiPluginCacheValue(values, "target");
            case "runtime_harmony_patch":
                return "Harmony patch: " + FirstAiPluginCacheValue(values, "target") + " [" + FirstAiPluginCacheValue(values, "mode") + "]";
            default:
                return NormalizeAiKey(toolName) + ": " + TruncateForLog(arguments ?? "", 120);
        }
    }
    private static string FirstAiPluginCacheValue(Dictionary<string, string> values, params string[] keys)
    {
        if (values == null || keys == null)
            return "?";
        for (var i = 0; i < keys.Length; i++)
        {
            string value;
            if (values.TryGetValue(keys[i], out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return "?";
    }
    private void ClearAiPluginCache()
    {
        _aiPluginCacheEntries.Clear();
        _aiPluginCacheBySignature.Clear();
        _aiPluginCacheLog = T("AI 功能缓存已清空", "AI feature cache cleared");
    }
    private string ExportAiPluginCacheToWorkspace()
    {
        return ExportAiPluginCacheToWorkspace(null);
    }
    private string ExportAiPluginCacheToWorkspace(AiPluginCacheEntry singleEntry)
    {
        var entries = singleEntry == null
            ? _aiPluginCacheEntries.Where(entry => entry != null).Reverse().ToList()
            : new List<AiPluginCacheEntry> { singleEntry };
        if (entries.Count == 0)
            return T("没有可导出的 AI 缓存。", "No AI cache to export.");

        try
        {
            var root = Path.Combine(GetEmpPluginWorkspaceDirectory(), "AI_Cache");
            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var pluginId = "ai_cache_" + stamp;
            var pluginName = singleEntry == null
                ? "AI Cache Export " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : "AI Cache " + TruncateForLog(singleEntry.DisplayTitle, 48);
            var json = BuildAiPluginCacheExportJson(pluginId, pluginName, entries);
            var path = Path.Combine(root, pluginId + ".json");
            File.WriteAllText(path, json, Encoding.UTF8);
            _pluginDefinitionsDirty = true;
            RefreshEmpPluginDefinitions(true);
            return "ok: exported " + entries.Count.ToString(CultureInfo.InvariantCulture) + " AI cache entr" + (entries.Count == 1 ? "y" : "ies") + " to " + path;
        }
        catch (Exception ex)
        {
            return "failed: export AI cache failed: " + ex.Message;
        }
    }
    private static string BuildAiPluginCacheExportJson(string pluginId, string pluginName, List<AiPluginCacheEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"id\": \"" + EscapeJson(SanitizeAiPluginCacheId(pluginId)) + "\",");
        sb.AppendLine("  \"name\": \"" + EscapeJson(pluginName) + "\",");
        sb.AppendLine("  \"description\": \"Exported from Elin Modifier AI feature cache.\",");
        sb.AppendLine("  \"functions\": [");
        for (var i = 0; i < entries.Count; i++)
        {
            AppendAiPluginCacheFunctionJson(sb, entries[i], i + 1);
            if (i < entries.Count - 1)
                sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }
    private static void AppendAiPluginCacheFunctionJson(StringBuilder sb, AiPluginCacheEntry entry, int index)
    {
        var functionId = "ai_" + index.ToString("00", CultureInfo.InvariantCulture) + "_" + SanitizeAiPluginCacheId(entry.ToolName);
        var toolName = NormalizeAiKey(entry.ToolName);
        var name = TruncateForLog(entry.DisplayTitle, 80);
        var description = TruncateForLog(entry.Summary, 220);

        sb.AppendLine("    {");
        sb.AppendLine("      \"id\": \"" + EscapeJson(functionId) + "\",");
        sb.AppendLine("      \"name\": \"" + EscapeJson(name) + "\",");
        sb.AppendLine("      \"description\": \"" + EscapeJson(description) + "\",");

        if (toolName == "set_feature_enabled")
        {
            Dictionary<string, string> values;
            TryReadAiPluginCacheArguments(entry.Arguments, out values);
            var feature = FirstAiPluginCacheValue(values, "feature");
            sb.AppendLine("      \"kind\": \"toggle\",");
            sb.AppendLine("      \"enabled\": false,");
            sb.AppendLine("      \"on_enable\": [");
            AppendAiPluginCacheOperationJson(sb, "set_feature_enabled", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "feature", feature },
                { "enabled", "true" }
            }, "Enable " + feature, "        ");
            sb.AppendLine();
            sb.AppendLine("      ],");
            sb.AppendLine("      \"on_disable\": [");
            AppendAiPluginCacheOperationJson(sb, "set_feature_enabled", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "feature", feature },
                { "enabled", "false" }
            }, "Disable " + feature, "        ");
            sb.AppendLine();
            sb.AppendLine("      ]");
        }
        else if (toolName == "runtime_harmony_patch")
        {
            Dictionary<string, string> values;
            TryReadAiPluginCacheArguments(entry.Arguments, out values);
            var patchId = FirstAiPluginCacheValue(values, "patch_id");
            if (patchId == "?")
            {
                patchId = functionId + "_patch";
                values["patch_id"] = patchId;
            }
            sb.AppendLine("      \"kind\": \"patch\",");
            sb.AppendLine("      \"enabled\": false,");
            sb.AppendLine("      \"on_enable\": [");
            AppendAiPluginCacheOperationJson(sb, "runtime_harmony_patch", values, "Install " + name, "        ");
            sb.AppendLine();
            sb.AppendLine("      ],");
            sb.AppendLine("      \"on_disable\": [");
            AppendAiPluginCacheOperationJson(sb, "runtime_harmony_unpatch", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "patch_id", patchId }
            }, "Remove " + name, "        ");
            sb.AppendLine();
            sb.AppendLine("      ]");
        }
        else
        {
            Dictionary<string, string> values;
            TryReadAiPluginCacheArguments(entry.Arguments, out values);
            Dictionary<string, string> operationArguments;
            List<string> valueKeys;
            if (TryBuildAiPluginCacheValueOperationArguments(toolName, values, out operationArguments, out valueKeys))
            {
                sb.AppendLine("      \"kind\": \"value\",");
                sb.AppendLine("      \"value_kind\": \"string\",");
                sb.AppendLine("      \"enabled\": false,");
                sb.AppendLine("      \"default_value\": \"\",");
                if (valueKeys.Count > 1)
                {
                    sb.AppendLine("      \"parameters\": [");
                    AppendAiPluginCacheParameterJson(sb, valueKeys, "        ");
                    sb.AppendLine();
                    sb.AppendLine("      ],");
                }
                sb.AppendLine("      \"operations\": [");
                AppendAiPluginCacheOperationJson(sb, toolName, operationArguments, name, "        ");
                sb.AppendLine();
                sb.AppendLine("      ]");
            }
            else
            {
                sb.AppendLine("      \"enabled\": false,");
                sb.AppendLine("      \"kind\": \"button\",");
                sb.AppendLine("      \"operations\": [");
                AppendAiPluginCacheOperationJson(sb, toolName, values, name, "        ");
                sb.AppendLine();
                sb.AppendLine("      ]");
            }
        }

        sb.Append("    }");
    }
    private static bool TryBuildAiPluginCacheValueOperationArguments(string toolName, Dictionary<string, string> values, out Dictionary<string, string> operationArguments, out List<string> valueKeys)
    {
        operationArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        valueKeys = new List<string>();
        if (values == null)
            return false;

        foreach (var pair in values)
            operationArguments[pair.Key] = pair.Value;

        var candidates = GetAiPluginCacheValueKeys(toolName);
        if (candidates.Length == 0)
            return false;

        for (var i = 0; i < candidates.Length; i++)
        {
            if (values.ContainsKey(candidates[i]))
                valueKeys.Add(candidates[i]);
        }
        if (valueKeys.Count == 0)
            return false;

        if (valueKeys.Count == 1)
        {
            operationArguments[valueKeys[0]] = "{{value}}";
        }
        else
        {
            for (var i = 0; i < valueKeys.Count; i++)
                operationArguments[valueKeys[i]] = "{{value:" + valueKeys[i] + "}}";
        }
        return true;
    }
    private static void AppendAiPluginCacheParameterJson(StringBuilder sb, List<string> valueKeys, string indent)
    {
        for (var i = 0; i < valueKeys.Count; i++)
        {
            var key = valueKeys[i] ?? "";
            sb.Append(indent).Append("{ ");
            sb.Append("\"key\": \"").Append(EscapeJson(key)).Append("\", ");
            sb.Append("\"label\": \"").Append(EscapeJson(key)).Append("\", ");
            sb.Append("\"value_kind\": \"string\", ");
            sb.Append("\"default_value\": \"\"");
            sb.Append(" }");
            if (i < valueKeys.Count - 1)
                sb.Append(",");
            sb.AppendLine();
        }
    }
    private static string[] GetAiPluginCacheValueKeys(string toolName)
    {
        switch (NormalizeAiKey(toolName))
        {
            case "set_plant_harvest_multiplier_settings":
                return new[] { "crop_multiplier", "seed_multiplier" };
            case "set_inventory_item_amount":
                return new[] { "count", "amount" };
            case "spawn_item":
                return new[] { "count", "level", "material_id" };
            case "spawn_npc":
                return new[] { "level", "affinity", "relationship" };
            case "set_item_data":
                return new[] { "level", "enhance", "material_id", "weight", "variant_id", "fixed_price", "value", "value_bonus", "blessed_state", "is_stolen", "is_crafted", "is_gifted", "is_replica", "is_copy", "is_fireproof", "is_acidproof", "is_broken", "no_sell", "is_lost_property", "rarity", "enchantments" };
            case "set_food_data":
                return new[] { "level", "enhance", "material_id", "weight", "rot", "blessed_state", "is_stolen", "is_crafted", "is_gifted", "is_replica", "is_copy", "is_fireproof", "is_acidproof", "is_broken", "no_sell", "is_lost_property", "rarity", "effects" };
            case "set_weapon_data":
                return new[]
                {
                    "level", "enhance", "material_id", "damage_dice_sides", "hit", "damage_bonus", "dv", "pv",
                    "weight", "charges", "ammo", "range", "penetration", "modification_slots", "blessed_state", "is_stolen", "is_crafted", "is_gifted",
                    "is_replica", "is_copy", "is_fireproof", "is_acidproof", "is_broken", "no_sell",
                    "is_lost_property", "rarity", "enchantments"
                };
            case "set_gene_data":
                return new[] { "source_id", "level", "seed", "cost", "slots", "effects" };
            case "set_character_value":
            case "set_character_potential":
            case "set_home_value":
            case "set_ui_option":
            case "runtime_reflect_set":
                return new[] { "value" };
            case "set_ability_values":
                return new[] { "level", "chance", "power", "hp_cost", "mp_cost", "sp_cost", "stock" };
            case "set_npc_relationship":
                return new[] { "affinity", "relationship", "party_action" };
            case "teleport":
                return new[] { "landmark", "x", "y" };
            case "runtime_invoke_method":
                return new[] { "args" };
            case "set_player_info":
                return new[]
                {
                    "name", "alias", "honorific", "race_id", "job_id", "faith_id", "faction_id",
                    "gender", "age", "height_cm", "weight_kg", "birth_year", "birth_month", "birth_day",
                    "home_word_id", "location_word_id", "father_type_id", "father_prefix_id",
                    "mother_type_id", "mother_prefix_id", "liked_item_id",
                    "domain_ids", "hobby_ids", "work_ids", "background_text"
                };
            default:
                return Array.Empty<string>();
        }
    }
    private static void AppendAiPluginCacheOperationJson(StringBuilder sb, string toolName, Dictionary<string, string> arguments, string summary, string indent)
    {
        arguments = arguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sb.AppendLine(indent + "{");
        sb.AppendLine(indent + "  \"tool\": \"" + EscapeJson(toolName) + "\",");
        sb.AppendLine(indent + "  \"summary\": \"" + EscapeJson(summary ?? "") + "\",");
        sb.AppendLine(indent + "  \"args\": {");
        var pairs = arguments.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            sb.Append(indent).Append("    \"").Append(EscapeJson(pair.Key)).Append("\": \"").Append(EscapeJson(pair.Value ?? "")).Append("\"");
            if (i < pairs.Count - 1)
                sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine(indent + "  }");
        sb.Append(indent + "}");
    }
    private static bool TryReadAiPluginCacheArguments(string arguments, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(arguments))
            return true;
        try
        {
            using (var doc = JsonDocument.Parse(arguments, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return false;
                foreach (var prop in doc.RootElement.EnumerateObject())
                    values[prop.Name] = ReadEmpConfigScalar(prop.Value);
            }
            return true;
        }
        catch
        {
            values.Clear();
            return false;
        }
    }
    private static string BuildAiPluginCacheArgumentsJson(Dictionary<string, string> values)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        var pairs = values == null
            ? new List<KeyValuePair<string, string>>()
            : values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < pairs.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append('"').Append(EscapeJson(pairs[i].Key)).Append("\":\"").Append(EscapeJson(pairs[i].Value ?? "")).Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }
    private static string SanitizeAiPluginCacheId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "ai_cache";
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }
        var result = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "ai_cache" : result;
    }
}
