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
    private static double EstimateNpcMoreInfoResistDamage(double damage, int elementId, Chara defender, int penetrationLevel)
    {
        try
        {
            var row = FindSourceElementRowById(elementId);
            var aliasRef = GetString(row, "aliasRef");
            if (string.IsNullOrEmpty(aliasRef))
                return damage;
            var resist = SafeInt(() => defender.Evalue(aliasRef), 0);
            var level = SafeInt(() => Element.GetResistLv(resist), 0);
            if (penetrationLevel > 0 && level > 0)
                level = Math.Max(level - penetrationLevel, 0);
            if (level >= 4) return 0.0;
            if (level == 3) return damage / 4.0;
            if (level == 2) return damage / 3.0;
            if (level == 1) return damage / 2.0;
            if (level == 0) return damage;
            if (level == -1) return damage * 3.0 / 2.0;
            return damage * 2.0;
        }
        catch
        {
            return damage;
        }
    }
    private static int GetNpcMoreInfoResistPenetrationLevel(int elementId, AttackSource attackSource, Chara attacker, Chara defender)
    {
        var level = SafeInt(() => attacker.Evalue(1238), 0);
        if (attackSource == AttackSource.MagicSword)
        {
            level += 2;
            if (SafeInt(() => attacker.HasElement(1247) ? 1 : 0, 0) != 0)
                level++;
        }
        if (attackSource == AttackSource.MagicArrow && SafeInt(() => attacker.HasElement(1244) ? 1 : 0, 0) != 0)
            level++;
        if (attackSource == AttackSource.MagicHand && SafeInt(() => attacker.HasElement(1246) ? 1 : 0, 0) != 0)
            level++;
        if (elementId == 916 && (SafeInt(() => defender.HasElement(1253) ? 1 : 0, 0) != 0 || SafeInt(() => attacker.HasElement(1253) ? 1 : 0, 0) != 0))
            level++;
        return Math.Max(0, level);
    }
    private static double EstimateNpcMoreInfoBaneMultiplier(Chara attacker, Chara defender, Thing weapon)
    {
        if (defender == null)
            return 1.0;
        var bane = 0;
        AddBane(true, 468, 50);
        AddBane(SafeInt(() => defender.IsUndead ? 1 : 0, 0) != 0, 461, 100);
        AddBane(SafeInt(() => defender.IsAnimal ? 1 : 0, 0) != 0, 463, 100);
        AddBane(SafeInt(() => defender.IsHuman ? 1 : 0, 0) != 0, 464, 100);
        AddBane(SafeInt(() => defender.IsDragon ? 1 : 0, 0) != 0, 460, 100);
        AddBane(SafeInt(() => defender.IsGod ? 1 : 0, 0) != 0, 466, 100);
        AddBane(SafeInt(() => defender.IsMachine ? 1 : 0, 0) != 0, 465, 100);
        AddBane(SafeInt(() => defender.IsFish ? 1 : 0, 0) != 0, 467, 100);
        AddBane(SafeInt(() => defender.IsFairy ? 1 : 0, 0) != 0, 462, 100);
        return bane == 0 ? 1.0 : (100.0 + bane * 3.0) / 100.0;

        void AddBane(bool valid, int id, int mod)
        {
            if (!valid)
                return;
            bane += (SafeInt(() => attacker.Evalue(id), 0) + (weapon == null ? 0 : SafeInt(() => weapon.Evalue(id, true), 0))) * mod / 100;
        }
    }
    private static double EstimateNpcMoreInfoWeaponEnchantExpectedDamage(double primaryDamageBeforeDamageHp, Chara attacker, Chara defender, Thing weapon, bool isRanged, bool isMartial)
    {
        if (primaryDamageBeforeDamageHp <= 0.0 || attacker == null || defender == null)
            return 0.0;

        var expected = 0.0;
        try
        {
            foreach (var element in EnumerateNpcMoreInfoAttackElements(attacker, weapon, isRanged, isMartial))
            {
                if (element == null || element.Value <= 0)
                    continue;
                if (SafeInt(() => element.IsActive(weapon) ? 1 : 0, 0) == 0)
                    continue;
                if (!string.Equals(SafeText(() => element.source.categorySub, ""), "eleAttack", StringComparison.OrdinalIgnoreCase))
                    continue;

                var upper = primaryDamageBeforeDamageHp * (100.0 + element.Value * 10.0) / 500.0 + 5.0;
                var procRaw = Math.Max(0.0, (upper - 1.0) / 2.0);
                procRaw = procRaw * (100.0 + EstimateNpcMoreInfoTwoHandEncBonus(attacker, weapon)) / 100.0;
                var power = isRanged ? 30 + element.Value : 30 + element.Value;
                expected += 0.25 * EstimateNpcMoreInfoApplyDamageHpExpected(procRaw, element.id, power, AttackSource.WeaponEnchant, attacker, defender);
            }
        }
        catch { }
        return expected;
    }
    private static IEnumerable<Element> EnumerateNpcMoreInfoAttackElements(Chara attacker, Thing weapon, bool isRanged, bool isMartial)
    {
        if (weapon != null && !(weapon.trait is TraitToolRangeCane))
        {
            foreach (var element in EnumerateNpcMoreInfoElementValues(weapon))
                yield return element;

            var ammo = SafeObject(() => weapon.ammoData) as Thing;
            if (ammo != null && !(ammo.trait is TraitAmmoTalisman) && SafeInt(() => attacker.HasCondition<ConReload>() ? 1 : 0, 0) == 0)
            {
                foreach (var element in EnumerateNpcMoreInfoElementValues(ammo))
                    yield return element;
            }
        }

        try
        {
            foreach (var element in attacker.elements.dict.Values)
            {
                if (element == null || element.IsGlobalElement)
                    continue;
                if (string.Equals(SafeText(() => element.source.categorySub, ""), "eleAttack", StringComparison.OrdinalIgnoreCase))
                    yield return element;
            }
        }
        finally { }

        if (SafeInt(() => attacker.IsPCFaction ? 1 : 0, 0) != 0)
        {
            foreach (var element in EnumerateNpcMoreInfoElementValues(SafeObject(() => GameAccess.Characters.PlayerCharacter.faction.charaElements)))
                yield return element;
        }
    }
    private static IEnumerable<Element> EnumerateNpcMoreInfoElementValues(object owner)
    {
        if (owner == null)
            yield break;
        IDictionary dict = null;
        try { dict = GetMemberValue(owner, "dict") as IDictionary; }
        catch { dict = null; }
        if (dict == null)
            yield break;
        foreach (var value in dict.Values)
        {
            if (value is Element element)
                yield return element;
        }
    }
    private static int GetNpcMoreInfoWeaponEnc(Chara attacker, Thing weapon, int elementId, bool addSelfEnc)
    {
        var value = weapon == null ? 0 : SafeInt(() => weapon.Evalue(elementId), 0);
        if (SafeInt(() => attacker.IsPCFactionOrMinion ? 1 : 0, 0) != 0)
            value += SafeInt(() => GameAccess.Characters.PlayerCharacter.faction.charaElements.Value(elementId), 0);
        value = value * (100 + EstimateNpcMoreInfoTwoHandEncBonus(attacker, weapon)) / 100;
        if (addSelfEnc)
            value += SafeInt(() => attacker.Evalue(elementId), 0);
        return value;
    }
    private static int EstimateNpcMoreInfoTwoHandEncBonus(Chara attacker, Thing weapon)
    {
        if (attacker == null || weapon == null)
            return 0;
        if (SafeInt(() => attacker.body.GetAttackStyle() == AttackStyle.TwoHand && weapon.IsWeapon ? 1 : 0, 0) == 0)
            return 0;
        return Clamp(SafeInt(() => attacker.Evalue(130), 0) / 15, 0, 2) * 25;
    }
    private static double EstimateNpcMoreInfoAbilityDamage(Chara attacker, Chara defender, NpcCombatEstimateStats attackerStats)
    {
        var best = 0.0;
        try
        {
            var items = attacker.ability?.list?.items;
            if (items == null)
                return 0.0;

            foreach (var item in items)
            {
                if (item == null || item.act == null)
                    continue;
                var act = item.act;
                if (!IsNpcMoreInfoOffensiveAbility(act))
                    continue;

                var level = GetNpcMoreInfoAbilityLevel(attacker, act);
                var chance = SafeInt(() => item.chance, 0);
                if (chance <= 0)
                    chance = SafeInt(() => act.source.chance, 0);
                chance = Clamp(chance <= 0 ? 30 : chance, 1, 100);

                var power = SafeInt(() => act.GetPower(attacker), 0);
                if (power <= 0)
                    power = SafeInt(() => act.source.value, 0);
                if (power <= 0)
                    power = level * 3;

                var damage = Math.Max(1.0, power * 0.85 + level * 0.65 +
                                            attackerStats.Mag * 0.28 + attackerStats.Wil * 0.18 +
                                            attackerStats.Level * 0.12);
                var elementId = GetNpcMoreInfoElementIdFromAlias(SafeText(() => act.source.aliasRef, ""));
                damage = EstimateNpcMoreInfoApplyDamageHpExpected(damage, elementId, power, EstimateNpcMoreInfoAbilityAttackSource(act), attacker, defender);
                damage *= chance / 100.0;
                if (damage > best)
                    best = damage;
            }
        }
        catch { }
        return best;
    }
    private static AttackSource EstimateNpcMoreInfoAbilityAttackSource(Act act)
    {
        try
        {
            var proc = GetStringArray(act.source, "proc");
            var id = proc.Length > 0 ? proc[0] : "";
            if (TextHas(id, "Arrow")) return AttackSource.MagicArrow;
            if (TextHas(id, "Hand")) return AttackSource.MagicHand;
            if (TextHas(id, "Sword")) return AttackSource.MagicSword;
            if (TextHas(id, "MoonSpear") || TextHas(id, "MoonArrow")) return AttackSource.MoonSpear;
        }
        catch { }
        return AttackSource.None;
    }
    private static bool IsNpcMoreInfoOffensiveAbility(Act act)
    {
        try
        {
            var types = GetStringArray(act.source, "abilityType");
            foreach (var type in types)
            {
                if (TextHas(type, "attack") || TextHas(type, "dot") || TextHas(type, "debuff"))
                    return true;
                if (TextHas(type, "heal") || TextHas(type, "buff") || TextHas(type, "summon"))
                    return false;
            }

            var proc = GetStringArray(act.source, "proc");
            foreach (var value in proc)
            {
                if (TextHas(value, "damage") || TextHas(value, "attack") || TextHas(value, "ball") ||
                    TextHas(value, "bolt") || TextHas(value, "breath") || TextHas(value, "touch"))
                    return true;
            }

            var category = GetString(act.source, "category");
            var categorySub = GetString(act.source, "categorySub");
            if (TextHas(category, "spell") || TextHas(categorySub, "attack") || TextHas(categorySub, "eleAttack"))
                return true;
        }
        catch { }
        return false;
    }
    private static double EstimateNpcMoreInfoAbilityResistFactor(Chara defender, Act act)
    {
        var alias = SafeText(() => act.source.aliasRef, "");
        var elementId = GetNpcMoreInfoElementIdFromAlias(alias);
        if (elementId <= 0)
            return 1.0;

        var resist = SafeInt(() => defender.Evalue(elementId), 0);
        var resistLevel = SafeInt(() => Element.GetResistLv(resist), 0);
        if (resistLevel <= -2) return 1.5;
        if (resistLevel == -1) return 1.25;
        if (resistLevel == 0) return 1.0;
        if (resistLevel == 1) return 0.82;
        if (resistLevel == 2) return 0.65;
        if (resistLevel == 3) return 0.45;
        return 0.25;
    }
    private static int GetNpcMoreInfoElementIdFromAlias(string alias)
    {
        if (string.IsNullOrEmpty(alias))
            return 0;
        try
        {
            if (GameAccess.Sources.Elements?.alias != null &&
                GameAccess.Sources.Elements.alias.TryGetValue(alias, out var row) &&
                row != null)
                return row.id;
        }
        catch { }
        try
        {
            var row = FindSourceElementRowByAlias(alias);
            return row == null ? 0 : GetInt(row, "id");
        }
        catch
        {
            return 0;
        }
    }
    private static double EstimateNpcMoreInfoWeaponAverageDamage(Chara chara, NpcCombatEstimateStats stats)
    {
        var total = 0.0;
        var index = 0;
        try
        {
            foreach (var thing in EnumerateNpcMoreInfoEquippedThings(chara))
            {
                var isWeapon = SafeInt(() => thing.IsWeapon ? 1 : 0, 0) != 0;
                if (!isWeapon)
                    continue;

                var offense = SafeObject(() => thing.source.offense) as int[];
                var diceNum = offense != null && offense.Length > 0 ? Math.Max(1, offense[0]) : 1;
                var diceDim = Math.Max(1, SafeInt(() => thing.c_diceDim, offense != null && offense.Length > 1 ? offense[1] : 1));
                var average = diceNum * (diceDim + 1) / 2.0 +
                              SafeInt(() => thing.DMG, 0) +
                              SafeInt(() => thing.encLV, 0) +
                              SafeInt(() => thing.rarityLv, 0) * 0.5;
                average += EstimateNpcMoreInfoWeaponElementBonus(thing);
                total += average * (index == 0 ? 1.0 : 0.65);
                index++;
            }
        }
        catch { }

        if (total > 0.0)
            return total;

        var martial = Math.Max(0, SafeInt(() => chara.Evalue(100), 0));
        return 2.0 * (5.0 + Math.Sqrt(Math.Max(0, martial / 3.0)) + 1.0) / 2.0 +
               Math.Sqrt(Math.Max(0, stats.Str + martial));
    }
    private static double EstimateNpcMoreInfoWeaponElementBonus(Thing thing)
    {
        var bonus = 0.0;
        try
        {
            foreach (var element in thing.elements.dict.Values)
            {
                if (element == null || element.Value <= 0)
                    continue;
                var categorySub = SafeText(() => element.source.categorySub, "");
                if (string.Equals(categorySub, "eleAttack", StringComparison.OrdinalIgnoreCase))
                    bonus += Math.Sqrt(element.Value) * 1.8;
                else if (string.Equals(categorySub, "eleConvert", StringComparison.OrdinalIgnoreCase))
                    bonus += Math.Sqrt(element.Value);
            }
        }
        catch { }
        return bonus;
    }
}
