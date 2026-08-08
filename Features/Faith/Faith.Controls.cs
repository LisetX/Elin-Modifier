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
    private void SetUnlimitedOfferingFaithPoints(bool enabled)
    {
        _unlimitedOfferingFaithPoints = enabled;
        _log = enabled
            ? T("供奉提升虔诚度无上限已开启", "Unlimited piety gain per offering enabled")
            : T("供奉提升虔诚度无上限已关闭", "Unlimited piety gain per offering disabled");
    }
    private void SetIgnoreGodArtifactFaithRequirement(bool enabled)
    {
        if (_ignoreGodArtifactFaithRequirement == enabled)
            return;
        _ignoreGodArtifactFaithRequirement = enabled;
        RefreshGodArtifactFaithRestrictionState();
        _log = enabled
            ? T("无视神器信仰条件限制已开启", "God artifact faith requirement ignored")
            : T("无视神器信仰条件限制已关闭", "God artifact faith requirement restored");
    }
}
