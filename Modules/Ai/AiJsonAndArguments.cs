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
    private static string BuildAiRuntimeParameterSearchText(MethodBase method)
    {
        if (method == null)
            return "";
        var sb = new StringBuilder();
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(parameters[i].Name).Append(' ').Append(GetDebugTypeName(parameters[i].ParameterType));
        }
        return sb.ToString();
    }
    private static List<string> ParseAiRuntimeInvokeArguments(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;
        var raw = text.Trim();
        if (raw.StartsWith("[", StringComparison.Ordinal) && raw.EndsWith("]", StringComparison.Ordinal))
        {
            var matches = Regex.Matches(raw, "\"((?:\\\\.|[^\"])*)\"");
            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count > 1)
                    result.Add(UnescapeJsonString(match.Groups[1].Value));
            }
            if (result.Count > 0)
                return result;
        }

        var parts = raw.Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
            result.Add(parts[i].Trim());
        return result;
    }
    private bool TryResolveAiRuntimeInvokableMethod(Type ownerType, string methodName, List<string> values, out MethodInfo method, out object[] parsedArgs, out string error)
    {
        method = null;
        parsedArgs = Array.Empty<object>();
        error = "";
        if (ownerType == null)
        {
            error = "owner type is null";
            return false;
        }

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var candidates = new List<Tuple<MethodInfo, object[]>>();
        foreach (var candidate in ownerType.GetMethods(flags))
        {
            if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                !string.Equals(candidate.Name, methodName, StringComparison.OrdinalIgnoreCase))
                continue;
            var parameters = candidate.GetParameters();
            if (parameters.Length != values.Count)
                continue;
            var args = new object[parameters.Length];
            var ok = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].IsOut || parameters[i].ParameterType.IsByRef)
                {
                    ok = false;
                    break;
                }
                object parsed;
                if (!TryParseAiRuntimeInvokeValue(values[i], parameters[i].ParameterType, out parsed))
                {
                    ok = false;
                    break;
                }
                args[i] = parsed;
            }
            if (ok)
                candidates.Add(Tuple.Create(candidate, args));
        }

        if (candidates.Count == 0)
        {
            var sb = new StringBuilder();
            sb.Append("method not found or arguments could not be parsed: ").Append(methodName).Append("(").Append(values.Count.ToString(CultureInfo.InvariantCulture)).Append(" args) on ").Append(GetDebugTypeName(ownerType));
            var listed = 0;
            foreach (var candidate in ownerType.GetMethods(flags))
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                    !string.Equals(candidate.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (listed++ >= 8) break;
                sb.Append("\n").Append(FormatMethodForAiRuntime(candidate));
            }
            error = sb.ToString();
            return false;
        }
        if (candidates.Count > 1)
        {
            var sb = new StringBuilder();
            sb.Append("ambiguous invokable method: ").Append(methodName).Append(" candidates:");
            for (var i = 0; i < Math.Min(candidates.Count, 8); i++)
                sb.Append("\n").Append(FormatMethodForAiRuntime(candidates[i].Item1));
            error = sb.ToString();
            return false;
        }

        method = candidates[0].Item1;
        parsedArgs = candidates[0].Item2;
        return true;
    }
    private bool TryParseAiRuntimeInvokeValue(string text, Type type, out object value)
    {
        value = null;
        if (type == null)
            return false;
        var originalType = type;
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
            type = nullable;
        var key = NormalizeAiKey(text);
        if (key == "null")
        {
            if (!originalType.IsValueType || Nullable.GetUnderlyingType(originalType) != null)
            {
                value = null;
                return true;
            }
            return false;
        }
        if (!string.IsNullOrWhiteSpace(text) && text.Trim().StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
        {
            var expression = text.Trim().Substring(4).Trim();
            if (TryResolveAiRuntimeReferenceArgument(expression, originalType, out value))
                return true;
            return false;
        }
        if (TryParseDebugValue(text, type, out value))
            return true;
        if (type == typeof(object))
        {
            value = text;
            return true;
        }
        return false;
    }
    private bool TryResolveAiRuntimeReferenceArgument(string expression, Type expectedType, out object value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;
        if (!TryResolveAiRuntimeMemberTarget(expression, out var member, out var error))
            return false;
        value = member.GetValue();
        if (value == null)
            return !expectedType.IsValueType || Nullable.GetUnderlyingType(expectedType) != null;
        var targetType = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
        return targetType == typeof(object) || targetType.IsInstanceOfType(value);
    }
    private Hostility AiArgRelationship(string args, string name, Hostility fallback)
    {
        var value = NormalizeAiKey(AiArgString(args, name));
        switch (value)
        {
            case "enemy":
            case "敌对": return Hostility.Enemy;
            case "neutral":
            case "中立": return Hostility.Neutral;
            case "friend":
            case "friendly":
            case "友好": return Hostility.Friend;
            case "ally":
            case "盟友": return Hostility.Ally;
            default: return fallback;
        }
    }
    private string SetAiUiStyle(string value)
    {
        var key = NormalizeAiKey(value);
        for (var i = 0; i < UiStyleNamesZh.Length; i++)
        {
            if (NormalizeAiKey(UiStyleNamesZh[i]) == key ||
                NormalizeAiKey(UiStyleNamesEn[i]) == key ||
                NormalizeAiKey(UiStyleNamesRu[i]) == key ||
                i.ToString(CultureInfo.InvariantCulture) == value)
            {
                _uiStyleIndex = i;
                if (_uiTextColorFollowsStyle)
                    _uiTextColor = GetDefaultUiTextColor();
                return "ok: ui_style = " + CurrentUiStyleName();
            }
        }
        return "failed: unknown ui_style " + value;
    }
    private static void ApplyAiOptionalString(string args, string name, Action<string> setter)
    {
        if (AiHasArg(args, name))
            setter(AiArgString(args, name));
    }
    private static void ApplyAiOptionalInt(string args, string name, Action<int> setter)
    {
        if (AiHasArg(args, name))
            setter(AiArgInt(args, name, 0));
    }
    private static bool AiHasArg(string args, string name)
    {
        return HasJsonValue(args ?? "", name);
    }
    private static string AiArgString(string args, string name, string fallback = "")
    {
        return ExtractString(args ?? "", name, fallback);
    }
    private static int AiArgInt(string args, string name, int fallback)
    {
        return ExtractInt(args ?? "", name, fallback);
    }
    private static bool AiArgBool(string args, string name, bool fallback)
    {
        return ExtractBool(args ?? "", name, fallback);
    }
    private static bool AiParseBool(string text, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        var key = NormalizeAiKey(text);
        if (key == "true" || key == "1" || key == "on" || key == "yes" || key == "enable" || key == "enabled" || key == "开启" || key == "打开" || key == "是")
            return true;
        if (key == "false" || key == "0" || key == "off" || key == "no" || key == "disable" || key == "disabled" || key == "关闭" || key == "否")
            return false;
        return fallback;
    }
    private static float AiParseFloat(string text, float fallback)
    {
        float value;
        return float.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
    }
    private static string UnescapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch != '\\' || i + 1 >= value.Length)
            {
                sb.Append(ch);
                continue;
            }

            var next = value[++i];
            switch (next)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 < value.Length)
                    {
                        var hex = value.Substring(i + 1, 4);
                        int code;
                        if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            sb.Append("\\u").Append(hex);
                            i += 4;
                        }
                    }
                    else
                    {
                        sb.Append("\\u");
                    }
                    break;
                default:
                    sb.Append(next);
                    break;
            }
        }
        return sb.ToString();
    }
    private static string JsonStringProperty(string json, string name)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(name))
            return "";
        return ExtractString(json, name, "");
    }
    private static string ExtractJsonArrayProperty(string json, string name)
    {
        return ExtractJsonBracketProperty(json, name, '[', ']');
    }
    private static string ExtractJsonObjectProperty(string json, string name)
    {
        return ExtractJsonBracketProperty(json, name, '{', '}');
    }
    private static string ExtractJsonBracketProperty(string json, string name, char open, char close)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(name))
            return "";
        var marker = "\"" + name + "\"";
        var index = json.IndexOf(marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var colon = json.IndexOf(':', index + marker.Length);
            if (colon < 0)
                return "";
            var start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;
            if (start >= json.Length)
                return "";
            if (json[start] == open)
            {
                var end = FindJsonMatchingBracket(json, start, open, close);
                return end > start ? json.Substring(start, end - start + 1) : "";
            }
            index = json.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
        }
        return "";
    }
    private static int FindJsonMatchingBracket(string json, int start, char open, char close)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < json.Length; i++)
        {
            var ch = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (ch == '"')
                    inString = false;
                continue;
            }
            if (ch == '"')
            {
                inString = true;
                continue;
            }
            if (ch == open)
                depth++;
            else if (ch == close)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }
    private static IEnumerable<string> EnumerateTopLevelJsonObjects(string jsonArray)
    {
        if (string.IsNullOrEmpty(jsonArray))
            yield break;
        var index = 0;
        while (index < jsonArray.Length)
        {
            var start = jsonArray.IndexOf('{', index);
            if (start < 0)
                yield break;
            var end = FindJsonMatchingBracket(jsonArray, start, '{', '}');
            if (end < 0)
                yield break;
            yield return jsonArray.Substring(start, end - start + 1);
            index = end + 1;
        }
    }
    private static string TruncateForLog(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? "";
        return text.Substring(0, maxLength) + "...";
    }
}
