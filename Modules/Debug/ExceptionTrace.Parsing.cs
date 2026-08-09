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
    private string BuildDebugExceptionTrace(string channel, string sourceName, string level, string message, string stackTrace, Exception exception, int frame)
    {
        var frames = ParseDebugStackFrames(message, stackTrace);
        var root = FindBestDebugRootFrame(frames);
        var firstPluginFrame = FindFirstDebugPluginFrame(frames);
        var sb = new StringBuilder(8192);

        sb.AppendLine("Exception trace");
        sb.AppendLine("Frame: " + frame.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("Channel: " + SafeDebugText(channel));
        sb.AppendLine("Level: " + SafeDebugText(level));
        sb.AppendLine("Source: " + SafeDebugText(sourceName));
        sb.AppendLine("Exception: " + GetDebugExceptionSummary(message, exception));
        sb.AppendLine("Message: " + CompactDebugMultiline(message, 900));
        sb.AppendLine();

        sb.AppendLine("Root cause path");
        if (root == null)
        {
            sb.AppendLine("[WARN] No stack frame was provided by this Error log.");
            sb.AppendLine("Owner: " + ResolveDebugLogOwnerFromSource(sourceName));
        }
        else
        {
            sb.AppendLine("Top throwing frame: " + FormatDebugStackFrame(root));
            sb.AppendLine("Resolved method: " + FormatDebugResolvedMethod(root.ResolvedMethod));
            sb.AppendLine("Assembly: " + FormatDebugAssembly(root.ResolvedAssembly));
            sb.AppendLine("Owner: " + ResolveDebugFrameOwner(root, sourceName));
            if (!string.IsNullOrEmpty(root.Location))
                sb.AppendLine("Location: " + root.Location);
        }

        if (firstPluginFrame != null && !ReferenceEquals(firstPluginFrame, root))
        {
            sb.AppendLine("First external mod/plugin frame: " + FormatDebugStackFrame(firstPluginFrame));
            sb.AppendLine("External owner: " + ResolveDebugFrameOwner(firstPluginFrame, sourceName));
        }

        sb.AppendLine();
        AppendDebugStackSummary(sb, frames);
        sb.AppendLine();
        AppendDebugSnapshot(sb);
        sb.AppendLine();
        AppendDebugFocusedProbe(sb, root, frames, sourceName, frame);
        return sb.ToString();
    }
    private static string BuildDebugExceptionTraceKey(string channel, string sourceName, string level, string message, string stackTrace)
    {
        var stackHead = "";
        if (!string.IsNullOrEmpty(stackTrace))
        {
            var lines = stackTrace.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length > 0)
                {
                    stackHead = line;
                    break;
                }
            }
        }
        return (sourceName ?? "") + "\n" + (level ?? "") + "\n" + CompactDebugMultiline(message, 260) + "\n" + stackHead;
    }
    internal static string ExtractDebugStackTraceFromLogText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var normalized = text.Replace("\r\n", "\n");
        var marker = normalized.IndexOf("Stack trace:", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            return normalized.Substring(marker + "Stack trace:".Length).Trim();

        var lines = normalized.Split('\n');
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("at ", StringComparison.Ordinal) ||
                line.Contains(" (at ", StringComparison.Ordinal) ||
                line.Contains(":DMD<", StringComparison.Ordinal) ||
                line.Contains(".DMD<", StringComparison.Ordinal))
                sb.AppendLine(line);
        }
        return sb.ToString().Trim();
    }
    private static string GetDebugExceptionSummary(string message, Exception exception)
    {
        if (exception != null)
            return exception.GetType().FullName + " - " + exception.Message;
        if (string.IsNullOrWhiteSpace(message))
            return "<unknown>";
        var firstLine = message.Replace("\r\n", "\n").Split('\n')[0].Trim();
        return string.IsNullOrEmpty(firstLine) ? "<unknown>" : firstLine;
    }
    private static List<DebugStackFrameInfo> ParseDebugStackFrames(string message, string stackTrace)
    {
        var text = stackTrace;
        if (string.IsNullOrWhiteSpace(text))
            text = ExtractDebugStackTraceFromLogText(message);
        var frames = new List<DebugStackFrameInfo>();
        if (string.IsNullOrWhiteSpace(text))
            return frames;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            DebugStackFrameInfo frame;
            if (TryParseDebugStackFrame(lines[i], out frame))
                frames.Add(frame);
        }
        return frames;
    }
    private static bool TryParseDebugStackFrame(string rawLine, out DebugStackFrameInfo frame)
    {
        frame = null;
        if (string.IsNullOrWhiteSpace(rawLine))
            return false;

        var raw = rawLine.Trim();
        if (raw.StartsWith("Stack trace:", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("Rethrow as ", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("Exception: ", StringComparison.OrdinalIgnoreCase))
            return false;

        var work = raw;
        if (work.StartsWith("at ", StringComparison.Ordinal))
            work = work.Substring(3).Trim();

        var wrapper = "";
        if (work.StartsWith("(wrapper ", StringComparison.Ordinal))
        {
            var end = work.IndexOf(')');
            if (end > 0)
            {
                wrapper = work.Substring(0, end + 1);
                work = work.Substring(end + 1).Trim();
            }
        }

        var location = "";
        var atIndex = work.IndexOf(" (at ", StringComparison.Ordinal);
        if (atIndex >= 0)
        {
            location = work.Substring(atIndex + 5).Trim();
            if (location.EndsWith(")", StringComparison.Ordinal))
                location = location.Substring(0, location.Length - 1);
            work = work.Substring(0, atIndex).Trim();
        }

        var typeName = "";
        var methodName = "";
        var dmdStart = work.IndexOf("DMD<", StringComparison.Ordinal);
        if (dmdStart >= 0)
        {
            var innerStart = dmdStart + 4;
            var innerEnd = work.IndexOf('>', innerStart);
            if (innerEnd > innerStart)
            {
                var inner = work.Substring(innerStart, innerEnd - innerStart);
                var sep = inner.LastIndexOf("::", StringComparison.Ordinal);
                if (sep > 0)
                {
                    typeName = inner.Substring(0, sep);
                    methodName = inner.Substring(sep + 2);
                }
            }
        }

        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
        {
            var signatureEnd = work.IndexOf(" (", StringComparison.Ordinal);
            if (signatureEnd < 0)
                signatureEnd = work.IndexOf('(');
            var head = signatureEnd >= 0 ? work.Substring(0, signatureEnd).Trim() : work.Trim();
            var colon = head.LastIndexOf(':');
            var dot = head.LastIndexOf('.');
            var split = colon > 0 ? colon : dot;
            if (split > 0)
            {
                typeName = head.Substring(0, split);
                methodName = head.Substring(split + 1);
            }
        }

        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
            return false;

        var resolvedType = LoadedAssemblyTypeResolver.Resolve(typeName, allowSimpleName: true);
        var resolvedMethod = ResolveDebugMethod(resolvedType, methodName);
        frame = new DebugStackFrameInfo(raw, wrapper, typeName, methodName, location, resolvedType, resolvedMethod);
        return true;
    }
    private static DebugStackFrameInfo FindBestDebugRootFrame(List<DebugStackFrameInfo> frames)
    {
        if (frames == null || frames.Count == 0)
            return null;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame == null)
                continue;
            var owner = frame.ResolvedAssembly == null ? "" : frame.ResolvedAssembly.GetName().Name ?? "";
            if (owner.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                owner.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                owner.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
                continue;
            if (frame.MethodName.StartsWith("DMD<", StringComparison.Ordinal))
                continue;
            return frame;
        }
        return frames[0];
    }
    private DebugStackFrameInfo FindFirstDebugPluginFrame(List<DebugStackFrameInfo> frames)
    {
        if (frames == null)
            return null;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame == null || frame.ResolvedAssembly == null)
                continue;
            if (FindDebugPluginForAssembly(frame.ResolvedAssembly, "") != null)
                return frame;
        }
        return null;
    }
}
