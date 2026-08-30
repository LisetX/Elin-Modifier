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
    [HarmonyPatch(typeof(Thing), "WriteNote")]
    private static class ThingWriteNotePatch
    {
        private static void Prefix(Thing __instance, UINote n)
        {
            BeginItemPanelValueWrite(__instance, n);
        }

        private static void Postfix(Thing __instance, UINote n)
        {
            try
            {
                AppendItemPanelMilkBonus(__instance, n);
                if (Instance != null && Instance._showFoodRot && ShouldShowFoodRot(__instance) && n != null)
                {
                    try { n.AddText(GetFoodRotText(__instance), FontColor.FoodMisc); }
                    catch { }
                }
            }
            finally
            {
                ClearItemPanelValueWrite();
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            ClearItemPanelValueWrite();
            return __exception;
        }
    }
    [HarmonyPatch(typeof(UINote), "AddHeaderCard")]
    private static class UINoteAddHeaderCardItemPanelValuePatch
    {
        private static void Postfix(UINote __instance, UIItem __result)
        {
            CaptureItemPanelValueHeader(__instance, __result);
        }
    }
}
