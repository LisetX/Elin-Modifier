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

public sealed partial class ElinModifierPlugin
{
    private void SetHostileThreatMarker(bool enabled)
    {
        _hostileThreatMarker = enabled;
        _modules.ThreatOverlay.ClearPredictionEvents();
        InvalidateThreatData();
        _log = enabled
            ? T("敌对威胁标记已开启", "Hostile threat marker enabled")
            : T("敌对威胁标记已关闭", "Hostile threat marker disabled");
    }
    private void SetHostileThreatBehaviorPrediction(bool enabled)
    {
        if (_hostileThreatBehaviorPrediction == enabled)
            return;
        _hostileThreatBehaviorPrediction = enabled;
        _modules.ThreatOverlay.ClearPredictionEvents();
        InvalidateThreatData();
    }
    private void SetHostileThreatPredecisionLock(bool enabled)
    {
        if (_hostileThreatPredecisionLock == enabled)
            return;
        _hostileThreatPredecisionLock = enabled;
        _modules.ThreatOverlay.ClearLockedDecisions();
        InvalidateThreatData();
    }
}
