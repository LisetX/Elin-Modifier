using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void OpenLGuiNearbyNpcSelector()
    {
        if (_targetTab != 2)
            _targetTab = 2;
        var modal = CreateLGuiCompleteModal("RuntimeNearbyNpcSelector", T("选择附近NPC", "Select nearby NPC"), out var content, 1480f, 930f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "NearbyFilter", T("过滤", "Filter"), 0f, y, 430f, 44f);
        filter.text = _nearbyNpcFilter;
        filter.onValueChanged.AddListener(value => _nearbyNpcFilter = value ?? "");
        CreateLGuiButton(content, "RefreshNearby", T("刷新", "Refresh"), 444f, y, 100f, 44f, () =>
        {
            _nearbyNpcPage = 0;
            InvalidateNearbyNpcCache();
            OpenLGuiNearbyNpcSelector();
        });
        y += 54f;

        var rows = GetSortedNearbyNpcs();
        const int perPage = 12;
        var pages = Math.Max(1, (rows.Count + perPage - 1) / perPage);
        _nearbyNpcPage = Clamp(_nearbyNpcPage, 0, pages - 1);
        CreateLGuiButton(content, "Prev", "◀", 0f, y, 48f, 42f, () => { _nearbyNpcPage = Math.Max(0, _nearbyNpcPage - 1); OpenLGuiNearbyNpcSelector(); });
        var pageText = CreateLGuiText(content, "Page", (_nearbyNpcPage + 1) + " / " + pages + "  (" + rows.Count + ")", 16, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(pageText.rectTransform, 58f, y, 200f, 42f);
        CreateLGuiButton(content, "Next", "▶", 268f, y, 48f, 42f, () => { _nearbyNpcPage = Math.Min(pages - 1, _nearbyNpcPage + 1); OpenLGuiNearbyNpcSelector(); });
        y += 52f;
        var start = _nearbyNpcPage * perPage;
        var end = Math.Min(rows.Count, start + perPage);
        for (var i = start; i < end; i++)
        {
            var entry = rows[i];
            var local = entry;
            var selected = entry.Uid == _nearbyNpcSelectedUid ? "→ " : "";
            CreateLGuiButton(content, "Npc" + i, selected + entry.Label, 0f, y, 1340f, 46f, () =>
            {
                _nearbyNpcSelectedUid = local.Uid;
                SyncNpcRelationshipInputs(local.Chara);
                SyncNpcGeneEditorState(local.Chara);
                MarkCharacterDataDirty();
                RebuildLGuiCharacterRows();
                CloseLGuiEditorModal(true);
            });
            y += 50f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(760f, y + 20f));
    }
    private void OpenLGuiCharacterTeleport()
    {
        var target = GetCurrentDataTarget();
        if (target == null) return;
        if (_targetTab != 0)
        {
            TeleportPlayerBesideNpc(target);
            return;
        }
        OpenLGuiPlayerTeleportEditor(target);
    }
    private void OpenLGuiPlayerTeleportEditor(Chara pc)
    {
        var modal = CreateLGuiCompleteModal("RuntimeTeleportEditor", T("传送", "Teleport"), out var content, 1480f, 930f);
        if (modal == null) return;
        var y = 4f;
        var pos = SafeText(() => pc.pos == null ? "?,?" : pc.pos.x + "," + pc.pos.z, "?,?");
        y = AddLGuiReadOnlyRow(content, T("当前位置", "Current position"), SafeText(() => pc.currentZone == null ? "???" : pc.currentZone.Name, "???") + " / " + pos, y);
        var onWorldMap = IsPlayerOnWorldMap();
        if (!onWorldMap)
        {
            y = AddLGuiReadOnlyRow(content, T("状态", "Status"), T("传送功能仅限人物处于世界板块中使用，地图区块内无法使用", "Teleport is only available on the world map."), y);
            content.sizeDelta = new Vector2(0f, 760f);
            return;
        }

        var filter = CreateLGuiInput(content, "TeleportFilter", T("地标过滤", "Landmark filter"), 0f, y, 420f, 44f);
        filter.text = _teleportFilter;
        filter.onValueChanged.AddListener(value => _teleportFilter = value ?? "");
        CreateLGuiButton(content, "Refresh", T("刷新", "Refresh"), 434f, y, 100f, 44f, () =>
        {
            _teleportPage = 0;
            _lastTeleportFilter = _teleportFilter;
            _teleportFilterCacheDirty = true;
            _teleportZoneCacheDirty = true;
            OpenLGuiPlayerTeleportEditor(pc);
        });
        y += 54f;
        var zones = GetFilteredTeleportZones();
        const int perPage = 8;
        var pages = Math.Max(1, (zones.Count + perPage - 1) / perPage);
        _teleportPage = Clamp(_teleportPage, 0, pages - 1);
        CreateLGuiButton(content, "Prev", "◀", 0f, y, 48f, 42f, () => { _teleportPage = Math.Max(0, _teleportPage - 1); OpenLGuiPlayerTeleportEditor(pc); });
        var pageText = CreateLGuiText(content, "Page", (_teleportPage + 1) + " / " + pages + "  (" + zones.Count + ")", 16, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(pageText.rectTransform, 58f, y, 200f, 42f);
        CreateLGuiButton(content, "Next", "▶", 268f, y, 48f, 42f, () => { _teleportPage = Math.Min(pages - 1, _teleportPage + 1); OpenLGuiPlayerTeleportEditor(pc); });
        y += 52f;
        var start = _teleportPage * perPage;
        var end = Math.Min(zones.Count, start + perPage);
        for (var i = start; i < end; i++)
        {
            var entry = zones[i];
            var local = entry;
            var label = CreateLGuiText(content, "Zone", entry.Label, 16, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(label.rectTransform, 0f, y, 1040f, 44f);
            CreateLGuiButton(content, "Teleport" + i, T("传送", "Teleport"), 1060f, y, 120f, 44f, () => { QueueTeleportToZone(local.Zone); CloseLGuiEditorModal(); });
            y += 48f;
        }
        y += 10f;
        y = AddLGuiSectionTitle(content, T("自定义位置", "Custom position"), y);
        AddLGuiInlineInput(content, T("世界地图X", "World X"), () => _teleportXInput, value => _teleportXInput = value, 0f, y, 130f, 120f);
        AddLGuiInlineInput(content, T("世界地图Y", "World Y"), () => _teleportYInput, value => _teleportYInput = value, 280f, y, 130f, 120f);
        CreateLGuiButton(content, "TeleportPosition", T("传送至位置", "Teleport to position"), 560f, y, 160f, 42f, () => { QueueTeleportToWorldPosition(); CloseLGuiEditorModal(); });
        content.sizeDelta = new Vector2(0f, Math.Max(760f, y + 70f));
    }
}
