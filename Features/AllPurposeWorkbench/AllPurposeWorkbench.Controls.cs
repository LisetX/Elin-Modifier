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
    private void SetAllPurposeWorkbench(bool enabled)
    {
        if (!_modules.AllPurposeWorkbench.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("全能制作台已开启", "All-purpose workbench enabled")
            : T("全能制作台已关闭", "All-purpose workbench disabled");
    }
}
