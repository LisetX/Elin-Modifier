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
    [HarmonyPatch(typeof(FoodEffect), "ProcNutrition")]
    private static class FoodEffectPotentialMultiplierContextPatch
    {
        private static void Prefix()
        {
            _foodPotentialMultiplierDepth++;
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (_foodPotentialMultiplierDepth > 0)
                _foodPotentialMultiplierDepth--;
            return __exception;
        }
    }
    [HarmonyPatch(typeof(FoodEffect), "Proc", new[] { typeof(Chara), typeof(Thing), typeof(bool) })]
    private static class FoodEffectRestorePlayerSpPatch
    {
        private static void Postfix(Chara __0, Thing __1, bool __2)
        {
            try
            {
                var instance = Instance;
                if (instance == null || !instance._modules.Progression.FoodRestoresSpEnabled || !__2 ||
                    __0 == null || !__0.IsPC || __0.isDead || __1 == null ||
                    string.Equals(__1.id, "bloodsample", StringComparison.OrdinalIgnoreCase))
                    return;

                var stamina = __0.stamina;
                var maximum = stamina.max;
                if (maximum <= 0 || stamina.value >= maximum)
                    return;

                var restored = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(1d, Math.Round(maximum * (double)instance._modules.Progression.FoodRestoresSpPercent / 100d, MidpointRounding.AwayFromZero)));
                stamina.Mod(restored);
            }
            catch
            {
            }
        }
    }
}
