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
    [HarmonyPatch(typeof(ElementContainer), "ModPotential")]
    private static class ElementContainerFoodPotentialMultiplierPatch
    {
        private static void Prefix(ElementContainer __instance, ref int __1)
        {
            try
            {
                var instance = Instance;
                if (instance == null || !instance._modules.Progression.ExperienceMultiplierEnabled ||
                    !IsExperienceMultiplierTarget(__instance?.Card) ||
                    _foodPotentialMultiplierDepth <= 0 || __1 <= 0)
                    return;
                __1 = ScalePositiveExperienceValue(__1, instance._modules.Progression.FoodPotentialGainMultiplier);
            }
            catch
            {
            }
        }
    }
    [HarmonyPatch(typeof(ElementContainer), "ModTempPotential")]
    private static class ElementContainerTemporaryPotentialMultiplierPatch
    {
        private static void Prefix(ElementContainer __instance, ref int __1, int __2)
        {
            try
            {
                var instance = Instance;
                if (instance == null || !instance._modules.Progression.ExperienceMultiplierEnabled ||
                    !IsExperienceMultiplierTarget(__instance?.Card) || __1 <= 0)
                    return;
                if (_foodPotentialMultiplierDepth > 0)
                    __1 = ScalePositiveExperienceValue(__1, instance._modules.Progression.FoodPotentialGainMultiplier);
                else if (__2 == 9999)
                    __1 = ScalePositiveExperienceValue(__1, instance._modules.Progression.TrainingPotentialGainMultiplier);
            }
            catch
            {
            }
        }
    }
    [HarmonyPatch(typeof(ElementContainer), "Train")]
    private static class ElementContainerTrainingPotentialMultiplierPatch
    {
        private static void Prefix(ElementContainer __instance, ref int __1)
        {
            try
            {
                var instance = Instance;
                if (instance == null || !instance._modules.Progression.ExperienceMultiplierEnabled ||
                    !IsExperienceMultiplierTarget(__instance?.Card) || __1 <= 0)
                    return;
                __1 = ScalePositiveExperienceValue(__1, instance._modules.Progression.TrainingPotentialGainMultiplier);
            }
            catch
            {
            }
        }
    }
}
