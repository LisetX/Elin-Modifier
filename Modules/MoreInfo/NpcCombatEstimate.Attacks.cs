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
    private static bool TryEstimateNpcMoreInfoPreparedPhysical(Chara attacker, Chara defender, out double expectedDamage, out double averageHitChance)
    {
        expectedDamage = 0.0;
        averageHitChance = 0.0;
        try
        {
            var weapons = SelectNpcMoreInfoAttackWeapons(attacker, defender).ToList();
            if (weapons.Count == 0)
                weapons.Add(null);

            var totalPotential = 0.0;
            var meleeIndex = 0;
            for (var i = 0; i < weapons.Count; i++)
            {
                var weapon = weapons[i];
                var isRanged = IsNpcMoreInfoRangedAttackWeapon(weapon);
                var attackIndex = isRanged ? 0 : meleeIndex++;
                if (!TryEstimateNpcMoreInfoPreparedAttack(attacker, defender, weapon, attackIndex, out var damageOnHit, out var hitChance))
                    continue;

                if (isRanged)
                {
                    EstimateNpcMoreInfoRangedAttackFactors(attacker, weapon, hitChance, out var rangedHitFactor, out var rangedPotentialFactor);
                    expectedDamage += damageOnHit * rangedHitFactor;
                    totalPotential += damageOnHit * rangedPotentialFactor;
                }
                else
                {
                    var chaser = EstimateNpcMoreInfoAdjustedHitChance(hitChance,
                        EstimateNpcMoreInfoExpectedRepeatCount(GetNpcMoreInfoWeaponEnc(attacker, weapon, 620, true), 4, 4, 0));
                    var flurry = EstimateNpcMoreInfoExpectedRepeatCount(GetNpcMoreInfoWeaponEnc(attacker, weapon, 621, true), 25, 5, 0);
                    expectedDamage += damageOnHit * chaser * flurry;
                    totalPotential += damageOnHit * flurry;
                }
            }

            if (totalPotential <= 0.0)
                return false;

            averageHitChance = ClampDouble(expectedDamage / totalPotential, 0.0, 1.0);
            return expectedDamage > 0.0;
        }
        catch
        {
            expectedDamage = 0.0;
            averageHitChance = 0.0;
            return false;
        }
    }
    private static List<Thing> SelectNpcMoreInfoAttackWeapons(Chara attacker, Chara defender)
    {
        var melee = EnumerateNpcMoreInfoMeleeWeapons(attacker).ToList();
        var ranged = EnumerateNpcMoreInfoRangedWeapons(attacker).Where(w => IsNpcMoreInfoRangedWeaponUsable(attacker, defender, w)).ToList();
        var distance = 1;
        try
        {
            if (attacker?.pos != null && defender?.pos != null)
                distance = attacker.pos.Distance(defender.pos);
        }
        catch { }

        var meleeReach = EstimateNpcMoreInfoMeleeReach(attacker, melee);
        if (distance > meleeReach && ranged.Count > 0)
            return ranged;
        if (melee.Count > 0)
            return melee;
        if (distance > meleeReach && ranged.Count > 0)
            return ranged;
        if (ranged.Count > 0 && distance > 1)
            return ranged;
        return new List<Thing>();
    }
    private static int EstimateNpcMoreInfoMeleeReach(Chara attacker, List<Thing> meleeWeapons)
    {
        var reach = Math.Max(0, SafeInt(() => attacker.Evalue(666), 0));
        if (meleeWeapons != null)
        {
            foreach (var weapon in meleeWeapons)
                reach = Math.Max(reach, SafeInt(() => weapon.Evalue(666), 0));
        }
        return 1 + reach;
    }
    private static bool IsNpcMoreInfoRangedAttackWeapon(Thing weapon)
    {
        return weapon != null && SafeInt(() => weapon.IsRangedWeapon ? 1 : 0, 0) != 0;
    }
    private static bool IsNpcMoreInfoRangedWeaponUsable(Chara attacker, Chara defender, Thing weapon)
    {
        if (attacker == null || weapon == null || !IsNpcMoreInfoRangedAttackWeapon(weapon))
            return false;
        try
        {
            if (!weapon.CanAutoFire(attacker, defender))
                return false;
        }
        catch { }
        if (weapon.trait is TraitToolRangeCane)
            return true;
        if (SafeInt(() => attacker.HasCondition<ConReload>() ? 1 : 0, 0) != 0)
            return false;
        return SafeInt(() => weapon.c_ammo, 0) > 0 || SafeObject(() => weapon.ammoData) != null;
    }
    private static void EstimateNpcMoreInfoRangedAttackFactors(Chara attacker, Thing weapon, double hitChance, out double hitFactor, out double potentialFactor)
    {
        var hitTotal = 0.0;
        var potentialTotal = 0.0;
        var numFire = Math.Max(1.0, EstimateNpcMoreInfoBaseNumFire(weapon));
        var noLoss = numFire;
        var multiFire = Math.Max(0, GetNpcMoreInfoWeaponEnc(attacker, weapon, 602, false));
        if (multiFire > 0)
        {
            numFire += Math.Min(multiFire / 10.0, 10.0);
            noLoss += multiFire / 100.0;
        }
        numFire += Math.Max(0, SafeInt(() => attacker.Evalue(1652), 0));
        noLoss = Math.Max(0.0, noLoss);

        var chaser = Math.Max(0, GetNpcMoreInfoWeaponEnc(attacker, weapon, 620, false));
        var whole = Clamp((int)Math.Floor(numFire), 1, 80);
        var fractional = ClampDouble(numFire - whole, 0.0, 0.9999);
        for (var shot = 0; shot < whole; shot++)
            AddShot(shot, 1.0);
        if (fractional > 0.0001 && whole < 80)
            AddShot(whole, fractional);

        if (potentialTotal <= 0.0)
            potentialTotal = 1.0;
        if (hitTotal <= 0.0)
            hitTotal = hitChance;
        hitFactor = hitTotal;
        potentialFactor = potentialTotal;

        void AddShot(int shotIndex, double weight)
        {
            var falloff = 1.0;
            if (shotIndex >= noLoss)
                falloff = 100.0 / (100.0 + (shotIndex - noLoss + 1.0) * 30.0);
            var attempts = EstimateNpcMoreInfoExpectedRepeatCount(chaser, 4, 4, shotIndex);
            hitTotal += weight * falloff * EstimateNpcMoreInfoAdjustedHitChance(hitChance, attempts);
            potentialTotal += weight * falloff;
        }
    }
    private static int EstimateNpcMoreInfoBaseNumFire(Thing weapon)
    {
        try
        {
            var guns = SafeObject(() => GameAccess.Runtime.Settings.effect.guns) as IDictionary;
            if (guns == null)
                return 1;

            object effect = null;
            var key = SafeText(() => weapon.id, "");
            if (!string.IsNullOrEmpty(key) && guns.Contains(key))
                effect = guns[key];
            if (effect == null)
            {
                var fallback = weapon.trait is TraitToolRangeCane ? "cane" : (weapon.trait is TraitToolRangeGun ? "gun" : "bow");
                if (guns.Contains(fallback))
                    effect = guns[fallback];
            }
            return Math.Max(1, GetInt(effect, "num"));
        }
        catch
        {
            return 1;
        }
    }
    private static double EstimateNpcMoreInfoExpectedRepeatCount(int value, int baseDenominator, int powBase, int extraPowerOffset)
    {
        if (value <= 0)
            return 1.0;
        var count = 1.0;
        for (var i = 0; i < 10; i++)
        {
            var denominator = baseDenominator + Math.Pow(powBase, i + 2 + Math.Max(0, extraPowerOffset));
            if (denominator <= 0.0)
                continue;
            count += ClampDouble(value / denominator, 0.0, 1.0);
        }
        return ClampDouble(count, 1.0, 20.0);
    }
    private static double EstimateNpcMoreInfoAdjustedHitChance(double hitChance, double expectedAttempts)
    {
        hitChance = ClampDouble(hitChance, 0.0, 1.0);
        expectedAttempts = ClampDouble(expectedAttempts, 1.0, 20.0);
        return ClampDouble(1.0 - Math.Pow(1.0 - hitChance, expectedAttempts), 0.0, 1.0);
    }
    private static bool TryEstimateNpcMoreInfoPreparedAttack(Chara attacker, Chara defender, Thing weapon, int attackIndex, out double damageOnHit, out double hitChance)
    {
        damageOnHit = 0.0;
        hitChance = 0.0;
        try
        {
            var isMartial = weapon == null;
            var isMartialWeapon = !isMartial && SafeInt(() => weapon.category.skill == 100 ? 1 : 0, 0) != 0;
            var isRanged = !isMartial && SafeInt(() => weapon.IsRangedWeapon ? 1 : 0, 0) != 0;
            var isCane = !isMartial && weapon.trait is TraitToolRangeCane;
            var attackStyle = SafeObject(() => attacker.body.GetAttackStyle()) is AttackStyle style ? style : AttackStyle.Default;
            var distMod = EstimateNpcMoreInfoDistanceModifier(attacker, defender, weapon, isRanged);

            int dNum;
            int dDim;
            int dBonus;
            int dNumAmmo = 0;
            int dDimAmmo = 0;
            int dBonusAmmo = 0;
            double dMulti;
            long toHitBase;
            int toHitFix;
            int penetration;

            if (isMartial || isMartialWeapon)
            {
                var martialSkillId = !isMartial && SafeInt(() => weapon.Evalue(482), 0) > 0 ? 305 : 100;
                var weaponSkill = SafeInt(() => attacker.Evalue(martialSkillId), 0);
                var parent = GetNpcMoreInfoElementParentValue(attacker, martialSkillId);
                dBonus = SafeInt(() => attacker.DMG, 0) + SafeInt(() => attacker.encLV, 0) +
                         (int)Math.Sqrt(Math.Max(0.0, parent / 5.0 + weaponSkill / 4.0));
                dNum = 2 + Math.Min(weaponSkill / 10, 4);
                dDim = 5 + (int)Math.Sqrt(Math.Max(0.0, weaponSkill / 3.0));
                dMulti = 0.6 + (parent / 2.0 + weaponSkill / 2.0 + SafeInt(() => attacker.Evalue(martialSkillId == 305 ? 304 : 132), 0) / 2.0) / 50.0;
                dMulti += 0.05 * SafeInt(() => attacker.Evalue(1400), 0);
                toHitBase = SafeLongCurve(SafeInt(() => attacker.DEX, 0) / 3 + parent / 3 + weaponSkill, 50, 25) + 50;
                toHitFix = SafeInt(() => attacker.HIT, 0);
                if (attackStyle == AttackStyle.Shield)
                    toHitBase = toHitBase * 75 / 100;
                penetration = Clamp(weaponSkill / 10 + 5, 5, 20) + SafeInt(() => attacker.Evalue(92), 0);
                if (SafeInt(() => attacker.HasElement(1246) ? 1 : 0, 0) != 0)
                    penetration += 25;

                if (isMartialWeapon)
                {
                    var offense = SafeObject(() => weapon.source.offense) as int[];
                    dBonus += SafeInt(() => weapon.DMG, 0);
                    dNum += offense != null && offense.Length > 0 ? offense[0] : 0;
                    dDim = Math.Max(dDim / 2 + SafeInt(() => weapon.c_diceDim, 1), 1);
                    toHitFix += SafeInt(() => weapon.HIT, 0);
                    penetration += SafeInt(() => weapon.Penetration, 0);
                }
            }
            else
            {
                var skillId = EstimateNpcMoreInfoWeaponSkillId(weapon, isRanged, isCane);
                var weaponSkill = SafeInt(() => attacker.Evalue(skillId), 0);
                var parent = GetNpcMoreInfoElementParentValue(attacker, skillId);
                var styleSkill = isCane || SafeInt(() => weapon.Evalue(482), 0) > 0 ? 304 : (isRanged ? 133 : 132);
                dBonus = SafeInt(() => attacker.DMG, 0) + SafeInt(() => attacker.encLV, 0) + SafeInt(() => weapon.DMG, 0);
                var offense = SafeObject(() => weapon.source.offense) as int[];
                dNum = offense != null && offense.Length > 0 ? offense[0] : 1;
                dDim = SafeInt(() => weapon.c_diceDim, offense != null && offense.Length > 1 ? offense[1] : 1);
                dMulti = 0.6 + (parent + weaponSkill / 2.0 + SafeInt(() => attacker.Evalue(styleSkill), 0)) / 50.0;
                dMulti += 0.05 * SafeInt(() => attacker.Evalue(isRanged ? 1404 : 1400), 0);
                toHitBase = SafeLongCurve((isCane ? SafeInt(() => attacker.WIL, 0) : SafeInt(() => attacker.DEX, 0)) / 4 + parent / 3 + weaponSkill, 50, 25) + 50;
                if (SafeInt(() => attacker.HasElement(1208) && skillId == 101 ? 1 : 0, 0) != 0)
                    toHitBase = toHitBase * 115 / 100;
                toHitFix = SafeInt(() => attacker.HIT, 0) + SafeInt(() => weapon.HIT, 0);
                penetration = SafeInt(() => weapon.Penetration, 0) + SafeInt(() => attacker.Evalue(92), 0);
                if (isRanged)
                {
                    if (SafeInt(() => attacker.HasElement(1244) ? 1 : 0, 0) != 0)
                        penetration += 25;
                }
                else if (SafeInt(() => attacker.HasElement(1247) ? 1 : 0, 0) != 0)
                {
                    penetration += 25;
                }
                if (isCane)
                    toHitBase += 50;

                var ammo = SafeObject(() => weapon.ammoData) as Thing;
                if (ammo != null && !(ammo.trait is TraitAmmoTalisman) && SafeInt(() => attacker.HasCondition<ConReload>() ? 1 : 0, 0) == 0)
                {
                    var ammoOffense = SafeObject(() => ammo.source.offense) as int[];
                    dNumAmmo = ammoOffense != null && ammoOffense.Length > 0 ? ammoOffense[0] : 0;
                    dDimAmmo = SafeInt(() => ammo.c_diceDim, ammoOffense != null && ammoOffense.Length > 1 ? ammoOffense[1] : 1);
                    dBonusAmmo = SafeInt(() => ammo.DMG, 0) + SafeInt(() => ammo.encLV, 0);
                    if (dNumAmmo < 1) dNumAmmo = 1;
                    if (dDimAmmo < 1) dDimAmmo = 1;
                    toHitFix += SafeInt(() => ammo.HIT, 0);
                }
            }

            if (dNum < 1) dNum = 1;
            if (dDim < 1) dDim = 1;
            penetration = Clamp(penetration, 0, 100);
            if (attackStyle == AttackStyle.TwoHand)
                dMulti = dMulti * 1.5 + 0.1 * Math.Sqrt(Math.Max(0, SafeInt(() => attacker.Evalue(130), 0)));
            dMulti *= distMod / 100.0;

            var toHit = (toHitBase + toHitFix) * (100L + SafeInt(() => attacker.Evalue(414), 0)) / 100L;
            toHit = (long)(toHit * distMod / 100.0);
            if (SafeInt(() => attacker.HasCondition<ConBane>() ? 1 : 0, 0) != 0)
                toHit = toHit * 75 / 100;
            if (SafeInt(() => attacker.HasHigherGround(defender) ? 1 : 0, 0) != 0)
                toHit = toHit * 120 / 100;
            if (SafeObject(() => attacker.ride) != null)
                toHit = toHit * 100 / (100 + 500 / Math.Max(5, 10 + SafeInt(() => attacker.EvalueRiding(), 0)));
            if (SafeObject(() => attacker.parasite) != null)
                toHit = toHit * 100 / (100 + 1000 / Math.Max(5, 10 + SafeInt(() => attacker.Evalue(227), 0)));
            var host = SafeObject(() => attacker.host) as Chara;
            if (host != null)
            {
                if (ReferenceEquals(SafeObject(() => host.ride), attacker))
                    toHit = toHit * 100 / (100 + 1000 / Math.Max(5, 10 + SafeInt(() => attacker.STR, 0)));
                if (ReferenceEquals(SafeObject(() => host.parasite), attacker))
                    toHit = toHit * 100 / (100 + 2000 / Math.Max(5, 10 + SafeInt(() => attacker.DEX, 0)));
            }
            if (attackStyle == AttackStyle.TwoHand)
                toHit += 25 + (int)Math.Sqrt(Math.Max(0, SafeInt(() => attacker.Evalue(130), 0)) * 2.0);
            else if (attackStyle == AttackStyle.TwoWield && toHit > 0)
            {
                var twoWield = Math.Max(SafeInt(() => attacker.Evalue(131), 0), -10);
                var penalty = twoWield >= 50 ? 10 : (twoWield >= 25 ? 12 : 15);
                toHit = toHit * 100 / (100 + (attackIndex + 1) * penalty + attackIndex * Clamp(2000 / Math.Max(1, 20 + twoWield), 0, 100));
            }
            if (SafeInt(() => attacker.isBlind ? 1 : 0, 0) != 0)
                toHit /= isRanged ? 10 : 3;
            if (SafeInt(() => attacker.isConfused || attacker.HasCondition<ConDim>() ? 1 : 0, 0) != 0)
                toHit /= 2;

            var evasion = SafeLongCurve(SafeInt(() => defender.PER, 0) / 3 + SafeInt(() => defender.Evalue(150), 0), 50, 10) +
                          SafeInt(() => defender.DV, 0) + 25L;
            if (SafeInt(() => defender.isBlind ? 1 : 0, 0) != 0)
                evasion /= 2;
            if (SafeInt(() => defender.HasCondition<ConDim>() ? 1 : 0, 0) != 0)
                evasion /= 2;
            if (SafeInt(() => defender.HasHigherGround(attacker) ? 1 : 0, 0) != 0)
                evasion = evasion * 120 / 100;

            hitChance = EstimateNpcMoreInfoPreparedHitChance(toHit, evasion, isRanged, attacker, defender, weapon);
            var normalRoll = EstimateNpcMoreInfoDiceAverage(dNum, dDim, dBonus) + EstimateNpcMoreInfoDiceAverage(dNumAmmo, dDimAmmo, dBonusAmmo);
            var criticalRoll = EstimateNpcMoreInfoDiceMax(dNum, dDim, dBonus) + EstimateNpcMoreInfoDiceMax(dNumAmmo, dDimAmmo, dBonusAmmo);
            if (SafeInt(() => attacker.Evalue(1355), 0) > 0)
            {
                normalRoll += 1.0;
                criticalRoll += 1.0;
            }
            var rawNormal = EstimateNpcMoreInfoRawAttackDamage(normalRoll, dMulti, attacker);
            var rawCritical = EstimateNpcMoreInfoRawAttackDamage(criticalRoll, dMulti * ((isMartial || isMartialWeapon) ? 1.25 : 1.0), attacker);
            var critChance = EstimateNpcMoreInfoCriticalChance(attacker, defender, weapon);
            var normalDamage = EstimateNpcMoreInfoDamageAfterProtection(rawNormal, penetration, attacker, defender, weapon, isMartial, isRanged, out var normalProtected);
            var criticalDamage = EstimateNpcMoreInfoDamageAfterProtection(rawCritical, penetration, attacker, defender, weapon, isMartial, isRanged, out var criticalProtected);
            damageOnHit = normalDamage * (1.0 - critChance) + criticalDamage * critChance;
            damageOnHit += EstimateNpcMoreInfoWeaponEnchantExpectedDamage(normalProtected * (1.0 - critChance) + criticalProtected * critChance,
                attacker, defender, weapon, isRanged, isMartial);
            return damageOnHit > 0.0;
        }
        catch
        {
            damageOnHit = 0.0;
            hitChance = 0.0;
            return false;
        }
    }
}
