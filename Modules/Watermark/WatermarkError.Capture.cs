using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private float GetWatermarkRelativeScale()
    {
        return Mathf.Max(0.1f, 1f + WatermarkUiScaleOffset / 10f);
    }
    internal void SetGameErrorNotification(bool value)
    {
        _watermarkGameErrorNotification = value;
        _watermarkConfigDirty = true;
        RefreshWatermarkErrorCapture();
        if (!value)
        {
            lock (_watermarkPendingErrorLock)
            {
                _watermarkPendingErrors.Clear();
                _watermarkRecentErrorChannels.Clear();
            }
            DismissWatermarkErrorNotification(true);
        }
    }
    internal void SetSuppressWarningNotification(bool value)
    {
        if (_watermarkSuppressWarningNotification == value)
            return;

        _watermarkSuppressWarningNotification = value;
        _watermarkConfigDirty = true;
        if (!value)
            return;

        lock (_watermarkPendingErrorLock)
        {
            if (_watermarkPendingErrors.Count > 0)
            {
                var retained = new Queue<WatermarkPendingError>(_watermarkPendingErrors.Count);
                while (_watermarkPendingErrors.Count > 0)
                {
                    var pending = _watermarkPendingErrors.Dequeue();
                    if (!IsWarningLevel(pending.Level))
                        retained.Enqueue(pending);
                }
                while (retained.Count > 0)
                    _watermarkPendingErrors.Enqueue(retained.Dequeue());
            }
            _watermarkRecentErrorChannels.Clear();
        }

        if (_watermarkErrorActive && _watermarkErrorIsWarning)
            DismissWatermarkErrorNotification(true);
    }
    private void RefreshWatermarkErrorCapture()
    {
        var shouldCapture = _watermarkGameErrorNotification && _showElinModifierWatermark;
        if (shouldCapture)
        {
            if (!_watermarkErrorCaptureInstalled)
            {
                try
                {
                    Application.logMessageReceived += OnWatermarkUnityErrorLog;
                    _watermarkErrorCaptureInstalled = true;
                }
                catch
                {
                    _watermarkErrorCaptureInstalled = false;
                }
            }

            try
            {
                if (_watermarkBepInExErrorListener == null)
                {
                    _watermarkBepInExErrorListener = new WatermarkBepInExErrorListener(this);
                    BepInEx.Logging.Logger.Listeners.Add(_watermarkBepInExErrorListener);
                }
            }
            catch
            {
                _watermarkBepInExErrorListener = null;
            }
            return;
        }

        if (!_watermarkErrorCaptureInstalled && _watermarkBepInExErrorListener == null)
            return;

        try
        {
            Application.logMessageReceived -= OnWatermarkUnityErrorLog;
        }
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
}
