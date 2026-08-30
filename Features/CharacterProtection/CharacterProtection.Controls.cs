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
    private void SetAffinityOnlyIncrease(bool enabled)
    {
        if (!_modules.CharacterProtection.SetAffinityOnlyIncrease(enabled))
            return;
        _log = enabled
            ? T("好感度只增不减已开启", "Affinity only increases enabled")
            : T("好感度只增不减已关闭", "Affinity only increases disabled");
    }
    private void SetIgnoreSpecialNpcHatchRestriction(bool enabled)
    {
        if (!_modules.SpecialNpcHatch.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("无视特殊NPC孵化限制已开启", "Special NPC hatching restriction ignored")
            : T("无视特殊NPC孵化限制已关闭", "Special NPC hatching restriction restored");
    }
    private void SetIgnoreSpecialNpcCaptureRestriction(bool enabled)
    {
        if (!_modules.SpecialNpcCapture.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("无视特殊NPC捕获限制已开启", "Special NPC capture restriction ignored")
            : T("无视特殊NPC捕获限制已关闭", "Special NPC capture restriction restored");
    }
    private void SetKarmaOnlyIncrease(bool enabled)
    {
        if (!_modules.CharacterProtection.SetKarmaOnlyIncrease(enabled))
            return;
        _log = enabled
            ? T("善恶值只增不减已开启", "Karma only increases enabled")
            : T("善恶值只增不减已关闭", "Karma only increases disabled");
    }
    private void SetAttackCannotBeInterrupted(bool enabled)
    {
        if (!_modules.CharacterProtection.SetAttackCannotBeInterrupted(enabled))
            return;
        _log = enabled
            ? T("攻击不会被打断已开启", "Attack interruption prevention enabled")
            : T("攻击不会被打断已关闭", "Attack interruption prevention disabled");
    }
    private void SetAttackCannotBeInterruptedIncludeParty(bool enabled)
    {
        _modules.CharacterProtection.SetAttackCannotBeInterruptedIncludeParty(enabled);
    }
}
