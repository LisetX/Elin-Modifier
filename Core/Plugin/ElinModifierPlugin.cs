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

[BepInPlugin(ModMetadata.PluginId, ModMetadata.Name, ModMetadata.Version)]
public sealed partial class ElinModifierPlugin : BaseUnityPlugin, ILGuiRowHandler
{
    private void Awake()
    {
        if (!IsAllowedHostProcess())
        {
            DisableSelfForInvalidHost();
            return;
        }

        Instance = this;
        BeginTermsConfirmation();
    }

    private void InitializePluginAfterTermsConfirmation()
    {
        _modules.InitializeAll();
    }

    private bool IsAllowedHostProcess()
    {
        try
        {
            var processName = Process.GetCurrentProcess().ProcessName;
            return string.Equals(processName, "Elin", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processName, "Elin.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void DisableSelfForInvalidHost()
    {
        try
        {
            Logger.LogWarning("Elin Modifier disabled: host process is not Elin.exe.");
        }
        catch { }

        if (ReferenceEquals(Instance, this))
            Instance = null;
        enabled = false;
        try { Destroy(this); }
        catch { }
    }

    private bool CheckDebugAuthorizationOnce()
    {
        try
        {
            var path = Path.Combine(GetPluginDirectory(), DebugLicenseFileName);
            if (!File.Exists(path))
                return false;

            var hash = ComputeFileSha256(path);
            return string.Equals(hash, DebugLicenseSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void InstallDebugErrorLogCaptureIfAuthorized()
    {
        if (!_debugAuthorized)
            return;

        try
        {
            if (!_debugUnityLogCaptureInstalled)
            {
                Application.logMessageReceived += OnDebugUnityLogMessage;
                _debugUnityLogCaptureInstalled = true;
            }
        }
        catch { }

        try
        {
            if (_debugBepInExErrorLogListener == null)
            {
                _debugBepInExErrorLogListener = new DebugErrorLogListener(this);
                BepInEx.Logging.Logger.Listeners.Add(_debugBepInExErrorLogListener);
            }
        }
        catch
        {
            _debugBepInExErrorLogListener = null;
        }
    }

    private void RemoveDebugErrorLogCapture()
    {
        try
        {
            if (_debugUnityLogCaptureInstalled)
                Application.logMessageReceived -= OnDebugUnityLogMessage;
        }
        catch { }
        _debugUnityLogCaptureInstalled = false;

        try
        {
            if (_debugBepInExErrorLogListener != null)
                BepInEx.Logging.Logger.Listeners.Remove(_debugBepInExErrorLogListener);
        }
        catch { }
        _debugBepInExErrorLogListener = null;
    }

    private void OnDebugUnityLogMessage(string condition, string stackTrace, LogType type)
    {
        if (!_debugAuthorized)
            return;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;
        CaptureDebugErrorLog("Unity", "Unity Log", type.ToString(), condition, stackTrace, null);
    }

    internal void OnDebugBepInExErrorLog(object sender, LogEventArgs eventArgs)
    {
        if (!_debugAuthorized || eventArgs == null)
            return;
        if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0)
            return;

        var sourceName = "";
        try { sourceName = eventArgs.Source == null ? "" : eventArgs.Source.SourceName; }
        catch { }
        if (string.IsNullOrEmpty(sourceName))
        {
            try { sourceName = (sender as ILogSource)?.SourceName ?? ""; }
            catch { }
        }

        var data = eventArgs.Data;
        var message = data == null ? "" : data.ToString();
        var stackTrace = "";
        var exception = data as Exception;
        if (exception != null)
        {
            message = exception.GetType().FullName + ": " + exception.Message;
            stackTrace = exception.StackTrace ?? "";
        }
        else if (!string.IsNullOrEmpty(message))
        {
            stackTrace = ExtractDebugStackTraceFromLogText(message);
        }

        CaptureDebugErrorLog("BepInEx", sourceName, eventArgs.Level.ToString(), message, stackTrace, exception);
    }

    private static string ComputeFileSha256(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
        {
            var bytes = sha.ComputeHash(stream);
            var sb = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    private void OnDestroy()
    {
        try
        {
            _moduleRegistry?.Dispose();
        }
        finally
        {
            _moduleRegistry = null;
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }
    }

    private void InstallHarmonyPatches()
    {
        try
        {
            _modules.Harmony.Install(typeof(ElinModifierPlugin).Assembly, Logger);
            EnsureCwlErrorNotificationPatch(true);
            if (_modules.Harmony.Failures.Count > 0)
                _log = "Harmony patch partial: " +
                       _modules.Harmony.InstalledPatchCount.ToString(CultureInfo.InvariantCulture) + "/" +
                       _modules.Harmony.DiscoveredPatchCount.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            _log = "Harmony patch failed: " + ex.Message;
        }
    }
}
