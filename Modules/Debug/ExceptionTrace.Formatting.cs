using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
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
using static ElinModifierPlugin;

internal sealed partial class ExceptionTraceModule
{
    private string ResolveDebugFrameOwner(DebugStackFrameInfo frame, string sourceName)
    {
        if (frame == null)
            return ResolveDebugLogOwnerFromSource(sourceName);
        var plugin = FindDebugPluginForAssembly(frame.ResolvedAssembly, sourceName);
        if (plugin != null)
            return "BepInEx Mod: " + GetDebugBepInExPluginDisplayName(plugin);
        var asmName = frame.ResolvedAssembly == null ? "" : frame.ResolvedAssembly.GetName().Name ?? "";
        if (string.Equals(asmName, "Elin", StringComparison.OrdinalIgnoreCase) ||
            asmName.StartsWith("Plugins.", StringComparison.OrdinalIgnoreCase))
            return "Game: " + asmName;
        if (asmName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase))
            return "Unity: " + asmName;
        if (asmName.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) ||
            asmName.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase) ||
            asmName.StartsWith("Harmony", StringComparison.OrdinalIgnoreCase))
            return "Framework: " + asmName;
        if (!string.IsNullOrEmpty(asmName))
            return "Assembly: " + asmName;
        return ResolveDebugLogOwnerFromSource(sourceName);
    }
    private string ResolveDebugLogOwnerFromSource(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName))
            return "<unknown>";
        var plugins = _host.GetOtherLoadedBepInExPluginsCached();
        foreach (var plugin in plugins)
        {
            if (plugin == null)
                continue;
            if (DebugPassesFilter(GetDebugBepInExPluginDisplayName(plugin), sourceName) ||
                DebugPassesFilter(GetDebugPluginGuid(plugin.Info), sourceName) ||
                DebugPassesFilter(GetDebugPluginName(plugin.Info), sourceName))
                return "BepInEx Mod: " + GetDebugBepInExPluginDisplayName(plugin);
        }
        if (sourceName.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Unity/Game log source";
        return "LogSource: " + sourceName;
    }
    private DebugBepInExPlugin FindDebugPluginForAssembly(Assembly assembly, string sourceName)
    {
        if (assembly == null)
            return null;
        var assemblyLocation = GetDebugAssemblyLocation(assembly);
        var plugins = _host.GetOtherLoadedBepInExPluginsCached();
        foreach (var plugin in plugins)
        {
            if (plugin == null)
                continue;
            var instance = plugin.Instance ?? _host.SafeDebugValue(() => plugin.Info?.Instance);
            if (instance != null && ReferenceEquals(instance.GetType().Assembly, assembly))
                return plugin;
            var location = GetDebugPluginLocation(plugin.Info);
            if (!string.IsNullOrEmpty(assemblyLocation) && !string.IsNullOrEmpty(location))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(assemblyLocation), Path.GetFullPath(location), StringComparison.OrdinalIgnoreCase))
                        return plugin;
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(sourceName) &&
                (DebugPassesFilter(GetDebugBepInExPluginDisplayName(plugin), sourceName) ||
                 DebugPassesFilter(GetDebugPluginGuid(plugin.Info), sourceName)))
                return plugin;
        }
        return null;
    }
    private static string FormatDebugStackFrame(DebugStackFrameInfo frame)
    {
        if (frame == null)
            return "<none>";
        var resolved = frame.ResolvedType == null ? frame.TypeName : GetDebugTypeName(frame.ResolvedType);
        return resolved + "." + frame.MethodName;
    }
    private static string FormatDebugResolvedMethod(MethodBase method)
    {
        if (method == null)
            return "<unresolved>";
        try
        {
            return (method.DeclaringType == null ? "" : GetDebugTypeName(method.DeclaringType) + ".") + method.Name + " " + method.ToString();
        }
        catch
        {
            return method.Name;
        }
    }
    private static string FormatDebugAssembly(Assembly assembly)
    {
        if (assembly == null)
            return "<unresolved>";
        var name = "";
        try { name = assembly.GetName().Name; } catch { }
        var location = GetDebugAssemblyLocation(assembly);
        return (string.IsNullOrEmpty(name) ? "<unknown>" : name) + (string.IsNullOrEmpty(location) ? "" : " | " + location);
    }
    private static string GetDebugAssemblyLocation(Assembly assembly)
    {
        if (assembly == null)
            return "";
        try { return assembly.Location ?? ""; }
        catch { return ""; }
    }
    private static string SafeDebugText(string text)
    {
        return string.IsNullOrEmpty(text) ? "<none>" : text;
    }
    private static string CompactDebugMultiline(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return "<none>";
        var compact = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (compact.Length <= maxChars)
            return compact;
        return compact.Substring(0, Math.Max(0, maxChars)) + "...";
    }
    internal static string DescribeDebugTraceValue(object value)
    {
        if (value == null)
            return "null";
        try
        {
            if (value is Chara c)
                return c.GetType().Name + "(" + SafeName(c) + ")";
            if (value is Card card)
                return card.GetType().Name + "(" + (card.id ?? "?") + ")";
            if (value is Zone zone)
                return zone.GetType().Name + "(" + (zone.Name ?? "?") + ")";
            if (value is ICollection collection)
                return value.GetType().Name + "(Count=" + collection.Count.ToString(CultureInfo.InvariantCulture) + ")";
            if (value is string s)
                return s;
            if (value is int || value is bool || value is float || value is double || value is long)
                return DebugValueToString(value);
            return value.GetType().FullName ?? value.GetType().Name;
        }
        catch { return "?"; }
    }
    private static string SafeTrace(Func<string> getter)
    {
        try { return getter(); }
        catch (Exception ex) { return "读取失败:" + ex.GetType().Name; }
    }
}
