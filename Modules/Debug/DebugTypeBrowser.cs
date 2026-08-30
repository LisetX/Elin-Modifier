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
    private List<DebugTypeEntry> GetDebugGameTypeEntries(params string[] keywords)
    {
        EnsureDebugGameTypeEntries();
        var cacheKey = string.Join("|", keywords);
        List<DebugTypeEntry> cached;
        if (_debugTypeCategoryCache.TryGetValue(cacheKey, out cached))
            return cached;

        var result = new List<DebugTypeEntry>();
        foreach (var entry in _debugGameTypeEntries)
        {
            var haystack = entry.SearchText;
            var matched = false;
            foreach (var keyword in keywords)
            {
                if (haystack.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched = true;
                    break;
                }
            }
            if (matched)
                result.Add(entry);
        }
        _debugTypeCategoryCache[cacheKey] = result;
        return result;
    }
    private List<DebugTypeEntry> GetDebugFilteredTypeEntries(IEnumerable<DebugTypeEntry> entries, string localFilter, string cacheKey)
    {
        var filterKey = (_debugFilter ?? "") + "\n" + (localFilter ?? "");
        DebugTypeFilterCache cached;
        if (!string.IsNullOrEmpty(cacheKey) &&
            _debugTypeFilterCache.TryGetValue(cacheKey, out cached) &&
            cached.FilterKey == filterKey)
            return cached.Entries;

        var result = new List<DebugTypeEntry>();
        foreach (var entry in entries)
        {
            if ((string.IsNullOrEmpty(_debugFilter) ||
                 DebugPassesFilter(entry.DisplayName) ||
                 DebugPassesFilter(entry.SearchText)) &&
                (string.IsNullOrEmpty(localFilter) ||
                 DebugPassesFilter(entry.DisplayName, localFilter) ||
                 DebugPassesFilter(entry.SearchText, localFilter)))
            {
                result.Add(entry);
            }
        }
        if (!string.IsNullOrEmpty(cacheKey))
            _debugTypeFilterCache[cacheKey] = new DebugTypeFilterCache(filterKey, result);
        return result;
    }
    private void EnsureDebugGameTypeEntries()
    {
        if (_debugGameTypeEntries != null)
            return;

        _debugGameTypeEntries = new List<DebugTypeEntry>();
        _debugTypeCategoryCache.Clear();
        _debugTypeFilterCache.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var assemblies = new HashSet<Assembly>
        {
            typeof(EClass).Assembly,
            typeof(RecipeManager).Assembly,
            typeof(LayerCraft).Assembly,
            typeof(DropdownGrid).Assembly
        };
        try
        {
            foreach (var plugin in GetOtherLoadedBepInExPluginsCached())
            {
                var instance = plugin?.Instance ?? SafeDebugValue(() => plugin?.Info?.Instance);
                if (instance != null)
                    assemblies.Add(instance.GetType().Assembly);
            }
        }
        catch { }
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                    continue;
                var name = assembly.GetName().Name ?? "";
                if (ShouldScanDebugAssembly(assembly))
                {
                    assemblies.Add(assembly);
                }
            }
        }
        catch { }
        foreach (var assembly in assemblies)
        {
            if (assembly == null)
                continue;
            if (assembly == typeof(ElinModifierPlugin).Assembly)
                continue;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            catch { continue; }
            if (types == null)
                continue;

            foreach (var type in types)
            {
                if (type == null || type.IsGenericTypeDefinition || type.FullName == null)
                    continue;
                var assemblyIdentity = "";
                try { assemblyIdentity = assembly.FullName ?? assembly.GetName().Name ?? ""; } catch { }
                if (!seen.Add(assemblyIdentity + "|" + type.FullName))
                    continue;

                var searchText = BuildDebugTypeSearchText(type);
                _debugGameTypeEntries.Add(new DebugTypeEntry(type, searchText, GetDebugSingletonValue(type), HasDebugStaticMembers(type), IsDebugInterestingGameType(type, searchText)));
            }
        }

        _debugGameTypeEntries.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
    }
    private static bool ShouldScanDebugAssembly(Assembly assembly)
    {
        if (assembly == null)
            return false;
        try
        {
            if (assembly.IsDynamic)
                return false;
        }
        catch { }

        var name = "";
        try { name = assembly.GetName().Name ?? ""; } catch { }
        if (string.IsNullOrEmpty(name))
            return false;

        if (name.IndexOf("Elin", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Plugins.", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
            return true;

        var excludedPrefixes = new[]
        {
            "System", "Microsoft", "Unity", "UnityEngine", "Unity.", "BepInEx", "Harmony", "0Harmony",
            "Mono.", "MonoMod", "mscorlib", "netstandard", "Newtonsoft", "SemanticVersioning", "YamlDotNet",
            "DOTween", "Demigiant"
        };
        foreach (var prefix in excludedPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

        var location = "";
        try { location = assembly.Location ?? ""; } catch { }
        if (location.IndexOf("\\BepInEx\\core\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
            location.IndexOf("\\Elin_Data\\Managed\\Unity", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        return true;
    }
    private List<MethodInfo> GetDebugMethods(Type type)
    {
        var cacheKey = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        List<MethodInfo> cached;
        if (_debugMethodCache.TryGetValue(cacheKey, out cached))
            return cached;

        var methods = new List<MethodInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t != null; t = t.BaseType)
        {
            MethodInfo[] declared;
            try { declared = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
            catch { continue; }
            foreach (var method in declared)
            {
                if (method == null || method.IsSpecialName)
                    continue;
                var key = method.Name + ":" + method.ToString();
                if (!seen.Add(key))
                    continue;
                methods.Add(method);
            }
        }
        methods.Sort((a, b) =>
        {
            var c = string.Compare(a.IsStatic ? "0" : "1", b.IsStatic ? "0" : "1", StringComparison.Ordinal);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        _debugMethodCache[cacheKey] = methods;
        return methods;
    }
    private string GetDebugMethodSignature(MethodInfo method)
    {
        string cached;
        if (method != null && _debugMethodSignatureCache.TryGetValue(method, out cached))
            return cached;

        string signature;
        try
        {
            var sb = new StringBuilder();
            sb.Append(method.IsStatic ? "static " : "inst ");
            sb.Append(GetDebugTypeName(method.ReturnType)).Append(' ');
            sb.Append(method.DeclaringType != null ? method.DeclaringType.Name : "").Append('.');
            sb.Append(method.Name).Append('(');
            var parameters = method.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(GetDebugTypeName(parameters[i].ParameterType)).Append(' ').Append(parameters[i].Name);
            }
            sb.Append(')');
            signature = sb.ToString();
        }
        catch
        {
            signature = method == null ? "" : method.Name;
        }
        if (method != null)
            _debugMethodSignatureCache[method] = signature;
        return signature;
    }
    private static bool IsDebugInterestingGameType(Type type, string searchText)
    {
        if (type == null || string.IsNullOrEmpty(searchText))
            return false;
        var keywords = new[]
        {
            "drop", "loot", "treasure", "reward", "spawn", "map", "zone", "gen", "biome", "terrain", "cell", "fov", "light", "fog", "encounter",
            "recipe", "craft", "ingredient", "material", "factory", "stock", "source", "row", "thing", "chara", "card", "npc", "enemy", "monster",
            "layer", "manager", "controller", "system", "player", "faction", "home", "branch", "party", "guild", "config", "setting", "option",
            "combat", "battle", "attack", "damage", "ai", "act", "action", "effect", "condition", "buff", "time", "date", "weather", "simulate"
        };
        foreach (var keyword in keywords)
            if (searchText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
    private static string BuildDebugTypeSearchText(Type type)
    {
        var sb = new StringBuilder();
        try { sb.Append(type.FullName).Append(' '); } catch { }
        try
        {
            var assembly = type.Assembly;
            sb.Append(assembly.GetName().Name).Append(' ')
              .Append(assembly.FullName).Append(' ')
              .Append(assembly.Location).Append(' ');
        }
        catch { }
        try
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                sb.Append(field.Name).Append(' ').Append(field.FieldType.Name).Append(' ');
        }
        catch { }
        try
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                sb.Append(prop.Name).Append(' ').Append(prop.PropertyType.Name).Append(' ');
        }
        catch { }
        try
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                sb.Append(method.Name).Append(' ');
        }
        catch { }
        return sb.ToString();
    }
    private static bool HasDebugStaticMembers(Type type)
    {
        try
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Length > 0 ||
                   type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Length > 0;
        }
        catch { return false; }
    }
    internal static object GetDebugSingletonValue(Type type)
    {
        try
        {
            var prop = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (prop != null && prop.GetIndexParameters().Length == 0 && type.IsAssignableFrom(prop.PropertyType))
                return prop.GetValue(null, null);
        }
        catch { }
        try
        {
            var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null && type.IsAssignableFrom(field.FieldType))
                return field.GetValue(null);
        }
        catch { }
        try
        {
            var prop = type.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (prop != null && prop.GetIndexParameters().Length == 0 && type.IsAssignableFrom(prop.PropertyType))
                return prop.GetValue(null, null);
        }
        catch { }
        try
        {
            var field = type.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null && type.IsAssignableFrom(field.FieldType))
                return field.GetValue(null);
        }
        catch { }
        return null;
    }
    private bool IsDebugExpanded(string key)
    {
        bool expanded;
        return _debugExpanded.TryGetValue(key, out expanded) && expanded;
    }
    internal bool DebugPassesFilter(string text)
    {
        return DebugPassesFilter(text, _debugFilter);
    }
    internal static bool DebugPassesFilter(string text, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;
        return text != null && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    private bool DebugSectionMightContainFilter(object target)
    {
        if (string.IsNullOrEmpty(_debugFilter))
            return true;
        if (target == null)
            return false;
        var type = target as Type ?? target.GetType();
        try
        {
            foreach (var member in GetDebugMembers(type, target is Type))
                if (DebugPassesFilter(member.Name))
                    return true;
        }
        catch { }
        return false;
    }
    internal List<DebugMember> GetDebugMembers(Type type, bool staticOnly)
    {
        var cacheKey = (type.AssemblyQualifiedName ?? type.FullName ?? type.Name) + "|" + (staticOnly ? "S" : "I");
        List<DebugMember> cached;
        if (_debugMemberCache.TryGetValue(cacheKey, out cached))
            return cached;

        var members = new List<DebugMember>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t != null; t = t.BaseType)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | (staticOnly ? BindingFlags.Static : BindingFlags.Instance);
            foreach (var field in t.GetFields(flags))
            {
                if (field.IsSpecialName)
                    continue;
                var key = "F:" + field.Name;
                if (!seen.Add(key))
                    continue;
                members.Add(new DebugMember(field));
            }
            foreach (var prop in t.GetProperties(flags))
            {
                if (prop.GetIndexParameters().Length != 0)
                    continue;
                var key = "P:" + prop.Name;
                if (!seen.Add(key))
                    continue;
                members.Add(new DebugMember(prop));
            }
        }
        members.Sort((a, b) =>
        {
            var c = string.Compare(a.Kind, b.Kind, StringComparison.Ordinal);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        _debugMemberCache[cacheKey] = members;
        return members;
    }
}
