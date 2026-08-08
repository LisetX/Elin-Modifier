using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private void DrainWatermarkErrorNotification()
    {
        if (_watermarkErrorActive)
            return;

        WatermarkPendingError pending;
        lock (_watermarkPendingErrorLock)
        {
            if (_watermarkPendingErrors.Count == 0)
                return;
            pending = _watermarkPendingErrors.Dequeue();
        }
        ShowWatermarkErrorNotification(pending.Source, pending.Level, pending.Message, pending.StackTrace);
    }
    private void ShowWatermarkErrorNotification(string sourceName, string level, string message, string stackTrace)
    {
        if (!_watermarkGameErrorNotification || !_showElinModifierWatermark)
            return;

        sourceName = sourceName ?? "";
        level = level ?? "";
        message = message ?? "";
        stackTrace = stackTrace ?? "";
        EnsureWatermark();
        EnsureWatermarkErrorNotification();
        if (_watermarkErrorRoot == null || _watermarkErrorBar == null)
            return;

        _watermarkErrorDetails = BuildWatermarkErrorDetails(sourceName ?? "", level ?? "", message, stackTrace);
        _watermarkErrorIsWarning = IsWarningLevel(level);
        _watermarkErrorLastSummarySeconds = -1;
        _watermarkErrorNormalWidth = 520f;
        _watermarkErrorExpandedHeight = 300f;
        _watermarkErrorLastDetailLayoutWidth = -1f;
        _watermarkErrorDetailLayoutDirty = true;
        if (_watermarkErrorDetailText != null)
            _watermarkErrorDetailText.text = _watermarkErrorDetails;
        if (_watermarkErrorScrollRect != null)
            _watermarkErrorScrollRect.verticalNormalizedPosition = 1f;

        _watermarkErrorBelow = ShouldWatermarkErrorSlideBelow();
        _watermarkErrorBar.pivot = _watermarkErrorBelow
            ? new Vector2(0.5f, 1f)
            : new Vector2(0.5f, 0f);
        var watermarkPosition = _watermarkBar == null ? Vector2.zero : _watermarkBar.anchoredPosition;
        var hiddenY = GetWatermarkErrorHiddenY(watermarkPosition);
        if (!_watermarkErrorActive)
        {
            _watermarkErrorCurrentX = watermarkPosition.x;
            _watermarkErrorCurrentY = hiddenY;
            _watermarkErrorCurrentWidth = 320f;
            _watermarkErrorCurrentHeight = WatermarkErrorNormalHeight;
            _watermarkErrorCurrentAlpha = 0f;
            _watermarkErrorVelocityX = 0f;
            _watermarkErrorVelocityY = 0f;
            _watermarkErrorVelocityWidth = 0f;
            _watermarkErrorVelocityHeight = 0f;
            _watermarkErrorVelocityAlpha = 0f;
        }

        _watermarkErrorActive = true;
        _watermarkErrorDismissing = false;
        _watermarkErrorExpanded = false;
        _watermarkErrorHovered = false;
        _watermarkErrorRemainingSeconds = WatermarkErrorTimeoutSeconds;
        _watermarkErrorLastTickAt = Time.realtimeSinceStartup;
        if (_watermarkErrorSummaryCanvasGroup != null)
            _watermarkErrorSummaryCanvasGroup.alpha = 1f;
        if (_watermarkErrorDetailCanvasGroup != null)
            _watermarkErrorDetailCanvasGroup.alpha = 0f;
        if (_watermarkErrorViewport != null)
            _watermarkErrorViewport.gameObject.SetActive(false);
        _watermarkErrorRoot.SetActive(true);
        RefreshWatermarkErrorSummaryText(true);
        ApplyWatermarkErrorVisualSettings();
    }
    private string BuildWatermarkErrorDetails(string sourceName, string level, string message, string stackTrace)
    {
        var text = T("时间", "Time") + ": " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n" +
                   T("来源", "Source") + ": " + (sourceName ?? "") + "\n" +
                   T("级别", "Level") + ": " + (level ?? "") + "\n\n" +
                   T("报错内容", "Error details") + ":\n" + (message ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(stackTrace))
            text += "\n\n" + T("堆栈", "Stack trace") + ":\n" + stackTrace.Trim();
        const int maxLength = 48000;
        if (text.Length > maxLength)
            text = text.Substring(0, maxLength) + "\n\n" + T("[内容过长，已截断]", "[Content too long; truncated]");
        return text;
    }
}
