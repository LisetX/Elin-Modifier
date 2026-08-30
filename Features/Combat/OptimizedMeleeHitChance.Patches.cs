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
    [HarmonyPatch(typeof(AttackProcess), "CalcHit")]
    private static class AttackProcessOptimizedMeleeHitChancePatch
    {
        private static bool Prefix(AttackProcess __instance, ref bool __result)
        {
            try
            {
                var instance = Instance;
                var attacker = __instance?.CC;
                if (instance == null || !instance._modules.Progression.OptimizeMeleeHitChance || attacker == null ||
                    __instance.IsRanged || __instance.isThrow ||
                    (!attacker.IsPC && (!instance._modules.Progression.OptimizeMeleeHitChanceIncludeParty || !attacker.IsPCParty)))
                    return true;

                __result = CalculateOptimizedMeleeHit(__instance);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
    private static bool CalculateOptimizedMeleeHit(AttackProcess attack) =>
        ProgressionModule.CalculateOptimizedMeleeHit(attack);
}
