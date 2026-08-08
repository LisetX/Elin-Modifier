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

public sealed partial class ElinModifierPlugin
{
    private void ApplyUnlockFrameRate()
    {
        if (!_unlockFrameRate)
            return;

        try
        {
            if (!_frameRateLimitSaved)
            {
                _originalVSyncCount = QualitySettings.vSyncCount;
                _originalTargetFrameRate = Application.targetFrameRate;
                _frameRateLimitSaved = true;
            }

            if (QualitySettings.vSyncCount == 0 && Application.targetFrameRate == -1)
                return;

            if (QualitySettings.vSyncCount != 0)
                QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate != -1)
                Application.targetFrameRate = -1;
        }
        catch (Exception ex)
        {
            _log = T("解锁刷新率上限", "Unlock refresh rate limit") + T(" 设置失败: ", " failed: ") + ex.Message;
        }
    }
    private void RestoreFrameRateLimit()
    {
        if (!_frameRateLimitSaved)
            return;

        try
        {
            if (!TryApplyGameFrameRateSettings())
            {
                QualitySettings.vSyncCount = _originalVSyncCount;
                Application.targetFrameRate = _originalTargetFrameRate;
            }
        }
        catch { }

        _frameRateLimitSaved = false;
    }
    private bool TryApplyGameFrameRateSettings()
    {
        try
        {
            var config = GameAccess.Runtime.Core?.config;
            if (config == null)
                return false;
            config.ApplyFPS(true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
