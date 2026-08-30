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
    private void SetOptimizeDungeonVoidScaling(bool enabled)
    {
        _optimizeDungeonVoidScaling = enabled;
        _log = enabled
            ? T("优化地牢Void缩放逻辑已开启", "Dungeon Void scaling optimization enabled")
            : T("优化地牢Void缩放逻辑已关闭", "Dungeon Void scaling optimization disabled");
    }
}
