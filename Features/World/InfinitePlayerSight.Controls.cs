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
    private void SetInfinitePlayerSight(bool enabled)
    {
        _infinitePlayerSight = enabled;
        _scheduler.Invalidate(ModifierTask.InfiniteSight);
        if (enabled)
        {
            _infinitePlayerSightApplied = false;
            _infinitePlayerSightPointCount = 0;
            ApplyInfinitePlayerSight();
            _log = T("无视迷雾+无限视野已开启", "Ignore fog + infinite sight enabled");
        }
        else
        {
            ClearInfinitePlayerSight();
            _log = T("无视迷雾+无限视野已关闭", "Ignore fog + infinite sight disabled");
        }
    }
}
