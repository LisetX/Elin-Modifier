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
    private void SetInvincibleMode(bool enabled)
    {
        _invincibleMode = enabled;
        _log = enabled
            ? T("无敌模式已开启", "Invincible mode enabled")
            : T("无敌模式已关闭", "Invincible mode disabled");
    }
    private void SetInvincibleModeIncludeParty(bool enabled)
    {
        _invincibleModeIncludeParty = enabled;
    }
    private void SetIgnoreBuffEffects(bool enabled)
    {
        _ignoreBuffEffects = enabled;
        if (enabled)
            RemoveIgnoredBuffEffectsFromTargets();
        else
        {
            RestoreIgnoredDebuffStatsForPlayer();
            TickIgnoreBuffEffectsPartyTargets();
        }
        _log = enabled
            ? T("无视Buff效果已开启", "Ignore Buff effects enabled")
            : T("无视Buff效果已关闭", "Ignore Buff effects disabled");
    }
    private void SetIgnoreBuffEffectsDebuff(bool enabled)
    {
        _ignoreBuffEffectsDebuff = enabled;
        if (_ignoreBuffEffects && enabled)
            RemoveIgnoredBuffEffectsFromTargets();
        else if (!enabled)
        {
            RestoreIgnoredDebuffStatsForPlayer();
            RestoreIgnoredDebuffStatsForTrackedPartyMembers();
        }
    }
    private void SetIgnoreBuffEffectsBuff(bool enabled)
    {
        _ignoreBuffEffectsBuff = enabled;
        if (_ignoreBuffEffects && enabled)
            RemoveIgnoredBuffEffectsFromTargets();
    }
    private void SetIgnoreBuffEffectsIncludeParty(bool enabled)
    {
        _ignoreBuffEffectsIncludeParty = enabled;
        TickIgnoreBuffEffectsPartyTargets();
        if (_ignoreBuffEffects && enabled)
            RemoveIgnoredBuffEffectsFromTargets();
    }
    private void RemoveIgnoredBuffEffectsFromTargets()
    {
        try
        {
            RemoveIgnoredBuffEffects(GameAccess.Characters.PlayerCharacter);
        }
        catch { }

        TickIgnoreBuffEffectsPartyTargets();
        for (var i = 0; i < _ignoreBuffEffectsTrackedPartyMembers.Count; i++)
        {
            try { RemoveIgnoredBuffEffects(_ignoreBuffEffectsTrackedPartyMembers[i]); }
            catch { }
        }
    }
    private static void RestoreIgnoredDebuffStatsForPlayer()
    {
        try
        {
            RestoreIgnoredDebuffStats(GameAccess.Characters.PlayerCharacter);
        }
        catch { }
    }
    private static void RestoreIgnoredDebuffStats(Chara target)
    {
        if (target == null)
            return;
        try
        {
            target.CalcBurden();
        }
        catch { }
    }
    private void RestoreIgnoredDebuffStatsForTrackedPartyMembers()
    {
        for (var i = 0; i < _ignoreBuffEffectsTrackedPartyMembers.Count; i++)
            RestoreIgnoredDebuffStats(_ignoreBuffEffectsTrackedPartyMembers[i]);
    }
    private void RestoreAndClearIgnoredBuffEffectsTrackedPartyMembers()
    {
        RestoreIgnoredDebuffStatsForTrackedPartyMembers();
        _ignoreBuffEffectsTrackedPartyMembers.Clear();
    }
    private void TickIgnoreBuffEffectsPartyTargets()
    {
        if (!_ignoreBuffEffects || !_ignoreBuffEffectsIncludeParty)
        {
            if (_ignoreBuffEffectsTrackedPartyMembers.Count > 0)
                RestoreAndClearIgnoredBuffEffectsTrackedPartyMembers();
            return;
        }

        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            var party = pc?.party;
            var members = party?.members;
            if (pc == null || party == null || members == null)
            {
                RestoreAndClearIgnoredBuffEffectsTrackedPartyMembers();
                return;
            }

            for (var trackedIndex = _ignoreBuffEffectsTrackedPartyMembers.Count - 1; trackedIndex >= 0; trackedIndex--)
            {
                var tracked = _ignoreBuffEffectsTrackedPartyMembers[trackedIndex];
                var isCurrentMember = false;
                for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
                {
                    if (ReferenceEquals(members[memberIndex], tracked))
                    {
                        isCurrentMember = true;
                        break;
                    }
                }

                if (isCurrentMember && tracked != null && ReferenceEquals(tracked.party, party))
                    continue;
                RestoreIgnoredDebuffStats(tracked);
                _ignoreBuffEffectsTrackedPartyMembers.RemoveAt(trackedIndex);
            }

            for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                var member = members[memberIndex];
                if (member == null || ReferenceEquals(member, pc) || !ReferenceEquals(member.party, party))
                    continue;

                var alreadyTracked = false;
                for (var trackedIndex = 0; trackedIndex < _ignoreBuffEffectsTrackedPartyMembers.Count; trackedIndex++)
                {
                    if (ReferenceEquals(_ignoreBuffEffectsTrackedPartyMembers[trackedIndex], member))
                    {
                        alreadyTracked = true;
                        break;
                    }
                }
                if (alreadyTracked)
                    continue;

                _ignoreBuffEffectsTrackedPartyMembers.Add(member);
                RemoveIgnoredBuffEffects(member);
            }
        }
        catch { }
    }
    private static bool IsIgnoreBuffEffectsTarget(Chara target)
    {
        var instance = Instance;
        if (instance == null || !instance._ignoreBuffEffects || target == null)
            return false;
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            if (pc == null)
                return false;
            if (ReferenceEquals(target, pc))
                return true;
            if (!instance._ignoreBuffEffectsIncludeParty)
                return false;

            var party = pc.party;
            if (party == null || !ReferenceEquals(target.party, party))
                return false;
            var members = party.members;
            if (members == null)
                return false;
            for (var i = 0; i < members.Count; i++)
            {
                if (ReferenceEquals(members[i], target))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    private static bool ShouldIgnoreBuffEffect(Chara target, Condition condition)
    {
        var instance = Instance;
        if (instance == null || condition == null || !IsIgnoreBuffEffectsTarget(target))
            return false;

        try
        {
            if (condition is BaseBuff)
                return instance._ignoreBuffEffectsBuff;
            if (condition is BaseDebuff)
                return instance._ignoreBuffEffectsDebuff;
        }
        catch { }

        try
        {
            var type = condition.Type;
            if (instance._ignoreBuffEffectsDebuff &&
                (type == ConditionType.Bad ||
                 type == ConditionType.Debuff ||
                 type == ConditionType.Disease ||
                 type == ConditionType.Wrath ||
                 type == ConditionType.Sentence))
                return true;

            if (instance._ignoreBuffEffectsBuff &&
                (type == ConditionType.Buff || type == ConditionType.Stance))
                return true;
        }
        catch { }
        return false;
    }
    private static void RemoveIgnoredBuffEffects(Chara target)
    {
        if (!IsIgnoreBuffEffectsTarget(target))
            return;

        var instance = Instance;
        if (instance != null && instance._ignoreBuffEffectsDebuff)
        {
            try
            {
                var hunger = target.hunger;
                var hungerPhase = hunger == null ? -1 : hunger.GetPhase();
                if (hunger != null && (hungerPhase == StatsHunger.Bloated || hungerPhase >= StatsHunger.Hungry))
                    hunger.Set(FindMiddleStatsValueForPhase(hunger, StatsHunger.Normal, 50));

                var burden = target.burden;
                if (burden != null && burden.GetPhase() > StatsBurden.None)
                    burden.Set(0);

                var sleepiness = target.sleepiness;
                if (sleepiness != null && sleepiness.value > 0)
                    sleepiness.Set(0);
            }
            catch { }
        }

        try
        {
            var conditions = target.conditions;
            if (conditions == null)
                return;
            for (var i = conditions.Count - 1; i >= 0; i--)
            {
                Condition condition;
                try { condition = conditions[i]; }
                catch { continue; }
                if (!ShouldIgnoreBuffEffect(target, condition))
                    continue;
                try { condition.Kill(true); }
                catch { }
            }
        }
        catch { }
    }
    private static int GetStatsPhaseForValue(Stats stats, int value)
    {
        if (stats == null)
            return -1;

        try
        {
            var clampedValue = Mathf.Clamp(value, stats.min, stats.max);
            int phaseIndex;
            if (stats.id == 1)
            {
                phaseIndex = clampedValue >= 100 ? (clampedValue - 100) / 10 + 1 : 0;
                phaseIndex = Mathf.Clamp(phaseIndex, 0, 9);
            }
            else
            {
                phaseIndex = (int)Mathf.Clamp(10f * clampedValue / Mathf.Max(1, stats.max), 0f, 9f);
            }

            var phases = stats.source?.phase;
            if (phases == null || phases.Length == 0)
                return -1;
            return phases[Mathf.Clamp(phaseIndex, 0, phases.Length - 1)];
        }
        catch
        {
            return -1;
        }
    }
    private static int FindMiddleStatsValueForPhase(Stats stats, int desiredPhase, int fallback)
    {
        if (stats == null)
            return fallback;

        try
        {
            var first = -1;
            var last = -1;
            for (var value = stats.min; value <= stats.max; value++)
            {
                if (GetStatsPhaseForValue(stats, value) != desiredPhase)
                    continue;
                if (first < 0)
                    first = value;
                last = value;
            }
            if (first >= 0)
                return first + (last - first) / 2;
        }
        catch { }
        return Mathf.Clamp(fallback, stats.min, stats.max);
    }
    private static bool ShouldBlockIgnoredDebuffStatChange(Stats stats, int nextValue)
    {
        var instance = Instance;
        if (instance == null || !instance._ignoreBuffEffects || !instance._ignoreBuffEffectsDebuff || stats == null)
            return false;

        try
        {
            var target = BaseStats.CC;
            if (target == null || !IsIgnoreBuffEffectsTarget(target))
                return false;

            switch (stats.id)
            {
                case 0:
                    {
                        var phase = GetStatsPhaseForValue(stats, nextValue);
                        return phase == StatsHunger.Bloated || phase >= StatsHunger.Hungry;
                    }
                case 1:
                    return GetStatsPhaseForValue(stats, nextValue) > StatsBurden.None;
                case 4:
                    return nextValue > stats.value;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }
    private static bool IsInvincibleModeTarget(Chara target)
    {
        var instance = Instance;
        if (instance == null || !instance._invincibleMode || target == null)
            return false;

        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            if (pc == null)
                return false;
            if (ReferenceEquals(target, pc))
                return true;
            if (!instance._invincibleModeIncludeParty)
                return false;

            var party = pc.party;
            if (party == null || !ReferenceEquals(target.party, party))
                return false;
            var members = party.members;
            if (members == null)
                return false;
            for (var i = 0; i < members.Count; i++)
            {
                if (ReferenceEquals(members[i], target))
                    return true;
            }
        }
        catch { }
        return false;
    }
}
