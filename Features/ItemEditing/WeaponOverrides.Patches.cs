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
    [HarmonyPatch(typeof(Thing), "range", MethodType.Getter)]
    private static class ThingRangeOverridePatch
    {
        private static void Postfix(Thing __instance, ref int __result)
        {
            try
            {
                if (__instance?.mapInt != null &&
                    __instance.mapInt.TryGetValue(WeaponRangeOverrideMapKey, out var value))
                    __result = value;
            }
            catch
            {
            }
        }
    }
    [HarmonyPatch(typeof(Thing), "Penetration", MethodType.Getter)]
    private static class ThingPenetrationOverridePatch
    {
        private static void Postfix(Thing __instance, ref int __result)
        {
            try
            {
                if (__instance?.mapInt != null &&
                    __instance.mapInt.TryGetValue(WeaponPenetrationOverrideMapKey, out var value))
                    __result = value;
            }
            catch
            {
            }
        }
    }
}
