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
    [HarmonyPatch(typeof(Card), "AddExp", new[] { typeof(int), typeof(bool) })]
    private static class CardCharacterLevelExperienceMultiplierPatch
    {
        private static void Prefix(Card __instance, ref int __0)
        {
            try
            {
                var instance = Instance;
                if (instance == null || !instance._modules.Progression.ExperienceMultiplierEnabled || !IsExperienceMultiplierTarget(__instance) || __0 <= 0)
                    return;
                __0 = ScalePositiveExperienceValue(__0, instance._modules.Progression.CharacterLevelExperienceMultiplier);
            }
            catch
            {
            }
        }
    }
    [HarmonyPatch(typeof(ElementContainer), "ModExp", new[] { typeof(int), typeof(float), typeof(bool) })]
    private static class ElementContainerSkillExperienceMultiplierPatch
    {
        private static void Prefix(ElementContainer __instance, int __0, ref float __1, bool __2, out bool __state)
        {
            __state = false;
            try
            {
                var instance = Instance;
                if (instance == null || !instance._modules.Progression.ExperienceMultiplierEnabled || __1 <= 0f)
                    return;
                var card = __instance?.Card;
                var element = __instance?.GetElement(__0);
                if (!IsExperienceMultiplierTarget(card) || element == null || element.source == null)
                    return;
                if (element.IsMainAttribute)
                {
                    if (!__2)
                        __1 *= instance._modules.Progression.MainAbilityExperienceMultiplier;
                    return;
                }
                if (_skillExperienceMultiplierDepth > 0)
                    return;
                if (IsMagicExperienceElement(element))
                    __1 *= instance._modules.Progression.MagicExperienceMultiplier;
                else if (string.Equals(element.source.category, "skill", StringComparison.OrdinalIgnoreCase))
                    __1 *= instance._modules.Progression.SkillExperienceMultiplier;
                else
                    return;
                _skillExperienceMultiplierDepth++;
                __state = true;
            }
            catch
            {
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state && _skillExperienceMultiplierDepth > 0)
                _skillExperienceMultiplierDepth--;
            return __exception;
        }
    }
}
