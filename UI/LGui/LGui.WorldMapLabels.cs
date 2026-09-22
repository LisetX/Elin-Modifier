using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private const int LGuiWorldMapLabelPoolSize = 96;

    private void RebuildLGuiWorldMapLabels()
    {
        _lGuiWorldMapLabels.Clear();
        if (_lGuiWorldMapTab != 0 || !HasLGuiWorldMapLabels())
            return;

        var pins = _modules.WorldMap.Pins;
        var used = new HashSet<int>();
        var zones = EnsureLGuiWorldMapZones();
        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            var isDungeon = zone.IsDungeon;
            if (isDungeon ? !_lGuiWorldMapShowDungeonNames : !_lGuiWorldMapShowLandmarkNames)
                continue;
            if (!isDungeon && !zone.IsLandmark && !zone.IsPlayerFaction && !zone.IsRandomSite && zone.IsField)
                continue;

            var key = WorldMapModule.PackPin(zone.X, zone.Y);
            var text = zone.Name;
            if (isDungeon)
                text = text + " Lv" + zone.Level.ToString(CultureInfo.InvariantCulture);
            if (_lGuiWorldMapShowPinNames && pins.TryGetValue(key, out var note) && note.Length > 0)
            {
                text = text + " · " + note;
                used.Add(key);
            }
            _lGuiWorldMapLabels.Add(new LGuiWorldMapLabel(zone.X, zone.Y, text));
        }

        if (!_lGuiWorldMapShowPinNames)
            return;
        foreach (var pin in pins)
        {
            if (used.Contains(pin.Key))
                continue;
            _lGuiWorldMapLabels.Add(new LGuiWorldMapLabel(
                WorldMapModule.UnpackPinX(pin.Key),
                WorldMapModule.UnpackPinY(pin.Key),
                pin.Value));
        }
    }

    private List<WorldMapExportLabel>? BuildLGuiWorldMapExportLabels(WorldMapExportCrop crop)
    {
        if (_lGuiWorldMapTab != 0 || !HasLGuiWorldMapLabels())
            return null;
        var terrain = _modules.WorldMap.Terrain;
        if (terrain == null)
            return null;
        RebuildLGuiWorldMapLabels();
        var result = new List<WorldMapExportLabel>(_lGuiWorldMapLabels.Count);
        for (var i = 0; i < _lGuiWorldMapLabels.Count; i++)
        {
            var label = _lGuiWorldMapLabels[i];
            var cellX = label.GridX - terrain.MinX;
            var cellY = label.GridY - terrain.MinY;
            if (!terrain.Contains(cellX, cellY))
                continue;
            if (!crop.IsEmpty)
            {
                cellX -= crop.X;
                cellY -= crop.Y;
                if (cellX < 0 || cellY < 0 || cellX >= crop.Width || cellY >= crop.Height)
                    continue;
            }
            result.Add(new WorldMapExportLabel(cellX, cellY, label.Text));
        }
        return result;
    }

    private bool HasLGuiWorldMapLabels() =>
        _lGuiWorldMapShowLandmarkNames || _lGuiWorldMapShowDungeonNames || _lGuiWorldMapShowPinNames;

    private Text CreateLGuiWorldMapLabel(RectTransform parent)
    {
        var rect = CreateLGuiRect(parent, "WorldMapLabel");
        var view = rect.gameObject.AddComponent<LGuiWorldMapLabelView>();
        var trigger = rect.gameObject.AddComponent<EventTrigger>();
        trigger.triggers = new List<EventTrigger.Entry>();
        AddLGuiEventTrigger(trigger, EventTriggerType.PointerEnter, _ => _lGuiWorldMapHovering = true);
        AddLGuiEventTrigger(trigger, EventTriggerType.PointerExit, _ => _lGuiWorldMapHovering = false);
        AddLGuiEventTrigger(trigger, EventTriggerType.Drag, data => DragLGuiWorldMap(data as PointerEventData));
        AddLGuiEventTrigger(trigger, EventTriggerType.Scroll, data => ZoomLGuiWorldMap(data as PointerEventData));
        AddLGuiEventTrigger(trigger, EventTriggerType.PointerClick, data => ClickLGuiWorldMapLabel(view, data as PointerEventData));

        var text = rect.gameObject.AddComponent<Text>();
        text.font = _lGuiFont;
        text.fontSize = WorldMapModule.WorldMapLabelFontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = new Color(1f, 1f, 1f, 0.96f);
        text.raycastTarget = true;
        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.92f);
        outline.effectDistance = new Vector2(1.4f, -1.4f);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(220f, 18f);
        rect.gameObject.SetActive(false);
        return text;
    }

    private void UpdateLGuiWorldMapLabels(int cellsX, int cellsY, float zoom)
    {
        var viewport = _lGuiWorldMapViewport;
        if (viewport == null)
            return;
        if (_lGuiWorldMapTab != 0 || !HasLGuiWorldMapLabels() || _lGuiWorldMapLabels.Count == 0)
        {
            HideLGuiWorldMapLabels(0);
            return;
        }

        var terrain = _modules.WorldMap.Terrain;
        if (terrain == null)
        {
            HideLGuiWorldMapLabels(0);
            return;
        }

        var rect = viewport.rect;
        var shown = 0;
        for (var i = 0; i < _lGuiWorldMapLabels.Count && shown < LGuiWorldMapLabelPoolSize; i++)
        {
            var label = _lGuiWorldMapLabels[i];
            var cellX = label.GridX - terrain.MinX;
            var cellY = label.GridY - terrain.MinY;
            if (cellX < 0 || cellY < 0 || cellX >= cellsX || cellY >= cellsY)
                continue;

            var x = _lGuiWorldMapPan.x + (cellX + 0.5f) * zoom;
            var y = _lGuiWorldMapPan.y - (cellsY - cellY - 0.5f) * zoom;
            if (x < -120f || x > rect.width + 120f || y > 20f || y < -rect.height - 20f)
                continue;

            while (_lGuiWorldMapLabelPool.Count <= shown)
                _lGuiWorldMapLabelPool.Add(CreateLGuiWorldMapLabel(_lGuiWorldMapLabelLayer ?? viewport));

            var text = _lGuiWorldMapLabelPool[shown];
            if (text == null)
                continue;
            text.text = label.Text;
            var view = text.GetComponent<LGuiWorldMapLabelView>();
            if (view != null)
            {
                view.GridX = label.GridX;
                view.GridY = label.GridY;
            }
            text.rectTransform.sizeDelta = new Vector2(
                Mathf.Clamp(text.preferredWidth + 10f, 24f, 400f),
                20f);
            var labelRect = text.rectTransform;
            labelRect.anchoredPosition = new Vector2(x, y + zoom * 0.5f + 2f);
            if (!text.gameObject.activeSelf)
                text.gameObject.SetActive(true);
            shown++;
        }

        HideLGuiWorldMapLabels(shown);
    }

    private void ClickLGuiWorldMapLabel(LGuiWorldMapLabelView view, PointerEventData? data)
    {
        if (view == null || data == null || data.dragging || _lGuiWorldMapTab != 0)
            return;
        var zone = _modules.WorldMap.FindZoneAt(EnsureLGuiWorldMapZones(), view.GridX, view.GridY);
        if (zone != null)
        {
            SelectLGuiWorldMapZone(zone, true);
            return;
        }
        ClickLGuiWorldMap(data);
    }

    private void HideLGuiWorldMapLabels(int from)
    {
        for (var i = from; i < _lGuiWorldMapLabelPool.Count; i++)
        {
            var text = _lGuiWorldMapLabelPool[i];
            if (text != null && text.gameObject.activeSelf)
                text.gameObject.SetActive(false);
        }
    }
}

internal sealed class LGuiWorldMapLabelView : MonoBehaviour
{
    public int GridX = int.MinValue;
    public int GridY = int.MinValue;
}

internal readonly struct LGuiWorldMapLabel
{
    internal readonly int GridX;
    internal readonly int GridY;
    internal readonly string Text;

    internal LGuiWorldMapLabel(int gridX, int gridY, string text)
    {
        GridX = gridX;
        GridY = gridY;
        Text = text ?? "";
    }
}
