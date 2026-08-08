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
    private static bool TryGetAbilityChanceOverride(Element element, out int value)
    {
        value = 0;
        try
        {
            if (Instance == null || element == null ||
                !Instance._abilityChanceOverrides.TryGetValue(element.id, out value))
                return false;
            if (value < 0)
            {
                value = 0;
                return false;
            }
            value = Clamp(value, 0, 100);
            return true;
        }
        catch { return false; }
    }
    private static bool TryGetAbilityPowerOverride(Element element, out int value)
    {
        value = 0;
        try
        {
            if (Instance == null || element == null ||
                !Instance._abilityPowerOverrides.TryGetValue(element.id, out value))
                return false;
            if (value < 0)
            {
                value = 0;
                return false;
            }
            value = Math.Max(0, value);
            return true;
        }
        catch { return false; }
    }
    private static bool TryGetAbilityCostOverride(Element element, out AbilityCostOverride? cost)
    {
        cost = null;
        try
        {
            if (Instance == null || element == null)
                return false;
            return Instance._abilityCostOverrides.TryGetValue(element.id, out cost);
        }
        catch { return false; }
    }
    private static bool HasEnoughAbilityCustomCost(Chara chara, AbilityCostOverride? cost)
    {
        if (chara == null || cost == null) return true;
        try
        {
            if (cost.Hp > 0 && chara.hp <= cost.Hp) return false;
            if (cost.Mp > 0 && chara.mana.value < cost.Mp) return false;
            if (cost.Sp > 0 && chara.stamina.value < cost.Sp) return false;
        }
        catch { }
        return true;
    }
    private static void ApplyAbilityCustomCost(Chara chara, AbilityCostOverride? cost)
    {
        if (chara == null || cost == null) return;
        try
        {
            if (cost.Hp > 0)
                chara.hp = Math.Max(1, chara.hp - cost.Hp);
            if (cost.Mp > 0)
                chara.mana.Mod(-cost.Mp);
            if (cost.Sp > 0)
                chara.stamina.Mod(-cost.Sp);
        }
        catch { }
    }
}
