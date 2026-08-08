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
    private ConfigEntryBase[] GetDebugConfigEntries(ConfigFile config)
    {
        var result = new List<ConfigEntryBase>();
        var seen = new HashSet<ConfigEntryBase>();

        AddDebugConfigEntries(result, seen, SafeGetConfigValues(config));
        AddDebugConfigEntries(result, seen, SafeGetConfigDictionaryValues(config, "Entries"));
        AddDebugConfigEntries(result, seen, SafeInvokeConfigEntryArray(config, "GetConfigEntries"));
        return result.ToArray();
    }
    private static IEnumerable<ConfigEntryBase> SafeGetConfigValues(ConfigFile config)
    {
        try
        {
            if (config.Values == null)
                return Array.Empty<ConfigEntryBase>();
            return new List<ConfigEntryBase>(config.Values);
        }
        catch
        {
            return Array.Empty<ConfigEntryBase>();
        }
    }
    private static IEnumerable<ConfigEntryBase> SafeGetConfigDictionaryValues(ConfigFile config, string propertyName)
    {
        try
        {
            var property = typeof(ConfigFile).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var dictionary = property == null ? null : property.GetValue(config, null) as IDictionary;
            if (dictionary == null || dictionary.Count == 0)
                return Array.Empty<ConfigEntryBase>();

            var values = new List<ConfigEntryBase>();
            foreach (DictionaryEntry entry in dictionary)
            {
                var configEntry = entry.Value as ConfigEntryBase;
                if (configEntry != null)
                    values.Add(configEntry);
            }
            return values;
        }
        catch
        {
            return Array.Empty<ConfigEntryBase>();
        }
    }
    private static IEnumerable<ConfigEntryBase> SafeInvokeConfigEntryArray(ConfigFile config, string methodName)
    {
        try
        {
            var method = typeof(ConfigFile).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var entries = method == null ? null : method.Invoke(config, null) as IEnumerable<ConfigEntryBase>;
            if (entries == null)
                return Array.Empty<ConfigEntryBase>();
            return new List<ConfigEntryBase>(entries);
        }
        catch
        {
            return Array.Empty<ConfigEntryBase>();
        }
    }
    private static void AddDebugConfigEntries(List<ConfigEntryBase> result, HashSet<ConfigEntryBase> seen, IEnumerable<ConfigEntryBase> entries)
    {
        if (entries == null)
            return;
        foreach (var entry in entries)
        {
            if (entry == null || seen.Contains(entry))
                continue;
            seen.Add(entry);
            result.Add(entry);
        }
    }
    private DebugRawConfigEntry[] GetDebugRawConfigEntries(ConfigFile config)
    {
        return GetDebugRawConfigEntries(ResolveDebugConfigPath(GetDebugConfigPath(config)));
    }
    private DebugRawConfigEntry[] GetDebugRawConfigEntries(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return Array.Empty<DebugRawConfigEntry>();

        DateTime lastWriteTime;
        try { lastWriteTime = File.GetLastWriteTimeUtc(path); }
        catch { lastWriteTime = DateTime.MinValue; }

        DebugRawConfigCache cache;
        if (_debugRawConfigCache.TryGetValue(path, out cache) && cache.LastWriteTimeUtc == lastWriteTime)
            return cache.Entries;

        var entries = ParseDebugRawConfigFile(path);
        _debugRawConfigCache[path] = new DebugRawConfigCache(lastWriteTime, entries, BuildDebugRawConfigSearchText(path, entries));
        return entries;
    }
    private string GetDebugRawConfigSearchText(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "";

        DateTime lastWriteTime;
        try { lastWriteTime = File.GetLastWriteTimeUtc(path); }
        catch { lastWriteTime = DateTime.MinValue; }

        DebugRawConfigCache cache;
        if (_debugRawConfigCache.TryGetValue(path, out cache) && cache.LastWriteTimeUtc == lastWriteTime)
            return cache.SearchText;

        var entries = ParseDebugRawConfigFile(path);
        cache = new DebugRawConfigCache(lastWriteTime, entries, BuildDebugRawConfigSearchText(path, entries));
        _debugRawConfigCache[path] = cache;
        return cache.SearchText;
    }
    private static string BuildDebugRawConfigSearchText(string path, DebugRawConfigEntry[] entries)
    {
        var sb = new StringBuilder();
        sb.Append(path ?? "").Append(' ');
        try { sb.Append(Path.GetFileName(path)).Append(' '); } catch { }
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;
                sb.Append(entry.Section).Append(' ')
                  .Append(entry.Key).Append(' ')
                  .Append(entry.Value).Append(' ');
            }
        }
        return sb.ToString();
    }
}
