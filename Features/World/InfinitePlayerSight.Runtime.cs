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
    private void ApplyInfinitePlayerSight()
    {
        if (!_infinitePlayerSight) return;
        if (!HasActiveMapContext())
        {
            ResetInfinitePlayerSightState();
            return;
        }

        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            var map = GameAccess.World.CurrentMap;
            if (pc == null || map == null)
            {
                ResetInfinitePlayerSightState();
                return;
            }

            var size = map.Size;
            if (size <= 0)
            {
                ResetInfinitePlayerSightState();
                return;
            }
            var sameMap = ReferenceEquals(_infinitePlayerSightMap, map);
            var needsFullApply = !_infinitePlayerSightApplied || !sameMap || _infinitePlayerSightPointCount <= 0;
            var now = SchedulerNow;
            var watchdogInterval = _lowPerformanceMode ? 4f : 2f;
            if (!needsFullApply &&
                (now < _infinitePlayerSightLastWatchdogTime || now - _infinitePlayerSightLastWatchdogTime >= watchdogInterval))
            {
                _infinitePlayerSightLastWatchdogTime = now;
                needsFullApply = NeedsInfinitePlayerSightCellRepair(map, pc, size);
            }

            if (needsFullApply)
            {
                _infinitePlayerSightPointCount = ApplyInfinitePlayerSightCells(map, size, true);
                _infinitePlayerSightLastWatchdogTime = now;
            }

            ApplyInfinitePlayerSightCharas(pc, map);
            _infinitePlayerSightApplied = true;
            _infinitePlayerSightMap = map;
        }
        catch (Exception ex)
        {
            if (HasActiveMapContext())
                _log = T("无视迷雾+无限视野失败: ", "Ignore fog + infinite sight failed: ") + ex.Message;
            else
                ResetInfinitePlayerSightState();
        }
    }
    private static bool HasActiveMapContext()
    {
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            var map = GameAccess.World.CurrentMap;
            var player = GameAccess.Runtime.Player;
            return pc != null &&
                   !pc.isDestroyed &&
                   map != null &&
                   player != null &&
                   map.Size > 0;
        }
        catch
        {
            return false;
        }
    }
    private void ResetInfinitePlayerSightState()
    {
        _infinitePlayerSightApplied = false;
        _infinitePlayerSightMap = null;
        _infinitePlayerSightPointCount = 0;
        _infinitePlayerSightSavedTelepathy = false;
        _infinitePlayerSightOriginalTelepathy = false;
        _infinitePlayerSightCharaMap = null;
        _infinitePlayerSightOriginalTelepathyVisibility.Clear();
        _infinitePlayerSightObservedCharaCount = -1;
        _infinitePlayerSightLastCharaAuditTime = -9999f;
        _infinitePlayerSightLastWatchdogTime = -9999f;
    }
    private static int ApplyInfinitePlayerSightCells(Map map, int size, bool markSeen)
    {
        var pointCount = 0;
        var cells = map.cells;
        if (cells == null)
            return 0;
        for (var z = 0; z < size; z++)
        {
            for (var x = 0; x < size; x++)
            {
                var cell = cells[x, z];
                if (cell == null || cell.outOfBounds)
                    continue;

                pointCount++;
                if (markSeen && !cell.isSeen)
                    map.SetSeen(x, z, true, false);
            }
        }
        return pointCount;
    }
    private static bool NeedsInfinitePlayerSightCellRepair(Map map, Chara pc, int size)
    {
        if (map == null || size <= 0)
            return false;

        var center = size / 2;
        var currentX = SafeInt(() => pc.pos.x, center);
        var currentZ = SafeInt(() => pc.pos.z, center);
        var cells = map.cells;
        if (cells == null)
            return false;
        return NeedsInfinitePlayerSightCellRepair(cells[0, 0]) ||
               NeedsInfinitePlayerSightCellRepair(cells[size - 1, 0]) ||
               NeedsInfinitePlayerSightCellRepair(cells[0, size - 1]) ||
               NeedsInfinitePlayerSightCellRepair(cells[size - 1, size - 1]) ||
               NeedsInfinitePlayerSightCellRepair(cells[center, center]) ||
               NeedsInfinitePlayerSightCellRepair(cells[Clamp(currentX, 0, size - 1), Clamp(currentZ, 0, size - 1)]);
    }
    private static bool NeedsInfinitePlayerSightCellRepair(Cell? cell)
    {
        return cell != null && !cell.outOfBounds && !cell.isSeen;
    }
    private void ClearInfinitePlayerSight()
    {
        try
        {
            if (!HasActiveMapContext())
            {
                ResetInfinitePlayerSightState();
                return;
            }

            RestoreInfinitePlayerSightCharas(true);

            _infinitePlayerSightApplied = false;
            _infinitePlayerSightMap = null;
            _infinitePlayerSightPointCount = 0;
        }
        catch
        {
            ResetInfinitePlayerSightState();
        }
    }
    private void ApplyInfinitePlayerSightCharas(Chara pc, Map map)
    {
        if (!_infinitePlayerSightSavedTelepathy)
        {
            _infinitePlayerSightOriginalTelepathy = pc.hasTelepathy;
            _infinitePlayerSightSavedTelepathy = true;
        }
        pc.hasTelepathy = true;

        if (_infinitePlayerSightCharaMap != null && !ReferenceEquals(_infinitePlayerSightCharaMap, map))
            RestoreInfinitePlayerSightCharas(false);

        _infinitePlayerSightCharaMap = map;
        var charas = map.charas;
        if (charas == null) return;

        var now = SchedulerNow;
        var count = charas.Count;
        var auditInterval = _lowPerformanceMode ? 4f : 2f;
        if (_infinitePlayerSightObservedCharaCount == count &&
            now >= _infinitePlayerSightLastCharaAuditTime &&
            now - _infinitePlayerSightLastCharaAuditTime < auditInterval)
        {
            return;
        }

        for (var i = 0; i < charas.Count; i++)
        {
            var chara = charas[i];
            if (chara == null || ReferenceEquals(chara, pc)) continue;
            var uid = chara.uid;
            if (!_infinitePlayerSightOriginalTelepathyVisibility.ContainsKey(uid))
                _infinitePlayerSightOriginalTelepathyVisibility[uid] = chara.visibleWithTelepathy;
            if (!chara.visibleWithTelepathy)
                chara.visibleWithTelepathy = true;
        }
        _infinitePlayerSightObservedCharaCount = count;
        _infinitePlayerSightLastCharaAuditTime = now;
    }
    private void RestoreInfinitePlayerSightCharas(bool restorePlayerTelepathy)
    {
        try
        {
            if (restorePlayerTelepathy)
            {
                var pc = GameAccess.Characters.PlayerCharacter;
                if (pc != null && _infinitePlayerSightSavedTelepathy)
                    pc.hasTelepathy = _infinitePlayerSightOriginalTelepathy;
            }

            var map = _infinitePlayerSightCharaMap ?? GameAccess.World.CurrentMap;
            var charas = map?.charas;
            if (charas != null)
            {
                for (var i = 0; i < charas.Count; i++)
                {
                    var chara = charas[i];
                    if (chara == null) continue;
                    if (_infinitePlayerSightOriginalTelepathyVisibility.TryGetValue(chara.uid, out var original))
                        chara.visibleWithTelepathy = original;
                }
            }
        }
        catch { }

        if (restorePlayerTelepathy)
        {
            _infinitePlayerSightSavedTelepathy = false;
            _infinitePlayerSightOriginalTelepathy = false;
        }
        _infinitePlayerSightCharaMap = null;
        _infinitePlayerSightOriginalTelepathyVisibility.Clear();
        _infinitePlayerSightObservedCharaCount = -1;
        _infinitePlayerSightLastCharaAuditTime = -9999f;
    }
}
