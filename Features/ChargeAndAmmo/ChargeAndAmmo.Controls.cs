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
    private void SetInfiniteChargeAndAmmo(bool enabled)
    {
        if (_infiniteChargeAndAmmo == enabled)
            return;
        _infiniteChargeAndAmmo = enabled;
        _log = enabled
            ? T("无限充能&无限弹药已开启", "Infinite charge & ammo enabled")
            : T("无限充能&无限弹药已关闭", "Infinite charge & ammo disabled");
    }
    private void SetRodStacking(bool enabled)
    {
        if (_rodStacking == enabled)
            return;
        _rodStacking = enabled;
        if (!enabled)
        {
            _rodStackingTarget = null;
            _rodStackingSource = null;
            _rodStackingCandidatePage = 0;
        }
        _log = enabled
            ? T("充能堆叠已开启", "Charge stacking enabled")
            : T("充能堆叠已关闭", "Charge stacking disabled");
    }
}
