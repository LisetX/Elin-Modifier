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
    [HarmonyPatch(typeof(ButtonGrid), "SetCard")]
    private static class ButtonGridSetCardPatch
    {
        private static void Postfix(ButtonGrid __instance, Card c)
        {
            ApplyFoodRotOverlay(__instance, __instance == null ? c : __instance.Card ?? c);
        }
    }
    [HarmonyPatch(typeof(ButtonGrid), "Reset")]
    private static class ButtonGridResetFoodRotOverlayPatch
    {
        private static void Postfix(ButtonGrid __instance)
        {
            SetFoodRotOverlayVisible(__instance, false);
        }
    }
    [HarmonyPatch(typeof(ButtonHotItem), "RefreshItem")]
    private static class ButtonHotItemRefreshItemFoodRotOverlayPatch
    {
        private static void Postfix(ButtonHotItem __instance)
        {
            ApplyFoodRotOverlay(__instance, null);
        }
    }
    [HarmonyPatch(typeof(ButtonHotItem), "Refresh")]
    private static class ButtonHotItemRefreshFoodRotOverlayPatch
    {
        private static void Postfix(ButtonHotItem __instance)
        {
            ApplyFoodRotOverlay(__instance, null);
        }
    }
    [HarmonyPatch(typeof(Card), "DecayNatural")]
    private static class CardDecayNaturalPatch
    {
        private static bool Prefix(Card __instance)
        {
            return !ShouldKeepFoodFresh(__instance);
        }
    }
    [HarmonyPatch(typeof(Card), "Decay")]
    private static class CardDecayPatch
    {
        private static bool Prefix(Card __instance)
        {
            return !ShouldKeepFoodFresh(__instance);
        }
    }
    [HarmonyPatch(typeof(Card), "set_decay")]
    private static class CardSetDecayPatch
    {
        private static void Postfix(Card __instance)
        {
            RefreshFoodRotOverlayForCard(__instance);
        }
    }
    [HarmonyPatch(typeof(Card), "Destroy")]
    private static class CardDestroyFoodRotOverlayPatch
    {
        private static void Postfix(Card __instance)
        {
            RefreshFoodRotOverlayForCard(__instance);
        }
    }
    [HarmonyPatch(typeof(Card), "get_IsDecayed")]
    private static class CardIsDecayedPatch
    {
        private static void Postfix(Card __instance, ref bool __result)
        {
            if (ShouldKeepFoodFresh(__instance))
                __result = false;
        }
    }
    [HarmonyPatch(typeof(Card), "get_IsRotting")]
    private static class CardIsRottingPatch
    {
        private static void Postfix(Card __instance, ref bool __result)
        {
            if (ShouldKeepFoodFresh(__instance))
                __result = false;
        }
    }
    [HarmonyPatch(typeof(Card), "get_IsFresn")]
    private static class CardIsFresnPatch
    {
        private static void Postfix(Card __instance, ref bool __result)
        {
            if (ShouldKeepFoodFresh(__instance))
                __result = true;
        }
    }
}
