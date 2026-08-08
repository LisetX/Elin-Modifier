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
    private void SetLowPerformanceMode(bool enabled)
    {
        _lowPerformanceMode = enabled;
        _scheduler.Invalidate(ModifierTask.IgnoreBuffEffects);
        ClearLowPerformanceCaches();
        _log = enabled
            ? T("低性能模式已开启", "Low performance mode enabled")
            : T("低性能模式已关闭", "Low performance mode disabled");
    }
    private void ClearLowPerformanceCaches()
    {
        _lowPerformanceValueCache.Clear();
        InvalidateNpcMoreInfoCaches();
        InvalidateItemMoreInfoCache();
        _teleportZoneCacheDirty = true;
        _teleportFilterCacheDirty = true;
    }
}
