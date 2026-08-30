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
    private void SyncNpcRelationshipInputs(Chara target)
    {
        if (ReferenceEquals(_lastNpcRelationTarget, target))
            return;
        _lastNpcRelationTarget = target;
        _npcAffinityInput = GetNpcAffinity(target);
        _npcHostilityIndex = GetRelationshipIndex(target);
    }
    private string GetNpcAffinity(Chara target)
    {
        try { return target._affinity.ToString(CultureInfo.InvariantCulture); }
        catch { return "?"; }
    }
    private static int GetNpcAffinityValue(Chara target)
    {
        try { return target._affinity; }
        catch { return 0; }
    }
    private void SetNpcAffinity(Chara target, int value)
    {
        try
        {
            target._affinity = value;
            target.Refresh(false);
            _npcAffinityInput = value.ToString(CultureInfo.InvariantCulture);
            InvalidateNearbyNpcCache();
            _log = T("已设置好感度: ", "Set affinity: ") + value;
        }
        catch (Exception ex) { _log = T("好感度设置失败: ", "Set affinity failed: ") + ex.Message; }
    }
    private string GetNpcHostilityLabel(Chara target)
    {
        try { return GetHostilityLabel(target.hostility); }
        catch { return "?"; }
    }
    private void SetNpcHostility(Chara target, Hostility value)
    {
        try
        {
            target.SetHostility(value);
            target.Refresh(false);
            _npcHostilityIndex = GetRelationshipIndex(value);
            InvalidateNearbyNpcCache();
            _log = T("已设置关系状态: ", "Set relationship: ") + GetHostilityLabel(value);
        }
        catch (Exception ex) { _log = T("关系状态设置失败: ", "Set relationship failed: ") + ex.Message; }
    }
    private string GetNpcFaithDisplay(Chara target)
    {
        try
        {
            var id = target.idFaith ?? "";
            var faith = TryFindReligion(id);
            if (faith != null)
            {
                var name = SafeText(() => faith.Name, "");
                return string.IsNullOrEmpty(name) || string.Equals(name, id, StringComparison.OrdinalIgnoreCase)
                    ? id
                    : name + " (" + id + ")";
            }
            return string.IsNullOrEmpty(id) ? "-" : id;
        }
        catch
        {
            return "?";
        }
    }
    internal static Religion? TryFindReligion(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        try
        {
            var dict = GameAccess.Runtime.Game?.religions?.dictAll;
            if (dict != null && dict.TryGetValue(id, out var religion))
                return religion;
        }
        catch
        {
        }
        return null;
    }
    private void SetNpcFaith(Chara target, FaithDef faith)
    {
        try
        {
            target.SetFaith(faith.Id);
            target.Refresh(false);
            InvalidateCachedUiValues(GetTargetCachePrefix(target, false));
            _log = T("已设置信仰: ", "Set faith: ") + SafeName(target) + " = " + faith.DisplayName;
        }
        catch (Exception ex)
        {
            _log = T("信仰设置失败: ", "Set faith failed: ") + ex.Message;
        }
    }
    private void SetPlayerFaith(Chara target, FaithDef faith)
    {
        try
        {
            var religion = TryFindReligion(faith.Id);
            if (religion == null)
            {
                _log = T("未找到信仰: ", "Faith not found: ") + faith.Id;
                return;
            }

            target.SetFaith(religion);
            RefreshGodArtifactFaithRestrictionState();
            target.Refresh(false);
            InvalidateCachedUiValues(GetTargetCachePrefix(target, true));
            var name = string.IsNullOrWhiteSpace(faith.Name) ? faith.Id : faith.Name;
            _log = T("已修改信仰: ", "Faith changed: ") + name;
        }
        catch (Exception ex)
        {
            _log = T("信仰设置失败: ", "Set faith failed: ") + ex.Message;
        }
    }
    private string GetNpcPartyState(Chara target)
    {
        try
        {
            var states = new List<string>();
            if (target.IsPCParty) states.Add(T("队伍中", "In party"));
            if (target.IsPCPartyMinion) states.Add(T("玩家随从", "PC minion"));
            if (target.IsPCFaction) states.Add(T("玩家阵营", "PC faction"));
            return states.Count == 0 ? T("未加入", "Not joined") : string.Join(" / ", states.ToArray());
        }
        catch { return "?"; }
    }
    private void MakeNpcPartyMember(Chara target)
    {
        try
        {
            if (target.IsPCParty)
            {
                _log = SafeName(target) + T(" 已经在队伍中", " is already in party");
                return;
            }
            target.MakePartyMemeber();
            target.Refresh(false);
            _npcHostilityIndex = GetRelationshipIndex(target);
            InvalidateNearbyNpcCache();
            _log = T("已加入队伍: ", "Joined party: ") + SafeName(target);
        }
        catch (Exception ex) { _log = T("加入队伍失败: ", "Join party failed: ") + ex.Message; }
    }
    private void RemoveNpcPartyMember(Chara target)
    {
        try
        {
            if (!target.IsPCParty)
            {
                _log = SafeName(target) + T(" 不在队伍中", " is not in party");
                return;
            }

            var party = target.party ?? GameAccess.Characters.PlayerCharacter?.party;
            if (party == null)
            {
                _log = T("离开队伍失败: ", "Leave party failed: ") + "party not found";
                return;
            }

            party.RemoveMember(target);
            target.Refresh(false);
            _npcHostilityIndex = GetRelationshipIndex(target);
            InvalidateNearbyNpcCache();
            _log = T("已离开队伍: ", "Left party: ") + SafeName(target);
        }
        catch (Exception ex) { _log = T("离开队伍失败: ", "Leave party failed: ") + ex.Message; }
    }
    private void AddNpcToPlayerFactionOnly(Chara target)
    {
        try
        {
            if (target.IsPCFaction)
            {
                _log = SafeName(target) + T(" 已经在玩家阵营", " is already in player faction");
                return;
            }

            target._MakeAlly();
            target.Refresh(false);
            _npcHostilityIndex = GetRelationshipIndex(target);
            InvalidateNearbyNpcCache();
            _log = T("已加入玩家阵营: ", "Joined player faction: ") + SafeName(target);
        }
        catch (Exception ex) { _log = T("加入阵营失败: ", "Join faction failed: ") + ex.Message; }
    }
    private void RemoveNpcFromPlayerFactionOnly(Chara target)
    {
        try
        {
            if (!target.IsPCFaction)
            {
                _log = SafeName(target) + T(" 不在玩家阵营", " is not in player faction");
                return;
            }

            TryRemoveFromPlayerBranch(target);
            ClearNpcFaction(target);
            target.SetHostility(Hostility.Friend);
            target.Refresh(false);
            _npcHostilityIndex = GetRelationshipIndex(target);
            InvalidateNearbyNpcCache();
            _log = T("已退出玩家阵营: ", "Left player faction: ") + SafeName(target);
        }
        catch (Exception ex) { _log = T("退出阵营失败: ", "Leave faction failed: ") + ex.Message; }
    }
    private static void ClearNpcFaction(Chara target)
    {
        target.faction = null;
        typeof(Chara).GetField("_faction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, null);
    }
    private static FactionBranch? GetPlayerBranch()
    {
        try
        {
            var branch = GameAccess.World.BranchOrHomeBranch;
            if (branch != null)
                return branch;
        }
        catch { }
        try { return GameAccess.Characters.PlayerCharacter?.homeBranch; }
        catch { return null; }
    }
    private static void TryAddToPlayerBranch(Chara target)
    {
        try { GetPlayerBranch()?.AddMemeber(target); }
        catch { }
    }
    private static void TryRemoveFromPlayerBranch(Chara target)
    {
        try { GetPlayerBranch()?.RemoveMemeber(target); }
        catch { }
    }
    private int GetRelationshipIndex(Chara target)
    {
        try { return GetRelationshipIndex(target.hostility); }
        catch { return 1; }
    }
    private static int GetRelationshipIndex(Hostility value)
    {
        for (var i = 0; i < RelationshipOptions.Length; i++)
            if (RelationshipOptions[i].Value == value)
                return i;
        return 1;
    }
    private string GetRelationshipLabel(RelationshipOption option)
    {
        return GetHostilityLabel(option.Value);
    }
    private string GetHostilityLabel(Hostility value)
    {
        switch (value)
        {
            case Hostility.Enemy: return T("敌对", "Enemy");
            case Hostility.Neutral: return T("中立", "Neutral");
            case Hostility.Friend: return T("友好", "Friend");
            case Hostility.Ally: return T("盟友", "Ally");
            default: return value.ToString();
        }
    }
    private static object FindStats(object owner)
    {
        if (owner == null) return null;
        var t = owner.GetType();
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (f.FieldType == typeof(Stats) || f.FieldType.Name == "Stats")
                return f.GetValue(owner);
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (p.CanRead && (p.PropertyType == typeof(Stats) || p.PropertyType.Name == "Stats"))
                return p.GetValue(owner, null);
        return null;
    }
}
