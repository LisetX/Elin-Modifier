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
    [HarmonyPatch(typeof(Card), "hp", MethodType.Setter)]
    private static class CardHpInvincibleModePatch
    {
        private static bool Prefix(Card __instance, int __0)
        {
            try
            {
                var target = __instance as Chara;
                return target == null || !IsInvincibleModeTarget(target) || __0 >= __instance.hp;
            }
            catch
            {
                return true;
            }
        }
    }
    [HarmonyPatch(typeof(Stats), "value", MethodType.Setter)]
    private static class StatsValueInvincibleModePatch
    {
        private static bool Prefix(Stats __instance, int __0)
        {
            try
            {
                if (ShouldBlockIgnoredDebuffStatChange(__instance, __0))
                    return false;
                if (__instance == null || (__instance.id != 3 && __instance.id != 8))
                    return true;
                var target = BaseStats.CC;
                return target == null || !IsInvincibleModeTarget(target) || __0 >= __instance.value;
            }
            catch
            {
                return true;
            }
        }
    }
}
