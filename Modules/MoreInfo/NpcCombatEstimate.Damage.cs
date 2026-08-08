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
    private static NpcCombatEstimateStats BuildNpcMoreInfoCombatStats(Chara chara)
    {
        var stats = new NpcCombatEstimateStats();
        stats.Level = Math.Max(1, SafeInt(() => chara.LV, 1));
        stats.CurrentHp = Math.Max(1, SafeInt(() => chara.hp, 1));
        stats.MaxHp = Math.Max(stats.CurrentHp, SafeInt(() => chara.MaxHP, stats.CurrentHp));
        stats.Speed = Math.Max(10, SafeInt(() => chara.Speed, 100));
        stats.Hit = SafeInt(() => chara.HIT, 0);
        stats.DamageBonus = SafeInt(() => chara.DMG, 0);
        stats.DV = SafeInt(() => chara.DV, 0);
        stats.PV = SafeInt(() => chara.PV, 0);
        stats.Str = SafeInt(() => chara.STR, SafeInt(() => chara.Evalue(70), 0));
        stats.End = SafeInt(() => chara.END, SafeInt(() => chara.Evalue(71), 0));
        stats.Dex = SafeInt(() => chara.DEX, SafeInt(() => chara.Evalue(72), 0));
        stats.Per = SafeInt(() => chara.PER, SafeInt(() => chara.Evalue(73), 0));
        stats.Wil = SafeInt(() => chara.WIL, SafeInt(() => chara.Evalue(75), 0));
        stats.Mag = SafeInt(() => chara.MAG, SafeInt(() => chara.Evalue(76), 0));
        stats.MainTotal = Math.Max(1, stats.Str + stats.End + stats.Dex + stats.Per +
                                      SafeInt(() => chara.Evalue(74), 0) + stats.Wil + stats.Mag +
                                      SafeInt(() => chara.Evalue(77), 0));
        stats.WeaponSkill = Math.Max(0, SafeInt(() => chara.GetFavWeaponSkill().Value, 0));
        stats.CombatSkill = Math.Max(Math.Max(SafeInt(() => chara.Evalue(132), 0), SafeInt(() => chara.Evalue(133), 0)),
                                     SafeInt(() => chara.Evalue(304), 0));
        stats.ArmorSkill = Math.Max(0, SafeInt(() => chara.Evalue(chara.GetArmorSkill()), 0));
        stats.WeaponAverageDamage = EstimateNpcMoreInfoWeaponAverageDamage(chara, stats);
        stats.EquipmentPower = EstimateNpcMoreInfoEquipmentPower(chara);
        stats.Penetration = EstimateNpcMoreInfoPenetration(chara);
        return stats;
    }
    private static double EstimateNpcMoreInfoHitChance(NpcCombatEstimateStats attacker, NpcCombatEstimateStats defender)
    {
        var toHit = 50.0 + attacker.Hit + attacker.Dex * 0.45 + attacker.Per * 0.18 +
                    attacker.Level * 0.35 + attacker.WeaponSkill * 0.65 + attacker.CombatSkill * 0.25 +
                    attacker.EquipmentPower * 0.08;
        var evasion = 25.0 + defender.DV + defender.Per / 3.0 + defender.Dex * 0.12 +
                      defender.Level * 0.18 + defender.CombatSkill * 0.12;
        if (toHit <= 0)
            return 0.05;
        if (evasion <= 0)
            return 0.95;
        var chance = toHit / (toHit + evasion * 1.25);
        return ClampDouble(chance, 0.05, 0.95);
    }
    private static double EstimateNpcMoreInfoPhysicalDamage(NpcCombatEstimateStats attacker, NpcCombatEstimateStats defender)
    {
        var raw = attacker.WeaponAverageDamage + attacker.DamageBonus + attacker.Str * 0.35 +
                  attacker.Dex * 0.12 + attacker.Per * 0.08 + attacker.Level * 0.22 +
                  attacker.WeaponSkill * 0.28 + attacker.CombatSkill * 0.18 +
                  attacker.EquipmentPower * 0.05;
        raw = Math.Max(1.0, raw);
        var protection = Math.Max(0.0, defender.PV + defender.ArmorSkill + defender.Dex / 10.0);
        var penetratedProtection = protection * (100.0 - ClampDouble(attacker.Penetration, 0.0, 100.0)) / 100.0;
        var mitigated = raw * 100.0 / Math.Max(100.0 + penetratedProtection, 1.0);
        mitigated -= penetratedProtection / 12.0;
        return Math.Max(0.1, mitigated);
    }
    private static int EstimateNpcMoreInfoDistanceModifier(Chara attacker, Chara defender, Thing weapon, bool isRanged)
    {
        if (!isRanged || weapon == null)
            return 100;
        try
        {
            if (!(weapon.trait is TraitToolRange toolRange) || attacker.pos == null || defender.pos == null)
                return 100;
            var distance = attacker.pos.Distance(defender.pos);
            return Math.Max(80, 115 - 10 * Math.Abs(distance - toolRange.BestDist) * 100 / Math.Max(100 + weapon.Evalue(605) * 10, 1));
        }
        catch
        {
            return 100;
        }
    }
    private static int EstimateNpcMoreInfoWeaponSkillId(Thing weapon, bool isRanged, bool isCane)
    {
        if (weapon == null)
            return 100;
        try
        {
            if (isCane || weapon.Evalue(482) > 0)
                return 305;
        }
        catch { }
        try
        {
            if (isRanged && weapon.trait is TraitToolRange toolRange)
                return toolRange.WeaponSkill == null ? 0 : toolRange.WeaponSkill.id;
        }
        catch { }
        return Math.Max(0, SafeInt(() => weapon.category.skill, 100));
    }
    private static int GetNpcMoreInfoElementParentValue(Chara chara, int elementId)
    {
        if (chara == null || elementId <= 0)
            return 0;
        try
        {
            if (GameAccess.Sources.Elements?.map == null || !GameAccess.Sources.Elements.map.TryGetValue(elementId, out var row) || row == null)
                return 0;
            var aliasParent = SafeText(() => row.aliasParent, "");
            if (string.IsNullOrEmpty(aliasParent) || GameAccess.Sources.Elements.alias == null ||
                !GameAccess.Sources.Elements.alias.TryGetValue(aliasParent, out var parentRow) || parentRow == null)
                return 0;
            return SafeInt(() => chara.Evalue(parentRow.id), 0);
        }
        catch
        {
            return 0;
        }
    }
    private static long SafeLongCurve(int value, int a, int b)
    {
        try { return GameAccess.Random.Curve(value, a, b); }
        catch { return value; }
    }
    private static double EstimateNpcMoreInfoPreparedHitChance(long toHit, long evasion, bool isRanged, Chara attacker, Chara defender, Thing weapon)
    {
        try
        {
            if (SafeInt(() => attacker.HasCondition<ConAmbush>() ? 1 : 0, 0) != 0)
                return 1.0;
            if (SafeInt(() => attacker.HasCondition<ConSevenSense>() &&
                               (attacker.HasElement(1244) || attacker.HasElement(1246) || attacker.HasElement(1247) || attacker.HasElement(1253)) ? 1 : 0, 0) != 0)
                return 1.0;
            if (SafeInt(() => defender.IsDeadOrSleeping ? 1 : 0, 0) != 0)
                return 1.0;
        }
        catch { }

        var normal = 0.0;
        if (toHit >= 1)
        {
            if (evasion < 1)
                normal = 1.0;
            else
                normal = EstimateNpcMoreInfoRandomContestChance(toHit, Math.Max(1L, evasion * (isRanged ? 150L : 125L) / 100L));
        }

        var chance = 0.05 + 0.9025 * normal;
        var evadePlus = SafeInt(() => defender.Evalue(57), 0);
        if (evadePlus > 0)
            chance *= 1.0 - ClampDouble(evadePlus / 100.0, 0.0, 0.95);
        else if (evadePlus < 0)
        {
            var forcedHit = ClampDouble(-evadePlus / 100.0, 0.0, 0.95);
            chance = forcedHit + (1.0 - forcedHit) * chance;
        }

        var extraEvasion = SafeInt(() => defender.Evalue(151), 0);
        if (extraEvasion > 0 && toHit < extraEvasion * 10L && evasion > 0)
        {
            var pressure = evasion * 100.0 / Math.Max(1.0, toHit);
            if (pressure > 300.0) chance *= 0.72;
            else if (pressure > 200.0) chance *= 0.82;
            else if (pressure > 150.0) chance *= 0.9;
        }

        return ClampDouble(chance, 0.01, 0.99);
    }
    private static double EstimateNpcMoreInfoRandomContestChance(long attack, long evasion)
    {
        attack = Math.Max(1L, attack);
        evasion = Math.Max(1L, evasion);
        if (evasion <= attack)
            return ClampDouble(1.0 - (evasion - 1.0) / (2.0 * attack), 0.0, 1.0);
        return ClampDouble((attack + 1.0) / (2.0 * evasion), 0.0, 1.0);
    }
    private static double EstimateNpcMoreInfoDiceAverage(int diceNum, int diceDim, int bonus)
    {
        if (diceNum <= 0 || diceDim <= 0)
            return Math.Max(0, bonus);
        return Math.Max(0.0, diceNum * (diceDim + 1.0) / 2.0 + bonus);
    }
    private static double EstimateNpcMoreInfoDiceMax(int diceNum, int diceDim, int bonus)
    {
        if (diceNum <= 0 || diceDim <= 0)
            return Math.Max(0, bonus);
        return Math.Max(0.0, diceNum * (double)diceDim + bonus);
    }
    private static double EstimateNpcMoreInfoRawAttackDamage(double roll, double dMulti, Chara attacker)
    {
        var raw = Math.Max(0.0, roll) * Math.Max(0.05, dMulti);
        try
        {
            var strife = SafeObject(() => attacker.GetCondition<ConStrife>());
            var lv = GetInt(strife, "lv");
            if (lv > 0)
                raw = raw * (100.0 + lv * 10.0) / 100.0;
        }
        catch { }
        if (SafeInt(() => attacker.isRestrained ? 1 : 0, 0) != 0)
            raw /= 2.0;
        return raw;
    }
    private static double EstimateNpcMoreInfoCriticalDamageFactor(Chara attacker, Thing weapon, bool martialAttack)
    {
        try
        {
            var criticalChance = ClampDouble((SafeInt(() => attacker.Evalue(73), 0) + 50.0) / 5000.0, 0.0, 0.25);
            criticalChance += ClampDouble((SafeInt(() => attacker.Evalue(90), 0) +
                                           (weapon == null ? 0 : SafeInt(() => weapon.Evalue(90, true), 0)) +
                                           Math.Sqrt(Math.Max(0, SafeInt(() => attacker.Evalue(134), 0)))) / 200.0, 0.0, 0.6);
            criticalChance = ClampDouble(criticalChance, 0.0, 0.75);
            return 1.0 + criticalChance * (martialAttack ? 0.25 : 0.35);
        }
        catch
        {
            return 1.0;
        }
    }
    private static double EstimateNpcMoreInfoCriticalChance(Chara attacker, Chara defender, Thing weapon)
    {
        try
        {
            if (SafeInt(() => attacker.HasCondition<ConAmbush>() ? 1 : 0, 0) != 0 ||
                SafeInt(() => defender.IsDeadOrSleeping ? 1 : 0, 0) != 0)
                return 1.0;

            var earlyCrit = SafeInt(() => defender.HasCondition<ConDim>() ? 1 : 0, 0) != 0 ? 0.25 : 0.0;
            var p1 = ClampDouble((SafeInt(() => attacker.Evalue(73), 0) + 50.0) / 5000.0, 0.0, 1.0);
            var p2 = ClampDouble((SafeInt(() => attacker.Evalue(90), 0) +
                                  (weapon == null ? 0 : SafeInt(() => weapon.Evalue(90, true), 0)) +
                                  Math.Sqrt(Math.Max(0, SafeInt(() => attacker.Evalue(134), 0)))) / 200.0, 0.0, 1.0);
            var p3 = 0.0;
            var fury = SafeInt(() => attacker.Evalue(1420), 0);
            if (fury > 0)
            {
                var maxHp = Math.Max(1, SafeInt(() => attacker.MaxHP, 1));
                var missing = Math.Min(100, Math.Max(0, 100 - SafeInt(() => attacker.hp, maxHp) * 100 / maxHp));
                var value = missing * (50 + fury * 50) / 100.0;
                if (value >= 50.0)
                    p3 = ClampDouble(value * value * value * value / 300000000.0, 0.0, 1.0);
            }
            var regular = 1.0 - (1.0 - p1) * (1.0 - p2) * (1.0 - p3);
            return ClampDouble(earlyCrit + (1.0 - earlyCrit) * regular, 0.0, 1.0);
        }
        catch
        {
            return 0.0;
        }
    }
    private static double EstimateNpcMoreInfoDamageAfterProtection(double rawDamage, int penetration, Chara attacker, Chara defender, Thing weapon, bool isMartial, bool isRanged, out double protectedDamage)
    {
        rawDamage = Math.Max(0.0, rawDamage);
        var convertRatio = EstimateNpcMoreInfoElementConvertRatio(attacker, weapon, isMartial, out var elementId, out var elementPower);
        var baneMultiplier = EstimateNpcMoreInfoBaneMultiplier(attacker, defender, weapon);
        rawDamage *= baneMultiplier;
        var converted = rawDamage * convertRatio;
        var remaining = rawDamage - converted;
        var penetrationRatio = ClampDouble(penetration / 100.0, 0.0, 1.0);
        var penetrated = remaining * penetrationRatio;
        var protectedInput = remaining - penetrated;
        protectedDamage = Math.Max(0.0, EstimateNpcMoreInfoApplyProtectionExpected(protectedInput, defender) + penetrated + converted);
        return Math.Max(0.1, EstimateNpcMoreInfoApplyDamageHpExpected(protectedDamage, elementId, elementPower, isRanged ? AttackSource.Range : AttackSource.Melee, attacker, defender));
    }
    private static double EstimateNpcMoreInfoApplyProtectionExpected(double damage, Chara defender)
    {
        if (damage <= 0.0)
            return 0.0;
        var armorSkill = SafeInt(() => defender.GetArmorSkill(), 0);
        var armorValue = armorSkill == 0 ? 0 : SafeInt(() => defender.Evalue(armorSkill), 0);
        var protection = SafeInt(() => defender.PV, 0) + armorValue + SafeInt(() => defender.DEX, 0) / 10.0;
        if (protection <= 0.0)
            return damage;

        var reduced = damage * 100.0 / Math.Max(100.0 + protection, 1.0);
        var num3 = (int)protection / 4;
        var diceNum = num3 / 10 + 1;
        var sides = num3 / Math.Max(1, diceNum) + 1;
        reduced -= EstimateNpcMoreInfoDiceAverage(diceNum, sides, 0);
        return Math.Max(0.0, reduced);
    }
    private static double EstimateNpcMoreInfoElementConvertRatio(Chara attacker, Thing weapon, bool isMartial, out int elementId, out int elementPower)
    {
        elementId = 0;
        elementPower = 0;
        try
        {
            if (weapon != null)
            {
                foreach (var element in weapon.elements.dict.Values)
                {
                    if (element == null || element.Value <= 0)
                        continue;
                    var categorySub = SafeText(() => element.source.categorySub, "");
                    if (string.Equals(categorySub, "eleConvert", StringComparison.OrdinalIgnoreCase))
                    {
                        elementId = GetNpcMoreInfoElementIdFromAlias(SafeText(() => element.source.aliasRef, ""));
                        elementPower = 50 + element.Value * 2;
                        return ClampDouble(Math.Min(element.Value, 100) / 100.0, 0.0, 1.0);
                    }
                }
                if (weapon.trait is TraitToolRangeCane)
                {
                    Element selected = null;
                    foreach (var element in weapon.elements.dict.Values)
                    {
                        if (element != null && element.Value > 0 &&
                            string.Equals(SafeText(() => element.source.categorySub, ""), "eleAttack", StringComparison.OrdinalIgnoreCase))
                        {
                            if (selected == null || element.Value > selected.Value)
                                selected = element;
                        }
                    }
                    if (selected != null)
                    {
                        elementId = selected.id;
                        elementPower = selected.id == 920 ? 30 : (selected.id == 914 || selected.id == 918 ? 50 : 100);
                        return 0.5;
                    }
                }
            }

            if (isMartial && SafeInt(() => attacker.MainElement != null && attacker.MainElement != Element.Void ? 1 : 0, 0) != 0)
            {
                elementId = SafeInt(() => attacker.MainElement.id, 0);
                var power = Math.Max(1, SafeInt(() => attacker.Power, 1));
                elementPower = power / 3 + Math.Max(0, power / 4);
                return 0.5;
            }
        }
        catch { }
        return 0.0;
    }
    private static double EstimateNpcMoreInfoApplyDamageHpExpected(double damage, int elementId, int elementPower, AttackSource attackSource, Chara attacker, Chara defender)
    {
        damage = Math.Max(0.0, damage);
        if (damage <= 0.0)
            return 0.0;

        damage = damage * (100.0 + SafeInt(() => attacker.Evalue(94), 0)) / 100.0;

        var isVoid = elementId == 0 || elementId == 926;
        if (isVoid)
        {
            damage = damage * Math.Max(100.0 + SafeInt(() => attacker.Evalue(93), 0) / 2.0, 10.0) / 100.0;
        }
        else
        {
            damage = EstimateNpcMoreInfoResistDamage(damage, elementId, defender, GetNpcMoreInfoResistPenetrationLevel(elementId, attackSource, attacker, defender));
            damage = damage * 100.0 / (100.0 + Clamp(SafeInt(() => defender.Evalue(961), 0) * 5, -50, 200));
            damage = damage * Math.Max(100.0 - SafeInt(() => defender.Evalue(93), 0), 10.0) / 100.0;
            if (elementId == 910 && SafeInt(() => defender.isWet ? 1 : 0, 0) != 0)
                damage /= 3.0;
            else if (elementId == 912 && SafeInt(() => defender.isWet ? 1 : 0, 0) != 0)
                damage = damage * 150.0 / 100.0;
        }

        if (SafeInt(() => attacker.HasCondition<ConSupress>() ? 1 : 0, 0) != 0)
            damage = damage * 2.0 / 3.0;
        if (SafeInt(() => attacker.HasCondition<ConBerserk>() ? 1 : 0, 0) != 0)
            damage = damage * 3.0 / 2.0;

        if (SafeInt(() => defender.IsPCFaction ? 1 : 0, 0) == 0)
        {
            var lv = SafeInt(() => defender.LV, 0);
            if (lv > 50)
                damage = damage * (100.0 - Math.Min(80.0, Math.Sqrt(lv - 50) * 2.5)) / 100.0;
        }
        if (SafeInt(() => GameAccess.Runtime.Game.principal.enableDamageReduction && defender.IsPCFaction ? 1 : 0, 0) != 0)
        {
            var originLv = Math.Max(0, SafeInt(() => attacker.LV, SafeInt(() => GameAccess.World.CurrentZone.DangerLv, 0)));
            if (originLv > 50)
                damage = damage * (100.0 - Math.Min(95.0, Math.Sqrt(originLv - 50))) / 100.0;
        }

        if (attackSource == AttackSource.Range || attackSource == AttackSource.Throw)
            damage = damage * 100.0 / Math.Max(100.0 + SafeInt(() => defender.Evalue(435), 0) * 2.0, 1.0);

        var reductionElement = isVoid ? 55 : 56;
        damage = damage * Math.Max(0.0, 100.0 - Math.Min(SafeInt(() => defender.Evalue(reductionElement), 0), 100) / 1.0) / 100.0;

        if (isVoid && SafeInt(() => defender.body.GetAttackStyle() == AttackStyle.Shield && defender.elements.ValueWithoutLink(123) >= 5 ? 1 : 0, 0) != 0)
            damage = damage * 90.0 / 100.0;
        if (SafeInt(() => defender.HasElement(971) ? 1 : 0, 0) != 0)
            damage = damage * 100.0 / Clamp(100 + SafeInt(() => defender.Evalue(971), 0), 25, 1000);
        if (SafeInt(() => defender.HasElement(1305) ? 1 : 0, 0) != 0)
            damage = damage * 90.0 / 100.0;
        if (SafeInt(() => defender.HasElement(1218) ? 1 : 0, 0) != 0)
            damage = damage * (1000.0 - Math.Min(SafeInt(() => defender.Evalue(1218), 0), 1000)) / 1000.0;

        var maxHp = Math.Max(1, SafeInt(() => defender.MaxHP, 1));
        var threshold = maxHp / 10.0;
        var toughness = SafeInt(() => defender.Evalue(68), 0);
        if (damage >= threshold && toughness > 0)
            damage = threshold + (damage - threshold) * 100.0 / (200.0 + toughness * 10.0);

        return Math.Max(0.0, damage);
    }
}
