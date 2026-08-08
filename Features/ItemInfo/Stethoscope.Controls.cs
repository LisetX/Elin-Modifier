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
    private void SetStethoscopeNoTargetLimit(bool enabled)
    {
        _stethoscopeNoTargetLimit = enabled;
        _log = enabled
            ? T("听诊器无对象限制已开启", "Stethoscope no target limit enabled")
            : T("听诊器无对象限制已关闭", "Stethoscope no target limit disabled");
    }
}
