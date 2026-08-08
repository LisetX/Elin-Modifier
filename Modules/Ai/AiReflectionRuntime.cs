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
    private bool TryResolveAiRuntimeTypeReadExpression(string text, string assemblyName, out string description, out object value, out Type valueType, out string error)
    {
        description = "";
        value = null;
        valueType = null;
        error = "";
        var parts = SplitAiRuntimePath(text);
        if (parts.Count == 0)
        {
            error = string.IsNullOrEmpty(assemblyName) ? "Type expression must end with .member" : "Assembly expression must end with .member";
            return false;
        }

        for (var count = parts.Count; count >= 1; count--)
        {
            var typeName = string.Join(".", parts.GetRange(0, count).ToArray());
            var type = FindAiRuntimeType(typeName, assemblyName);
            if (type == null)
                continue;
            var owner = (object)type;
            var ownerType = type;
            description = (string.IsNullOrEmpty(assemblyName) ? "Type:" : "Assembly:" + assemblyName + ":") + typeName;
            if (count == parts.Count)
            {
                value = owner;
                valueType = ownerType;
                return true;
            }
            return TryResolveAiRuntimeReadPathAfterOwner(parts, count, ref owner, ref ownerType, ref description, out value, out valueType, out error);
        }

        error = string.IsNullOrEmpty(assemblyName) ? "type not found in expression: " + text : "type not found in assembly " + assemblyName + ": " + text;
        return false;
    }
    private bool TryResolveAiRuntimeReadPathAfterOwner(List<string> parts, int startIndex, ref object owner, ref Type ownerType, ref string description, out object value, out Type valueType, out string error)
    {
        value = null;
        valueType = null;
        error = "";
        for (var i = startIndex; i < parts.Count; i++)
        {
            var token = parts[i];
            if (!TryGetAiRuntimeTokenValue(owner, ownerType, token, out owner, out ownerType, out error))
                return false;
            description += "." + token;
        }
        value = owner;
        valueType = owner == null ? ownerType : owner.GetType();
        return true;
    }
    private bool TryGetAiRuntimeTokenValue(object owner, Type ownerType, string token, out object value, out Type valueType, out string error)
    {
        value = null;
        valueType = null;
        error = "";
        if (string.IsNullOrWhiteSpace(token))
        {
            error = "empty path token";
            return false;
        }
        var memberName = ExtractAiRuntimeIndexedMemberName(token);
        var indexes = ExtractAiRuntimeIndexers(token);
        if (!string.IsNullOrEmpty(memberName))
        {
            if (!TryGetAiRuntimeMemberValue(owner, ownerType, memberName, out value, out valueType, out error))
                return false;
        }
        else
        {
            value = owner;
            valueType = owner == null ? ownerType : owner.GetType();
        }

        for (var i = 0; i < indexes.Count; i++)
        {
            if (!TryGetAiRuntimeIndexedValue(value, valueType, indexes[i], out value, out valueType, out error))
                return false;
        }
        return true;
    }
    private static string ExtractAiRuntimeIndexedMemberName(string token)
    {
        token = (token ?? "").Trim();
        var bracket = token.IndexOf('[');
        return (bracket < 0 ? token : token.Substring(0, bracket)).Trim();
    }
    private static List<string> ExtractAiRuntimeIndexers(string token)
    {
        var result = new List<string>();
        token = token ?? "";
        var i = 0;
        while (i < token.Length)
        {
            var open = token.IndexOf('[', i);
            if (open < 0)
                break;
            var close = FindAiRuntimeMatchingBracket(token, open);
            if (close < 0)
            {
                result.Add(token.Substring(open + 1));
                break;
            }
            result.Add(token.Substring(open + 1, close - open - 1).Trim());
            i = close + 1;
        }
        return result;
    }
    private static int FindAiRuntimeMatchingBracket(string text, int open)
    {
        var quote = '\0';
        var escape = false;
        for (var i = open + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'')
            {
                quote = c;
                continue;
            }
            if (c == ']')
                return i;
        }
        return -1;
    }
    private bool TryGetAiRuntimeIndexedValue(object owner, Type ownerType, string indexText, out object value, out Type valueType, out string error)
    {
        value = null;
        valueType = null;
        error = "";
        if (owner == null)
        {
            error = "cannot index null value";
            return false;
        }
        indexText = UnquoteAiRuntimeIndexText(indexText);
        bool genericDictionaryHandled;
        if (TryGetAiRuntimeGenericDictionaryIndexedValue(owner, indexText, out genericDictionaryHandled, out value, out valueType, out error))
            return true;
        if (genericDictionaryHandled)
            return false;
        if (owner is IDictionary dictionary)
        {
            object key;
            if (!TryConvertAiRuntimeIndexKey(indexText, GetAiRuntimeDictionaryKeyType(owner.GetType()), out key))
            {
                error = "cannot parse dictionary key '" + indexText + "'";
                return false;
            }
            if (!dictionary.Contains(key))
            {
                error = "dictionary key not found: " + DebugValueToString(key);
                return false;
            }
            value = dictionary[key];
            valueType = value == null ? GetAiRuntimeDictionaryValueType(owner.GetType()) : value.GetType();
            return true;
        }
        if (owner is Array array)
        {
            int index;
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                error = "array index must be integer: " + indexText;
                return false;
            }
            if (index < 0 || index >= array.Length)
            {
                error = "array index out of range: " + index.ToString(CultureInfo.InvariantCulture) + "/" + array.Length.ToString(CultureInfo.InvariantCulture);
                return false;
            }
            value = array.GetValue(index);
            valueType = value == null ? owner.GetType().GetElementType() : value.GetType();
            return true;
        }
        if (owner is IList list)
        {
            int index;
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                error = "list index must be integer: " + indexText;
                return false;
            }
            if (index < 0 || index >= list.Count)
            {
                error = "list index out of range: " + index.ToString(CultureInfo.InvariantCulture) + "/" + list.Count.ToString(CultureInfo.InvariantCulture);
                return false;
            }
            value = list[index];
            valueType = value == null ? GetAiRuntimeListElementType(owner.GetType()) : value.GetType();
            return true;
        }

        var defaultMember = owner.GetType().GetDefaultMembers();
        foreach (var member in defaultMember)
        {
            var property = member as PropertyInfo;
            if (property == null)
                continue;
            var indexParams = property.GetIndexParameters();
            if (indexParams.Length != 1)
                continue;
            object parsed;
            if (!TryConvertAiRuntimeIndexKey(indexText, indexParams[0].ParameterType, out parsed))
                continue;
            value = property.GetValue(owner, new[] { parsed });
            valueType = value == null ? property.PropertyType : value.GetType();
            return true;
        }

        error = "value is not indexable: " + GetDebugTypeName(owner.GetType());
        return false;
    }
    private bool TryGetAiRuntimeGenericDictionaryIndexedValue(object owner, string indexText, out bool handled, out object value, out Type valueType, out string error)
    {
        handled = false;
        value = null;
        valueType = null;
        error = "";
        if (owner == null)
            return false;
        var type = owner.GetType();
        var dictInterface = FindAiRuntimeGenericInterface(type, typeof(IDictionary<,>)) ??
                            FindAiRuntimeGenericInterface(type, typeof(IReadOnlyDictionary<,>));
        if (dictInterface == null)
            return false;
        handled = true;
        var args = dictInterface.GetGenericArguments();
        var keyType = args.Length > 0 ? args[0] : typeof(object);
        var resultType = args.Length > 1 ? args[1] : typeof(object);
        object key;
        if (!TryConvertAiRuntimeIndexKey(indexText, keyType, out key))
        {
            error = "cannot parse dictionary key '" + indexText + "' as " + GetDebugTypeName(keyType);
            return false;
        }

        try
        {
            var contains = dictInterface.GetMethod("ContainsKey");
            if (contains != null)
            {
                var exists = contains.Invoke(owner, new[] { key });
                if (exists is bool b && !b)
                {
                    error = "dictionary key not found: " + DebugValueToString(key);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            error = "dictionary ContainsKey failed: " + ex.Message;
            return false;
        }

        try
        {
            var item = dictInterface.GetProperty("Item");
            if (item == null)
            {
                error = "dictionary indexer not found on " + GetDebugTypeName(type);
                return false;
            }
            value = item.GetValue(owner, new[] { key });
            valueType = value == null ? resultType : value.GetType();
            return true;
        }
        catch (Exception ex)
        {
            error = "dictionary index read failed: " + ex.Message;
            return false;
        }
    }
    private static bool TryConvertAiRuntimeIndexKey(string text, Type targetType, out object key)
    {
        key = null;
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType ?? typeof(string);
        if (targetType == typeof(object))
        {
            int intValue;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                key = intValue;
                return true;
            }
            key = text;
            return true;
        }
        if (targetType == typeof(string))
        {
            key = text;
            return true;
        }
        return TryParseDebugValue(text, targetType, out key);
    }
    private static string UnquoteAiRuntimeIndexText(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length >= 2 && ((text[0] == '"' && text[text.Length - 1] == '"') || (text[0] == '\'' && text[text.Length - 1] == '\'')))
            return UnescapeAiRuntimeQuotedText(text.Substring(1, text.Length - 2));
        return text;
    }
    private static string UnescapeAiRuntimeQuotedText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder();
        var escape = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (escape)
            {
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    default: sb.Append(c); break;
                }
                escape = false;
            }
            else if (c == '\\')
                escape = true;
            else
                sb.Append(c);
        }
        if (escape)
            sb.Append('\\');
        return sb.ToString();
    }
    private static Type GetAiRuntimeDictionaryKeyType(Type type)
    {
        var args = FindAiRuntimeGenericInterfaceArguments(type, typeof(IDictionary<,>));
        return args == null || args.Length < 1 ? typeof(object) : args[0];
    }
    private static Type GetAiRuntimeDictionaryValueType(Type type)
    {
        var args = FindAiRuntimeGenericInterfaceArguments(type, typeof(IDictionary<,>));
        return args == null || args.Length < 2 ? typeof(object) : args[1];
    }
    private static Type GetAiRuntimeListElementType(Type type)
    {
        if (type == null)
            return typeof(object);
        if (type.IsArray)
            return type.GetElementType() ?? typeof(object);
        var args = FindAiRuntimeGenericInterfaceArguments(type, typeof(IList<>)) ??
                   FindAiRuntimeGenericInterfaceArguments(type, typeof(ICollection<>)) ??
                   FindAiRuntimeGenericInterfaceArguments(type, typeof(IEnumerable<>));
        return args == null || args.Length < 1 ? typeof(object) : args[0];
    }
    private static Type[] FindAiRuntimeGenericInterfaceArguments(Type type, Type genericDefinition)
    {
        var iface = FindAiRuntimeGenericInterface(type, genericDefinition);
        return iface == null ? null : iface.GetGenericArguments();
    }
    private static Type FindAiRuntimeGenericInterface(Type type, Type genericDefinition)
    {
        if (type == null || genericDefinition == null)
            return null;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition)
            return type;
        var interfaces = type.GetInterfaces();
        for (var i = 0; i < interfaces.Length; i++)
        {
            var iface = interfaces[i];
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == genericDefinition)
                return iface;
        }
        return null;
    }
    private DebugBepInExPlugin FindAiRuntimePlugin(string pluginKey)
    {
        var key = NormalizeAiKey(pluginKey);
        var plugins = GetOtherLoadedBepInExPluginsCached();
        foreach (var plugin in plugins)
        {
            var info = plugin.Info;
            if (NormalizeAiKey(GetDebugPluginGuid(info)) == key ||
                NormalizeAiKey(GetDebugPluginName(info)) == key ||
                NormalizeAiKey(GetDebugBepInExPluginDisplayName(plugin)) == key)
                return plugin;
        }
        return null;
    }
    private static Type FindAiRuntimeType(string typeName, string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var name = assembly.GetName().Name ?? "";
                if (!string.IsNullOrWhiteSpace(assemblyName) &&
                    name.IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (assembly.FullName ?? "").IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var type = assembly.GetType(typeName, false, false) ?? assembly.GetType(typeName, false, true);
                if (type != null)
                    return type;
                foreach (var candidate in assembly.GetTypes())
                {
                    if (string.Equals(candidate.FullName, typeName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            catch { }
        }
        return null;
    }
    private static List<string> SplitAiRuntimePath(string path)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(path))
            return result;
        var sb = new StringBuilder();
        var bracketDepth = 0;
        var quote = '\0';
        var escape = false;
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (escape)
            {
                sb.Append(c);
                escape = false;
                continue;
            }
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'')
            {
                quote = c;
                sb.Append(c);
                continue;
            }
            if (c == '[')
            {
                bracketDepth++;
                sb.Append(c);
                continue;
            }
            if (c == ']')
            {
                if (bracketDepth > 0)
                    bracketDepth--;
                sb.Append(c);
                continue;
            }
            if (c == '.' && bracketDepth == 0)
            {
                var part = sb.ToString().Trim();
                if (part.Length > 0)
                    result.Add(part);
                sb.Length = 0;
                continue;
            }
            sb.Append(c);
        }
        var last = sb.ToString().Trim();
        if (last.Length > 0)
            result.Add(last);
        return result;
    }
    private static int FindAiRuntimeFirstTopLevelDot(string text)
    {
        if (string.IsNullOrEmpty(text))
            return -1;
        var bracketDepth = 0;
        var quote = '\0';
        var escape = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'')
            {
                quote = c;
                continue;
            }
            if (c == '[')
            {
                bracketDepth++;
                continue;
            }
            if (c == ']')
            {
                if (bracketDepth > 0)
                    bracketDepth--;
                continue;
            }
            if (c == '.' && bracketDepth == 0)
                return i;
        }
        return -1;
    }
    private static int FindAiRuntimeLastPathDot(string text)
    {
        return string.IsNullOrEmpty(text) ? -1 : text.LastIndexOf('.');
    }
    private static bool IsStaticPropertyForAiRuntime(PropertyInfo property)
    {
        var getter = property.GetGetMethod(true);
        if (getter != null)
            return getter.IsStatic;
        var setter = property.GetSetMethod(true);
        return setter != null && setter.IsStatic;
    }
    private static string GetAiRuntimeMethodKey(MethodBase method)
    {
        if (method == null)
            return "";
        return (method.DeclaringType == null ? "" : method.DeclaringType.AssemblyQualifiedName) + "::" + method.MetadataToken.ToString(CultureInfo.InvariantCulture);
    }
    private static string FormatMethodForAiRuntime(MethodBase method)
    {
        if (method == null)
            return "<null>";
        var sb = new StringBuilder();
        sb.Append(method.DeclaringType == null ? "<no type>" : method.DeclaringType.FullName).Append(".").Append(method.Name).Append("(");
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(GetDebugTypeName(parameters[i].ParameterType));
        }
        sb.Append(")");
        return sb.ToString();
    }
    private static string[] SplitAiRuntimeSearchKeywords(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();
        var expanded = ExpandAiRuntimeSearchQuery(query);
        var raw = expanded.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '|', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < raw.Length; i++)
        {
            var key = NormalizeAiKey(raw[i]);
            if (!string.IsNullOrEmpty(key) && seen.Add(key))
                list.Add(key);
        }
        return list.ToArray();
    }
    private static string ExpandAiRuntimeSearchQuery(string query)
    {
        var text = query ?? "";
        var key = NormalizeAiKey(text);
        var sb = new StringBuilder(text);
        if (key.Contains("时间") || key.Contains("世界时间") || key.Contains("不再推进") || key.Contains("冻结时间") || key.Contains("停止时间"))
            sb.Append(" time date hour minute advance simulate tick update GameDate VirtualDate World date AdvanceHour AdvanceMin SimulateHour");
        if (key.Contains("世界"))
            sb.Append(" world World EClass");
        if (key.Contains("推进") || key.Contains("停止") || key.Contains("冻结"))
            sb.Append(" advance simulate update tick stop pause freeze lock");
        if (key.Contains("背包") || key.Contains("物品"))
            sb.Append(" item thing inventory inv owner card");
        if (key.Contains("制作"))
            sb.Append(" craft recipe ingredient material");
        return sb.ToString();
    }
    private static bool AiRuntimeAssemblyMatches(Assembly assembly, string filter)
    {
        if (assembly == null)
            return false;
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        var key = NormalizeAiKey(filter);
        var name = NormalizeAiKey(assembly.GetName().Name ?? "");
        var full = NormalizeAiKey(assembly.FullName ?? "");
        var location = "";
        try { location = NormalizeAiKey(assembly.Location ?? ""); }
        catch { }
        return name.Contains(key) || full.Contains(key) || location.Contains(key);
    }
    private static bool AiRuntimeTextMatchesAll(string text, string[] keywords)
    {
        if (keywords == null || keywords.Length == 0)
            return true;
        var haystack = NormalizeAiKey(text);
        for (var i = 0; i < keywords.Length; i++)
        {
            if (haystack.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }
        return true;
    }
    private static bool AiRuntimeTextMatchesAny(string text, string[] keywords)
    {
        if (keywords == null || keywords.Length == 0)
            return true;
        var haystack = NormalizeAiKey(text);
        for (var i = 0; i < keywords.Length; i++)
            if (haystack.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
    private static int ScoreAiRuntimeSearchEntry(AiRuntimeSearchEntry entry, string[] keywords)
    {
        if (entry == null || keywords == null || keywords.Length == 0)
            return 0;
        var score = 0;
        for (var i = 0; i < keywords.Length; i++)
        {
            var keyword = keywords[i];
            if (string.IsNullOrEmpty(keyword))
                continue;
            if (entry.SearchText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                score += 10;
            if (NormalizeAiKey(entry.Description).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                score += 6;
            if (NormalizeAiKey(entry.Target).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                score += 4;
            if (NormalizeAiKey(entry.TypeName).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                score += 3;
        }
        if (entry.Kind == "method")
        {
            var desc = NormalizeAiKey(entry.Description);
            if (desc.Contains("advance") || desc.Contains("simulate") || desc.Contains("update") || desc.Contains("tick"))
                score += 8;
            if (desc.Contains("gamedate") || desc.Contains("virtualdate") || desc.Contains("date"))
                score += 6;
        }
        return score;
    }
    private static bool AiRuntimeSearchKindMatches(string entryKind, string requestedKind)
    {
        requestedKind = NormalizeAiKey(requestedKind);
        if (requestedKind == "" || requestedKind == "all")
            return true;
        if (requestedKind == "types")
            return entryKind == "type";
        if (requestedKind == "methods")
            return entryKind == "method";
        if (requestedKind == "members")
            return entryKind == "field" || entryKind == "property";
        return entryKind == requestedKind;
    }
    private static int AiRuntimeKindPriority(string kind)
    {
        if (kind == "method") return 0;
        if (kind == "property") return 1;
        if (kind == "field") return 2;
        return 3;
    }
    private static string BuildAiRuntimeSearchText(string text)
    {
        return NormalizeAiKey((text ?? "")
            .Replace("GameDate", "GameDate game date time hour minute advance")
            .Replace("VirtualDate", "VirtualDate virtual date time hour simulate")
            .Replace("AdvanceHour", "AdvanceHour advance hour time date")
            .Replace("AdvanceMin", "AdvanceMin advance minute time date")
            .Replace("SimulateHour", "SimulateHour simulate hour time date"));
    }
    private static string BuildAiRuntimeSearchFallbackHints(string[] keywords)
    {
        var joined = string.Join(" ", keywords ?? Array.Empty<string>());
        if (joined.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0 ||
            joined.IndexOf("date", StringComparison.OrdinalIgnoreCase) >= 0 ||
            joined.IndexOf("时间", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "hint: try runtime_search query=\"GameDate Advance\" kind=\"methods\" assembly_filter=\"Elin\", runtime_search query=\"VirtualDate Simulate\" kind=\"methods\" assembly_filter=\"Elin\", or runtime_list_type type=\"GameDate\" member_filter=\"Advance\".";
        }
        return "";
    }
    private static bool AiRuntimeMemberLineMatches(string line, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        return AiRuntimeTextMatchesAll(line, SplitAiRuntimeSearchKeywords(filter));
    }
    private static void AppendAiRuntimeSearchLine(StringBuilder sb, ref int count, int limit, string kind, string assemblyName, string description, string target)
    {
        if (count >= limit)
            return;
        count++;
        sb.Append(count.ToString(CultureInfo.InvariantCulture))
            .Append(". [").Append(kind).Append("] ")
            .Append(description)
            .Append(" | assembly=").Append(assemblyName)
            .Append(" | target=").Append(target)
            .AppendLine();
    }
}
