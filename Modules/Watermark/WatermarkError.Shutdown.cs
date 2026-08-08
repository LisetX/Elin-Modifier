using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private void ShutdownWatermarkErrorNotification()
    {
        RefreshWatermarkErrorCaptureForShutdown();
        if (_watermarkErrorCapsuleSprite != null)
            UnityEngine.Object.Destroy(_watermarkErrorCapsuleSprite);
        if (_watermarkErrorCapsuleTexture != null)
            UnityEngine.Object.Destroy(_watermarkErrorCapsuleTexture);
        if (_watermarkErrorRoot != null)
            UnityEngine.Object.Destroy(_watermarkErrorRoot);
        _watermarkErrorRoot = null;
        _watermarkErrorBar = null;
        _watermarkErrorBackground = null;
        _watermarkErrorCapsuleTexture = null;
        _watermarkErrorCapsuleSprite = null;
        _watermarkErrorCanvasGroup = null;
        _watermarkErrorSummaryText = null;
        _watermarkErrorSummaryCanvasGroup = null;
        _watermarkErrorViewport = null;
        _watermarkErrorDetailCanvasGroup = null;
        _watermarkErrorContent = null;
        _watermarkErrorDetailText = null;
        _watermarkErrorScrollRect = null;
        _watermarkErrorActive = false;
        _watermarkErrorDismissing = false;
        _watermarkErrorExpanded = false;
        _watermarkErrorHovered = false;
        _watermarkErrorIsWarning = false;
        _watermarkErrorNormalWidth = 520f;
        _watermarkErrorExpandedHeight = 300f;
        _watermarkErrorLastDetailLayoutWidth = -1f;
        _watermarkErrorLastSummarySeconds = -1;
        _watermarkErrorDetailLayoutDirty = true;
        lock (_watermarkPendingErrorLock)
        {
            _watermarkPendingErrors.Clear();
            _watermarkRecentErrorChannels.Clear();
        }
    }
    private void RefreshWatermarkErrorCaptureForShutdown()
    {
        try { Application.logMessageReceived -= OnWatermarkUnityErrorLog; }
        catch { }
        _watermarkErrorCaptureInstalled = false;
        try
        {
            if (_watermarkBepInExErrorListener != null)
                BepInEx.Logging.Logger.Listeners.Remove(_watermarkBepInExErrorListener);
        }
        catch { }
        _watermarkBepInExErrorListener = null;
    }
    private sealed class WatermarkBepInExErrorListener : ILogListener
    {
        private readonly WatermarkModule _owner;

        public WatermarkBepInExErrorListener(WatermarkModule owner)
        {
            _owner = owner;
        }

        public LogLevel LogLevelFilter => LogLevel.Warning | LogLevel.Error | LogLevel.Fatal;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try { _owner.OnWatermarkBepInExErrorLog(sender, eventArgs); }
            catch { }
        }

        public void Dispose()
        {
        }
    }
}
