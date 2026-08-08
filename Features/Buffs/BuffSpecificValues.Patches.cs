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
    [HarmonyPatch(typeof(NotificationCondition), "OnRefresh")]
    private static class NotificationConditionSpecificValuesPatch
    {
        private static void Postfix(NotificationCondition __instance)
        {
            AppendBuffConditionSpecificValues(__instance);
        }
    }
    [HarmonyPatch(typeof(NotificationBuff), "OnRefresh")]
    private static class NotificationBuffSpecificValuesPatch
    {
        private static void Postfix(NotificationBuff __instance)
        {
            ApplyBuffIconSpecificInfo(__instance);
        }
    }
    [HarmonyPatch(typeof(BaseNotification), "Refresh")]
    private static class BaseNotificationBuffSpecificInfoPositionPatch
    {
        private static void Postfix(BaseNotification __instance)
        {
            var buff = __instance as NotificationBuff;
            if (buff != null)
            {
                PositionBuffIconSpecificInfo(buff);
                return;
            }

            if (__instance is NotificationCondition || __instance is NotificationStats)
                PositionBuffTextSpecificInfo(__instance);
        }
    }
    [HarmonyPatch(typeof(NotificationStats), "OnRefresh")]
    private static class NotificationStatsSpecificValuesPatch
    {
        private static void Postfix(NotificationStats __instance)
        {
            AppendBuffStatsSpecificValue(__instance);
        }
    }
}
