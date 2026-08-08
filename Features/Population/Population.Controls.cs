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
    private void SetUnlimitedHomeResidentCap(bool enabled)
    {
        _unlimitedHomeResidentCap = enabled;
        _log = enabled
            ? T("家园居民上限无限制已开启", "Unlimited home resident cap enabled")
            : T("家园居民上限无限制已关闭", "Unlimited home resident cap disabled");
    }
    private void SetUnlimitedPartyMemberCap(bool enabled)
    {
        _unlimitedPartyMemberCap = enabled;
        try { GameAccess.Runtime.Player?.RefreshEmptyAlly(); } catch { }
        _log = enabled
            ? T("队伍人数上限无限制已开启", "Unlimited party member cap enabled")
            : T("队伍人数上限无限制已关闭", "Unlimited party member cap disabled");
    }
}
