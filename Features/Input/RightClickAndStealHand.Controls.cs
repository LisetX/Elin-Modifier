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
    private void SetRightClickInterruptOperation(bool enabled)
    {
        if (!_modules.RightClickInterrupt.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("右键打断操作已开启", "Right-click action interruption enabled")
            : T("右键打断操作已关闭", "Right-click action interruption disabled");
    }
    private void SetStealHandNoTargetLimit(bool enabled)
    {
        _stealHandNoTargetLimit = enabled;
        _log = enabled
            ? T("盗窃之手无对象限制已开启", "Steal hand target restrictions disabled")
            : T("盗窃之手无对象限制已关闭", "Steal hand target restrictions restored");
    }
    private void SetStealHandUndetectable(bool enabled)
    {
        _stealHandUndetectable = enabled;
        _log = enabled
            ? T("盗窃之手不会被发现已开启", "Undetectable steal hand enabled")
            : T("盗窃之手不会被发现已关闭", "Undetectable steal hand disabled");
    }
}
