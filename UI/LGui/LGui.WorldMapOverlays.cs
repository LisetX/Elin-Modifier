using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private int CountLGuiWorldMapSidePanels()
    {
        var count = 0;
        if (_lGuiWorldMapTab == 0 && _lGuiWorldMapShowList)
            count++;
        if (_lGuiWorldMapShowNpcs)
            count++;
        if (_lGuiWorldMapTab == 0 && _modules.WorldMap.SearchQuery.Length > 0)
            count++;
        return count;
    }

    private void BuildLGuiWorldMapOverlays(List<WorldMapZoneEntry> zones, float rightInset, float bottomInset)
    {
        if (_lGuiWorldMapShowInfo)
            BuildLGuiWorldMapInfoPanel(zones, rightInset);

        var panels = CountLGuiWorldMapSidePanels();
        if (panels == 0)
            return;

        var column = CreateLGuiRect(_lGuiPageHost!, "WorldMapRightColumn");
        column.anchorMin = new Vector2(1f, 0f);
        column.anchorMax = new Vector2(1f, 1f);
        column.pivot = new Vector2(1f, 0.5f);
        column.sizeDelta = new Vector2(
            LGuiWorldMapRightInset - 16f,
            -(LGuiWorldMapToolbarHeight + bottomInset));
        column.anchoredPosition = new Vector2(
            -8f,
            (bottomInset - LGuiWorldMapToolbarHeight) * 0.5f);

        var band = 1f / panels;
        var slot = 0;
        if (_lGuiWorldMapTab == 0 && _lGuiWorldMapShowList)
        {
            BuildLGuiWorldMapZonePanel(column, 1f - band * (slot + 1), 1f - band * slot);
            slot++;
        }
        if (_lGuiWorldMapShowNpcs)
        {
            BuildLGuiWorldMapNpcPanel(column, 1f - band * (slot + 1), 1f - band * slot);
            slot++;
        }
        if (_lGuiWorldMapTab == 0 && _modules.WorldMap.SearchQuery.Length > 0)
            BuildLGuiWorldMapSearchPanel(column, 1f - band * (slot + 1), 1f - band * slot);
    }

    private void BuildLGuiWorldMapInfoPanel(List<WorldMapZoneEntry> zones, float rightInset)
    {
        var panel = CreateLGuiRect(_lGuiPageHost!, "WorldMapInfoPanel");
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(1f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.offsetMin = new Vector2(0f, 0f);
        panel.offsetMax = new Vector2(-rightInset, LGuiWorldMapBottomInset - 10f);
        _lGuiWorldMapBlockRects.Add(panel);

        if (string.IsNullOrEmpty(_lGuiWorldMapInfo))
        {
            _lGuiWorldMapInfoIsSummary = true;
            _lGuiWorldMapInfo = BuildLGuiWorldMapSummaryText(zones);
        }

        _lGuiWorldMapInfoText = CreateLGuiText(panel, "WorldMapInfo", "", 16, TextAnchor.UpperLeft, FontStyle.Normal);
        var leftColumn = _lGuiWorldMapInfoText.rectTransform;
        leftColumn.anchorMin = new Vector2(0f, 0f);
        leftColumn.anchorMax = new Vector2(0.5f, 1f);
        leftColumn.offsetMin = new Vector2(14f, 56f);
        leftColumn.offsetMax = new Vector2(-8f, -10f);

        _lGuiWorldMapInfoTextRight = CreateLGuiText(panel, "WorldMapInfoRight", "", 16, TextAnchor.UpperLeft, FontStyle.Normal);
        var rightColumn = _lGuiWorldMapInfoTextRight.rectTransform;
        rightColumn.anchorMin = new Vector2(0.5f, 0f);
        rightColumn.anchorMax = new Vector2(1f, 1f);
        rightColumn.offsetMin = new Vector2(8f, 56f);
        rightColumn.offsetMax = new Vector2(-14f, -10f);
        ApplyLGuiWorldMapInfoText(_lGuiWorldMapInfo);

        if (_lGuiWorldMapTab != 0)
            return;

        var row = LGuiWorldMapBottomInset - 10f - 46f;
        var pinInput = CreateLGuiInput(panel, "WorldMapPinNote", T("在此格添加标记", "Note for this cell"), 14f, row, 420f, 34f);
        pinInput.text = ResolveLGuiWorldMapPinNote();
        _lGuiWorldMapPinInput = pinInput;
        CreateLGuiButton(panel, "WorldMapPinSet", T("标记", "Pin"), 444f, row, 100f, 34f, ApplyLGuiWorldMapPin);
        CreateLGuiButton(panel, "WorldMapPinClearAll", T("清除全部标记", "Clear pins"), 554f, row, 130f, 34f, ClearLGuiWorldMapPins);
    }

    private RectTransform CreateLGuiWorldMapSidePanel(RectTransform column, string name, float bottom, float top)
    {
        var panel = CreateLGuiRect(column, name);
        panel.anchorMin = new Vector2(0f, bottom);
        panel.anchorMax = new Vector2(1f, top);
        panel.offsetMin = new Vector2(0f, 6f);
        panel.offsetMax = new Vector2(0f, -6f);
        _lGuiWorldMapBlockRects.Add(panel);
        return panel;
    }

    private void BuildLGuiWorldMapZonePanel(RectTransform column, float bottom, float top)
    {
        var panel = CreateLGuiWorldMapSidePanel(column, "WorldMapZonePanel", bottom, top);
        _lGuiWorldMapZoneHeader = CreateLGuiText(panel, "WorldMapZoneHeader", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiWorldMapZoneHeader.rectTransform, 14f, 8f, 250f, 32f);
        var sortButton = CreateLGuiButton(
            panel,
            "WorldMapSort",
            DescribeLGuiWorldMapSort(),
            272f,
            6f,
            140f,
            36f,
            () =>
            {
                _lGuiWorldMapSort = (_lGuiWorldMapSort + 1) % 3;
                if (_lGuiWorldMapSortLabel != null)
                    _lGuiWorldMapSortLabel.text = DescribeLGuiWorldMapSort();
                RefreshLGuiWorldMapZoneItems();
            });
        _lGuiWorldMapSortLabel = sortButton.GetComponentInChildren<Text>(true);

        var scroll = CreateLGuiScroll(panel, "WorldMapZoneScroll", 48f);
        _lGuiWorldMapZoneList = new VirtualList<WorldMapZoneEntry>(
            scroll,
            LGuiWorldMapRowHeight,
            LGuiWorldMapRowPool,
            CreateLGuiWorldMapRow,
            BindLGuiWorldMapZoneRow);
        RefreshLGuiWorldMapZoneItems();
    }

    private void BuildLGuiWorldMapSearchPanel(RectTransform column, float bottom, float top)
    {
        var panel = CreateLGuiWorldMapSidePanel(column, "WorldMapSearchPanel", bottom, top);
        _lGuiWorldMapHitHeader = CreateLGuiText(panel, "WorldMapHitHeader", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiWorldMapHitHeader.rectTransform, 14f, 8f, 402f, 32f);

        var scroll = CreateLGuiScroll(panel, "WorldMapHitScroll", 48f);
        _lGuiWorldMapHitList = new VirtualList<WorldMapSearchHit>(
            scroll,
            LGuiWorldMapRowHeight,
            LGuiWorldMapRowPool,
            CreateLGuiWorldMapRow,
            BindLGuiWorldMapHitRow);
        RefreshLGuiWorldMapHitItems();
    }

    private List<WorldMapZoneEntry> FilterLGuiWorldMapZones(List<WorldMapZoneEntry> zones)
    {
        var result = new List<WorldMapZoneEntry>();
        for (var i = 0; i < zones.Count; i++)
        {
            if (_lGuiWorldMapKnownOnly && !zones[i].IsKnown)
                continue;
            result.Add(zones[i]);
        }
        return result;
    }

    private void SortLGuiWorldMapZones(List<WorldMapZoneEntry> zones, int playerX, int playerY)
    {
        switch (_lGuiWorldMapSort)
        {
            case 1:
                zones.Sort((a, b) =>
                {
                    var compare = b.Level.CompareTo(a.Level);
                    return compare != 0 ? compare : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                });
                break;
            case 2:
                zones.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                break;
            default:
                zones.Sort((a, b) =>
                {
                    var compare = LGuiWorldMapDistance(a, playerX, playerY)
                        .CompareTo(LGuiWorldMapDistance(b, playerX, playerY));
                    return compare != 0 ? compare : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                });
                break;
        }
    }

    private string DescribeLGuiWorldMapSort()
    {
        switch (_lGuiWorldMapSort)
        {
            case 1: return T("排序", "Sort") + ": " + T("等级", "Level");
            case 2: return T("排序", "Sort") + ": " + T("名称", "Name");
            default: return T("排序", "Sort") + ": " + T("距离", "Distance");
        }
    }

    private static int LGuiWorldMapDistance(WorldMapZoneEntry zone, int playerX, int playerY)
    {
        if (playerX == int.MinValue || playerY == int.MinValue)
            return 0;
        return Math.Max(Math.Abs(zone.X - playerX), Math.Abs(zone.Y - playerY));
    }

    private string BuildLGuiWorldMapZoneRowLabel(WorldMapZoneEntry zone, int playerX, int playerY)
    {
        var sb = new StringBuilder();
        sb.Append(LGuiWorldMapZoneMark(zone)).Append(' ').Append(zone.Name);
        sb.Append("  Lv").Append(zone.Level.ToString(CultureInfo.InvariantCulture));
        if (playerX != int.MinValue && playerY != int.MinValue)
            sb.Append("  ").Append(T("距离", "Distance")).Append(' ')
                .Append(LGuiWorldMapDistance(zone, playerX, playerY).ToString(CultureInfo.InvariantCulture));
        var expiry = _modules.WorldMap.DescribeZoneExpiry(zone.Zone);
        if (!string.IsNullOrEmpty(expiry))
            sb.Append("  ").Append(T("剩余", "left")).Append(' ').Append(expiry);
        return sb.ToString();
    }

    private string LGuiWorldMapZoneMark(WorldMapZoneEntry zone)
    {
        const string mark = "*";
        if (zone.IsPlayerFaction)
            return LGuiSwatch(LGuiWorldMapSwatchHome, mark);
        if (zone.IsLandmark)
            return LGuiSwatch(LGuiWorldMapSwatchLandmark, mark);
        if (zone.IsDungeon)
            return LGuiSwatch(LGuiWorldMapSwatchDungeon, mark);
        if (zone.IsRandomSite)
            return LGuiSwatch(LGuiWorldMapSwatchSite, mark);
        if (zone.IsField)
            return LGuiSwatch(LGuiWorldMapSwatchOther, mark);
        return LGuiSwatch(LGuiWorldMapSwatchLandmark, mark);
    }

    private void SelectLGuiWorldMapZone(WorldMapZoneEntry zone) => SelectLGuiWorldMapZone(zone, true);

    private void SelectLGuiWorldMapZone(WorldMapZoneEntry zone, bool allowToggleOff)
    {
        if (allowToggleOff &&
            zone.X == _lGuiWorldMapSelectedGridX &&
            zone.Y == _lGuiWorldMapSelectedGridY &&
            Time.frameCount != _lGuiWorldMapSelectedFrame)
        {
            ClearLGuiWorldMapSelection();
            return;
        }

        var terrain = _modules.WorldMap.Terrain;
        var sb = new StringBuilder();
        AppendLGuiWorldMapPosition(sb, zone.X, zone.Y);
        if (terrain != null)
        {
            var x = zone.X - terrain.MinX;
            var y = zone.Y - terrain.MinY;
            sb.AppendLine();
            sb.Append(T("地形", "Terrain")).Append(": ").Append(DescribeLGuiWorldTerrain(terrain.GetKind(x, y)));
            AppendLGuiWorldTileSource(sb, terrain.GetSource(x, y));
        }
        AppendLGuiWorldMapZoneDetails(sb, zone);
        AppendLGuiWorldMapPinNote(sb, zone.X, zone.Y);

        _lGuiWorldMapFocusGridX = zone.X;
        _lGuiWorldMapFocusGridY = zone.Y;
        SetLGuiWorldMapInfo(sb.ToString());
        _lGuiWorldMapHoverCell = int.MinValue;
        _modules.WorldMap.Log = T("已选择区块", "Selected zone") + ": " + zone.Name +
                                " (" + zone.X.ToString(CultureInfo.InvariantCulture) + ", " +
                                zone.Y.ToString(CultureInfo.InvariantCulture) + ")";

        _lGuiWorldMapSelectedGridX = zone.X;
        _lGuiWorldMapSelectedGridY = zone.Y;
        _lGuiWorldMapSelectedFrame = Time.frameCount;
        _lGuiWorldMapInfoLocked = true;
        SyncLGuiWorldMapNavigationTarget();
        if (_lGuiWorldMapReadSaves)
            _modules.WorldMap.TryLoadZoneNpcSnapshot(zone.Zone, false);
        _lGuiWorldMapZoneList?.RefreshBoundRows();
        if (_lGuiWorldMapShowNpcs)
            RefreshLGuiWorldMapNpcItems();
        SyncLGuiWorldMapPinInput();
        ApplyLGuiWorldMapTransform();
    }

    private void ClearLGuiWorldMapSelection()
    {
        var hadZone = _lGuiWorldMapSelectedGridX != int.MinValue;
        _lGuiWorldMapSelectedGridX = int.MinValue;
        _lGuiWorldMapSelectedGridY = int.MinValue;
        _lGuiWorldMapSelectedFrame = -1;
        _lGuiWorldMapInfoLocked = false;
        _modules.WorldMap.ClearNavigationTarget();
        _lGuiWorldMapFocusGridX = int.MinValue;
        _lGuiWorldMapFocusGridY = int.MinValue;
        _lGuiWorldMapHoverCell = int.MinValue;
        if (hadZone)
            _modules.WorldMap.Log = T("已取消选中", "Selection cleared");
        _lGuiWorldMapZoneList?.RefreshBoundRows();
        if (_lGuiWorldMapShowNpcs)
            RefreshLGuiWorldMapNpcItems();
        ShowLGuiWorldMapSummary();
        SyncLGuiWorldMapPinInput();
        ApplyLGuiWorldMapTransform();
    }

    private WorldMapZoneEntry? ResolveLGuiWorldMapSelectedZone()
    {
        if (_lGuiWorldMapSelectedGridX == int.MinValue)
            return null;
        return _modules.WorldMap.FindZoneAt(
            EnsureLGuiWorldMapZones(),
            _lGuiWorldMapSelectedGridX,
            _lGuiWorldMapSelectedGridY);
    }

    private void SetLGuiWorldMapInfo(string text)
    {
        _lGuiWorldMapInfoIsSummary = false;
        _lGuiWorldMapInfo = text;
        ApplyLGuiWorldMapInfoText(text);
    }

    private void ApplyLGuiWorldMapInfoText(string text)
    {
        if (_lGuiWorldMapInfoText == null)
            return;
        if (_lGuiWorldMapInfoTextRight == null)
        {
            _lGuiWorldMapInfoText.text = text ?? "";
            return;
        }
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
        var half = (lines.Length + 1) / 2;
        _lGuiWorldMapInfoText.text = string.Join("\n", lines, 0, half);
        _lGuiWorldMapInfoTextRight.text = lines.Length > half
            ? string.Join("\n", lines, half, lines.Length - half)
            : "";
    }

    private string BuildLGuiWorldMapSummaryText(List<WorldMapZoneEntry> zones)
    {
        return _lGuiWorldMapTab == 0
            ? _modules.WorldMap.BuildWorldSummary(zones, GetLGuiWorldMapPlayerX(), GetLGuiWorldMapPlayerY())
            : _modules.WorldMap.BuildLocalSummary();
    }

    private void ShowLGuiWorldMapSummary()
    {
        _lGuiWorldMapInfoLocked = false;
        _lGuiWorldMapInfoIsSummary = true;
        _lGuiWorldMapHoverCell = int.MinValue;
        _lGuiWorldMapInfo = BuildLGuiWorldMapSummaryText(
            _lGuiWorldMapTab == 0 ? EnsureLGuiWorldMapZones() : new List<WorldMapZoneEntry>());
        ApplyLGuiWorldMapInfoText(_lGuiWorldMapInfo);
    }

    private string ResolveLGuiWorldMapPinNote()
    {
        if (_lGuiWorldMapFocusGridX == int.MinValue || _lGuiWorldMapFocusGridY == int.MinValue)
            return "";
        return _modules.WorldMap.TryGetPin(_lGuiWorldMapFocusGridX, _lGuiWorldMapFocusGridY, out var note)
            ? note
            : "";
    }

    private void SyncLGuiWorldMapPinInput()
    {
        if (_lGuiWorldMapPinInput == null)
            return;
        if (_modules.LGuiFocus.HasFocusedInputWithin(_lGuiPageHost))
            return;
        _lGuiWorldMapPinInput.text = ResolveLGuiWorldMapPinNote();
    }

    private void AppendLGuiWorldMapPinNote(StringBuilder sb, int gridX, int gridY)
    {
        if (_modules.WorldMap.TryGetPin(gridX, gridY, out var note))
            AppendLGuiWorldMapLine(sb, T("标记", "Pin"), note);
    }

    private void ApplyLGuiWorldMapPin()
    {
        if (_lGuiWorldMapFocusGridX == int.MinValue || _lGuiWorldMapFocusGridY == int.MinValue)
        {
            _modules.WorldMap.Log = T("先在地图上选一个格子", "Pick a cell on the map first");
            return;
        }
        _modules.WorldMap.SetPin(
            _lGuiWorldMapFocusGridX,
            _lGuiWorldMapFocusGridY,
            _lGuiWorldMapPinInput == null ? "" : _lGuiWorldMapPinInput.text);
        SaveConfig(false);
        _modules.WorldMap.Log = T("标记已更新", "Pin updated");
        RefreshLGuiWorldMapSprite();
        RebuildLGuiWorldMapLabels();
        ApplyLGuiWorldMapTransform();
        SetLGuiWorldMapInfo(BuildLGuiWorldMapFocusInfo());
    }

    private void ClearLGuiWorldMapPins()
    {
        _modules.WorldMap.ClearPins();
        SaveConfig(false);
        _modules.WorldMap.Log = T("标记已清除", "Pins cleared");
        RefreshLGuiWorldMapSprite();
        RebuildLGuiWorldMapLabels();
        ApplyLGuiWorldMapTransform();
        SyncLGuiWorldMapPinInput();
    }

    private string BuildLGuiWorldMapFocusInfo()
    {
        var terrain = _modules.WorldMap.Terrain;
        if (terrain == null || _lGuiWorldMapFocusGridX == int.MinValue || _lGuiWorldMapFocusGridY == int.MinValue)
            return _lGuiWorldMapInfo;
        return BuildLGuiWorldMapCellInfo(
            _lGuiWorldMapFocusGridX - terrain.MinX,
            _lGuiWorldMapFocusGridY - terrain.MinY);
    }

    private void ExportLGuiWorldMapImage()
    {
        var directory = System.IO.Path.Combine(GetPluginDirectory(), "export");
        var crop = GetLGuiWorldMapExportCrop();
        var path = _lGuiWorldMapTab == 0
            ? _modules.WorldMap.ExportWorldMapPng(
                directory,
                _lGuiFont,
                BuildLGuiWorldMapExportLabels(crop),
                crop)
            : _modules.WorldMap.ExportLocalMapPng(directory, crop);
        _modules.WorldMap.Log = string.IsNullOrEmpty(path)
            ? T("导出失败", "Export failed")
            : T("已导出: ", "Exported: ") + path;
    }
}
