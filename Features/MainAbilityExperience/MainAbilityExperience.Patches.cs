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
    [HarmonyPatch(typeof(Element), "WriteNote", new[] { typeof(UINote), typeof(ElementContainer), typeof(Action<UINote>) })]
    private static class ElementWriteNoteMainAbilityExperiencePatch
    {
        private static void Prefix(Element __instance, UINote __0, out bool __state)
        {
            __state = BeginMainAbilityExperienceTooltip(__instance, __0);
        }

        private static Exception? Finalizer(Exception? __exception, bool __state)
        {
            if (__state)
                ClearMainAbilityExperienceTooltip();
            return __exception;
        }
    }
    [HarmonyPatch(typeof(UINote), "AddTopic", new[] { typeof(string), typeof(string), typeof(string) })]
    private static class UINoteAddTopicMainAbilityExperiencePatch
    {
        private static void Prefix(UINote __instance, string __0, string __1)
        {
            AddMainAbilityExperienceBeforeCurrent(__instance, __0, __1);
        }

        private static void Postfix(UINote __instance, string __1, UIItem __result)
        {
            AlignMainAbilityExperienceWithCurrent(__instance, __1, __result);
        }
    }
    [HarmonyPatch(typeof(WidgetTracker), "Refresh")]
    private static class WidgetTrackerMainAbilityExperiencePatch
    {
        private static void Postfix(WidgetTracker __instance)
        {
            AppendMainAbilityExperienceToTracker(__instance);
        }
    }
}
