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
    [HarmonyPatch(typeof(Chara), "AddCondition", new[] { typeof(Condition), typeof(bool) })]
    private static class CharaAddConditionIgnoreBuffEffectsPatch
    {
        private static bool Prefix(Chara __instance, Condition __0, ref Condition __result)
        {
            if (!ShouldIgnoreBuffEffect(__instance, __0))
                return true;
            __result = null!;
            return false;
        }
    }
    [HarmonyPatch(typeof(Chara), "TickConditions")]
    private static class CharaTickConditionsIgnoreBuffEffectsPatch
    {
        private static void Prefix(Chara __instance)
        {
            RemoveIgnoredBuffEffects(__instance);
        }
    }
}
