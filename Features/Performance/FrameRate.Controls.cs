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
    private void SetUnlockFrameRate(bool enabled)
    {
        _unlockFrameRate = enabled;
        _scheduler.Invalidate(ModifierTask.FrameRateUnlock);
        if (enabled)
            ApplyUnlockFrameRate();
        else
            RestoreFrameRateLimit();
        _log = enabled
            ? T("解锁刷新率上限已开启", "Refresh rate limit unlocked")
            : T("解锁刷新率上限已关闭", "Refresh rate limit restored");
    }
}
