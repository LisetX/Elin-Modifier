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
    private static string GetDebugConfigPath(ConfigFile config)
    {
        try { return config == null ? "" : config.ConfigFilePath ?? ""; }
        catch { return ""; }
    }
    private static string ResolveDebugConfigPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        try
        {
            if (File.Exists(path))
                return path;

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return path;

            var expectedName = NormalizeDebugConfigFileName(Path.GetFileNameWithoutExtension(path));
            if (string.IsNullOrEmpty(expectedName))
                return path;

            var bestPath = "";
            var bestScore = 0;
            foreach (var candidate in Directory.GetFiles(directory, "*.cfg"))
            {
                var candidateName = NormalizeDebugConfigFileName(Path.GetFileNameWithoutExtension(candidate));
                var score = GetDebugConfigFileMatchScore(expectedName, candidateName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPath = candidate;
                }
            }
            return bestScore >= 70 && !string.IsNullOrEmpty(bestPath) ? bestPath : path;
        }
        catch
        {
            return path;
        }
    }
    private static int GetDebugConfigFileMatchScore(string expectedName, string candidateName)
    {
        if (string.IsNullOrEmpty(expectedName) || string.IsNullOrEmpty(candidateName))
            return 0;
        if (string.Equals(expectedName, candidateName, StringComparison.Ordinal))
            return 100;
        if (expectedName.EndsWith(candidateName, StringComparison.Ordinal) && candidateName.Length >= 4)
            return 90 + Math.Min(candidateName.Length, 9);
        if (candidateName.EndsWith(expectedName, StringComparison.Ordinal) && expectedName.Length >= 4)
            return 80 + Math.Min(expectedName.Length, 9);
        if (expectedName.Contains(candidateName) && candidateName.Length >= 5)
            return 70 + Math.Min(candidateName.Length, 9);
        if (candidateName.Contains(expectedName) && expectedName.Length >= 5)
            return 65 + Math.Min(expectedName.Length, 9);
        return 0;
    }
    private static string NormalizeDebugConfigFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }
    private static DebugRawConfigEntry[] ParseDebugRawConfigFile(string path)
    {
        try
        {
            var result = new List<DebugRawConfigEntry>();
            var section = "";
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i];
                var line = rawLine == null ? "" : rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal) && line.Length > 2)
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                var configKey = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1).Trim();
                result.Add(new DebugRawConfigEntry(path, i, section, configKey, value));
            }
            return result.ToArray();
        }
        catch
        {
            return Array.Empty<DebugRawConfigEntry>();
        }
    }
    private void ApplyDebugRawConfigValue(string key, DebugRawConfigEntry entry)
    {
        try
        {
            string text;
            if (!_debugInputs.TryGetValue(key, out text))
                return;
            if (!WriteDebugRawConfigValue(entry, text))
            {
                _debugLog = "Raw config apply failed: " + key;
                return;
            }
            _debugBindings[key] = new DebugBinding(entry);
            if (!string.IsNullOrEmpty(entry.Path))
                _debugRawConfigCache.Remove(entry.Path);
            _debugConfigFileFilterCache.Clear();
            _debugRawConfigEntryFilterCache.Clear();
            _debugLog = "Applied raw config: " + entry.Section + "." + entry.Key + " = " + text;
        }
        catch (Exception ex)
        {
            _debugLog = "Raw config apply failed: " + key + " / " + ex.Message;
        }
    }
    internal static bool WriteDebugRawConfigValue(DebugRawConfigEntry entry, string value)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Path))
            return false;
        try
        {
            var lines = File.Exists(entry.Path) ? File.ReadAllLines(entry.Path) : Array.Empty<string>();
            var lineIndex = FindDebugRawConfigLine(lines, entry.Section, entry.Key, entry.LineIndex);
            var replacement = entry.Key + " = " + (value ?? "");
            if (lineIndex >= 0)
            {
                var line = lines[lineIndex] ?? "";
                if (line.Trim() == replacement)
                    return true;
                lines[lineIndex] = replacement;
                File.WriteAllLines(entry.Path, lines);
                return true;
            }

            var list = new List<string>(lines);
            if (!string.IsNullOrEmpty(entry.Section))
            {
                if (list.Count > 0 && !string.IsNullOrWhiteSpace(list[list.Count - 1]))
                    list.Add("");
                list.Add("[" + entry.Section + "]");
            }
            list.Add(replacement);
            File.WriteAllLines(entry.Path, list.ToArray());
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static int FindDebugRawConfigLine(string[] lines, string section, string key, int preferredLineIndex)
    {
        if (lines == null || string.IsNullOrEmpty(key))
            return -1;
        if (preferredLineIndex >= 0 && preferredLineIndex < lines.Length && IsDebugRawConfigLineKey(lines[preferredLineIndex], section, key))
            return preferredLineIndex;
        var currentSection = "";
        for (var i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i] ?? "";
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal) && line.Length > 2)
            {
                currentSection = line.Substring(1, line.Length - 2).Trim();
                continue;
            }
            if (!string.Equals(currentSection, section ?? "", StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsDebugRawConfigLineKey(line, section, key))
                return i;
        }
        return -1;
    }
    private static bool IsDebugRawConfigLineKey(string rawLine, string section, string key)
    {
        var line = rawLine == null ? "" : rawLine.Trim();
        var separator = line.IndexOf('=');
        if (separator <= 0)
            return false;
        var currentKey = line.Substring(0, separator).Trim();
        return string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase);
    }
    private void ApplyDebugConfigEntryValue(string key, ConfigEntryBase entry, Type valueType)
    {
        try
        {
            string text;
            if (!_debugInputs.TryGetValue(key, out text))
                return;
            object parsed;
            if (!TryParseDebugValue(text, valueType, out parsed))
            {
                _debugLog = "Parse failed: " + key;
                return;
            }
            entry.BoxedValue = parsed;
            _debugBindings[key] = new DebugBinding(entry, valueType);
            _debugLog = "Applied config: " + key + " = " + text;
        }
        catch (Exception ex)
        {
            _debugLog = "Config apply failed: " + key + " / " + ex.Message;
        }
    }
    private void ApplyDebugValue(string key, DebugBinding binding, Type valueType)
    {
        try
        {
            string text;
            if (!_debugInputs.TryGetValue(key, out text))
                return;
            object parsed;
            if (!TryParseDebugValue(text, valueType, out parsed))
            {
                _debugLog = "Parse failed: " + key;
                return;
            }
            binding.SetValue(parsed);
            _debugBindings[key] = binding;
            _debugLog = "Applied: " + key + " = " + text;
        }
        catch (Exception ex)
        {
            _debugLog = "Apply failed: " + key + " / " + ex.Message;
        }
    }
    private void ApplyDebugLocks()
    {
        if (!IsDebugModeActive() || _debugLocks.Count == 0)
            return;
        foreach (var pair in _debugLocks)
        {
            if (!pair.Value)
                continue;
            DebugBinding binding;
            if (!_debugBindings.TryGetValue(pair.Key, out binding))
                continue;
            string text;
            if (!_debugInputs.TryGetValue(pair.Key, out text))
                continue;
            try
            {
                var type = binding.ValueType;
                object parsed;
                if (!TryParseDebugValue(text, type, out parsed))
                    continue;
                binding.SetValue(parsed);
            }
            catch { }
        }
    }
}
