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
    private void EnsureEmpPluginWorkspace()
    {
        try
        {
            var dir = GetEmpPluginWorkspaceDirectory();
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            _pluginManagerLog = "EMP workspace error: " + ex.Message;
        }
    }
    private static string GetEmpPluginWorkspaceDirectory()
    {
        return Path.Combine(GetPluginDirectory(), "workspace", "Plugin");
    }
    private void ReloadEmpPluginDefinitions()
    {
        _pluginDefinitionsDirty = true;
        RefreshEmpPluginDefinitions(true);
        MarkAllEmpPluginStatesPending();
        ApplySavedEmpPluginStates(true);
    }
    private void RefreshEmpPluginDefinitionsIfNeeded()
    {
        if (_pluginDefinitionsDirty)
            RefreshEmpPluginDefinitions(false);
    }
    private void RefreshEmpPluginDefinitions(bool forceReload)
    {
        EnsureEmpPluginWorkspace();
        var root = GetEmpPluginWorkspaceDirectory();
        List<EmpPluginDefinition> loaded;
        string stamp;
        try
        {
            loaded = EmpPluginJsonLoader.Load(root, out stamp);
        }
        catch (Exception ex)
        {
            _pluginManagerLog = "EMP load failed: " + ex.Message;
            return;
        }

        if (!forceReload && !string.IsNullOrEmpty(stamp) && string.Equals(stamp, _pluginDefinitionsStamp, StringComparison.Ordinal))
        {
            _pluginDefinitionsDirty = false;
            return;
        }

        _pluginDefinitions.Clear();
        for (var i = 0; i < loaded.Count; i++)
        {
            var plugin = loaded[i];
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.Id))
                continue;
            _pluginDefinitions[plugin.Id] = plugin;
        }

        _pluginDefinitionsStamp = stamp ?? "";
        _pluginDefinitionsDirty = false;
        EnsureEmpPluginStateDefaults();
        if (forceReload)
            MarkAllEmpPluginStatesPending();
        _pluginManagerLog = "EMP loaded: " + _pluginDefinitions.Count.ToString(CultureInfo.InvariantCulture);
    }
    private void MarkAllEmpPluginStatesPending()
    {
        foreach (var state in _empFunctionStates.Values)
        {
            if (state == null)
                continue;
            state.PendingApply = true;
        }
        MarkEmpPending();
    }
    private void EnsureEmpPluginStateDefaults()
    {
        foreach (var plugin in _pluginDefinitions.Values)
        {
            if (plugin == null)
                continue;
            foreach (var function in plugin.Functions)
                GetEmpFunctionState(plugin, function);
        }
    }
    private void LoadEmpPluginStatesFromConfig(string json)
    {
        _empFunctionStates.Clear();
        MarkEmpPending();
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using (var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }))
            {
                if (!doc.RootElement.TryGetProperty("empPlugins", out var empElement) || empElement.ValueKind != JsonValueKind.Object)
                    return;

                foreach (var entry in empElement.EnumerateObject())
                {
                    var state = new EmpFunctionState();
                    if (entry.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (entry.Value.TryGetProperty("enabled", out var enabledElement))
                            state.Enabled = ReadEmpConfigBool(enabledElement, false);
                        if (entry.Value.TryGetProperty("value", out var valueElement))
                            state.Value = ReadEmpConfigScalar(valueElement);
                    }
                    state.PendingApply = true;
                    state.Initialized = false;
                    _empFunctionStates[entry.Name] = state;
                }
            }
        }
        catch (Exception ex)
        {
            _pluginManagerLog = "EMP config read failed: " + ex.Message;
        }

        EnsureEmpPluginStateDefaults();
    }
    private static bool ReadEmpConfigBool(JsonElement element, bool fallback)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                int i;
                return element.TryGetInt32(out i) ? i != 0 : fallback;
            case JsonValueKind.String:
                return ParseEmpBoolStatic(element.GetString(), fallback);
            default:
                return fallback;
        }
    }
    private static string ReadEmpConfigScalar(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? "";
            case JsonValueKind.Number:
                return element.ToString();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return "";
            default:
                return element.ToString();
        }
    }
    private void ApplySavedEmpPluginStates(bool showStatus)
    {
        RefreshEmpPluginDefinitionsIfNeeded();
        var statusLines = new List<string>();
        foreach (var plugin in _pluginDefinitions.Values)
        {
            if (plugin == null || !plugin.IsValid)
                continue;
            foreach (var function in plugin.Functions)
            {
                if (function == null || !function.IsValid || function.Kind == EmpFunctionKind.Button)
                    continue;
                var state = GetEmpFunctionState(plugin, function);
                if (!state.PendingApply && state.Initialized)
                    continue;
                var result = ApplyEmpFunctionStateNow(plugin, function, state, showStatus);
                if (showStatus && !string.IsNullOrWhiteSpace(result))
                    statusLines.Add(SafeEmpText(plugin.Name, plugin.Id) + "." + SafeEmpText(function.Name, function.Id) + ": " + result);
            }
        }
        if (showStatus && statusLines.Count > 0)
            _pluginManagerLog = "Status:\n" + string.Join("\n", statusLines);
    }
    private EmpFunctionState GetEmpFunctionState(EmpPluginDefinition plugin, EmpFunctionDefinition function)
    {
        var key = GetEmpFunctionKey(plugin, function);
        EmpFunctionState state;
        if (!_empFunctionStates.TryGetValue(key, out state))
        {
            state = new EmpFunctionState
            {
                Enabled = function != null && function.DefaultEnabled,
                Value = GetEmpDefaultValue(function),
                PendingApply = true,
                Initialized = false
            };
            _empFunctionStates[key] = state;
            MarkEmpPending();
        }
        else if (!state.Initialized && string.IsNullOrEmpty(state.Value) && function != null)
        {
            state.Value = GetEmpDefaultValue(function);
        }
        return state;
    }
    private static string GetEmpPluginKey(EmpPluginDefinition plugin)
    {
        return plugin == null ? "" : SafeEmpText(plugin.Id, "");
    }
    private static string GetEmpFunctionKey(EmpPluginDefinition plugin, EmpFunctionDefinition function)
    {
        return (plugin == null ? "" : SafeEmpText(plugin.Id, "")) + "::" + (function == null ? "" : SafeEmpText(function.Id, ""));
    }
    private bool GetPluginExpandedState(string key)
    {
        bool expanded;
        return _pluginExpanded.TryGetValue(key, out expanded) && expanded;
    }
    private void SetPluginExpandedState(string key, bool expanded)
    {
        _pluginExpanded[key] = expanded;
    }
    private bool GetPluginFunctionExpandedState(string key)
    {
        bool expanded;
        return _pluginFunctionExpanded.TryGetValue(key, out expanded) && expanded;
    }
    private void SetPluginFunctionExpandedState(string key, bool expanded)
    {
        _pluginFunctionExpanded[key] = expanded;
    }
    private static string SafeEmpText(string text, string fallback)
    {
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
    private static string GetEmpFunctionKindToken(EmpFunctionKind kind)
    {
        switch (kind)
        {
            case EmpFunctionKind.Value: return "value";
            case EmpFunctionKind.Patch: return "patch";
            case EmpFunctionKind.Button: return "button";
            default: return "toggle";
        }
    }
    private static string GetEmpFunctionKindDisplayName(EmpFunctionKind kind)
    {
        switch (kind)
        {
            case EmpFunctionKind.Value: return "Value";
            case EmpFunctionKind.Patch: return "Patch";
            case EmpFunctionKind.Button: return "Button";
            default: return "Toggle";
        }
    }
    private static string GetEmpValueKindToken(EmpValueKind kind)
    {
        switch (kind)
        {
            case EmpValueKind.Int: return "int";
            case EmpValueKind.Float: return "float";
            case EmpValueKind.Bool: return "bool";
            case EmpValueKind.Enum: return "enum";
            default: return "string";
        }
    }
    private static bool ParseEmpBool(string text, bool fallback)
    {
        return ParseEmpBoolStatic(text, fallback);
    }
    private static bool ParseEmpBoolStatic(string text, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        var key = NormalizeAiKey(text);
        if (key == "true" || key == "1" || key == "on" || key == "yes" || key == "enable" || key == "enabled" || key == "开" || key == "开启")
            return true;
        if (key == "false" || key == "0" || key == "off" || key == "no" || key == "disable" || key == "disabled" || key == "关" || key == "关闭")
            return false;
        return fallback;
    }
    private static int GetEmpOperationCount(EmpFunctionDefinition function)
    {
        if (function == null)
            return 0;
        return function.Operations.Count + function.OnEnableOperations.Count + function.OnDisableOperations.Count;
    }
    private static int GetEmpValueOptionIndex(EmpFunctionDefinition function, string value)
    {
        if (function == null || function.ValueOptions.Count == 0)
            return 0;
        for (var i = 0; i < function.ValueOptions.Count; i++)
        {
            if (string.Equals(function.ValueOptions[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }
    private static string GetEmpValueOptionDisplay(EmpFunctionDefinition function, int index)
    {
        if (function == null || function.ValueOptions.Count == 0)
            return "";
        if (index < 0 || index >= function.ValueOptions.Count)
            index = 0;
        return function.ValueOptions[index];
    }
    private static string GetEmpDefaultValue(EmpFunctionDefinition function)
    {
        if (function == null)
            return "";
        if (function.ValueParameters.Count > 0)
            return BuildEmpDefaultMultiValueState(function);
        if (!string.IsNullOrWhiteSpace(function.DefaultValue))
            return function.DefaultValue;
        if (function.ValueKind == EmpValueKind.Enum && function.ValueOptions.Count > 0)
            return function.ValueOptions[0];
        if (function.ValueKind == EmpValueKind.Bool)
            return function.DefaultEnabled ? "true" : "false";
        return "";
    }
    private static Dictionary<string, string> ReadEmpMultiValueState(EmpFunctionDefinition function, EmpFunctionState state)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (function != null)
        {
            for (var i = 0; i < function.ValueParameters.Count; i++)
            {
                var parameter = function.ValueParameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Key))
                    continue;
                result[parameter.Key] = parameter.DefaultValue ?? "";
            }
        }

        var text = state == null ? "" : state.Value ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return result;

        try
        {
            using (var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        result[prop.Name] = ReadEmpConfigScalar(prop.Value);
                    return result;
                }
            }
        }
        catch
        {
        }

        if (function != null && function.ValueParameters.Count == 1)
            result[function.ValueParameters[0].Key] = text;
        return result;
    }
    private static string BuildEmpMultiValueState(Dictionary<string, string> values)
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
    private static string BuildEmpDefaultMultiValueState(EmpFunctionDefinition function)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (function != null)
        {
            for (var i = 0; i < function.ValueParameters.Count; i++)
            {
                var parameter = function.ValueParameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Key))
                    continue;
                values[parameter.Key] = parameter.DefaultValue ?? "";
            }
        }
        return BuildEmpMultiValueState(values);
    }
    private static string GetEmpParameterValue(EmpFunctionDefinition function, EmpFunctionState state, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "";
        var values = ReadEmpMultiValueState(function, state);
        return values.TryGetValue(key, out var value) ? value ?? "" : "";
    }
    private static string BuildEmpFunctionSignature(EmpPluginDefinition plugin, EmpFunctionDefinition function, EmpFunctionState state)
    {
        var sb = new StringBuilder();
        sb.Append(SafeEmpText(plugin == null ? "" : plugin.Id, ""))
            .Append('|').Append(SafeEmpText(function == null ? "" : function.Id, ""))
            .Append('|').Append(function == null ? "" : GetEmpFunctionKindToken(function.Kind))
            .Append('|').Append(function == null ? "" : GetEmpValueKindToken(function.ValueKind))
            .Append('|').Append(state != null && state.Enabled ? "1" : "0")
            .Append('|').Append(state == null ? "" : state.Value ?? "");
        if (function != null)
        {
            sb.Append('|').Append(function.DefaultEnabled ? "1" : "0");
            sb.Append('|').Append(SafeEmpText(function.DefaultValue, ""));
            sb.Append('|').Append(function.SourcePath ?? "");
            sb.Append('|').Append(function.Error ?? "");
            for (var i = 0; i < function.ValueOptions.Count; i++)
                sb.Append('|').Append(function.ValueOptions[i] ?? "");
            for (var i = 0; i < function.ValueParameters.Count; i++)
            {
                var parameter = function.ValueParameters[i];
                if (parameter == null)
                    continue;
                sb.Append('|').Append(parameter.Key ?? "")
                    .Append('=').Append(parameter.DefaultValue ?? "")
                    .Append(':').Append(GetEmpValueKindToken(parameter.ValueKind));
            }
            AppendEmpOperationSignature(sb, function.Operations);
            AppendEmpOperationSignature(sb, function.OnEnableOperations);
            AppendEmpOperationSignature(sb, function.OnDisableOperations);
        }
        return sb.ToString();
    }
    private static void AppendEmpOperationSignature(StringBuilder sb, List<EmpOperationDefinition> operations)
    {
        if (operations == null)
            return;
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op == null)
            {
                sb.Append("|<null>");
                continue;
            }
            sb.Append('|').Append(SafeEmpText(op.Tool, ""));
            sb.Append('|').Append(SafeEmpText(op.Summary, ""));
            foreach (var pair in op.Arguments)
                sb.Append('|').Append(pair.Key ?? "").Append('=').Append(pair.Value ?? "");
        }
    }
    private string ApplyEmpFunctionStateNow(EmpPluginDefinition plugin, EmpFunctionDefinition function, EmpFunctionState state, bool fromUi)
    {
        if (plugin == null || function == null || state == null)
            return "failed: invalid emp state";
        if (!plugin.IsValid)
        {
            state.PendingApply = false;
            state.Initialized = true;
            state.LastApplySucceeded = false;
            return "failed: EMP security blocked plugin: " + plugin.Error;
        }
        if (!function.IsValid)
        {
            state.PendingApply = false;
            state.Initialized = true;
            state.LastApplySucceeded = false;
            return "failed: EMP security blocked function: " + function.Error;
        }

        var result = ExecuteEmpFunctionOperations(plugin, function, state);
        state.PendingApply = false;
        state.Initialized = true;
        state.LastApplySucceeded = result.IndexOf("failed:", StringComparison.OrdinalIgnoreCase) < 0;
        state.LastAppliedSignature = BuildEmpFunctionSignature(plugin, function, state);
        if (fromUi && !string.IsNullOrWhiteSpace(result))
            _pluginManagerLog = "Status: " + result;
        return result;
    }
    private string ExecuteEmpFunctionOperations(EmpPluginDefinition plugin, EmpFunctionDefinition function, EmpFunctionState state)
    {
        var operations = GetEmpOperationsForState(function, state);
        if (operations == null || operations.Count == 0)
            return "ok: " + SafeEmpText(plugin.Name, plugin.Id) + "." + SafeEmpText(function.Name, function.Id);

        var sb = new StringBuilder();
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op == null || string.IsNullOrWhiteSpace(op.Tool))
                continue;

            var args = BuildEmpOperationArgumentsJson(plugin, function, state, op);
            try
            {
                var securityScan = EmpSecurityScanner.ScanOperation(op, args);
                if (securityScan.Blocked)
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(op.Tool).Append(": failed: EMP security blocked: ").Append(securityScan.Reason);
                    continue;
                }

                var result = ExecuteAiToolCall(op.Tool, args, false, false);
                if (ShouldDisplayEmpOperationResult(result))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(op.Tool).Append(": ").Append(result);
                }
            }
            catch (Exception ex)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(op.Tool).Append(": failed: ").Append(ex.Message);
            }
        }

        var output = sb.ToString().TrimEnd();
        if (string.IsNullOrWhiteSpace(output))
            output = "ok: " + SafeEmpText(plugin.Name, plugin.Id) + "." + SafeEmpText(function.Name, function.Id);
        return output;
    }
    private static bool ShouldDisplayEmpOperationResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return false;
        if (result.IndexOf("already absent", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return true;
    }
    private static List<EmpOperationDefinition> GetEmpOperationsForState(EmpFunctionDefinition function, EmpFunctionState state)
    {
        if (function == null)
            return new List<EmpOperationDefinition>();
        if (function.Kind == EmpFunctionKind.Button)
            return function.Operations;
        if (function.Kind == EmpFunctionKind.Value)
            return function.Operations;
        if (state != null && state.Enabled)
            return function.OnEnableOperations.Count > 0 ? function.OnEnableOperations : function.Operations;
        return function.OnDisableOperations.Count > 0 ? function.OnDisableOperations : new List<EmpOperationDefinition>();
    }
    private static string BuildEmpOperationArgumentsJson(EmpPluginDefinition plugin, EmpFunctionDefinition function, EmpFunctionState state, EmpOperationDefinition operation)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;

        void Add(string key, string value)
        {
            if (!first)
                sb.Append(',');
            first = false;
            sb.Append('"').Append(EscapeJson(key)).Append("\":\"").Append(EscapeJson(value ?? "")).Append('"');
        }

        if (operation != null)
        {
            foreach (var pair in operation.Arguments)
            {
                Add(pair.Key, ExpandEmpOperationArgument(pair.Value, plugin, function, state));
            }
        }

        if (first)
        {
            sb.Append("\"value\":\"");
            sb.Append(EscapeJson(state == null ? "" : state.Value ?? ""));
            sb.Append('"');
        }

        sb.Append('}');
        return sb.ToString();
    }
    private static string ExpandEmpOperationArgument(string text, EmpPluginDefinition plugin, EmpFunctionDefinition function, EmpFunctionState state)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var result = text;
        result = Regex.Replace(result, "\\{\\{value:([^}]+)\\}\\}", match => GetEmpParameterValue(function, state, match.Groups[1].Value.Trim()));
        result = result.Replace("{{enabled}}", state != null && state.Enabled ? "true" : "false");
        result = result.Replace("{{value}}", state == null ? "" : state.Value ?? "");
        result = result.Replace("{{plugin_id}}", plugin == null ? "" : SafeEmpText(plugin.Id, ""));
        result = result.Replace("{{plugin_name}}", plugin == null ? "" : SafeEmpText(plugin.Name, ""));
        result = result.Replace("{{function_id}}", function == null ? "" : SafeEmpText(function.Id, ""));
        result = result.Replace("{{function_name}}", function == null ? "" : SafeEmpText(function.Name, ""));
        result = result.Replace("{{function_kind}}", function == null ? "" : GetEmpFunctionKindToken(function.Kind));
        result = result.Replace("{{value_kind}}", function == null ? "" : GetEmpValueKindToken(function.ValueKind));
        result = result.Replace("{{default_value}}", function == null ? "" : SafeEmpText(function.DefaultValue, ""));
        result = result.Replace("{{default_enabled}}", function != null && function.DefaultEnabled ? "true" : "false");
        result = result.Replace("{{source_path}}", function == null ? "" : SafeEmpText(function.SourcePath, ""));
        result = result.Replace("{{relative_path}}", plugin == null ? "" : SafeEmpText(plugin.RelativePath, ""));
        return result;
    }
    private void AppendEmpPluginConfigJson(StringBuilder sb)
    {
        sb.AppendLine("  \"empPlugins\": {");
        var entries = _empFunctionStates.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < entries.Count; i++)
        {
            var pair = entries[i];
            var state = pair.Value ?? new EmpFunctionState();
            sb.Append("    \"").Append(EscapeJson(pair.Key)).Append("\": {");
            sb.Append("\"enabled\": ").Append(state.Enabled ? "true" : "false").Append(", ");
            sb.Append("\"value\": \"").Append(EscapeJson(state.Value ?? "")).Append("\"");
            sb.Append("}");
            if (i < entries.Count - 1)
                sb.Append(",");
            sb.AppendLine();
        }
        sb.Append("  }");
    }
}
