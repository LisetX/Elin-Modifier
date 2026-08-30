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
using static ElinModifierPlugin;

internal sealed partial class MoreInfoModule
{
    private static string BuildNpcMoreInfoCombatEstimateLine(Chara npc)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (instance == null)
            return BuildNpcMoreInfoCombatEstimateLineUncached(npc);

        Chara? pc = null;
        try { pc = GameAccess.Characters.PlayerCharacter; }
        catch { }
        if (pc == null || npc == null || ReferenceEquals(pc, npc) || ShouldSkipNpcMoreInfo(pc))
            return "";

        var now = instance.SchedulerNow;
        var map = SafeObject(() => GameAccess.World.CurrentMap) as Map;
        var pcUid = GetCharaUid(pc);
        var npcUid = GetCharaUid(npc);
        var samePair = ReferenceEquals(instance._npcMoreInfoCombatCacheMap, map) &&
                       ReferenceEquals(instance._npcMoreInfoCombatCachePc, pc) &&
                       ReferenceEquals(instance._npcMoreInfoCombatCacheNpc, npc) &&
                       instance._npcMoreInfoCombatCachePcUid == pcUid &&
                       instance._npcMoreInfoCombatCacheNpcUid == npcUid;
        var checkInterval = instance._lowPerformanceMode ? 0.25f : 0.1f;
        if (samePair &&
            now >= instance._npcMoreInfoCombatCacheCheckTime &&
            now - instance._npcMoreInfoCombatCacheCheckTime < checkInterval)
        {
            return instance._npcMoreInfoCombatCacheValue;
        }

        var structuralInterval = instance._lowPerformanceMode ? 2f : 1f;
        var structuralDue = !samePair ||
                            !instance._npcMoreInfoCombatCacheEstimatesValid ||
                            now < instance._npcMoreInfoCombatCacheFullTime ||
                            now - instance._npcMoreInfoCombatCacheFullTime >= structuralInterval;
        var structuralChanged = !samePair || !instance._npcMoreInfoCombatCacheEstimatesValid;
        if (structuralDue)
        {
            var fingerprint = BuildNpcMoreInfoCombatFingerprint(pc, npc);
            structuralChanged |= fingerprint != instance._npcMoreInfoCombatCacheFingerprint;
            instance._npcMoreInfoCombatCacheFingerprint = fingerprint;
            instance._npcMoreInfoCombatCacheFullTime = now;
            if (structuralChanged)
            {
                instance._npcMoreInfoCombatCacheKillEstimate = EstimateNpcMoreInfoCombat(pc, npc);
                instance._npcMoreInfoCombatCacheDeathEstimate = EstimateNpcMoreInfoCombat(npc, pc);
                instance._npcMoreInfoCombatCacheEstimatesValid = true;
            }
        }

        var dynamicFingerprint = BuildNpcMoreInfoCombatDynamicFingerprint(pc, npc);
        if (samePair && !structuralChanged && dynamicFingerprint == instance._npcMoreInfoCombatCacheDynamicFingerprint)
        {
            instance._npcMoreInfoCombatCacheCheckTime = now;
            return instance._npcMoreInfoCombatCacheValue;
        }

        var value = FormatNpcMoreInfoCombatEstimateLine(
            RefreshNpcMoreInfoCombatTarget(instance._npcMoreInfoCombatCacheKillEstimate, npc),
            RefreshNpcMoreInfoCombatTarget(instance._npcMoreInfoCombatCacheDeathEstimate, pc));
        instance._npcMoreInfoCombatCacheMap = map;
        instance._npcMoreInfoCombatCachePc = pc;
        instance._npcMoreInfoCombatCacheNpc = npc;
        instance._npcMoreInfoCombatCachePcUid = pcUid;
        instance._npcMoreInfoCombatCacheNpcUid = npcUid;
        instance._npcMoreInfoCombatCacheDynamicFingerprint = dynamicFingerprint;
        instance._npcMoreInfoCombatCacheCheckTime = now;
        instance._npcMoreInfoCombatCacheValue = value;
        return value;
    }
    private static string BuildNpcMoreInfoCombatEstimateLineUncached(Chara npc)
    {
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            if (pc == null || npc == null || ReferenceEquals(pc, npc) || ShouldSkipNpcMoreInfo(pc))
                return "";

            var killEstimate = EstimateNpcMoreInfoCombat(pc, npc);
            var deathEstimate = EstimateNpcMoreInfoCombat(npc, pc);
            return FormatNpcMoreInfoCombatEstimateLine(killEstimate, deathEstimate);
        }
        catch
        {
            return "";
        }
    }
    private static string FormatNpcMoreInfoCombatEstimateLine(NpcCombatEstimate killEstimate, NpcCombatEstimate deathEstimate)
    {
        var killChance = EstimateNpcMoreInfoWinChance(killEstimate, deathEstimate);
        var deathChance = EstimateNpcMoreInfoWinChance(deathEstimate, killEstimate);
        var line = Tr("击杀成功率", "Kill chance") + ":" + FormatNpcMoreInfoChance(killChance) + "% " +
                   Tr("击杀预估所需轮数", "Estimated kill rounds") + ":" + FormatNpcMoreInfoRounds(killEstimate.Rounds) +
                   " | " +
                   Tr("被击杀概率", "Death chance") + ":" + FormatNpcMoreInfoChance(deathChance) + "% " +
                   Tr("被击杀预估所需轮数", "Estimated death rounds") + ":" + FormatNpcMoreInfoRounds(deathEstimate.Rounds);
        return ColorNpcMoreInfoText(line, NpcMoreInfoCombatColor);
    }
    private static NpcCombatEstimate RefreshNpcMoreInfoCombatTarget(NpcCombatEstimate estimate, Chara defender)
    {
        var targetHp = EstimateNpcMoreInfoEffectiveTargetHp(defender, SafeInt(() => defender.hp, 1));
        var rounds = Clamp((int)Math.Ceiling(targetHp / Math.Max(0.05, estimate.ExpectedDamagePerRound)), 1, 999);
        return new NpcCombatEstimate(estimate.ExpectedDamagePerRound, targetHp, estimate.HitChance, rounds);
    }
    private static ulong BuildNpcMoreInfoCombatDynamicFingerprint(Chara pc, Chara npc)
    {
        var hash = 1469598103934665603UL;
        hash = AddNpcMoreInfoCombatDynamicFingerprint(hash, pc);
        hash = AddNpcMoreInfoCombatDynamicFingerprint(hash, npc);
        return hash;
    }
    private static ulong AddNpcMoreInfoCombatDynamicFingerprint(ulong hash, Chara chara)
    {
        hash = MixNpcMoreInfoFingerprint(hash, GetCharaUid(chara));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.hp, 0));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.mana.value, 0));
        return hash;
    }
    private static ulong BuildNpcMoreInfoCombatFingerprint(Chara pc, Chara npc)
    {
        var hash = 1469598103934665603UL;
        hash = AddNpcMoreInfoCombatFingerprint(hash, pc);
        hash = AddNpcMoreInfoCombatFingerprint(hash, npc);
        return hash;
    }
    private static ulong AddNpcMoreInfoCombatFingerprint(ulong hash, Chara chara)
    {
        hash = MixNpcMoreInfoFingerprint(hash, GetCharaUid(chara));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.LV, 0));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.MaxHP, 0));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.Speed, 0));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.HIT, 0));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.DMG, 0));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.DV, 0));
        hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.PV, 0));
        for (var id = 70; id <= 77; id++)
        {
            var elementId = id;
            hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.Evalue(elementId), 0));
        }
        foreach (var id in NpcMoreInfoCombatFingerprintElementIds)
        {
            var elementId = id;
            hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => chara.Evalue(elementId), 0));
        }

        try
        {
            var items = chara.ability?.list?.items;
            hash = MixNpcMoreInfoFingerprint(hash, items?.Count ?? 0);
            if (items != null)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null || item.act == null)
                        continue;
                    hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => item.act.id, 0));
                    hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => item.chance, 0));
                    hash = MixNpcMoreInfoFingerprint(hash, GetNpcMoreInfoAbilityLevel(chara, item.act));
                }
            }
        }
        catch { }

        try
        {
            var conditions = chara.conditions;
            hash = MixNpcMoreInfoFingerprint(hash, conditions?.Count ?? 0);
            if (conditions != null)
            {
                foreach (var condition in conditions)
                {
                    if (condition == null)
                        continue;
                    hash = MixNpcMoreInfoFingerprint(hash, StringComparer.Ordinal.GetHashCode(condition.GetType().FullName ?? condition.GetType().Name));
                }
            }
        }
        catch { }

        try
        {
            foreach (var thing in EnumerateNpcMoreInfoEquippedThings(chara))
            {
                hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => thing.uid, 0));
                hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => thing.encLV, 0));
                hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => thing.rarityLv, 0));
                hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => thing.DMG, 0));
                hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => thing.c_diceDim, 0));
                hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => thing.Penetration, 0));
                var elements = thing.elements?.dict;
                hash = MixNpcMoreInfoFingerprint(hash, elements?.Count ?? 0);
                if (elements == null)
                    continue;
                foreach (var element in elements.Values)
                {
                    if (element == null)
                        continue;
                    hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => element.id, 0));
                    hash = MixNpcMoreInfoFingerprint(hash, SafeInt(() => element.Value, 0));
                }
            }
        }
        catch { }
        return hash;
    }
    private static ulong MixNpcMoreInfoFingerprint(ulong hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 1099511628211UL;
            hash ^= (uint)(value >> 16);
            hash *= 1099511628211UL;
            return hash;
        }
    }
    private static NpcCombatEstimate EstimateNpcMoreInfoCombat(Chara attacker, Chara defender)
    {
        var atk = BuildNpcMoreInfoCombatStats(attacker);
        var def = BuildNpcMoreInfoCombatStats(defender);
        var hitChance = EstimateNpcMoreInfoHitChance(atk, def);
        var physicalExpected = EstimateNpcMoreInfoPhysicalDamage(atk, def) * hitChance;
        if (TryEstimateNpcMoreInfoPreparedPhysical(attacker, defender, out var preparedPhysical, out var preparedHitChance))
        {
            physicalExpected = preparedPhysical;
            hitChance = preparedHitChance;
        }
        var ability = EstimateNpcMoreInfoAbilityDamage(attacker, defender, atk);
        var actionRate = ClampDouble(atk.Speed / 100.0, 0.25, 8.0);
        var expectedDamagePerRound = Math.Max(0.05, (physicalExpected + ability) * actionRate);
        var targetHp = EstimateNpcMoreInfoEffectiveTargetHp(defender, def.CurrentHp);
        var rounds = Clamp((int)Math.Ceiling(targetHp / expectedDamagePerRound), 1, 999);
        return new NpcCombatEstimate(expectedDamagePerRound, targetHp, hitChance, rounds);
    }
    private static int EstimateNpcMoreInfoEffectiveTargetHp(Chara defender, int currentHp)
    {
        var hp = Math.Max(1, currentHp);
        try
        {
            var manaShield = SafeInt(() => defender.Evalue(1421), 0);
            if (manaShield > 0)
            {
                var mana = Math.Max(0, SafeInt(() => defender.mana.value, 0));
                hp += mana * (manaShield >= 2 ? 2 : 1);
            }
        }
        catch { }
        return Clamp(hp, 1, 999999999);
    }
}
