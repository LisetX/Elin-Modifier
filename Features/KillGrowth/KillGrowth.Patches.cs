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
    [HarmonyPatch(typeof(Chara), "Die", new[] { typeof(Element), typeof(Card), typeof(AttackSource), typeof(Chara) })]
    private static class CharaDieKillGrowthPatch
    {
        private static bool Prefix(Chara __instance, Card origin, out KillGrowthKillState __state)
        {
            if (IsInvincibleModeTarget(__instance))
            {
                __state = null;
                return false;
            }
            __state = CreateKillGrowthKillState(__instance, origin);
            return true;
        }

        private static void Postfix(Chara __instance, KillGrowthKillState __state)
        {
            AwardKillGrowthExperience(__state, __instance);
        }
    }
    [HarmonyPatch(typeof(ElementContainerCard), "ValueBonus")]
    private static class ElementContainerCardKillGrowthBonusPatch
    {
        private static void Postfix(ElementContainerCard __instance, Element e, ref int __result)
        {
            try
            {
                var owner = __instance == null ? null : __instance.owner;
                var chara = owner == null || !owner.isChara ? null : owner.Chara;
                __result += GetKillGrowthAttributeBonus(chara, e == null ? 0 : e.id);
            }
            catch { }
        }
    }
}
