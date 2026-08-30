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
    private string GetKillGrowthAttributeName(int elementId)
    {
        try
        {
            SourceElement.Row row;
            if (GameAccess.Sources.Manager != null && GameAccess.Sources.Elements != null && GameAccess.Sources.Elements.map.TryGetValue(elementId, out row))
                return GetElementDisplayName(row);
        }
        catch { }

        foreach (var row in _attributeRows)
        {
            if (row.Key == elementId.ToString(CultureInfo.InvariantCulture))
                return GetRowLabel(row);
        }
        return elementId.ToString(CultureInfo.InvariantCulture);
    }
    private void SetKillGrowthEnabled(bool enabled)
    {
        _killGrowthEnabled = enabled;
        RefreshKillGrowthAffectedCharacters();
        _log = enabled
            ? T("击杀成长已开启", "Kill growth enabled")
            : T("击杀成长已关闭", "Kill growth disabled");
    }
    private void SetKillGrowthSharedExperience(bool enabled)
    {
        _killGrowthSharedExperience = enabled;
        RefreshKillGrowthAffectedCharacters();
        _log = enabled
            ? T("共享经验已开启", "Shared EXP enabled")
            : T("共享经验已关闭", "Shared EXP disabled");
    }
    private IEnumerable<Chara> EnumerateKillGrowthCharacters()
    {
        var seen = new HashSet<int>();
        Chara pc = null;
        try { pc = GameAccess.Characters.PlayerCharacter; } catch { }
        if (pc != null)
        {
            seen.Add(pc.uid);
            yield return pc;
        }

        List<Chara> members = null;
        try { members = GameAccess.Characters.PlayerCharacter?.party?.members; } catch { }
        if (members == null)
            yield break;

        foreach (var member in members)
        {
            if (member == null)
                continue;
            int uid;
            try { uid = member.uid; } catch { continue; }
            if (seen.Add(uid))
                yield return member;
        }
    }
    private decimal GetKillGrowthExperience(Chara chara)
    {
        if (chara == null)
            return 0m;
        if (string.IsNullOrEmpty(_killGrowthActiveSaveId))
            EnsureKillGrowthSaveContext(true);
        decimal value;
        try { return _killGrowthExpByUid.TryGetValue(chara.uid, out value) ? NormalizeKillGrowthExperience(value) : 0m; }
        catch { return 0m; }
    }
    private void TickKillGrowthSaveContext()
    {
        var now = Time.realtimeSinceStartup;
        if (now < _killGrowthNextSaveContextCheckAt)
            return;

        _killGrowthNextSaveContextCheckAt = now + 1f;
        EnsureKillGrowthSaveContext(true);
    }
    private bool EnsureKillGrowthSaveContext(bool persistMigration)
    {
        var saveId = GetCurrentKillGrowthSaveId();
        if (string.IsNullOrEmpty(saveId))
        {
            _killGrowthActiveSaveId = "";
            _killGrowthExpByUid = new Dictionary<int, decimal>();
            return false;
        }

        var contextChanged = !string.Equals(_killGrowthActiveSaveId, saveId, StringComparison.Ordinal);
        if (contextChanged)
        {
            Dictionary<int, decimal> saveExp;
            if (!_killGrowthExpBySaveId.TryGetValue(saveId, out saveExp))
            {
                saveExp = new Dictionary<int, decimal>();
                _killGrowthExpBySaveId[saveId] = saveExp;
            }

            _killGrowthActiveSaveId = saveId;
            _killGrowthExpByUid = saveExp;
        }

        if (_killGrowthLegacyMigrationPending)
        {
            foreach (var pair in _killGrowthLegacyExpByUid)
            {
                if (pair.Value <= 0m)
                    continue;

                decimal existing;
                _killGrowthExpByUid.TryGetValue(pair.Key, out existing);
                _killGrowthExpByUid[pair.Key] = NormalizeKillGrowthExperience(Math.Max(existing, pair.Value));
            }

            _killGrowthLegacyExpByUid.Clear();
            _killGrowthLegacyMigrationPending = false;
            _killGrowthSaveMigrationWritePending = true;
            contextChanged = true;
        }

        if (contextChanged)
            RefreshKillGrowthAffectedCharacters();

        if (persistMigration && _killGrowthSaveMigrationWritePending && !_killGrowthSaveMigrationWriteInProgress)
        {
            try
            {
                _killGrowthSaveMigrationWriteInProgress = true;
                SaveConfig(false);
                _killGrowthSaveMigrationWritePending = false;
            }
            finally
            {
                _killGrowthSaveMigrationWriteInProgress = false;
            }
        }

        return true;
    }
    private static string GetCurrentKillGrowthSaveId()
    {
        if (!HasCharacterData())
            return "";

        try
        {
            var slot = Game.id;
            if (!string.IsNullOrWhiteSpace(slot))
                return slot.Trim();
        }
        catch { }

        try
        {
            var savePath = GameIO.pathCurrentSave;
            if (!string.IsNullOrWhiteSpace(savePath))
            {
                var normalized = savePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var folderName = Path.GetFileName(normalized);
                if (!string.IsNullOrWhiteSpace(folderName))
                    return folderName.Trim();
            }
        }
        catch { }

        return "";
    }
    private long GetKillGrowthLevel(Chara chara)
    {
        return GetKillGrowthLevelFromExp(GetKillGrowthExperience(chara));
    }
    private long GetKillGrowthEffectiveLevel(Chara chara)
    {
        if (!_killGrowthSharedExperience)
            return GetKillGrowthLevel(chara);

        long total = 0;
        foreach (var member in EnumerateKillGrowthCharacters())
        {
            if (!IsKillGrowthEligibleKiller(member))
                continue;

            var level = GetKillGrowthLevel(member);
            if (level <= 0)
                continue;

            if (long.MaxValue - total < level)
                return long.MaxValue;
            total += level;
        }
        return total;
    }
    private long GetKillGrowthLevelFromExp(decimal exp)
    {
        var roundedExp = decimal.Round(Math.Max(0m, exp), 0, MidpointRounding.AwayFromZero);
        var expPerLevel = Math.Max(0.01m, _killGrowthExpPerLevel);
        var level = Math.Floor(roundedExp / expPerLevel);
        if (level <= 0m)
            return 0;
        return level > long.MaxValue ? long.MaxValue : (long)level;
    }
    private int GetKillGrowthConfiguredAttributeBonus(int elementId)
    {
        int value;
        return _killGrowthAttributeBonus.TryGetValue(elementId, out value) ? Math.Max(0, value) : 0;
    }
    private static bool IsKillGrowthAttribute(int elementId)
    {
        for (var i = 0; i < KillGrowthAttributeIds.Length; i++)
            if (KillGrowthAttributeIds[i] == elementId)
                return true;
        return false;
    }
    private static bool IsKillGrowthEligibleKiller(Chara chara)
    {
        if (chara == null)
            return false;
        try { return chara.IsPCParty && !chara.isDead; }
        catch { return false; }
    }
    private static bool IsKillGrowthEligibleVictim(Chara victim, Chara killer)
    {
        if (victim == null || killer == null || victim == killer)
            return false;
        try { return !victim.isDead; }
        catch { return true; }
    }
    private static int GetKillGrowthMainAttributeTotal(Chara chara)
    {
        if (chara == null)
            return 0;

        var total = 0;
        for (var i = 0; i < KillGrowthAttributeIds.Length; i++)
        {
            try { total += Math.Max(0, chara.Evalue(KillGrowthAttributeIds[i])); }
            catch { }
        }
        return Math.Max(total, GetKillGrowthLevelBasedAttributeTotal(chara));
    }
    private static int GetKillGrowthLevelBasedAttributeTotal(Chara chara)
    {
        if (chara == null)
            return 0;

        var level = 1;
        try { level = Math.Max(level, chara.LV); } catch { }
        try { level = Math.Max(level, chara.genLv); } catch { }
        return Math.Max(1, level) * KillGrowthAttributeIds.Length + 80;
    }
    private static int GetKillGrowthAttributeBonus(Chara chara, int elementId)
    {
        var instance = Instance;
        if (instance == null || !instance._killGrowthEnabled || !IsKillGrowthAttribute(elementId) || !IsKillGrowthEligibleKiller(chara))
            return 0;

        var level = instance.GetKillGrowthEffectiveLevel(chara);
        if (level <= 0)
            return 0;

        var perLevel = instance.GetKillGrowthConfiguredAttributeBonus(elementId);
        if (perLevel <= 0)
            return 0;

        var bonus = level * perLevel;
        return bonus > int.MaxValue ? int.MaxValue : (int)bonus;
    }
    private static KillGrowthKillState CreateKillGrowthKillState(Chara victim, Card origin)
    {
        var instance = Instance;
        if (instance == null || !instance._killGrowthEnabled || victim == null || origin == null || !origin.isChara)
            return null;

        Chara killer;
        try { killer = origin.Chara; } catch { return null; }
        if (!IsKillGrowthEligibleKiller(killer) || !IsKillGrowthEligibleVictim(victim, killer))
            return null;

        return new KillGrowthKillState(killer, victim, GetKillGrowthMainAttributeTotal(killer), GetKillGrowthMainAttributeTotal(victim));
    }
    private static void AwardKillGrowthExperience(KillGrowthKillState state, Chara victim)
    {
        var instance = Instance;
        if (instance == null || state == null || !instance._killGrowthEnabled || victim == null)
            return;

        try
        {
            if (!victim.isDead || !IsKillGrowthEligibleKiller(state.Killer))
                return;

            if (!instance.EnsureKillGrowthSaveContext(true))
                return;

            var exp = instance.CalculateKillGrowthExperience(state);
            if (exp <= 0)
                return;

            decimal current;
            var uid = state.Killer.uid;
            instance._killGrowthExpByUid.TryGetValue(uid, out current);
            var next = NormalizeKillGrowthExperience(Math.Max(0m, current) + exp);
            instance._killGrowthExpByUid[uid] = next;
            instance.RefreshKillGrowthCharacter(state.Killer);
            instance.SaveConfig(false);
        }
        catch { }
    }
    private decimal CalculateKillGrowthExperience(KillGrowthKillState state)
    {
        if (state == null || _killGrowthBaseExp <= 0m)
            return 0m;

        var enemyTotal = Math.Max(1, state.EnemyMainAttributeTotal);
        var killerTotal = Math.Max(1, state.KillerMainAttributeTotal);
        var multiplier = GetKillGrowthExperienceMultiplierPercent(enemyTotal, killerTotal);
        var exp = NormalizeKillGrowthExperience(_killGrowthBaseExp * multiplier / 100m);
        if (exp <= 0m && _killGrowthBaseExp > 0m)
            exp = 0.01m;
        return exp;
    }
    private static int GetKillGrowthExperienceMultiplierPercent(long enemyTotal, long killerTotal)
    {
        enemyTotal = Math.Max(1, enemyTotal);
        killerTotal = Math.Max(1, killerTotal);
        var scaledEnemy = enemyTotal * 100L;

        if (scaledEnemy < killerTotal * 50L) return 50;
        if (scaledEnemy > killerTotal * 300L) return 2000;
        if (scaledEnemy == killerTotal * 300L) return 500;
        if (scaledEnemy > killerTotal * 275L) return 450;
        if (scaledEnemy > killerTotal * 250L) return 400;
        if (scaledEnemy > killerTotal * 225L) return 350;
        if (scaledEnemy > killerTotal * 200L) return 300;
        if (scaledEnemy > killerTotal * 175L) return 250;
        if (scaledEnemy > killerTotal * 150L) return 200;
        if (scaledEnemy > killerTotal * 125L) return 150;
        return 100;
    }
    private static decimal ClampKillGrowthDecimal(decimal value, decimal min, decimal max)
    {
        if (value < min) value = min;
        if (value > max) value = max;
        return NormalizeKillGrowthExperience(value);
    }
    private static decimal NormalizeKillGrowthExperience(decimal value)
    {
        if (value <= 0m)
            return 0m;
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }
    private static string FormatKillGrowthDecimal(decimal value)
    {
        return NormalizeKillGrowthExperience(value).ToString("0.##", CultureInfo.InvariantCulture);
    }
    private void RefreshKillGrowthAffectedCharacters()
    {
        foreach (var chara in EnumerateKillGrowthCharacters())
            RefreshKillGrowthCharacter(chara);
    }
    private void RefreshKillGrowthCharacter(Chara chara)
    {
        if (chara == null)
            return;
        try { chara.Refresh(false); } catch { }
        try { chara.CalculateMaxStamina(); } catch { }
        try { chara.SetDirtySpeed(); } catch { }
        try { InvalidateCachedUiValues(GetTargetCachePrefix(chara, chara.IsPC)); } catch { }
    }
}
