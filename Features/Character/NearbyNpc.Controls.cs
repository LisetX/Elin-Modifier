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
    private Chara? GetCurrentDataTarget()
    {
        if (_targetTab == 0)
            return GameAccess.Characters.PlayerCharacter;
        if (_targetTab == 1)
            return GetTalkingNpc();
        return GetSelectedNearbyNpc();
    }
    private List<NearbyNpcEntry> GetSortedNearbyNpcs()
    {
        RefreshNearbyNpcCacheIfNeeded();
        return _nearbyNpcCache;
    }
    private void RefreshNearbyNpcCacheIfNeeded()
    {
        try
        {
            var map = GameAccess.World.CurrentMap;
            var charas = map?.charas;
            var count = charas == null ? -1 : charas.Count;
            var interval = _lowPerformanceMode ? LowPerformanceUiValueCacheFrames : NearbyNpcCacheFrames;
            var frame = Time.frameCount;
            if (!_nearbyNpcCacheDirty &&
                ReferenceEquals(_nearbyNpcCacheMap, map) &&
                _nearbyNpcCacheCharaCount == count &&
                string.Equals(_nearbyNpcCacheFilter, _nearbyNpcFilter, StringComparison.Ordinal) &&
                string.Equals(_nearbyNpcCacheLanguage, _language, StringComparison.Ordinal) &&
                frame - _nearbyNpcCacheFrame < interval)
                return;

            _nearbyNpcCache.Clear();
            _nearbyNpcCacheMap = map;
            _nearbyNpcCacheCharaCount = count;
            _nearbyNpcCacheFilter = _nearbyNpcFilter;
            _nearbyNpcCacheLanguage = _language;
            _nearbyNpcCacheFrame = frame;
            _nearbyNpcCacheDirty = false;

            var pc = GameAccess.Characters.PlayerCharacter;
            if (charas == null)
                return;

            for (var i = 0; i < charas.Count; i++)
            {
                var chara = charas[i];
                if (!CanSelectNearbyNpc(chara, pc))
                    continue;
                var entry = CreateNearbyNpcEntry(chara);
                if (!PassNearbyNpcFilter(entry))
                    continue;
                _nearbyNpcCache.Add(entry);
            }
            _nearbyNpcCache.Sort(CompareNearbyNpc);
        }
        catch
        {
            _nearbyNpcCache.Clear();
        }
    }
    private void InvalidateNearbyNpcCache()
    {
        _nearbyNpcCacheDirty = true;
    }
    private static bool CanSelectNearbyNpc(Chara? chara, Chara? pc)
    {
        if (chara == null)
            return false;
        try
        {
            if (chara.isDestroyed || chara.IsPC)
                return false;
            if (pc != null && ReferenceEquals(chara, pc))
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
    private NearbyNpcEntry CreateNearbyNpcEntry(Chara chara)
    {
        var uid = GetCharaUid(chara);
        var name = SafeName(chara);
        var charaId = SafeCharaId(chara);
        var hostility = GetNpcHostilityLabel(chara);
        var affinity = GetNpcAffinityValue(chara);
        var label = name +
                    " [" + charaId + " / UID " + uid.ToString(CultureInfo.InvariantCulture) + "] " +
                    T("关系状态", "Relationship") + ": " + hostility + " / " +
                    T("好感度", "Affinity") + ": " + affinity.ToString(CultureInfo.InvariantCulture);
        return new NearbyNpcEntry(chara, uid, name, charaId, hostility, affinity, GetNearbyNpcFollowRank(chara), GetNearbyNpcRelationshipRank(chara), label);
    }
    private bool PassNearbyNpcFilter(NearbyNpcEntry entry)
    {
        if (string.IsNullOrEmpty(_nearbyNpcFilter))
            return true;

        var filter = _nearbyNpcFilter.ToLowerInvariant();
        return entry.Name.ToLowerInvariant().Contains(filter) ||
               entry.Id.ToLowerInvariant().Contains(filter) ||
               entry.Uid.ToString(CultureInfo.InvariantCulture).Contains(filter) ||
               entry.HostilityLabel.ToLowerInvariant().Contains(filter) ||
               entry.Affinity.ToString(CultureInfo.InvariantCulture).Contains(filter);
    }
    private static int CompareNearbyNpc(NearbyNpcEntry a, NearbyNpcEntry b)
    {
        if (a.FollowRank != b.FollowRank) return b.FollowRank.CompareTo(a.FollowRank);

        if (a.RelationshipRank != b.RelationshipRank) return b.RelationshipRank.CompareTo(a.RelationshipRank);

        if (a.Affinity != b.Affinity) return b.Affinity.CompareTo(a.Affinity);

        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }
    private static int GetNearbyNpcFollowRank(Chara chara)
    {
        try
        {
            if (chara.IsPCParty) return 3;
            if (chara.IsPCPartyMinion) return 2;
            if (chara.IsPCFaction) return 1;
        }
        catch
        {
        }
        return 0;
    }
    private static int GetNearbyNpcRelationshipRank(Chara chara)
    {
        try
        {
            switch (chara.hostility)
            {
                case Hostility.Ally: return 4;
                case Hostility.Friend: return 3;
                case Hostility.Neutral: return 2;
                case Hostility.Enemy: return 1;
                default: return 0;
            }
        }
        catch
        {
            return 0;
        }
    }
    private Chara? GetSelectedNearbyNpc()
    {
        var npcs = GetSortedNearbyNpcs();
        var selected = FindNearbyNpcEntryByUid(npcs, _nearbyNpcSelectedUid);
        if (selected != null)
            return selected.Chara;
        if (npcs.Count == 0)
            return null;
        _nearbyNpcSelectedUid = npcs[0].Uid;
        return npcs[0].Chara;
    }
    private static NearbyNpcEntry? FindNearbyNpcEntryByUid(List<NearbyNpcEntry> npcs, int uid)
    {
        if (uid < 0)
            return null;
        for (var i = 0; i < npcs.Count; i++)
        {
            var npc = npcs[i];
            if (npc.Uid == uid)
                return npc;
        }
        return null;
    }
}
