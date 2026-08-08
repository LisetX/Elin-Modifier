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

internal static class EmpPluginJsonLoader
{
    public static List<EmpPluginDefinition> Load(string rootDirectory, out string stamp)
    {
        stamp = BuildStamp(rootDirectory);
        var result = new List<EmpPluginDefinition>();
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return result;

        string[] files;
        try
        {
            files = Directory.GetFiles(rootDirectory, "*.json", SearchOption.AllDirectories);
        }
        catch
        {
            return result;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var def = TryParseDefinition(file, out var error);
            if (def == null)
            {
                result.Add(new EmpPluginDefinition
                {
                    Id = NormalizeId(Path.GetFileNameWithoutExtension(file)),
                    Name = Path.GetFileNameWithoutExtension(file),
                    SourcePath = file,
                    RelativePath = GetRelativePath(rootDirectory, file),
                    Error = error
                });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(error) && string.IsNullOrWhiteSpace(def.Error))
                def.Error = error;

            def.Id = MakeUniqueId(NormalizeId(def.Id), usedIds, Path.GetFileNameWithoutExtension(file));
            def.Name = string.IsNullOrWhiteSpace(def.Name) ? def.Id : def.Name;
            def.SourcePath = file;
            def.RelativePath = GetRelativePath(rootDirectory, file);
            result.Add(def);
            usedIds.Add(def.Id);
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static EmpPluginDefinition? TryParseDefinition(string path, out string error)
    {
        error = "";
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "empty file";
                return null;
            }

            using (var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }))
            {
                var def = ParseDefinition(doc.RootElement, path, out error);
                EmpSecurityScanner.ApplyDefinitionScan(def, text);
                return def;
            }
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + " - " + ex.Message;
            return null;
        }
    }

    private static EmpPluginDefinition ParseDefinition(JsonElement root, string path, out string error)
    {
        error = "";
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "root must be an object";
            return new EmpPluginDefinition();
        }

        var def = new EmpPluginDefinition
        {
            Id = ReadString(root, "id", ""),
            Name = ReadString(root, "name", ReadString(root, "title", "")),
            Description = ReadString(root, "description", ""),
            SourcePath = path
        };

        var functionsElement = ReadFirstArray(root, "functions", "entries", "items");
        if (functionsElement.HasValue)
        {
            var index = 0;
            foreach (var item in functionsElement.Value.EnumerateArray())
            {
                var function = ParseFunction(item, path, index, out var functionError);
                if (!string.IsNullOrEmpty(functionError))
                    function.Error = functionError;
                def.Functions.Add(function);
                index++;
            }
        }

        if (def.Functions.Count == 0)
            error = "no functions defined";
        return def;
    }

    private static EmpFunctionDefinition ParseFunction(JsonElement element, string path, int index, out string error)
    {
        error = "";
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "function entry must be an object";
            return new EmpFunctionDefinition();
        }

        var name = ReadString(element, "name", ReadString(element, "title", ""));
        var function = new EmpFunctionDefinition
        {
            Id = NormalizeId(ReadString(element, "id", string.IsNullOrWhiteSpace(name) ? "function_" + (index + 1).ToString(CultureInfo.InvariantCulture) : name)),
            Name = string.IsNullOrWhiteSpace(name) ? "Function " + (index + 1).ToString(CultureInfo.InvariantCulture) : name,
            Description = ReadString(element, "description", ""),
            SourcePath = path,
            DefaultEnabled = ReadBool(element, "enabled", ReadBool(element, "default_enabled", false)),
            DefaultValue = ReadString(element, "default_value", ReadString(element, "default", "")),
            Kind = ParseFunctionKind(ReadString(element, "kind", ReadString(element, "type", "toggle"))),
            ValueKind = ParseValueKind(ReadString(element, "value_kind", ReadString(element, "valueType", "string")))
        };

        if (element.TryGetProperty("options", out var optionsElement) && optionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in optionsElement.EnumerateArray())
            {
                var text = ReadScalarString(option);
                if (!string.IsNullOrWhiteSpace(text))
                    function.ValueOptions.Add(text);
            }
        }
        ParseValueParameters(function, element);

        if (element.TryGetProperty("operations", out var operationsElement) && operationsElement.ValueKind == JsonValueKind.Array)
            ParseOperationArray(function.Operations, operationsElement);
        if (element.TryGetProperty("on_enable", out var onEnableElement) && onEnableElement.ValueKind == JsonValueKind.Array)
            ParseOperationArray(function.OnEnableOperations, onEnableElement);
        if (element.TryGetProperty("on_disable", out var onDisableElement) && onDisableElement.ValueKind == JsonValueKind.Array)
            ParseOperationArray(function.OnDisableOperations, onDisableElement);

        var feature = ReadString(element, "feature", "");
        var reflectTarget = ReadString(element, "reflect_target", "");
        var reflectValue = ReadString(element, "reflect_value", "{{value}}");
        var patchTarget = ReadString(element, "patch_target", "");
        var patchMode = ReadString(element, "patch_mode", "skip_original");
        var patchId = ReadString(element, "patch_id", "");
        var patchMethod = ReadString(element, "patch_method", "");
        var patchCode = ReadString(element, "patch_code", ReadString(element, "code", ""));

        if (!string.IsNullOrWhiteSpace(feature) || !string.IsNullOrWhiteSpace(reflectTarget) || !string.IsNullOrWhiteSpace(patchTarget))
        {
            if (!string.IsNullOrWhiteSpace(feature))
            {
                AddFeatureToggleOperation(function.OnEnableOperations, feature, true);
                AddFeatureToggleOperation(function.OnDisableOperations, feature, false);
            }
            else if (!string.IsNullOrWhiteSpace(reflectTarget))
            {
                function.Operations.Add(MakeOperation("runtime_reflect_set", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "target", reflectTarget },
                    { "value", reflectValue }
                }));
            }
            else if (!string.IsNullOrWhiteSpace(patchTarget))
            {
                function.OnEnableOperations.Add(MakeOperation("runtime_harmony_patch", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "target", patchTarget },
                    { "mode", patchMode },
                    { "patch_id", string.IsNullOrWhiteSpace(patchId) ? function.Id : patchId },
                    { "patch_method", patchMethod },
                    { "code", patchCode }
                }));
                function.OnDisableOperations.Add(MakeOperation("runtime_harmony_unpatch", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "patch_id", string.IsNullOrWhiteSpace(patchId) ? function.Id : patchId }
                }));
            }
        }

        if (function.Kind == EmpFunctionKind.Toggle && function.OnEnableOperations.Count == 0 && function.OnDisableOperations.Count == 0 && function.Operations.Count == 0)
            error = "toggle function has no operations";
        if (function.Kind == EmpFunctionKind.Value && function.Operations.Count == 0)
            error = "value function has no operations";
        if (function.Kind == EmpFunctionKind.Patch && function.OnEnableOperations.Count == 0 && function.OnDisableOperations.Count == 0 && function.Operations.Count == 0)
            error = "patch function has no operations";

        return function;
    }

    private static void ParseValueParameters(EmpFunctionDefinition function, JsonElement element)
    {
        if (function == null || element.ValueKind != JsonValueKind.Object)
            return;

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("default_values", out var defaultsElement) && defaultsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in defaultsElement.EnumerateObject())
                defaults[prop.Name] = ReadScalarString(prop.Value);
        }

        JsonElement parametersElement;
        if (element.TryGetProperty("parameters", out parametersElement) ||
            element.TryGetProperty("params", out parametersElement) ||
            element.TryGetProperty("fields", out parametersElement))
        {
            if (parametersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in parametersElement.EnumerateArray())
                    AddValueParameter(function, item, defaults);
            }
            else if (parametersElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in parametersElement.EnumerateObject())
                    AddValueParameter(function, prop.Name, prop.Value, defaults);
            }
        }

        if (function.ValueParameters.Count == 0 && defaults.Count > 0)
        {
            foreach (var pair in defaults)
            {
                function.ValueParameters.Add(new EmpValueParameterDefinition
                {
                    Key = NormalizeId(pair.Key),
                    Label = pair.Key,
                    DefaultValue = pair.Value,
                    ValueKind = EmpValueKind.String
                });
            }
        }
    }

    private static void AddValueParameter(EmpFunctionDefinition function, JsonElement item, Dictionary<string, string> defaults)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            AddValueParameter(function, item.GetString() ?? "", default(JsonElement), defaults);
            return;
        }
        if (item.ValueKind == JsonValueKind.Object)
        {
            var key = ReadString(item, "key", ReadString(item, "id", ReadString(item, "name", "")));
            AddValueParameter(function, key, item, defaults);
        }
    }

    private static void AddValueParameter(EmpFunctionDefinition function, string key, JsonElement item, Dictionary<string, string> defaults)
    {
        if (function == null || string.IsNullOrWhiteSpace(key))
            return;
        var normalized = NormalizeId(key);
        if (string.IsNullOrWhiteSpace(normalized))
            return;
        for (var i = 0; i < function.ValueParameters.Count; i++)
        {
            if (string.Equals(function.ValueParameters[i].Key, normalized, StringComparison.OrdinalIgnoreCase))
                return;
        }

        var label = key;
        var defaultValue = defaults != null && defaults.TryGetValue(key, out var fromDefaults) ? fromDefaults : "";
        var valueKind = EmpValueKind.String;
        if (item.ValueKind == JsonValueKind.Object)
        {
            label = ReadString(item, "label", ReadString(item, "title", key));
            defaultValue = ReadString(item, "default_value", ReadString(item, "default", defaultValue));
            valueKind = ParseValueKind(ReadString(item, "value_kind", ReadString(item, "type", "string")));
        }

        function.ValueParameters.Add(new EmpValueParameterDefinition
        {
            Key = normalized,
            Label = string.IsNullOrWhiteSpace(label) ? normalized : label,
            DefaultValue = defaultValue ?? "",
            ValueKind = valueKind
        });
    }

    private static void AddFeatureToggleOperation(List<EmpOperationDefinition> operations, string feature, bool enabled)
    {
        operations.Add(MakeOperation("set_feature_enabled", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "feature", feature },
            { "enabled", enabled ? "true" : "false" }
        }));
    }

    private static EmpOperationDefinition MakeOperation(string tool, Dictionary<string, string> arguments)
    {
        var op = new EmpOperationDefinition { Tool = tool ?? "" };
        if (arguments != null)
        {
            foreach (var pair in arguments)
                op.Arguments[pair.Key] = pair.Value;
        }
        return op;
    }

    private static void ParseOperationArray(List<EmpOperationDefinition> result, JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var op = new EmpOperationDefinition
            {
                Tool = ReadString(item, "tool", ReadString(item, "name", "")),
                Summary = ReadString(item, "summary", ReadString(item, "description", ""))
            };

            if (item.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var arg in argsElement.EnumerateObject())
                    op.Arguments[arg.Name] = ReadScalarString(arg.Value);
            }
            else
            {
                foreach (var prop in item.EnumerateObject())
                {
                    var key = prop.Name;
                    if (string.Equals(key, "tool", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "summary", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
                        continue;
                    op.Arguments[key] = ReadScalarString(prop.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(op.Tool))
                result.Add(op);
        }
    }

    private static string ReadString(JsonElement element, string name, string fallback)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        return ReadScalarString(value, fallback);
    }

    private static bool ReadBool(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        switch (value.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.String:
                bool parsed;
                return bool.TryParse(value.GetString(), out parsed) ? parsed : fallback;
            case JsonValueKind.Number:
                return value.TryGetInt32(out var i) ? i != 0 : fallback;
            default:
                return fallback;
        }
    }

    private static JsonElement? ReadFirstArray(JsonElement element, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (element.TryGetProperty(names[i], out var value) && value.ValueKind == JsonValueKind.Array)
                return value;
        }
        return null;
    }

    private static string ReadScalarString(JsonElement value, string fallback = "")
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString() ?? fallback;
            case JsonValueKind.Number:
                return value.ToString();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return fallback;
            default:
                return value.ToString();
        }
    }

    private static EmpFunctionKind ParseFunctionKind(string text)
    {
        switch (NormalizeKey(text))
        {
            case "value":
            case "set":
            case "number":
            case "text":
                return EmpFunctionKind.Value;
            case "patch":
            case "harmony":
                return EmpFunctionKind.Patch;
            case "button":
            case "action":
            case "run":
                return EmpFunctionKind.Button;
            default:
                return EmpFunctionKind.Toggle;
        }
    }

    private static EmpValueKind ParseValueKind(string text)
    {
        switch (NormalizeKey(text))
        {
            case "int":
            case "integer":
                return EmpValueKind.Int;
            case "float":
            case "double":
                return EmpValueKind.Float;
            case "bool":
            case "boolean":
                return EmpValueKind.Bool;
            case "enum":
            case "choice":
                return EmpValueKind.Enum;
            default:
                return EmpValueKind.String;
        }
    }

    private static string NormalizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "";
        var sb = new StringBuilder(id.Length);
        for (var i = 0; i < id.Length; i++)
        {
            var ch = id[i];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }

    private static string NormalizeKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch == '_' || ch == '-' || ch == '.')
                sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }

    private static string MakeUniqueId(string id, HashSet<string> usedIds, string fallback)
    {
        var baseId = string.IsNullOrWhiteSpace(id) ? NormalizeId(fallback) : id;
        if (string.IsNullOrWhiteSpace(baseId))
            baseId = "emp_plugin";
        var candidate = baseId;
        var index = 2;
        while (usedIds.Contains(candidate))
        {
            candidate = baseId + "_" + index.ToString(CultureInfo.InvariantCulture);
            index++;
        }
        return candidate;
    }

    private static string BuildStamp(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return "";

        try
        {
            var files = Directory.GetFiles(rootDirectory, "*.json", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                try
                {
                    var info = new FileInfo(file);
                    sb.Append(file)
                        .Append('|')
                        .Append(info.Length.ToString(CultureInfo.InvariantCulture))
                        .Append('|')
                        .Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                        .Append('\n');
                }
                catch
                {
                    sb.Append(file).Append("|error\n");
                }
            }
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string GetRelativePath(string root, string file)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(file) &&
                file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
            }
        }
        catch
        {
        }
        return Path.GetFileName(file);
    }
}
