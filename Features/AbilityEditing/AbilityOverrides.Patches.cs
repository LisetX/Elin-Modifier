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
    [HarmonyPatch(typeof(Ability), "GetPower")]
    private static class AbilityGetPowerPatch
    {
        private static void Postfix(Ability __instance, ref int __result)
        {
            if (TryGetAbilityPowerOverride(__instance, out var value))
                __result = value;
        }
    }
    [HarmonyPatch(typeof(Chara), "CalcCastingChance")]
    private static class CharaCalcCastingChancePatch
    {
        private static void Postfix(Element __0, ref int __result)
        {
            if (TryGetAbilityChanceOverride(__0, out var value))
                __result = value;
        }
    }
    [HarmonyPatch(typeof(Element), "GetCost")]
    private static class ElementGetCostPatch
    {
        private static void Postfix(Element __instance, ref Act.Cost __result)
        {
            if (!TryGetAbilityCostOverride(__instance, out var cost) || cost == null)
                return;
            if (__result.type == Act.CostType.MP && cost.Mp < 0)
                return;
            if (__result.type == Act.CostType.SP && cost.Sp < 0)
                return;
            __result = default;
        }
    }
    [HarmonyPatch(typeof(Chara), "UseAbility", new[] { typeof(Act), typeof(Card), typeof(Point), typeof(bool) })]
    private static class CharaUseAbilityPatch
    {
        private static bool Prefix(Chara __instance, Act __0, ref bool __result)
        {
            if (!TryGetAbilityCostOverride(__0, out var cost))
                return true;
            if (HasEnoughAbilityCustomCost(__instance, cost))
                return true;
            __result = false;
            return false;
        }

        private static void Postfix(Chara __instance, Act __0, bool __result)
        {
            if (!__result || !TryGetAbilityCostOverride(__0, out var cost))
                return;
            ApplyAbilityCustomCost(__instance, cost);
        }
    }
}
