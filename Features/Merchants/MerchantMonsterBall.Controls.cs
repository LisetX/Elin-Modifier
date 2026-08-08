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
    private void SetMerchantAlwaysStocksMonsterBall(bool enabled)
    {
        if (!_modules.MerchantMonsterBall.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("道具商必刷精灵球已开启", "Guaranteed monster balls at goods merchants enabled")
            : T("道具商必刷精灵球已关闭", "Guaranteed monster balls at goods merchants disabled");
    }
    private void SetMerchantMonsterBallLevelOptimization(bool enabled)
    {
        if (!_modules.MerchantMonsterBall.SetLevelOptimizationEnabled(enabled))
            return;
        _log = enabled
            ? T("道具商精灵球等级优化已开启", "Goods merchant monster ball level optimization enabled")
            : T("道具商精灵球等级优化已关闭", "Goods merchant monster ball level optimization disabled");
    }
}
