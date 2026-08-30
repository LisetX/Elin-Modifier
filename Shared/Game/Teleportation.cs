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
    private List<TeleportZoneEntry> GetFilteredTeleportZones()
    {
        try
        {
            var region = GetCurrentRegion();
            var zones = GameAccess.Runtime.Game?.spatials?.Zones;
            if (region == null || zones == null)
                return _emptyTeleportZoneCache;

            var filter = (_teleportFilter ?? "").Trim().ToLowerInvariant();
            var sourceCount = zones.Count;
            if (_teleportZoneCacheDirty ||
                !ReferenceEquals(_teleportAllZoneCacheRegion, region) ||
                _teleportAllZoneCacheSourceCount != sourceCount)
            {
                _teleportAllZoneCache.Clear();
                for (var i = 0; i < zones.Count; i++)
                {
                    var zone = zones[i];
                    if (!CanTeleportToZone(zone, region))
                        continue;
                    _teleportAllZoneCache.Add(CreateTeleportZoneEntry(zone));
                }

                var homeZoneUids = GetTeleportHomeZoneUids();
                _teleportAllZoneCache.Sort((a, b) => CompareTeleportZoneEntries(a, b, homeZoneUids));
                _teleportAllZoneCacheRegion = region;
                _teleportAllZoneCacheSourceCount = sourceCount;
                _teleportZoneCacheDirty = false;
                _teleportFilterCacheDirty = true;
            }

            if (!_teleportFilterCacheDirty &&
                string.Equals(_teleportZoneCacheFilter, filter, StringComparison.Ordinal))
            {
                return _teleportZoneCache;
            }

            _teleportZoneCache.Clear();
            for (var i = 0; i < _teleportAllZoneCache.Count; i++)
            {
                var entry = _teleportAllZoneCache[i];
                if (!PassTeleportZoneFilter(entry, filter))
                    continue;
                _teleportZoneCache.Add(entry);
            }
            _teleportZoneCacheFilter = filter;
            _teleportFilterCacheDirty = false;
        }
        catch { }
        return _teleportZoneCache;
    }
    private static Region? GetCurrentRegion()
    {
        try
        {
            var zone = GameAccess.World.CurrentZone ?? GameAccess.Characters.PlayerCharacter?.currentZone;
            return zone?.Region ?? GameAccess.World.CurrentRegion;
        }
        catch
        {
            return null;
        }
    }
    private static bool IsPlayerOnWorldMap()
    {
        try
        {
            var zone = GameAccess.World.CurrentZone ?? GameAccess.Characters.PlayerCharacter?.currentZone;
            if (zone == null)
                return false;
            if (zone is Region)
                return true;
            var region = GameAccess.World.CurrentRegion;
            return region != null && ReferenceEquals(zone, region);
        }
        catch
        {
            return false;
        }
    }
    private static bool CanTeleportToZone(Zone? zone, Region region)
    {
        if (zone == null || region == null)
            return false;
        try
        {
            if (ReferenceEquals(zone, region))
                return false;
            if (!ReferenceEquals(zone.parent, region))
                return false;
            if (zone.IsInstance || zone.IsClosed)
                return false;
            if (zone.HiddenInRegionMap)
                return false;
            if (zone.x == 0 && zone.y == 0)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
    private bool PassTeleportZoneFilter(TeleportZoneEntry entry, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;
        return entry != null && entry.SearchText.Contains(filter);
    }
    private TeleportZoneEntry CreateTeleportZoneEntry(Zone zone)
    {
        var name = SafeText(() => zone.Name, "???");
        var id = SafeText(() => zone.id, "");
        var uid = SafeText(() => zone.uid.ToString(CultureInfo.InvariantCulture), "");
        var x = SafeInt(() => zone.x, 0);
        var y = SafeInt(() => zone.y, 0);
        var xy = x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
        var lv = SafeInt(() => zone.DangerLv, 0);
        var label = name + " [" + id + "]  " + xy + "  Lv." + lv.ToString(CultureInfo.InvariantCulture);
        var search = (name + " " + id + " " + uid + " " + xy).ToLowerInvariant();
        return new TeleportZoneEntry(zone, label, search, x, y, name);
    }
    private static int CompareTeleportZoneEntries(TeleportZoneEntry a, TeleportZoneEntry b, HashSet<int>? homeZoneUids = null)
    {
        if (a == null || b == null)
            return a == null ? (b == null ? 0 : 1) : -1;
        try
        {
            var priorityA = GetTeleportZonePriority(a.Zone, homeZoneUids);
            var priorityB = GetTeleportZonePriority(b.Zone, homeZoneUids);
            if (priorityA != priorityB) return priorityA.CompareTo(priorityB);
            if (a.X != b.X) return a.X.CompareTo(b.X);
            if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return 0;
        }
    }
    private static int GetTeleportZonePriority(Zone zone, HashSet<int>? homeZoneUids = null)
    {
        if (IsTeleportHomeZone(zone, homeZoneUids))
            return 0;
        if (IsTeleportCityZone(zone))
            return 1;
        return 2;
    }
    private static HashSet<int> GetTeleportHomeZoneUids()
    {
        var result = new HashSet<int>();
        try
        {
            var homes = GetPlayerHomeBranches();
            foreach (var home in homes)
            {
                var homeZone = GetHomeBranchOwner(home);
                if (homeZone == null)
                    continue;
                try { result.Add(homeZone.uid); } catch { }
            }
        }
        catch { }
        return result;
    }
    private static bool IsTeleportHomeZone(Zone? zone, HashSet<int>? homeZoneUids = null)
    {
        if (zone == null)
            return false;
        try
        {
            if (homeZoneUids != null && homeZoneUids.Contains(zone.uid))
                return true;
        }
        catch { }

        try
        {
            if (zone.IsPCFaction)
                return true;
        }
        catch { }

        return false;
    }
    private static bool IsTeleportCityZone(Zone? zone)
    {
        if (zone == null)
            return false;
        try
        {
            var text = (zone.GetType().Name + " " +
                        SafeText(() => zone.id, "") + " " +
                        SafeText(() => zone.Name, "") + " " +
                        GetTeleportZoneMemberText(zone)).ToLowerInvariant();
            return text.Contains("city") ||
                   text.Contains("town") ||
                   text.Contains("village") ||
                   text.Contains("都市") ||
                   text.Contains("街") ||
                   text.Contains("町") ||
                   text.Contains("村") ||
                   text.Contains("城市") ||
                   text.Contains("城镇") ||
                   text.Contains("村庄");
        }
        catch
        {
            return false;
        }
    }
    private static string GetTeleportZoneMemberText(Zone zone)
    {
        var parts = new List<string>();
        AddTeleportZoneMemberText(parts, zone, "category");
        AddTeleportZoneMemberText(parts, zone, "categorySub");
        AddTeleportZoneMemberText(parts, zone, "type");
        AddTeleportZoneMemberText(parts, zone, "tag");
        AddTeleportZoneMemberText(parts, zone, "tags");
        return string.Join(" ", parts.ToArray());
    }
    private static void AddTeleportZoneMemberText(List<string> parts, Zone zone, string name)
    {
        if (parts == null || zone == null || string.IsNullOrEmpty(name))
            return;
        try
        {
            var value = GetMemberValue(zone, name);
            if (value == null)
                return;
            if (value is string text)
            {
                if (!string.IsNullOrEmpty(text))
                    parts.Add(text);
                return;
            }
            if (value is IEnumerable enumerable && !(value is string))
            {
                foreach (var item in enumerable)
                    if (item != null)
                        parts.Add(item.ToString());
                return;
            }
            parts.Add(value.ToString());
        }
        catch { }
    }
    private void QueueTeleportToZone(Zone zone)
    {
        if (zone == null)
        {
            _log = T("传送请求失败: ", "Teleport request failed: ") + T("未找到可传送地标", "No teleportable landmarks found");
            return;
        }
        if (!IsPlayerOnWorldMap())
        {
            _log = T("传送请求失败: ", "Teleport request failed: ") + T("传送功能仅限人物处于世界板块中使用，地图区块内无法使用", "Teleport can only be used while the character is on the world map. It cannot be used inside map zones.");
            return;
        }
        _pendingTeleportRequest = PendingTeleportRequest.ForZone(zone);
        _log = T("已提交传送请求: ", "Queued teleport: ") + SafeText(() => zone.Name, "???");
    }
    private void QueueTeleportToWorldPosition()
    {
        if (!IsPlayerOnWorldMap())
        {
            _log = T("传送请求失败: ", "Teleport request failed: ") + T("传送功能仅限人物处于世界板块中使用，地图区块内无法使用", "Teleport can only be used while the character is on the world map. It cannot be used inside map zones.");
            return;
        }

        if (!int.TryParse((_teleportXInput ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse((_teleportYInput ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            _log = T("位置输入不是数字", "Position input is not a number");
            return;
        }

        _pendingTeleportRequest = PendingTeleportRequest.ForWorldPosition(x, y);
        _log = T("已提交传送请求: ", "Queued teleport: ") + x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
    }
    private void ExecutePendingTeleportRequest()
    {
        var request = _pendingTeleportRequest;
        if (request == null)
            return;

        _pendingTeleportRequest = null;
        try
        {
            if (request.TargetZone != null)
                TeleportPlayerToZone(request.TargetZone);
            else
                TeleportPlayerToWorldPosition(request.X, request.Y);
        }
        catch (Exception ex)
        {
            _log = T("传送失败: ", "Teleport failed: ") + ex.Message;
        }
    }
    private void TeleportPlayerToZone(Zone zone)
    {
        var pc = GameAccess.Characters.PlayerCharacter;
        var player = GameAccess.Runtime.Player;
        if (pc == null || player == null || zone == null)
        {
            _log = T("传送失败: ", "Teleport failed: ") + T("未获取到人物数据", "No character data");
            return;
        }
        if (!IsPlayerOnWorldMap())
        {
            _log = T("传送失败: ", "Teleport failed: ") + T("传送功能仅限人物处于世界板块中使用，地图区块内无法使用", "Teleport can only be used while the character is on the world map. It cannot be used inside map zones.");
            return;
        }

        var region = GetCurrentRegion();
        if (region == null)
        {
            _log = T("传送失败: ", "Teleport failed: ") + "Region null";
            return;
        }
        if (!CanTeleportToZone(zone, region))
        {
            _log = T("传送失败: ", "Teleport failed: ") + T("未找到可传送地标", "No teleportable landmarks found");
            return;
        }

        var worldPoint = zone.RegionPos ?? new Point(zone.x, zone.y);
        pc.MoveImmediate(worldPoint, true, true);
        player.lastZonePos = null;
        _log = T("已传送至地标: ", "Teleported to landmark: ") + SafeText(() => zone.Name, "???");
    }
    private void TeleportPlayerToWorldPosition(int x, int y)
    {
        var pc = GameAccess.Characters.PlayerCharacter;
        var player = GameAccess.Runtime.Player;
        var region = GetCurrentRegion();
        if (pc == null || player == null || region == null)
        {
            _log = T("传送失败: ", "Teleport failed: ") + T("未获取到人物数据", "No character data");
            return;
        }
        if (!IsPlayerOnWorldMap())
        {
            _log = T("传送失败: ", "Teleport failed: ") + T("传送功能仅限人物处于世界板块中使用，地图区块内无法使用", "Teleport can only be used while the character is on the world map. It cannot be used inside map zones.");
            return;
        }

        pc.MoveImmediate(new Point(x, y), true, true);
        player.lastZonePos = null;
        _log = T("已传送至位置: ", "Teleported to position: ") + x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
    }
}
