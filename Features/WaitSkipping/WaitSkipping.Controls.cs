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
    private void SetFishingNoWait(bool enabled)
    {
        if (!_modules.FishingNoWait.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("钓鱼无需等待已开启", "Instant fishing bite enabled")
            : T("钓鱼无需等待已关闭", "Instant fishing bite disabled");
    }
    private void SetGeneSynthesisNoWait(bool enabled)
    {
        if (!_modules.GeneSynthesisNoWait.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("基因合成无需等待已开启", "Instant gene synthesis enabled")
            : T("基因合成无需等待已关闭", "Instant gene synthesis disabled");
    }
    private void SetSleepWithoutSleepiness(bool enabled)
    {
        if (!_modules.SleepWithoutSleepiness.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("睡觉无需困意已开启", "Sleep without sleepiness enabled")
            : T("睡觉无需困意已关闭", "Sleep without sleepiness disabled");
    }
}
