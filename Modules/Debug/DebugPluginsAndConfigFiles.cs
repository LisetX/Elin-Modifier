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
    private DebugRawConfigEntry[] GetDebugFilteredRawConfigEntries(DebugRawConfigEntry[] entries, string globalFilter, string localFilter)
    {
        if (entries == null || entries.Length == 0)
            return Array.Empty<DebugRawConfigEntry>();
        if (string.IsNullOrEmpty(globalFilter) && string.IsNullOrEmpty(localFilter))
            return entries;
        var cacheKey = BuildDebugRawConfigEntryFilterCacheKey(entries, globalFilter, localFilter);
        DebugRawConfigEntryFilterCache cached;
        if (_debugRawConfigEntryFilterCache.TryGetValue(cacheKey, out cached))
            return cached.Entries;

        var result = new List<DebugRawConfigEntry>();
        foreach (var entry in entries)
        {
            if (entry == null)
                continue;
            if (DebugRawConfigEntryPassesFilter(entry, globalFilter) &&
                DebugRawConfigEntryPassesFilter(entry, localFilter))
                result.Add(entry);
        }
        var filtered = result.ToArray();
        _debugRawConfigEntryFilterCache[cacheKey] = new DebugRawConfigEntryFilterCache(filtered);
        return filtered;
    }
    private string[] GetDebugFilteredConfigFiles(string[] files, string globalFilter, string localFilter)
    {
        if (files == null || files.Length == 0)
            return Array.Empty<string>();
        if (string.IsNullOrEmpty(globalFilter) && string.IsNullOrEmpty(localFilter))
            return files;
        var cacheKey = BuildDebugConfigFileFilterCacheKey(files, globalFilter, localFilter);
        DebugStringArrayFilterCache cached;
        if (_debugConfigFileFilterCache.TryGetValue(cacheKey, out cached))
            return cached.Values;

        var result = new List<string>();
        foreach (var file in files)
        {
            if (DebugConfigFilePassesFilter(file, globalFilter) &&
                DebugConfigFilePassesFilter(file, localFilter))
                result.Add(file);
        }
        var filtered = result.ToArray();
        _debugConfigFileFilterCache[cacheKey] = new DebugStringArrayFilterCache(filtered);
        return filtered;
    }
    private bool DebugRawConfigEntryPassesFilter(DebugRawConfigEntry entry, string filter)
    {
        if (entry == null)
            return false;
        if (string.IsNullOrEmpty(filter))
            return true;
        return DebugPassesFilter(entry.Section, filter) ||
               DebugPassesFilter(entry.Key, filter) ||
               DebugPassesFilter(entry.Value, filter) ||
               DebugPassesFilter(entry.Path, filter);
    }
    private bool DebugConfigFilePassesFilter(string file, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;
        if (DebugPassesFilter(file, filter) || DebugPassesFilter(Path.GetFileName(file), filter))
            return true;
        return DebugPassesFilter(GetDebugRawConfigSearchText(file), filter);
    }
    private string BuildDebugConfigFileFilterCacheKey(string[] files, string globalFilter, string localFilter)
    {
        var sb = new StringBuilder();
        sb.Append(globalFilter ?? "").Append('\n').Append(localFilter ?? "").Append('\n');
        if (files != null)
        {
            sb.Append(files.Length.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < files.Length; i++)
                sb.Append('|').Append(files[i] ?? "");
        }
        return sb.ToString();
    }
    private static string BuildDebugRawConfigEntryFilterCacheKey(DebugRawConfigEntry[] entries, string globalFilter, string localFilter)
    {
        var path = entries != null && entries.Length > 0 && entries[0] != null ? entries[0].Path : "";
        return path + "\n" + (globalFilter ?? "") + "\n" + (localFilter ?? "") + "\n" + (entries == null ? "0" : entries.Length.ToString(CultureInfo.InvariantCulture));
    }
    private static string GetDebugBepInExConfigPath()
    {
        try { return Paths.ConfigPath ?? ""; }
        catch { }
        try { return Path.Combine(Paths.BepInExRootPath, "config"); }
        catch { return ""; }
    }
    private string[] GetDebugConfigFilesCached(string configPath)
    {
        var frame = Time.frameCount;
        if (string.Equals(_debugCachedConfigFilesPath, configPath, StringComparison.OrdinalIgnoreCase) &&
            _debugCachedConfigFiles != null &&
            frame - _debugCachedConfigFilesFrame < 120)
            return _debugCachedConfigFiles;

        _debugCachedConfigFilesPath = configPath ?? "";
        _debugCachedConfigFilesFrame = frame;
        _debugCachedConfigFiles = GetDebugConfigFiles(configPath);
        _debugConfigFileFilterCache.Clear();
        return _debugCachedConfigFiles;
    }
    private static string[] GetDebugConfigFiles(string configPath)
    {
        try
        {
            var files = Directory.GetFiles(configPath, "*.cfg");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
    private List<DebugBepInExPlugin> GetDebugFilteredBepInExPlugins(List<DebugBepInExPlugin> plugins, string localFilter)
    {
        var result = new List<DebugBepInExPlugin>();
        if (plugins == null)
            return result;
        var cacheKey = BuildDebugBepInExPluginFilterCacheKey(plugins, _debugFilter, localFilter);
        DebugBepInExPluginFilterCache cached;
        if (_debugBepInExPluginFilterCache.TryGetValue(cacheKey, out cached))
            return cached.Plugins;

        foreach (var plugin in plugins)
        {
            if (DebugBepInExPluginPassesFilter(plugin, _debugFilter) &&
                DebugBepInExPluginPassesFilter(plugin, localFilter))
                result.Add(plugin);
        }
        _debugBepInExPluginFilterCache[cacheKey] = new DebugBepInExPluginFilterCache(result);
        return result;
    }
    private static string BuildDebugBepInExPluginFilterCacheKey(List<DebugBepInExPlugin> plugins, string globalFilter, string localFilter)
    {
        var sb = new StringBuilder();
        sb.Append(globalFilter ?? "").Append('\n').Append(localFilter ?? "").Append('\n');
        if (plugins != null)
        {
            sb.Append(plugins.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var plugin in plugins)
                sb.Append('|').Append(GetDebugPluginGuid(plugin == null ? null : plugin.Info)).Append(':').Append(plugin == null ? "" : plugin.Source);
        }
        return sb.ToString();
    }
    private bool DebugBepInExPluginPassesFilter(DebugBepInExPlugin plugin, string filter)
    {
        if (plugin == null)
            return false;
        if (string.IsNullOrEmpty(filter))
            return true;
        var info = plugin.Info;
        var instance = plugin.Instance ?? SafeDebugValue(() => info?.Instance);
        var instanceType = instance == null ? null : instance.GetType();
        return DebugPassesFilter(GetDebugBepInExPluginDisplayName(plugin), filter) ||
               DebugPassesFilter(GetDebugPluginGuid(info), filter) ||
               DebugPassesFilter(GetDebugPluginName(info), filter) ||
               DebugPassesFilter(GetDebugPluginVersion(info), filter) ||
               DebugPassesFilter(GetDebugPluginTypeName(info), filter) ||
               DebugPassesFilter(GetDebugPluginLocation(info), filter) ||
               DebugPassesFilter(plugin.Source, filter) ||
               DebugPassesFilter(instanceType == null ? "" : instanceType.FullName ?? instanceType.Name, filter);
    }
    internal List<DebugBepInExPlugin> GetOtherLoadedBepInExPluginsCached()
    {
        var frame = Time.frameCount;
        if (_debugCachedBepInExPlugins != null && frame - _debugCachedBepInExPluginsFrame < 120)
            return _debugCachedBepInExPlugins;

        _debugCachedBepInExPluginsFrame = frame;
        _debugCachedBepInExPlugins = GetOtherLoadedBepInExPlugins();
        _debugBepInExPluginFilterCache.Clear();
        return _debugCachedBepInExPlugins;
    }
    private List<DebugBepInExPlugin> GetOtherLoadedBepInExPlugins()
    {
        var result = new List<DebugBepInExPlugin>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var plugins = UnityChainloader.Instance?.Plugins;
            if (plugins != null)
            {
                foreach (var pair in plugins)
                {
                    var info = pair.Value;
                    AddDebugBepInExPlugin(result, seen, info, SafeDebugValue(() => info?.Instance), "UnityChainloader");
                }
            }
        }
        catch { }
        try
        {
            var pluginObjects = GetDebugMemberValue(typeof(ModManager), "ListPluginObject") as IEnumerable;
            if (pluginObjects != null)
            {
                foreach (var obj in pluginObjects)
                {
                    var info = GetDebugPluginInfoFromInstance(obj);
                    AddDebugBepInExPlugin(result, seen, info, obj, "ModManager.ListPluginObject");
                }
            }
        }
        catch { }
        result.Sort((a, b) => string.Compare(GetDebugBepInExPluginDisplayName(a), GetDebugBepInExPluginDisplayName(b), StringComparison.OrdinalIgnoreCase));
        return result;
    }
    private void AddDebugBepInExPlugin(List<DebugBepInExPlugin> result, HashSet<string> seen, PluginInfo info, object instance, string source)
    {
        if (info == null && instance == null)
            return;

        var guid = GetDebugPluginGuid(info);
        if (string.Equals(guid, "local.elin.modifier", StringComparison.OrdinalIgnoreCase))
            return;

        var instanceType = instance == null ? null : instance.GetType();
        var typeName = GetDebugPluginTypeName(info);
        var uniqueKey = !string.IsNullOrEmpty(guid)
            ? "guid:" + guid
            : "type:" + (instanceType == null ? typeName : instanceType.AssemblyQualifiedName ?? instanceType.FullName ?? instanceType.Name);
        if (string.IsNullOrEmpty(uniqueKey) || !seen.Add(uniqueKey))
            return;

        result.Add(new DebugBepInExPlugin(info, instance, source));
    }
    private static PluginInfo GetDebugPluginInfoFromInstance(object instance)
    {
        if (instance == null)
            return null;
        try
        {
            if (instance is BaseUnityPlugin plugin)
                return plugin.Info;
        }
        catch { }
        try
        {
            var prop = instance.GetType().GetProperty("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop == null ? null : prop.GetValue(instance, null) as PluginInfo;
        }
        catch { return null; }
    }
    internal static string GetDebugPluginGuid(PluginInfo info)
    {
        try { return info?.Metadata?.GUID ?? ""; }
        catch { return ""; }
    }
    internal static string GetDebugPluginName(PluginInfo info)
    {
        try { return info?.Metadata?.Name ?? ""; }
        catch { return ""; }
    }
    private static string GetDebugPluginVersion(PluginInfo info)
    {
        try
        {
            var metadata = info?.Metadata;
            if (metadata == null)
                return "";
            var prop = metadata.GetType().GetProperty("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var value = prop == null ? null : prop.GetValue(metadata, null);
            return value == null ? "" : value.ToString();
        }
        catch { return ""; }
    }
    private static string GetDebugPluginTypeName(PluginInfo info)
    {
        try { return info?.TypeName ?? ""; }
        catch { return ""; }
    }
    internal static string GetDebugPluginLocation(PluginInfo info)
    {
        try { return info?.Location ?? ""; }
        catch { return ""; }
    }
    internal static string GetDebugBepInExPluginDisplayName(DebugBepInExPlugin plugin)
    {
        var name = GetDebugPluginDisplayName(plugin.Info);
        if (!string.Equals(name, "<unknown plugin>", StringComparison.Ordinal))
            return name;
        var instance = plugin.Instance;
        if (instance == null)
            return name;
        var type = instance.GetType();
        return type.FullName ?? type.Name;
    }
    private static string GetDebugPluginDisplayName(PluginInfo info)
    {
        var name = GetDebugPluginName(info);
        if (!string.IsNullOrEmpty(name))
            return name;
        var guid = GetDebugPluginGuid(info);
        if (!string.IsNullOrEmpty(guid))
            return guid;
        var typeName = GetDebugPluginTypeName(info);
        return string.IsNullOrEmpty(typeName) ? "<unknown plugin>" : typeName;
    }
    internal static object GetDebugMemberValue(object owner, string name)
    {
        if (owner == null || string.IsNullOrEmpty(name))
            return null;
        var type = owner as Type ?? owner.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        try
        {
            var field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(field.IsStatic ? null : owner);
        }
        catch { }
        try
        {
            var prop = type.GetProperty(name, flags);
            if (prop != null && prop.GetIndexParameters().Length == 0)
            {
                var getter = prop.GetGetMethod(true);
                if (getter != null)
                    return prop.GetValue(getter.IsStatic ? null : owner, null);
            }
        }
        catch { }
        return null;
    }
}
