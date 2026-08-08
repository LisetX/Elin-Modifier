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
using static ElinModifierPlugin;

internal sealed partial class ExceptionTraceModule
{
    private static Type ResolveDebugType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;
        typeName = typeName.Replace('/', '+');
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < assemblies.Length; i++)
        {
            Type type = null;
            try { type = assemblies[i].GetType(typeName, false); }
            catch { }
            if (type != null)
                return type;
        }

        var shortName = typeName;
        var dot = shortName.LastIndexOf('.');
        if (dot >= 0 && dot < shortName.Length - 1)
            shortName = shortName.Substring(dot + 1);
        for (var i = 0; i < assemblies.Length; i++)
        {
            foreach (var type in GetDebugAssemblyTypes(assemblies[i]))
            {
                if (type == null)
                    continue;
                if (string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                    string.Equals(type.Name, shortName, StringComparison.Ordinal))
                    return type;
            }
        }
        return null;
    }
    private static MethodBase ResolveDebugMethod(Type type, string methodName)
    {
        if (type == null || string.IsNullOrEmpty(methodName))
            return null;
        var normalized = methodName;
        var generic = normalized.IndexOf('<');
        if (generic > 0)
            normalized = normalized.Substring(0, generic);
        try
        {
            if (normalized == ".ctor" || normalized == "ctor" || normalized == "#ctor")
                return type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault();
        }
        catch { }

        for (var t = type; t != null; t = t.BaseType)
        {
            MethodInfo[] methods;
            try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
            catch { continue; }
            foreach (var method in methods)
            {
                if (method == null)
                    continue;
                if (string.Equals(method.Name, normalized, StringComparison.Ordinal) ||
                    string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    return method;
            }
        }
        return null;
    }
    private static IEnumerable<Type> GetDebugAssemblyTypes(Assembly assembly)
    {
        if (assembly == null)
            yield break;
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }
        catch { yield break; }
        if (types == null)
            yield break;
        for (var i = 0; i < types.Length; i++)
            if (types[i] != null)
                yield return types[i];
    }
    private void AppendDebugStackSummary(StringBuilder sb, List<DebugStackFrameInfo> frames)
    {
        sb.AppendLine("Resolved stack");
        if (frames == null || frames.Count == 0)
        {
            sb.AppendLine("[WARN] Stack trace is empty.");
            return;
        }

        var count = Math.Min(frames.Count, 14);
        for (var i = 0; i < count; i++)
        {
            var frame = frames[i];
            sb.Append('#').Append(i.ToString(CultureInfo.InvariantCulture)).Append(' ');
            sb.Append(FormatDebugStackFrame(frame));
            sb.Append(" | ").Append(ResolveDebugFrameOwner(frame, ""));
            if (!string.IsNullOrEmpty(frame.Location))
                sb.Append(" | ").Append(frame.Location);
            sb.AppendLine();
        }
        if (frames.Count > count)
            sb.AppendLine("... " + (frames.Count - count).ToString(CultureInfo.InvariantCulture) + " more frame(s)");
    }
    private void AppendDebugSnapshot(StringBuilder sb)
    {
        sb.AppendLine("Runtime snapshot");
        AppendDebugProbeLine(sb, "EClass.pc", _host.SafeDebugValue(() => GameAccess.Characters.PlayerCharacter));
        AppendDebugProbeLine(sb, "EClass.player", _host.SafeDebugValue(() => GameAccess.Runtime.Player));
        AppendDebugProbeLine(sb, "EClass.world", _host.SafeDebugValue(() => GameAccess.World.CurrentWorld));
        AppendDebugProbeLine(sb, "EClass.scene", _host.SafeDebugValue(() => GameAccess.Ui.Scene));
        AppendDebugProbeLine(sb, "EClass._zone", _host.SafeDebugValue(() => GameAccess.World.CurrentZone));
        AppendDebugProbeLine(sb, "EClass._map", _host.SafeDebugValue(() => GameAccess.World.CurrentMap));
        AppendDebugProbeLine(sb, "Map.charas.Count", _host.SafeDebugValue(() => GameAccess.World.CurrentCharacters == null ? null : (object)GameAccess.World.CurrentCharacters.Count));
        AppendDebugProbeLine(sb, "Map.things.Count", _host.SafeDebugValue(() => GameAccess.World.CurrentThings == null ? null : (object)GameAccess.World.CurrentThings.Count));
        AppendDebugProbeLine(sb, "Map.charas.nullItems", _host.SafeDebugValue(() => CountDebugNullItems(GameAccess.World.CurrentCharacters)));
        AppendDebugProbeLine(sb, "Map.things.nullItems", _host.SafeDebugValue(() => CountDebugNullItems(GameAccess.World.CurrentThings)));
        AppendDebugProbeLine(sb, "Current BranchOrHomeBranch", _host.SafeDebugValue(() => GameAccess.World.BranchOrHomeBranch));
        AppendDebugProbeLine(sb, "Talking NPC", _host.SafeDebugValue(() => GetTalkingNpc()));
    }
    private void AppendDebugFocusedProbe(StringBuilder sb, DebugStackFrameInfo root, List<DebugStackFrameInfo> frames, string sourceName, int frame)
    {
        sb.AppendLine("Focused probe");
        if (root == null)
        {
            sb.AppendLine("[INFO] No resolved root frame. Source owner: " + ResolveDebugLogOwnerFromSource(sourceName));
            return;
        }

        var type = root.ResolvedType;
        if (type == null)
        {
            sb.AppendLine("[WARN] Could not resolve type from stack path: " + root.TypeName);
            return;
        }

        AppendDebugProbeLine(sb, "Root type", GetDebugTypeName(type));
        AppendDebugProbeLine(sb, "Root assembly", FormatDebugAssembly(type.Assembly));
        AppendDebugProbeLine(sb, "Root owner", ResolveDebugFrameOwner(root, sourceName));

        AppendDebugNullMemberProbe(sb, "Root static null fields/properties", type, null, true, 28);
        var singleton = _host.SafeDebugValue(() => GetDebugSingletonValue(type));
        if (singleton != null)
            AppendDebugNullMemberProbe(sb, "Root singleton null fields/properties", singleton.GetType(), singleton, false, 28);

        AppendDebugRecentSubmoduleEvents(sb, frames, frame);
        AppendDebugKnownPathProbe(sb, frames, frame);
    }
    private void AppendDebugRecentSubmoduleEvents(StringBuilder sb, List<DebugStackFrameInfo> frames, int frame)
    {
        var events = GetRecentDebugSubmoduleEvents(frames, frame, 10);
        sb.AppendLine("Recent submodule events");
        if (events.Count == 0)
        {
            sb.AppendLine("  none captured near this error");
            return;
        }

        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            sb.Append("  #").Append((i + 1).ToString(CultureInfo.InvariantCulture));
            sb.Append(" frame=").Append(ev.Frame.ToString(CultureInfo.InvariantCulture));
            sb.Append(" method=").Append(ev.Method);
            sb.Append(" result=").Append(ev.Result);
            if (!string.IsNullOrEmpty(ev.Exception))
                sb.Append(" exception=").Append(ev.Exception);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(ev.Target))
                sb.AppendLine("     target=" + ev.Target);
            for (var a = 0; a < ev.Arguments.Length; a++)
                sb.AppendLine("     arg" + a.ToString(CultureInfo.InvariantCulture) + "=" + ev.Arguments[a]);
        }
    }
    private List<DebugSubmoduleTraceEvent> GetRecentDebugSubmoduleEvents(List<DebugStackFrameInfo> frames, int frame, int limit)
    {
        var result = new List<DebugSubmoduleTraceEvent>();
        if (_host._debugSubmoduleTraceEvents.Count == 0)
            return result;

        var stackText = frames == null ? "" : string.Join("\n", frames.Select(f => (f.TypeName ?? "") + "." + (f.MethodName ?? "")).ToArray());
        for (var i = _host._debugSubmoduleTraceEvents.Count - 1; i >= 0 && result.Count < limit; i--)
        {
            var ev = _host._debugSubmoduleTraceEvents[i];
            var delta = frame - ev.Frame;
            if (delta < 0 || delta > DebugSubmoduleTraceFrameWindow)
                continue;

            if (string.IsNullOrEmpty(stackText) ||
                stackText.IndexOf(ev.Method, StringComparison.OrdinalIgnoreCase) >= 0 ||
                stackText.IndexOf("FactionBranch.OnSimulateHour", StringComparison.OrdinalIgnoreCase) >= 0 ||
                stackText.IndexOf("GameDate.AdvanceHour", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Add(ev);
            }
        }
        result.Reverse();
        return result;
    }
}
