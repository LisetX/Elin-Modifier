using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private const float LGuiWorldMapRowHeight = 36f;
    private const int LGuiWorldMapRowPool = 20;

    private RectTransform CreateLGuiWorldMapRow(RectTransform parent)
    {
        var rect = CreateLGuiRect(parent, "WorldMapRow");
        var background = rect.gameObject.AddComponent<Image>();
        RegisterLGuiRoundedImage(background);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        var view = rect.gameObject.AddComponent<LGuiWorldMapRowView>();
        view.Background = background;

        var accent = CreateLGuiImage(rect, "Accent", 4f, 4f, 5f, LGuiWorldMapRowHeight - 12f);
        accent.raycastTarget = false;
        accent.color = new Color(1f, 1f, 1f, 0f);
        view.Accent = accent;

        var label = CreateLGuiText(rect, "Label", "", 15, TextAnchor.MiddleLeft, FontStyle.Normal);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        PlaceLGuiRect(label.rectTransform, 14f, 0f, 360f, LGuiWorldMapRowHeight);
        view.Label = label;

        button.onClick.AddListener(() => ClickLGuiWorldMapRow(view));
        return rect;
    }

    private void ClickLGuiWorldMapRow(LGuiWorldMapRowView view)
    {
        if (view == null || view.Index < 0)
            return;
        switch (view.Kind)
        {
            case LGuiWorldMapRowKind.Zone:
                if (_lGuiWorldMapZoneItems != null && view.Index < _lGuiWorldMapZoneItems.Count)
                    SelectLGuiWorldMapZone(_lGuiWorldMapZoneItems[view.Index]);
                break;
            case LGuiWorldMapRowKind.Npc:
                if (_lGuiWorldMapNpcItems != null && view.Index < _lGuiWorldMapNpcItems.Count)
                    SelectLGuiWorldMapNpc(_lGuiWorldMapNpcItems[view.Index]);
                break;
            case LGuiWorldMapRowKind.Hit:
                if (_lGuiWorldMapHitItems != null && view.Index < _lGuiWorldMapHitItems.Count)
                    SelectLGuiWorldMapHit(_lGuiWorldMapHitItems[view.Index]);
                break;
        }
    }

    private void BindLGuiWorldMapZoneRow(RectTransform row, WorldMapZoneEntry zone, int index)
    {
        var view = row.GetComponent<LGuiWorldMapRowView>();
        if (view == null)
            return;
        view.Kind = LGuiWorldMapRowKind.Zone;
        view.Index = index;
        if (view.Label != null)
            view.Label.text = BuildLGuiWorldMapZoneRowLabel(zone, GetLGuiWorldMapPlayerX(), GetLGuiWorldMapPlayerY());
        ApplyLGuiWorldMapRowAccent(
            view,
            zone.X == _lGuiWorldMapSelectedGridX && zone.Y == _lGuiWorldMapSelectedGridY);
    }

    private void BindLGuiWorldMapNpcRow(RectTransform row, WorldMapNpcEntry npc, int index)
    {
        var view = row.GetComponent<LGuiWorldMapRowView>();
        if (view == null)
            return;
        view.Kind = LGuiWorldMapRowKind.Npc;
        view.Index = index;
        if (view.Label != null)
            view.Label.text = BuildLGuiWorldMapNpcRowLabel(npc);
        ApplyLGuiWorldMapRowAccent(view, false);
    }

    private void BindLGuiWorldMapHitRow(RectTransform row, WorldMapSearchHit hit, int index)
    {
        var view = row.GetComponent<LGuiWorldMapRowView>();
        if (view == null)
            return;
        view.Kind = LGuiWorldMapRowKind.Hit;
        view.Index = index;
        if (view.Label != null)
        {
            var sb = new StringBuilder();
            sb.Append(hit.IsZone
                ? LGuiSwatch(LGuiWorldMapSwatchLandmark, "*")
                : LGuiSwatch(LGuiWorldMapSwatchSite, "*"));
            sb.Append(' ').Append(hit.Label);
            sb.Append("  (").Append(hit.GridX.ToString(CultureInfo.InvariantCulture)).Append(", ")
                .Append(hit.GridY.ToString(CultureInfo.InvariantCulture)).Append(')');
            view.Label.text = sb.ToString();
        }
        ApplyLGuiWorldMapRowAccent(view, false);
    }

    private static void ApplyLGuiWorldMapRowAccent(LGuiWorldMapRowView view, bool selected)
    {
        if (view.Accent == null)
            return;
        view.Accent.color = selected
            ? new Color(0.98f, 0.82f, 0.25f, 1f)
            : new Color(1f, 1f, 1f, 0f);
    }

    private void RefreshLGuiWorldMapZoneItems()
    {
        var zones = EnsureLGuiWorldMapZones();
        var playerX = GetLGuiWorldMapPlayerX();
        var playerY = GetLGuiWorldMapPlayerY();
        var matches = FilterLGuiWorldMapZones(zones);
        SortLGuiWorldMapZones(matches, playerX, playerY);
        _lGuiWorldMapZoneItems = matches;
        if (_lGuiWorldMapZoneHeader != null)
            _lGuiWorldMapZoneHeader.text = BuildLGuiWorldMapListHeader(
                T("区块列表", "Zone list"),
                matches.Count,
                zones.Count);
        _lGuiWorldMapZoneList?.SetItems(matches);
    }

    private void RefreshLGuiWorldMapNpcItems()
    {
        var module = _modules.WorldMap;
        var zone = ResolveLGuiWorldMapNpcZone();
        var npcs = module.ListZoneNpcs(zone);
        _lGuiWorldMapNpcItems = npcs;
        if (_lGuiWorldMapNpcHeader != null)
            _lGuiWorldMapNpcHeader.text = zone == null
                ? T("区块NPC", "Zone NPCs")
                : T("区块NPC", "Zone NPCs") + " - " + SafeText(() => zone.Name, "");
        if (_lGuiWorldMapNpcSubtitle != null)
            _lGuiWorldMapNpcSubtitle.text =
                npcs.Count.ToString(CultureInfo.InvariantCulture) + " " +
                DescribeLGuiWorldMapNpcOrigin(module.GetZoneNpcOrigin(zone)) +
                (npcs.Count >= WorldMapModule.WorldMapMaxNpcRows
                    ? "  " + T("仅显示前", "showing first") + " " +
                      WorldMapModule.WorldMapMaxNpcRows.ToString(CultureInfo.InvariantCulture)
                    : "");
        _lGuiWorldMapNpcList?.SetItems(npcs);
    }

    private void RefreshLGuiWorldMapHitItems()
    {
        var hits = _modules.WorldMap.SearchHits;
        var items = new List<WorldMapSearchHit>(hits.Count);
        for (var i = 0; i < hits.Count; i++)
            items.Add(hits[i]);
        _lGuiWorldMapHitItems = items;
        if (_lGuiWorldMapHitHeader != null)
            _lGuiWorldMapHitHeader.text = T("查找结果", "Search results") + " " +
                                          items.Count.ToString(CultureInfo.InvariantCulture);
        _lGuiWorldMapHitList?.SetItems(items);
    }

    private string BuildLGuiWorldMapListHeader(string title, int shown, int total)
    {
        var text = title + " " + shown.ToString(CultureInfo.InvariantCulture) + "/" +
                   total.ToString(CultureInfo.InvariantCulture);
        return text;
    }

    private void SelectLGuiWorldMapHit(WorldMapSearchHit hit)
    {
        var zones = EnsureLGuiWorldMapZones();
        var zone = _modules.WorldMap.FindZoneAt(zones, hit.GridX, hit.GridY);
        if (zone != null)
        {
            SelectLGuiWorldMapZone(zone);
        }
        else
        {
            var terrain = _modules.WorldMap.Terrain;
            if (terrain != null)
            {
                _lGuiWorldMapFocusGridX = hit.GridX;
                _lGuiWorldMapFocusGridY = hit.GridY;
                _lGuiWorldMapInfoLocked = true;
                SetLGuiWorldMapInfo(BuildLGuiWorldMapCellInfo(hit.GridX - terrain.MinX, hit.GridY - terrain.MinY));
            }
        }
        CenterLGuiWorldMapOnGrid(hit.GridX, hit.GridY);
    }
}

internal enum LGuiWorldMapRowKind
{
    Zone,
    Npc,
    Hit,
}

internal sealed class LGuiWorldMapRowView : MonoBehaviour
{
    public Image? Background;
    public Image? Accent;
    public Text? Label;
    public LGuiWorldMapRowKind Kind;
    public int Index = -1;
}
