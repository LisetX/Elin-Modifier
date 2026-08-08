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
    [HarmonyPatch(typeof(InvOwner), "ListInteractions", new[] { typeof(ButtonGrid), typeof(bool) })]
    private static class InvOwnerListInteractionsPatch
    {
        private static void Postfix(InvOwner __instance, ButtonGrid __0, bool __1, InvOwner.ListInteraction __result)
        {
            if (!__1 || !IsPlayerInventoryOwner(__instance))
                return;
            AddGeneEditorInteraction(__result, __0);
            AddWeaponEditorInteraction(__result, __0);
            AddCustomItemAmountInteraction(__result, __0);
            AddRodStackingInteraction(__result, __0);
            AddFoodEditorInteraction(__result, __0);
            AddItemDataEditorInteraction(__result, __0);
        }
    }
    [HarmonyPatch(typeof(TraitStethoscope), "TrySetHeldAct")]
    private static class TraitStethoscopeTrySetHeldActPatch
    {
        private static bool Prefix(TraitStethoscope __instance, ActPlan p)
        {
            if (!ShouldStethoscopeNoTargetLimit())
                return true;
            RegisterUnlimitedStethoscopeActs(__instance, p);
            return false;
        }
    }
}
