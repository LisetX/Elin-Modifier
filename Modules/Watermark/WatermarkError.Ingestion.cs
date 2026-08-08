using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private void OnWatermarkUnityErrorLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert && type != LogType.Warning)
            return;
        QueueWatermarkErrorNotification("Unity Log", type.ToString(), condition, stackTrace, "unity-callback");
    }
    private void OnWatermarkBepInExErrorLog(object sender, LogEventArgs eventArgs)
    {
        if (eventArgs == null || (eventArgs.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0)
            return;

        var sourceName = "BepInEx";
        try
        {
            if (eventArgs.Source != null && !string.IsNullOrWhiteSpace(eventArgs.Source.SourceName))
                sourceName = eventArgs.Source.SourceName;
            else if (sender is ILogSource source && !string.IsNullOrWhiteSpace(source.SourceName))
                sourceName = source.SourceName;
        }
        catch { }

        var message = "";
        var stackTrace = "";
        try
        {
            if (eventArgs.Data is Exception exception)
            {
                message = exception.GetType().FullName + ": " + exception.Message;
                stackTrace = exception.StackTrace ?? "";
            }
            else
            {
                message = eventArgs.Data?.ToString() ?? "";
                stackTrace = ExtractDebugStackTraceFromLogText(message);
            }
        }
        catch { }

        QueueWatermarkErrorNotification(sourceName, eventArgs.Level.ToString(), message, stackTrace, "bepinex-listener");
    }
    private void QueueWatermarkErrorNotification(string sourceName, string level, string message, string stackTrace, string captureChannel)
    {
        if (_watermarkSuppressWarningNotification && IsWarningLevel(level))
            return;
        if (ShouldIgnoreWatermarkError(message, stackTrace))
            return;

        var signature = BuildWatermarkErrorMessageSignature(level, message);
        var channel = captureChannel ?? "";
        var nowTicks = DateTime.UtcNow.Ticks;
        var channelDuplicateWindowTicks = TimeSpan.TicksPerMillisecond * 750L;
        lock (_watermarkPendingErrorLock)
        {
            if (_watermarkRecentErrorChannels.TryGetValue(signature, out var previous) &&
                nowTicks >= previous.Ticks && nowTicks - previous.Ticks <= channelDuplicateWindowTicks)
            {
                var duplicatedAcrossCaptureChannels = !string.Equals(previous.Channel, channel, StringComparison.Ordinal);
                var duplicatedUnityExceptionPair = string.Equals(previous.Channel, channel, StringComparison.Ordinal) &&
                                                   string.Equals(channel, "unity-callback", StringComparison.Ordinal) &&
                                                   IsWatermarkErrorExceptionPair(previous.Level, level);
                if (duplicatedAcrossCaptureChannels || duplicatedUnityExceptionPair)
                    return;
            }

            _watermarkRecentErrorChannels[signature] = new WatermarkRecentErrorChannel(channel, level, nowTicks);
            _watermarkPendingErrors.Enqueue(new WatermarkPendingError(sourceName, level, message, stackTrace));
            if (_watermarkRecentErrorChannels.Count > 128)
                TrimWatermarkRecentErrorChannels(nowTicks - TimeSpan.TicksPerSecond * 2L);
        }
    }
    private static bool ShouldIgnoreWatermarkError(string message, string stackTrace)
    {
        return (message ?? "").IndexOf("Failed to load the Steam App List", StringComparison.OrdinalIgnoreCase) >= 0 ||
               (stackTrace ?? "").IndexOf("Failed to load the Steam App List", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    private static string BuildWatermarkErrorMessageSignature(string level, string message)
    {
        var levelGroup = IsWarningLevel(level)
            ? "warning"
            : "error";
        return levelGroup + "|" + NormalizeWatermarkErrorSignaturePart(
            GetWatermarkPrimaryErrorMessage(message),
            2048);
    }
    private static bool IsWarningLevel(string level)
    {
        return (level ?? "").IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    private static string GetWatermarkPrimaryErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "";
        var text = message.Replace("\r", "").Trim();
        var lineBreak = text.IndexOf('\n');
        if (lineBreak >= 0)
            text = text.Substring(0, lineBreak).Trim();
        if (text.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            text = text.Substring("System.".Length);
        return text;
    }
    private static bool IsWatermarkErrorExceptionPair(string firstLevel, string secondLevel)
    {
        var firstIsException = (firstLevel ?? "").IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0;
        var secondIsException = (secondLevel ?? "").IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0;
        var firstIsError = string.Equals((firstLevel ?? "").Trim(), "Error", StringComparison.OrdinalIgnoreCase);
        var secondIsError = string.Equals((secondLevel ?? "").Trim(), "Error", StringComparison.OrdinalIgnoreCase);
        return (firstIsException && secondIsError) || (firstIsError && secondIsException);
    }
    private static string NormalizeWatermarkErrorSignaturePart(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var source = value.Trim();
        var builder = new System.Text.StringBuilder(Math.Min(source.Length, maxLength));
        var pendingSpace = false;
        for (var i = 0; i < source.Length && builder.Length < maxLength; i++)
        {
            var character = source[i];
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace && builder.Length < maxLength)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
    private void TrimWatermarkRecentErrorChannels(long minimumTicks)
    {
        var staleKeys = new List<string>();
        foreach (var pair in _watermarkRecentErrorChannels)
        {
            if (pair.Value.Ticks < minimumTicks)
                staleKeys.Add(pair.Key);
        }
        for (var i = 0; i < staleKeys.Count; i++)
            _watermarkRecentErrorChannels.Remove(staleKeys[i]);
    }
}
