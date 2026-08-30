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
    private readonly ElinModifierPlugin _host;
    internal ExceptionTraceModule(ElinModifierPlugin host)
    {
        _host = host;
    }
    private static void CaptureDebugExceptionTrace(Exception exception, string channel, string sourceName)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (instance == null || !instance._debugAuthorized || exception == null)
            return;
        ElinModifierPlugin.ActiveModules?.ExceptionTrace.CaptureDebugErrorLog(channel, sourceName, exception.GetType().Name, exception.GetType().FullName + ": " + exception.Message, exception.StackTrace ?? "", exception);
    }
    internal void CaptureDebugErrorLog(string channel, string sourceName, string level, string message, string stackTrace, Exception exception)
    {
        if (!_host._debugAuthorized)
            return;

        message = message ?? "";
        stackTrace = stackTrace ?? "";
        sourceName = sourceName ?? "";
        level = level ?? "";

        var frame = 0;
        try { frame = Time.frameCount; } catch { }
        var key = BuildDebugExceptionTraceKey(channel, sourceName, level, message, stackTrace);
        if (string.Equals(_host._debugLastExceptionTraceKey, key, StringComparison.Ordinal) &&
            frame - _host._debugLastExceptionTraceFrame >= 0 &&
            frame - _host._debugLastExceptionTraceFrame < 4)
            return;

        try
        {
            var trace = BuildDebugExceptionTrace(channel, sourceName, level, message, stackTrace, exception, frame);
            _host._debugLastExceptionTraceKey = key;
            _host._debugLastExceptionTraceFrame = frame;
            AddDebugExceptionTraceRecord(new DebugExceptionTraceRecord(frame, channel, sourceName, level, trace));
        }
        catch (Exception ex)
        {
            var trace = "Exception trace\n" +
                        "Frame: " + frame.ToString(CultureInfo.InvariantCulture) + "\n" +
                        "Channel: " + SafeDebugText(channel) + "\n" +
                        "Level: " + SafeDebugText(level) + "\n" +
                        "Source: " + SafeDebugText(sourceName) + "\n" +
                        "Message: " + CompactDebugMultiline(message, 1200) + "\n\n" +
                        "Trace analyzer failed: " + ex.GetType().FullName + " - " + ex.Message + "\n" +
                        "Raw stack:\n" + CompactDebugMultiline(stackTrace, 3000);
            _host._debugLastExceptionTraceKey = key;
            _host._debugLastExceptionTraceFrame = frame;
            AddDebugExceptionTraceRecord(new DebugExceptionTraceRecord(frame, channel, sourceName, level, trace));
        }
    }
    internal string GetDebugExceptionTraceRecordLabel()
    {
        var count = _host._debugExceptionTraceRecords.Count;
        if (count <= 0 || _host._debugExceptionTraceRecordIndex < 0)
            return "Record: 0 / 0";
        var index = Math.Max(0, Math.Min(_host._debugExceptionTraceRecordIndex, count - 1));
        return "Record: " + (index + 1).ToString(CultureInfo.InvariantCulture) + " / " + count.ToString(CultureInfo.InvariantCulture);
    }
    private void AddDebugExceptionTraceRecord(DebugExceptionTraceRecord record)
    {
        if (record == null)
            return;
        while (_host._debugExceptionTraceRecords.Count >= DebugExceptionTraceMaxRecords)
            _host._debugExceptionTraceRecords.RemoveAt(0);
        _host._debugExceptionTraceRecords.Add(record);
        SelectDebugExceptionTraceRecord(_host._debugExceptionTraceRecords.Count - 1);
    }
    internal void SelectDebugExceptionTraceRecord(int index)
    {
        if (_host._debugExceptionTraceRecords.Count == 0)
        {
            _host._debugExceptionTraceRecordIndex = -1;
            _host._debugExceptionTraceFrame = -1;
            _host._debugExceptionTrace = "No Error/Fatal log captured.";
            return;
        }

        _host._debugExceptionTraceRecordIndex = Math.Max(0, Math.Min(index, _host._debugExceptionTraceRecords.Count - 1));
        var record = _host._debugExceptionTraceRecords[_host._debugExceptionTraceRecordIndex];
        _host._debugExceptionTraceFrame = record.Frame;
        _host._debugExceptionTrace = record.Trace;
    }
    internal void ClearDebugExceptionTraceRecords()
    {
        _host._debugExceptionTraceRecords.Clear();
        _host._debugSubmoduleTraceEvents.Clear();
        _host._debugLastExceptionTraceKey = "";
        _host._debugLastExceptionTraceFrame = -9999;
        SelectDebugExceptionTraceRecord(-1);
    }
    internal static void RecordDebugSubmoduleTraceEvent(string method, object target, object result, Exception exception, params object[] arguments)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (instance == null || !instance._debugAuthorized)
            return;
        ElinModifierPlugin.ActiveModules?.ExceptionTrace.RecordDebugSubmoduleTraceEventInstance(method, target, result, exception, arguments);
    }
    private void RecordDebugSubmoduleTraceEventInstance(string method, object target, object result, Exception exception, object[] arguments)
    {
        try
        {
            var frame = 0;
            try { frame = Time.frameCount; } catch { }

            var argTexts = Array.Empty<string>();
            if (arguments != null && arguments.Length > 0)
            {
                argTexts = new string[arguments.Length];
                for (var i = 0; i < arguments.Length; i++)
                    argTexts[i] = DescribeDebugTraceValue(arguments[i]);
            }

            var exceptionText = exception == null ? "" : exception.GetType().FullName + " - " + exception.Message;
            _host._debugSubmoduleTraceEvents.Add(new DebugSubmoduleTraceEvent(
                frame,
                method ?? "",
                DescribeDebugTraceValue(target),
                DescribeDebugTraceValue(result),
                exceptionText,
                argTexts));

            while (_host._debugSubmoduleTraceEvents.Count > DebugSubmoduleTraceMaxRecords)
                _host._debugSubmoduleTraceEvents.RemoveAt(0);
        }
        catch { }
    }
}
