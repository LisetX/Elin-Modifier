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
    [HarmonyPatch(typeof(Card), "GetHoverText2")]
    private static class CardGetHoverText2ItemMoreInfoPatch
    {
        private static void Postfix(Card __instance, ref string __result)
        {
            if (!ShouldShowItemMoreInfo() || !(__instance is Thing thing) || thing.isDestroyed)
                return;

            try
            {
                var details = BuildItemMoreInfoHoverDetails(thing);
                __result = (__result ?? "") + details;
                if (!string.IsNullOrEmpty(details))
                {
                    _npcMoreInfoExpectedHoverFrame = Time.frameCount;
                    _npcMoreInfoExpectedHoverBlock = details;
                }
            }
            catch { }
        }
    }
}
