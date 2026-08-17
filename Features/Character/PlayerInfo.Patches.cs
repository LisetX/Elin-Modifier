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
    [HarmonyPatch(typeof(Chara), "GetFavCat")]
    private static class CharaGetFavCatPlayerInfoPatch
    {
        private static void Postfix(Chara __instance, ref SourceCategory.Row __result)
        {
            if (TryGetPlayerLikedCategoryOverride(__instance, out var overrideRow) && overrideRow != null)
                __result = overrideRow;
        }
    }

    [HarmonyPatch(typeof(Chara), "GetFavFood")]
    private static class CharaGetFavFoodPlayerInfoPatch
    {
        private static void Postfix(Chara __instance, ref SourceThing.Row __result)
        {
            if (TryGetPlayerLikedFoodOverride(__instance, out var overrideRow) && overrideRow != null)
                __result = overrideRow;
        }
    }
}
