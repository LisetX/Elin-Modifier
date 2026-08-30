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
    private void SetShowFoodRot(bool enabled)
    {
        _showFoodRot = enabled;
        RefreshFoodRotOverlays();
        _log = enabled
            ? T("显示食物腐烂度已开启", "Show food rot enabled")
            : T("显示食物腐烂度已关闭", "Show food rot disabled");
    }
    private void SetIgnoreFoodDecay(bool enabled)
    {
        _ignoreFoodDecay = enabled;
        RefreshFoodRotOverlays();
        _log = enabled
            ? T("无视食物腐烂已开启", "Ignore food rot enabled")
            : T("无视食物腐烂已关闭", "Ignore food rot disabled");
    }
}
