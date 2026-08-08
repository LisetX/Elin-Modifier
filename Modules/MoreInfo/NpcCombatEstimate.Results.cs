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
using static ElinModifierPlugin;

internal sealed partial class MoreInfoModule
{
    private static double EstimateNpcMoreInfoEquipmentPower(Chara chara)
    {
        var total = 0.0;
        try
        {
            foreach (var thing in EnumerateNpcMoreInfoEquippedThings(chara))
            {
                total += Math.Max(0, SafeInt(() => thing.encLV, 0)) * 1.5;
                total += Math.Max(0, SafeInt(() => thing.rarityLv, 0));
                total += Math.Max(0, GetThingElementBase(thing, 64)) * 0.2;
                total += Math.Max(0, GetThingElementBase(thing, 65)) * 0.25;
                total += Math.Max(0, GetThingElementBase(thing, 66)) * 0.15;
                total += Math.Max(0, GetThingElementBase(thing, 67)) * 0.2;
            }
        }
        catch { }
        return total;
    }
    private static int EstimateNpcMoreInfoPenetration(Chara chara)
    {
        var penetration = Math.Max(0, SafeInt(() => chara.Evalue(92), 0));
        try
        {
            foreach (var thing in EnumerateNpcMoreInfoEquippedThings(chara))
            {
                if (SafeInt(() => thing.IsWeapon ? 1 : 0, 0) == 0)
                    continue;
                penetration = Math.Max(penetration, SafeInt(() => thing.Penetration, 0) + SafeInt(() => chara.Evalue(92), 0));
            }
        }
        catch { }
        return Clamp(penetration, 0, 100);
    }
    private static IEnumerable<Thing> EnumerateNpcMoreInfoEquippedThings(Chara chara)
    {
        var seen = new List<Thing>();
        if (chara == null || chara.body?.slots == null)
            yield break;

        foreach (var slot in chara.body.slots)
        {
            Thing thing = null;
            try { thing = slot?.thing; }
            catch { thing = null; }
            if (thing == null || seen.Contains(thing))
                continue;
            seen.Add(thing);
            yield return thing;
        }
    }
    private static IEnumerable<Thing> EnumerateNpcMoreInfoMeleeWeapons(Chara chara)
    {
        var seen = new List<Thing>();
        if (chara == null || chara.body?.slots == null)
            yield break;

        foreach (var slot in chara.body.slots)
        {
            Thing thing = null;
            try { thing = slot?.thing; }
            catch { thing = null; }
            if (thing == null || seen.Contains(thing))
                continue;
            if (SafeInt(() => slot != null && slot.elementId == 35 && thing.IsWeapon && !thing.IsRangedWeapon ? 1 : 0, 0) == 0)
                continue;
            seen.Add(thing);
            yield return thing;
        }
    }
    private static IEnumerable<Thing> EnumerateNpcMoreInfoRangedWeapons(Chara chara)
    {
        var seen = new List<Thing>();
        if (chara == null)
            yield break;

        if (chara.body?.slots != null)
        {
            foreach (var slot in chara.body.slots)
            {
                Thing thing = null;
                try { thing = slot?.thing; }
                catch { thing = null; }
                if (thing == null || seen.Contains(thing))
                    continue;
                if (SafeInt(() => thing.IsRangedWeapon ? 1 : 0, 0) == 0)
                    continue;
                seen.Add(thing);
                yield return thing;
            }
        }

        var ranged = SafeObject(() => chara.ranged) as Thing;
        if (ranged != null && !seen.Contains(ranged) && SafeInt(() => ranged.IsRangedWeapon ? 1 : 0, 0) != 0)
            yield return ranged;
    }
    private static IEnumerable<Thing> EnumerateNpcMoreInfoAttackWeapons(Chara chara)
    {
        var seen = new List<Thing>();
        if (chara == null || chara.body?.slots == null)
            yield break;

        foreach (var slot in chara.body.slots)
        {
            Thing thing = null;
            try { thing = slot?.thing; }
            catch { thing = null; }
            if (thing == null || seen.Contains(thing))
                continue;
            var isWeapon = SafeInt(() => thing.IsWeapon ? 1 : 0, 0) != 0;
            if (!isWeapon)
                continue;
            var isHandWeapon = SafeInt(() => slot != null && slot.elementId == 35 ? 1 : 0, 0) != 0;
            var isRangedWeapon = SafeInt(() => thing.IsRangedWeapon ? 1 : 0, 0) != 0;
            if (!isHandWeapon && !isRangedWeapon)
                continue;
            seen.Add(thing);
            yield return thing;
        }
    }
    private static double EstimateNpcMoreInfoWinChance(NpcCombatEstimate attacker, NpcCombatEstimate defender)
    {
        var attackerTtk = EstimateNpcMoreInfoTimeToKill(attacker);
        var defenderTtk = EstimateNpcMoreInfoTimeToKill(defender);
        if (attackerTtk >= 999999.0 && defenderTtk >= 999999.0)
            return 50.0;

        var roundsRatio = Math.Log(defenderTtk / attackerTtk);
        var hitBias = attacker.HitChance - defender.HitChance;
        var score = roundsRatio * 2.4 + hitBias * 0.8;
        score = ClampDouble(score, -20.0, 20.0);
        var chance = 100.0 / (1.0 + Math.Exp(-score));
        return ClampDouble(chance, 0.01, 99.99);
    }
    private static double EstimateNpcMoreInfoTimeToKill(NpcCombatEstimate estimate)
    {
        if (estimate.ExpectedDamagePerRound <= 0.0)
            return 999999.0;
        return Math.Max(0.01, estimate.TargetHp / estimate.ExpectedDamagePerRound);
    }
    private static string FormatNpcMoreInfoChance(double chance)
    {
        return ClampDouble(chance, 0.0, 100.0).ToString("0.##", CultureInfo.InvariantCulture);
    }
    private static string FormatNpcMoreInfoRounds(int rounds)
    {
        rounds = Clamp(rounds, 1, 999);
        return rounds.ToString(CultureInfo.InvariantCulture) + Tr("轮", " rounds");
    }
    private static double ClampDouble(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
    private static object SafeObject(Func<object> getter)
    {
        try { return getter(); }
        catch { return null; }
    }
    private static string GetNpcMoreInfoAbilityName(Act act)
    {
        var name = SafeText(() => act.Name, "");
        if (!string.IsNullOrEmpty(name))
            return name;
        name = SafeText(() => act.source.GetName(), "");
        if (!string.IsNullOrEmpty(name))
            return name;
        name = SafeText(() => act.source.alias, "");
        if (!string.IsNullOrEmpty(name))
            return name;
        var id = SafeInt(() => act.id, 0);
        return id > 0 ? id.ToString(CultureInfo.InvariantCulture) : "";
    }
    private static int GetNpcMoreInfoAbilityLevel(Chara chara, Act act)
    {
        var level = 0;
        try
        {
            var element = GetElements(chara).GetElement(act.id);
            if (element != null)
                level = Math.Max(0, element.ValueWithoutLink);
        }
        catch { }

        if (level <= 0)
            level = Math.Max(0, SafeInt(() => act.source.LV, 0));
        return level;
    }
    internal static string ColorNpcMoreInfoText(string text, string color)
    {
        return "<color=" + color + ">" + text + "</color>";
    }
    private static string GetNpcMoreInfoGender(Chara chara)
    {
        var gender = SafeInt(() => chara.bio.gender, -1);
        if (gender == 2) return Tr("男", "Male");
        if (gender == 1) return Tr("女", "Female");
        if (gender == 0) return Tr("无性", "Neutral");
        return "?";
    }
    private static string GetNpcMoreInfoAge(Chara chara)
    {
        var age = SafeInt(() => chara.bio.GetAge(chara), -1);
        if (age < 0)
            return Tr("???岁", "??? years old");
        return age.ToString(CultureInfo.InvariantCulture) + Tr("岁", " years old");
    }
    private static string GetNpcMoreInfoAttackStyle(Chara chara)
    {
        try
        {
            switch (chara.body.GetAttackStyle())
            {
                case AttackStyle.TwoHand:
                    return Tr("双手武器", "Two-hand");
                case AttackStyle.TwoWield:
                    return Tr("双持武器", "Dual wield");
                case AttackStyle.Shield:
                    return Tr("盾牌风格", "Shield");
                default:
                    return Tr("普通风格", "Default");
            }
        }
        catch
        {
            return "?";
        }
    }
    private static string GetNpcMoreInfoArmorStyle(Chara chara)
    {
        var armorSkill = SafeInt(() => chara.GetArmorSkill(), 0);
        if (armorSkill != 0)
        {
            var name = SafeText(() => GameAccess.Sources.Elements.map[armorSkill].GetName(), "");
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        if (armorSkill == 122) return Tr("重甲", "Heavy armor");
        if (armorSkill == 120) return Tr("轻甲", "Light armor");
        return "?";
    }
    private static string GetNpcMoreInfoResistRank(int value)
    {
        var level = SafeInt(() => Element.GetResistLv(value), 0);
        if (level <= -2) return Tr("致命弱点", "Fatal weakness");
        if (level == -1) return Tr("弱点", "Weakness");
        if (level == 0) return Tr("普通", "Normal");
        if (level == 1) return Tr("抗性", "Resistant");
        if (level == 2) return Tr("强抗性", "Strong resistance");
        if (level == 3) return Tr("极强抗性", "Extreme resistance");
        return Tr("免疫", "Immune");
    }
    private static string JoinNpcMoreInfoParts(List<string> parts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part))
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(part);
        }
        return sb.ToString();
    }
}
