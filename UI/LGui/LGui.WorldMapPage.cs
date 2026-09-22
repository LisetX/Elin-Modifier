using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private const float LGuiWorldMapToolbarHeight = 106f;
    private const float LGuiWorldMapRightInset = 444f;
    private const float LGuiWorldMapBottomInset = 250f;
    private const float LGuiWorldMapDefaultZoom = 6f;
    private const float LGuiWorldMapSyncSeconds = 0.5f;
    private const float LGuiWorldMapClickSlackPixels = 8f;
    private const float LGuiWorldMapMaxZoom = 24f;

    private void BuildLGuiWorldMapPage()
    {
        var module = _modules.WorldMap;
        _lGuiWorldMapImage = null;
        _lGuiWorldMapInfoText = null;
        _lGuiWorldMapInfoTextRight = null;
        _lGuiWorldMapViewport = null;
        _lGuiWorldMapMarker = null;
        _lGuiWorldMapSelectionMarker = null;
        _lGuiWorldMapSelectionImage = null;
        _lGuiWorldMapLabelLayer = null;
        _lGuiWorldMapZoneHeader = null;
        _lGuiWorldMapNpcHeader = null;
        _lGuiWorldMapNpcSubtitle = null;
        _lGuiWorldMapHitHeader = null;
        _lGuiWorldMapSortLabel = null;
        _lGuiWorldMapPinInput = null;
        _lGuiWorldMapHovering = false;
        _lGuiWorldMapHoverCell = int.MinValue;
        _lGuiWorldMapBlockRects.Clear();
        _lGuiWorldMapNextSync = Time.unscaledTime + LGuiWorldMapSyncSeconds;
        _lGuiWorldMapSyncedZoneUid = SafeInt(() => GameAccess.World.CurrentZone?.uid ?? 0, 0);
        if (_lGuiWorldMapTab == 1 && IsLGuiWorldMapOnRegion())
            _lGuiWorldMapTab = 0;
        module.ShowLocalContainers = _lGuiWorldMapShowLocalContainers;
        module.ShowLocalNpcs = _lGuiWorldMapShowLocalNpcs;

        if (module.EnsureWorldIdentity())
        {
            _lGuiWorldMapZones = null;
            _lGuiWorldMapSelectedGridX = int.MinValue;
            _lGuiWorldMapSelectedGridY = int.MinValue;
            _lGuiWorldMapFocusGridX = int.MinValue;
            _lGuiWorldMapFocusGridY = int.MinValue;
            _lGuiWorldMapInfo = "";
            _lGuiWorldMapZoneSignature = "";
            module.LoadPins(_worldMapPinPayload);
        }
        _lGuiWorldMapLabelPool.Clear();
        if (_lGuiWorldMapZoom <= 0f)
            _lGuiWorldMapCenterPending = true;

        if (_lGuiWorldMapTab == 0 && !module.HasTerrain && module.TryRefreshTerrain())
            module.RefreshSearch();

        var toolbar = CreateLGuiRect(_lGuiPageHost!, "WorldMapToolbar");
        AnchorLGuiTop(toolbar, 0f, LGuiWorldMapToolbarHeight, 0f, 0f);
        BuildLGuiWorldMapToolbar(toolbar);

        var rightInset = CountLGuiWorldMapSidePanels() > 0 ? LGuiWorldMapRightInset : 0f;
        var bottomInset = _lGuiWorldMapShowInfo ? LGuiWorldMapBottomInset : 0f;

        var viewport = CreateLGuiRect(_lGuiPageHost!, "WorldMapViewport");
        viewport.anchorMin = new Vector2(0f, 0f);
        viewport.anchorMax = new Vector2(1f, 1f);
        viewport.offsetMin = new Vector2(0f, bottomInset);
        viewport.offsetMax = new Vector2(-rightInset, -LGuiWorldMapToolbarHeight);
        var background = viewport.gameObject.AddComponent<Image>();
        background.color = Color.clear;
        background.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();
        _lGuiWorldMapViewport = viewport;
        AttachLGuiWorldMapInput(background);

        var zones = _lGuiWorldMapTab == 0 ? EnsureLGuiWorldMapZones() : new List<WorldMapZoneEntry>();
        _lGuiWorldMapZoneSignature = module.BuildZoneStateSignature(zones);
        var sprite = _lGuiWorldMapTab == 0 ? module.BuildWorldSprite(zones) : module.BuildLocalSprite();

        if (sprite == null)
        {
            var empty = CreateLGuiText(
                viewport,
                "WorldMapEmpty",
                _lGuiWorldMapTab == 0
                    ? module.DescribeTerrainState()
                    : T("当前区块地图不可用", "Current zone map is unavailable"),
                18,
                TextAnchor.UpperLeft,
                FontStyle.Normal);
            PlaceLGuiRect(empty.rectTransform, 18f, 18f, 900f, 60f);
        }
        else
        {
            var image = CreateLGuiImage(viewport, "WorldMapImage", 0f, 0f, sprite.rect.width, sprite.rect.height);
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
            _lGuiWorldMapImage = image;

            var labelLayer = CreateLGuiRect(viewport, "WorldMapLabelLayer");
            labelLayer.anchorMin = Vector2.zero;
            labelLayer.anchorMax = Vector2.one;
            labelLayer.offsetMin = Vector2.zero;
            labelLayer.offsetMax = Vector2.zero;
            _lGuiWorldMapLabelLayer = labelLayer;

            var marker = CreateLGuiImage(viewport, "WorldMapMarker", 0f, 0f, 1f, 1f);
            marker.sprite = module.GetMarkerSprite();
            marker.type = Image.Type.Simple;
            marker.color = new Color(1f, 1f, 1f, 0.95f);
            marker.raycastTarget = false;
            _lGuiWorldMapMarker = marker.rectTransform;

            var selection = CreateLGuiImage(viewport, "WorldMapSelection", 0f, 0f, 1f, 1f);
            selection.sprite = module.GetSelectionSprite();
            selection.type = Image.Type.Simple;
            selection.raycastTarget = false;
            _lGuiWorldMapSelectionImage = selection;
            _lGuiWorldMapSelectionMarker = selection.rectTransform;
        }

        RebuildLGuiWorldMapLabels();
        BuildLGuiWorldMapOverlays(zones, rightInset, bottomInset);
        _lGuiWorldMapApplyPending = true;
    }

    private void BuildLGuiWorldMapToolbar(RectTransform toolbar)
    {
        CreateLGuiButton(
            toolbar,
            "WorldMapTabWorld",
            (_lGuiWorldMapTab == 0 ? "→ " : "") + T("世界地图", "World map"),
            0f,
            4f,
            170f,
            46f,
            () => SwitchLGuiWorldMapTab(0));
        CreateLGuiButton(
            toolbar,
            "WorldMapTabLocal",
            (_lGuiWorldMapTab == 1 ? "→ " : "") + T("当前区块", "Current zone"),
            180f,
            4f,
            170f,
            46f,
            () => SwitchLGuiWorldMapTab(1));
        CreateLGuiButton(toolbar, "WorldMapRefresh", T("刷新地图", "Refresh map"), 364f, 4f, 130f, 46f, RefreshLGuiWorldMap);
        CreateLGuiButton(
            toolbar,
            "WorldMapLocate",
            T("定位玩家", "Center on player"),
            504f,
            4f,
            130f,
            46f,
            CenterLGuiWorldMapOnPlayer);
        CreateLGuiButton(toolbar, "WorldMapFit", T("适应窗口", "Fit"), 644f, 4f, 120f, 46f, FitLGuiWorldMapToViewport);

        var cursor = 780f;
        CreateLGuiWorldMapToggle(
            toolbar,
            "WorldMapToggleInfo",
            T("信息栏", "Info"),
            cursor,
            6f,
            140f,
            42f,
            _lGuiWorldMapShowInfo,
            value => _lGuiWorldMapShowInfo = value);
        cursor += 150f;
        if (_lGuiWorldMapTab == 0)
        {
            CreateLGuiWorldMapToggle(
                toolbar,
                "WorldMapToggleList",
                T("区块列表", "Zone list"),
                cursor,
                6f,
                160f,
                42f,
                _lGuiWorldMapShowList,
                value => _lGuiWorldMapShowList = value);
            cursor += 170f;
        }
        CreateLGuiWorldMapToggle(
            toolbar,
            "WorldMapToggleNpcs",
            T("区块NPC", "Zone NPCs"),
            cursor,
            6f,
            160f,
            42f,
            _lGuiWorldMapShowNpcs,
            value => _lGuiWorldMapShowNpcs = value);
        var second = 0f;
        if (_lGuiWorldMapTab == 0)
        {
            var find = CreateLGuiInput(
                toolbar,
                "WorldMapFind",
                T("查找物品/采集物/NPC/区块", "Find item / resource / NPC / zone"),
                0f,
                58f,
                240f,
                42f);
            find.text = _modules.WorldMap.SearchQuery;
            find.onEndEdit.AddListener(value =>
            {
                _modules.WorldMap.SetSearchQuery(value);
                _lGuiWorldMapRebuildPending = true;
            });
            second = 250f;
            if (_lGuiWorldMapShowList)
            {
                CreateLGuiWorldMapToggle(
                    toolbar,
                    "WorldMapKnownOnly",
                    T("仅已知", "Known only"),
                    second,
                    58f,
                    140f,
                    42f,
                    _lGuiWorldMapKnownOnly,
                    value => _lGuiWorldMapKnownOnly = value);
                second += 150f;
            }
        }
        CreateLGuiButton(
            toolbar,
            "WorldMapExport",
            T("导出PNG", "Export PNG"),
            second,
            58f,
            130f,
            42f,
            ExportLGuiWorldMapImage);
        second += 140f;
        CreateLGuiWorldMapToggle(
            toolbar,
            "WorldMapNavigation",
            T("目的地导航", "Route guide"),
            second,
            58f,
            160f,
            42f,
            _lGuiWorldMapNavigation,
            ApplyLGuiWorldMapNavigation);
        second += 170f;
        CreateLGuiWorldMapToggle(
            toolbar,
            "WorldMapReadSaves",
            T("强制读取", "Force read"),
            second,
            58f,
            160f,
            42f,
            _lGuiWorldMapReadSaves,
            ApplyLGuiWorldMapReadSaves);
        second += 170f;
        if (_lGuiWorldMapTab == 0)
        {
            CreateLGuiWorldMapToggle(
                toolbar,
                "WorldMapShowLandmarkNames",
                T("显示地标名", "Landmark names"),
                second,
                58f,
                150f,
                42f,
                _lGuiWorldMapShowLandmarkNames,
                value => _lGuiWorldMapShowLandmarkNames = value);
            second += 160f;
            CreateLGuiWorldMapToggle(
                toolbar,
                "WorldMapShowDungeonNames",
                T("显示地牢名", "Dungeon names"),
                second,
                58f,
                150f,
                42f,
                _lGuiWorldMapShowDungeonNames,
                value => _lGuiWorldMapShowDungeonNames = value);
            second += 160f;
            CreateLGuiWorldMapToggle(
                toolbar,
                "WorldMapShowPinNames",
                T("显示标记名", "Pin names"),
                second,
                58f,
                150f,
                42f,
                _lGuiWorldMapShowPinNames,
                value => _lGuiWorldMapShowPinNames = value);
            second += 160f;
        }
        else
        {
            CreateLGuiWorldMapToggle(
                toolbar,
                "WorldMapShowContainers",
                T("显示容器", "Containers"),
                second,
                58f,
                150f,
                42f,
                _lGuiWorldMapShowLocalContainers,
                value =>
                {
                    _lGuiWorldMapShowLocalContainers = value;
                    _modules.WorldMap.ShowLocalContainers = value;
                });
            second += 160f;
            CreateLGuiWorldMapToggle(
                toolbar,
                "WorldMapShowLocalNpcs",
                T("显示NPC", "NPCs"),
                second,
                58f,
                150f,
                42f,
                _lGuiWorldMapShowLocalNpcs,
                value =>
                {
                    _lGuiWorldMapShowLocalNpcs = value;
                    _modules.WorldMap.ShowLocalNpcs = value;
                });
            second += 160f;
        }

        var legend = CreateLGuiText(toolbar, "WorldMapLegend", BuildLGuiWorldMapLegend(), 15, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(legend.rectTransform, second, 58f, Math.Max(200f, 1540f - second), 42f);
    }

    private void CreateLGuiWorldMapToggle(
        RectTransform parent,
        string name,
        string label,
        float x,
        float y,
        float width,
        float height,
        bool value,
        Action<bool> changed)
    {
        var toggle = CreateLGuiToggle(parent, name, x, y, width, height, out var text);
        text.text = label;
        toggle.isOn = value;
        toggle.onValueChanged.AddListener(next =>
        {
            changed(next);
            SaveConfig(false);
            _lGuiWorldMapRebuildPending = true;
        });
    }

    private void ApplyLGuiWorldMapNavigation(bool value)
    {
        _lGuiWorldMapNavigation = value;
        _modules.WorldMap.NavigationEnabled = value;
        if (value)
            SyncLGuiWorldMapNavigationTarget();
        _lGuiWorldMapRebuildPending = true;
    }

    private void SyncLGuiWorldMapNavigationTarget()
    {
        if (!_lGuiWorldMapNavigation)
            return;
        if (_lGuiWorldMapTab != 0 ||
            !_lGuiWorldMapInfoLocked ||
            _lGuiWorldMapFocusGridX == int.MinValue ||
            _lGuiWorldMapFocusGridY == int.MinValue)
        {
            _modules.WorldMap.ClearNavigationTarget();
            return;
        }
        _modules.WorldMap.SetNavigationTarget(_lGuiWorldMapFocusGridX, _lGuiWorldMapFocusGridY);
    }

    private void ApplyLGuiWorldMapReadSaves(bool value)
    {
        _lGuiWorldMapReadSaves = value;
        if (!value)
            _modules.WorldMap.ClearNpcSnapshots();
        else
            LoadLGuiWorldMapZoneNpcs(ResolveLGuiWorldMapNpcZone(), false);
    }

    private bool IsLGuiWorldMapOnRegion()
    {
        try
        {
            var zone = GameAccess.World.CurrentZone;
            return zone == null || zone.IsRegion;
        }
        catch
        {
            return false;
        }
    }

    private void SwitchLGuiWorldMapTab(int tab)
    {
        if (_lGuiWorldMapTab == tab)
            return;
        if (tab == 1 && IsLGuiWorldMapOnRegion())
        {
            _modules.WorldMap.Log = T("世界地图中无法查看当前区块地图", "The current zone map is unavailable on the world map");
            return;
        }
        _lGuiWorldMapSavedZooms[_lGuiWorldMapTab] = _lGuiWorldMapZoom;
        _lGuiWorldMapSavedPans[_lGuiWorldMapTab] = _lGuiWorldMapPan;
        _lGuiWorldMapTab = tab;
        _lGuiWorldMapZoom = _lGuiWorldMapSavedZooms[tab];
        _lGuiWorldMapPan = _lGuiWorldMapSavedPans[tab];
        _lGuiWorldMapInfo = "";
        _lGuiWorldMapInfoIsSummary = true;
        _lGuiWorldMapInfoLocked = false;
        if (_lGuiWorldMapZoom <= 0f)
            _lGuiWorldMapCenterPending = true;
        SwitchLGuiPage(LGuiPage.WorldMap);
    }

    private void RefreshLGuiWorldMap()
    {
        var module = _modules.WorldMap;
        _lGuiWorldMapZones = null;
        _lGuiWorldMapInfo = "";
        _lGuiWorldMapInfoIsSummary = true;
        _lGuiWorldMapCenterPending = true;
        module.ClearNpcSnapshots();
        if (_lGuiWorldMapTab == 0)
        {
            if (module.TryRefreshTerrain())
            {
                module.RefreshSearch();
                module.Log = T("世界地图已刷新", "World map refreshed");
            }
            else
            {
                module.Log = T("需要在世界地图上才能扫描地形", "Terrain can only be scanned while on the world map");
            }
        }
        else
        {
            module.ReleaseLocalTexture();
            module.Log = T("当前区块已刷新", "Current zone refreshed");
        }
        SwitchLGuiPage(LGuiPage.WorldMap);
    }

    private List<WorldMapZoneEntry> EnsureLGuiWorldMapZones()
    {
        _lGuiWorldMapZones ??= _modules.WorldMap.EnumerateZones();
        return _lGuiWorldMapZones;
    }

    private int GetLGuiWorldMapPlayerX() =>
        _modules.WorldMap.TryGetPlayerGrid(out var gridX, out _) ? gridX : int.MinValue;

    private int GetLGuiWorldMapPlayerY() =>
        _modules.WorldMap.TryGetPlayerGrid(out _, out var gridY) ? gridY : int.MinValue;

    private string BuildLGuiWorldMapLegend()
    {
        if (_lGuiWorldMapTab == 0)
            return T("图例", "Legend") + ": " +
                   LGuiSwatch(LGuiWorldMapSwatchLandmark, T("地标", "landmark")) + " " +
                   LGuiSwatch(LGuiWorldMapSwatchDungeon, T("地牢", "dungeon")) + " " +
                   LGuiSwatch(LGuiWorldMapSwatchHome, T("家园", "home")) + " " +
                   LGuiSwatch(LGuiWorldMapSwatchSite, T("随机点", "site")) + " " +
                   LGuiSwatch(LGuiWorldMapSwatchBlocked, T("不可达", "blocked"));
        return T("图例", "Legend") + ": " +
               LGuiSwatch(LGuiWorldMapSwatchUnknown, T("未探索", "unseen")) + " " +
               LGuiSwatch(LGuiWorldMapSwatchWater, T("水域", "water")) + " " +
               LGuiSwatch(LGuiWorldMapSwatchBlocked, T("墙壁", "wall")) + " " +
               LGuiSwatch(LGuiWorldMapSwatchObject, T("物件", "object")) + " " +
               LGuiSwatch(LGuiWorldMapSwatchLand, T("地面", "floor")) + " | " +
               LGuiSwatch(LGuiWorldMapSwatchContainer, T("容器", "container")) + " " +
               LGuiSwatch(LGuiWorldMapSwatchHostile, T("敌对", "enemy")) + " " +
               LGuiSwatch(LGuiWorldMapSwatchNpc, T("NPC", "NPC")) + " " +
               LGuiSwatch(LGuiWorldMapSwatchHome, T("同伴", "ally")) + "   " +
               T("拖动平移，滚轮缩放", "drag to pan, wheel to zoom");
    }

    private const int LGuiWorldMapSwatchWater = 0;
    private const int LGuiWorldMapSwatchLand = 1;
    private const int LGuiWorldMapSwatchRoad = 2;
    private const int LGuiWorldMapSwatchBlocked = 3;
    private const int LGuiWorldMapSwatchLandmark = 4;
    private const int LGuiWorldMapSwatchDungeon = 5;
    private const int LGuiWorldMapSwatchHome = 6;
    private const int LGuiWorldMapSwatchSite = 7;
    private const int LGuiWorldMapSwatchOther = 8;
    private const int LGuiWorldMapSwatchUnknown = 9;
    private const int LGuiWorldMapSwatchObject = 10;
    private const int LGuiWorldMapSwatchContainer = 11;
    private const int LGuiWorldMapSwatchHostile = 12;
    private const int LGuiWorldMapSwatchNpc = 13;

    private static readonly string[] LGuiWorldMapDarkSwatches =
    {
        "#5E88B6", "#6A9462", "#B6A072", "#8A8882", "#F0D660",
        "#E26058", "#70E296", "#C68CE2", "#D4D4D8", "#9A9AA0", "#80A468",
        "#E0A028", "#E04848", "#48C8E0",
    };

    private static readonly string[] LGuiWorldMapLightSwatches =
    {
        "#22568E", "#2E6A28", "#7E6220", "#4E4C46", "#8E6E04",
        "#B2241C", "#12783C", "#6E1E9E", "#4A4A50", "#66666C", "#3E6A20",
        "#9A6A04", "#B2241C", "#0A6E88",
    };

    private string LGuiSwatch(int swatch, string label)
    {
        var table = _uiStyleIndex == 5 ? LGuiWorldMapLightSwatches : LGuiWorldMapDarkSwatches;
        var color = swatch >= 0 && swatch < table.Length ? table[swatch] : table[0];
        return "<color=" + color + ">" + label + "</color>";
    }

    private void AttachLGuiWorldMapInput(Image target)
    {
        var trigger = target.gameObject.AddComponent<EventTrigger>();
        trigger.triggers = new List<EventTrigger.Entry>();
        AddLGuiEventTrigger(trigger, EventTriggerType.PointerEnter, _ => _lGuiWorldMapHovering = true);
        AddLGuiEventTrigger(trigger, EventTriggerType.PointerExit, _ => _lGuiWorldMapHovering = false);
        AddLGuiEventTrigger(trigger, EventTriggerType.Drag, data => DragLGuiWorldMap(data as PointerEventData));
        AddLGuiEventTrigger(trigger, EventTriggerType.Scroll, data => ZoomLGuiWorldMap(data as PointerEventData));
        AddLGuiEventTrigger(trigger, EventTriggerType.PointerClick, data => ClickLGuiWorldMap(data as PointerEventData));
    }

    private void DragLGuiWorldMap(PointerEventData? data)
    {
        if (data == null || _lGuiWorldMapViewport == null)
            return;
        var camera = _lGuiCanvas == null ? null : _lGuiCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_lGuiWorldMapViewport, data.position, camera, out var now))
            return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _lGuiWorldMapViewport, data.position - data.delta, camera, out var previous))
            return;
        _lGuiWorldMapPan += now - previous;
        ApplyLGuiWorldMapTransform();
    }

    private void ZoomLGuiWorldMap(PointerEventData? data)
    {
        if (data == null || _lGuiWorldMapViewport == null || !TryGetLGuiWorldMapSize(out var cellsX, out var cellsY))
            return;
        var step = data.scrollDelta.y;
        if (Mathf.Approximately(step, 0f))
            return;
        var camera = _lGuiCanvas == null ? null : _lGuiCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_lGuiWorldMapViewport, data.position, camera, out var local))
            return;

        var rect = _lGuiWorldMapViewport.rect;
        var pointerX = local.x - rect.xMin;
        var pointerY = rect.yMax - local.y;
        var previousZoom = GetLGuiWorldMapZoom(cellsX, cellsY);
        var zoom = Mathf.Clamp(
            previousZoom * (step > 0f ? 1.25f : 0.8f),
            GetLGuiWorldMapFitZoom(cellsX, cellsY),
            LGuiWorldMapMaxZoom);
        if (Mathf.Approximately(zoom, previousZoom))
            return;

        var mapX = (pointerX - _lGuiWorldMapPan.x) / previousZoom;
        var mapY = (pointerY + _lGuiWorldMapPan.y) / previousZoom;
        _lGuiWorldMapZoom = zoom;
        _lGuiWorldMapPan = new Vector2(pointerX - mapX * zoom, mapY * zoom - pointerY);
        ApplyLGuiWorldMapTransform();
    }

    private void FitLGuiWorldMapToViewport()
    {
        if (!TryGetLGuiWorldMapSize(out var cellsX, out var cellsY))
            return;
        _lGuiWorldMapZoom = GetLGuiWorldMapFitZoom(cellsX, cellsY);
        _lGuiWorldMapPan = Vector2.zero;
        ApplyLGuiWorldMapTransform();
    }

    private bool TryGetLGuiWorldMapSize(out int cellsX, out int cellsY)
    {
        cellsX = 0;
        cellsY = 0;
        if (_lGuiWorldMapTab == 0)
        {
            var terrain = _modules.WorldMap.Terrain;
            if (terrain == null)
                return false;
            cellsX = terrain.Width;
            cellsY = terrain.Height;
            return cellsX > 0 && cellsY > 0;
        }
        return _modules.WorldMap.TryGetLocalSize(out cellsX, out cellsY);
    }

    private float GetLGuiWorldMapFitZoom(int cellsX, int cellsY)
    {
        if (_lGuiWorldMapViewport == null || cellsX <= 0 || cellsY <= 0)
            return 1f;
        var rect = _lGuiWorldMapViewport.rect;
        if (rect.width <= 1f || rect.height <= 1f)
            return 1f;
        return Mathf.Clamp(Mathf.Min(rect.width / cellsX, rect.height / cellsY), 0.25f, LGuiWorldMapMaxZoom);
    }

    private float GetLGuiWorldMapZoom(int cellsX, int cellsY)
    {
        if (_lGuiWorldMapZoom <= 0f)
            _lGuiWorldMapZoom = Mathf.Clamp(
                LGuiWorldMapDefaultZoom,
                GetLGuiWorldMapFitZoom(cellsX, cellsY),
                LGuiWorldMapMaxZoom);
        return _lGuiWorldMapZoom;
    }

    private void ApplyLGuiWorldMapTransform()
    {
        if (_lGuiWorldMapViewport == null || _lGuiWorldMapImage == null ||
            !TryGetLGuiWorldMapSize(out var cellsX, out var cellsY))
            return;

        var zoom = GetLGuiWorldMapZoom(cellsX, cellsY);
        var mapWidth = cellsX * zoom;
        var mapHeight = cellsY * zoom;
        var rect = _lGuiWorldMapViewport.rect;

        var panX = mapWidth <= rect.width
            ? (rect.width - mapWidth) * 0.5f
            : Mathf.Clamp(_lGuiWorldMapPan.x, rect.width - mapWidth, 0f);
        var panY = mapHeight <= rect.height
            ? -(rect.height - mapHeight) * 0.5f
            : Mathf.Clamp(_lGuiWorldMapPan.y, 0f, mapHeight - rect.height);
        _lGuiWorldMapPan = new Vector2(panX, panY);

        var mapRect = _lGuiWorldMapImage.rectTransform;
        mapRect.anchorMin = new Vector2(0f, 1f);
        mapRect.anchorMax = new Vector2(0f, 1f);
        mapRect.pivot = new Vector2(0f, 1f);
        mapRect.sizeDelta = new Vector2(mapWidth, mapHeight);
        mapRect.anchoredPosition = new Vector2(panX, panY);

        UpdateLGuiWorldMapMarker(cellsX, cellsY, zoom);
        UpdateLGuiWorldMapSelectionMarker(cellsX, cellsY, zoom);
        UpdateLGuiWorldMapLabels(cellsX, cellsY, zoom);
    }

    private void UpdateLGuiWorldMapSelectionMarker(int cellsX, int cellsY, float zoom)
    {
        var marker = _lGuiWorldMapSelectionMarker;
        if (marker == null)
            return;
        var terrain = _modules.WorldMap.Terrain;
        if (_lGuiWorldMapTab != 0 || terrain == null || !_lGuiWorldMapInfoLocked ||
            _lGuiWorldMapFocusGridX == int.MinValue || _lGuiWorldMapFocusGridY == int.MinValue)
        {
            if (marker.gameObject.activeSelf)
                marker.gameObject.SetActive(false);
            return;
        }
        var cellX = _lGuiWorldMapFocusGridX - terrain.MinX;
        var cellY = _lGuiWorldMapFocusGridY - terrain.MinY;
        if (cellX < 0 || cellY < 0 || cellX >= cellsX || cellY >= cellsY)
        {
            if (marker.gameObject.activeSelf)
                marker.gameObject.SetActive(false);
            return;
        }
        if (!marker.gameObject.activeSelf)
            marker.gameObject.SetActive(true);
        marker.anchorMin = new Vector2(0f, 1f);
        marker.anchorMax = new Vector2(0f, 1f);
        marker.pivot = new Vector2(0.5f, 0.5f);
        _lGuiWorldMapSelectionSize = Mathf.Max(18f, zoom + 14f);
        marker.anchoredPosition = new Vector2(
            _lGuiWorldMapPan.x + (cellX + 0.5f) * zoom,
            _lGuiWorldMapPan.y - (cellsY - cellY - 0.5f) * zoom);
        AnimateLGuiWorldMapSelection();
    }

    private void AnimateLGuiWorldMapSelection()
    {
        var marker = _lGuiWorldMapSelectionMarker;
        var image = _lGuiWorldMapSelectionImage;
        if (marker == null || image == null || !marker.gameObject.activeSelf)
            return;
        var time = Time.unscaledTime;
        var pulse = 0.5f + 0.5f * Mathf.Sin(time * 2.6f);
        var color = Color.HSVToRGB(Mathf.Repeat(time * 0.22f, 1f), 0.75f, 1f);
        color.a = 0.55f + 0.45f * pulse;
        image.color = color;
        var size = _lGuiWorldMapSelectionSize * (1f + 0.18f * pulse);
        marker.sizeDelta = new Vector2(size, size);
    }

    private void UpdateLGuiWorldMapMarker(int cellsX, int cellsY, float zoom)
    {
        var marker = _lGuiWorldMapMarker;
        if (marker == null)
            return;
        if (!TryGetLGuiWorldMapPlayerCell(out var cellX, out var cellY) ||
            cellX < 0 || cellY < 0 || cellX >= cellsX || cellY >= cellsY)
        {
            if (marker.gameObject.activeSelf)
                marker.gameObject.SetActive(false);
            return;
        }
        if (!marker.gameObject.activeSelf)
            marker.gameObject.SetActive(true);
        marker.anchorMin = new Vector2(0f, 1f);
        marker.anchorMax = new Vector2(0f, 1f);
        marker.pivot = new Vector2(0.5f, 0.5f);
        var size = Mathf.Max(10f, zoom + 6f);
        marker.sizeDelta = new Vector2(size, size);
        marker.anchoredPosition = new Vector2(
            _lGuiWorldMapPan.x + (cellX + 0.5f) * zoom,
            _lGuiWorldMapPan.y - (cellsY - cellY - 0.5f) * zoom);
    }

    private bool TryGetLGuiWorldMapPlayerCell(out int cellX, out int cellY)
    {
        cellX = -1;
        cellY = -1;
        if (_lGuiWorldMapTab == 0)
        {
            var terrain = _modules.WorldMap.Terrain;
            if (terrain == null || !_modules.WorldMap.TryGetPlayerGrid(out var gridX, out var gridY))
                return false;
            cellX = gridX - terrain.MinX;
            cellY = gridY - terrain.MinY;
            return true;
        }
        try
        {
            var position = GameAccess.Characters.PlayerCharacter?.pos;
            if (position == null)
                return false;
            cellX = position.x;
            cellY = position.z;
            return cellX >= 0 && cellY >= 0;
        }
        catch
        {
            return false;
        }
    }

    private void CenterLGuiWorldMapOnPlayer()
    {
        if (!TryGetLGuiWorldMapPlayerCell(out var cellX, out var cellY))
        {
            ApplyLGuiWorldMapTransform();
            return;
        }
        CenterLGuiWorldMapOnCell(cellX, cellY);
    }

    private void CenterLGuiWorldMapOnGrid(int gridX, int gridY)
    {
        var terrain = _modules.WorldMap.Terrain;
        if (_lGuiWorldMapTab != 0 || terrain == null)
            return;
        CenterLGuiWorldMapOnCell(gridX - terrain.MinX, gridY - terrain.MinY);
    }

    private void CenterLGuiWorldMapOnCell(int cellX, int cellY)
    {
        if (_lGuiWorldMapViewport == null || !TryGetLGuiWorldMapSize(out var cellsX, out var cellsY))
            return;
        var zoom = GetLGuiWorldMapZoom(cellsX, cellsY);
        var rect = _lGuiWorldMapViewport.rect;
        _lGuiWorldMapPan = new Vector2(
            rect.width * 0.5f - (cellX + 0.5f) * zoom,
            (cellsY - cellY - 0.5f) * zoom - rect.height * 0.5f);
        ApplyLGuiWorldMapTransform();
    }

    private void RefreshLGuiWorldMapControls()
    {
        if (_lGuiPage != LGuiPage.WorldMap)
            return;
        if (_lGuiWorldMapRebuildPending)
        {
            _lGuiWorldMapRebuildPending = false;
            SwitchLGuiPage(LGuiPage.WorldMap);
            return;
        }
        if (_lGuiWorldMapViewport == null)
            return;
        if (_lGuiWorldMapApplyPending && _lGuiWorldMapViewport.rect.width > 1f)
        {
            _lGuiWorldMapApplyPending = false;
            if (_lGuiWorldMapCenterPending)
            {
                _lGuiWorldMapCenterPending = false;
                CenterLGuiWorldMapOnPlayer();
            }
            else
            {
                ApplyLGuiWorldMapTransform();
            }
        }
        AnimateLGuiWorldMapSelection();
        SyncLGuiWorldMapState();
        RefreshLGuiWorldMapRoute();
        if (!_lGuiWorldMapInfoLocked)
        {
            if (_lGuiWorldMapHovering && _lGuiEditorModal == null)
                UpdateLGuiWorldMapHover(Input.mousePosition);
            else if (!_lGuiWorldMapInfoIsSummary)
                ShowLGuiWorldMapSummary();
        }
    }

    private void RefreshLGuiWorldMapRoute()
    {
        if (!_lGuiWorldMapNavigation || _lGuiWorldMapTab != 0 || _lGuiWorldMapImage == null)
            return;
        var module = _modules.WorldMap;
        module.RefreshNavigation();
        if (module.NavigationVersion == _lGuiWorldMapRouteVersion)
            return;
        _lGuiWorldMapRouteVersion = module.NavigationVersion;
        var zones = _lGuiWorldMapZones;
        if (zones == null)
            return;
        var sprite = module.BuildWorldSprite(zones);
        if (sprite != null && !ReferenceEquals(_lGuiWorldMapImage.sprite, sprite))
            _lGuiWorldMapImage.sprite = sprite;
    }

    private void SyncLGuiWorldMapState()
    {
        if (_lGuiWorldMapTab == 1 && IsLGuiWorldMapOnRegion())
        {
            SwitchLGuiWorldMapTab(0);
            return;
        }
        if (_lGuiWorldMapImage == null)
            return;
        var now = Time.unscaledTime;
        if (now < _lGuiWorldMapNextSync)
        {
            UpdateLGuiWorldMapMarkers();
            return;
        }
        _lGuiWorldMapNextSync = now + LGuiWorldMapSyncSeconds;

        var module = _modules.WorldMap;
        if (module.EnsureWorldIdentity())
        {
            _lGuiWorldMapImage.sprite = null;
            _lGuiWorldMapZones = null;
            _lGuiWorldMapSelectedGridX = int.MinValue;
            _lGuiWorldMapSelectedGridY = int.MinValue;
            _lGuiWorldMapInfo = "";
            _lGuiWorldMapZoneSignature = "";
            module.LoadPins(_worldMapPinPayload);
            _lGuiWorldMapRebuildPending = true;
            return;
        }

        var zoneUid = SafeInt(() => GameAccess.World.CurrentZone?.uid ?? 0, 0);
        if (zoneUid != _lGuiWorldMapSyncedZoneUid)
        {
            _lGuiWorldMapSyncedZoneUid = zoneUid;
            _lGuiWorldMapZones = null;
            _lGuiWorldMapRebuildPending = true;
            return;
        }

        if (_lGuiWorldMapTab == 0)
        {
            var zones = module.EnumerateZones();
            var signature = module.BuildZoneStateSignature(zones);
            _lGuiWorldMapZones = zones;
            var zonesChanged = !string.Equals(signature, _lGuiWorldMapZoneSignature, StringComparison.Ordinal);
            if (zonesChanged)
            {
                _lGuiWorldMapZoneSignature = signature;
                module.RefreshSearch();
            }
            var refreshed = module.BuildWorldSprite(zones);
            var spriteChanged = refreshed != null && !ReferenceEquals(_lGuiWorldMapImage.sprite, refreshed);
            if (spriteChanged)
                _lGuiWorldMapImage.sprite = refreshed;
            if (zonesChanged)
            {
                RefreshLGuiWorldMapZoneItems();
                RefreshLGuiWorldMapHitItems();
                RebuildLGuiWorldMapLabels();
            }
            if (zonesChanged || spriteChanged)
                ApplyLGuiWorldMapTransform();
        }
        else
        {
            var sprite = module.BuildLocalSprite();
            if (sprite != null && !ReferenceEquals(_lGuiWorldMapImage.sprite, sprite))
                _lGuiWorldMapImage.sprite = sprite;
        }

        if (_lGuiWorldMapShowNpcs)
            RefreshLGuiWorldMapNpcItems();
        RefreshLGuiWorldMapSummary();
        UpdateLGuiWorldMapMarkers();
    }

    private void RefreshLGuiWorldMapSummary()
    {
        if (!_lGuiWorldMapInfoIsSummary || _lGuiWorldMapInfoText == null)
            return;
        _lGuiWorldMapInfo = BuildLGuiWorldMapSummaryText(
            _lGuiWorldMapTab == 0 ? EnsureLGuiWorldMapZones() : new List<WorldMapZoneEntry>());
        ApplyLGuiWorldMapInfoText(_lGuiWorldMapInfo);
    }

    private int GetLGuiWorldMapClickTolerance()
    {
        if (!TryGetLGuiWorldMapSize(out var cellsX, out var cellsY))
            return 0;
        var zoom = GetLGuiWorldMapZoom(cellsX, cellsY);
        return zoom >= LGuiWorldMapClickSlackPixels ? 0 : 1;
    }

    private WorldMapExportCrop GetLGuiWorldMapExportCrop()
    {
        if (_lGuiWorldMapViewport == null || !TryGetLGuiWorldMapSize(out var cellsX, out var cellsY))
            return WorldMapExportCrop.Full;
        var zoom = GetLGuiWorldMapZoom(cellsX, cellsY);
        var rect = _lGuiWorldMapViewport.rect;
        var mapWidth = cellsX * zoom;
        var mapHeight = cellsY * zoom;
        if (mapWidth <= rect.width + 0.5f && mapHeight <= rect.height + 0.5f)
            return WorldMapExportCrop.Full;

        var leftPixels = Mathf.Max(0f, -_lGuiWorldMapPan.x);
        var rightPixels = Mathf.Min(mapWidth, -_lGuiWorldMapPan.x + rect.width);
        var topPixels = Mathf.Max(0f, _lGuiWorldMapPan.y);
        var bottomPixels = Mathf.Min(mapHeight, _lGuiWorldMapPan.y + rect.height);
        if (rightPixels <= leftPixels || bottomPixels <= topPixels)
            return WorldMapExportCrop.Full;

        var firstX = Mathf.Clamp(Mathf.FloorToInt(leftPixels / zoom), 0, cellsX - 1);
        var lastX = Mathf.Clamp(Mathf.CeilToInt(rightPixels / zoom) - 1, firstX, cellsX - 1);
        var firstFromTop = Mathf.Clamp(Mathf.FloorToInt(topPixels / zoom), 0, cellsY - 1);
        var lastFromTop = Mathf.Clamp(Mathf.CeilToInt(bottomPixels / zoom) - 1, firstFromTop, cellsY - 1);
        var firstY = cellsY - 1 - lastFromTop;
        var lastY = cellsY - 1 - firstFromTop;
        return new WorldMapExportCrop(firstX, firstY, lastX - firstX + 1, lastY - firstY + 1);
    }

    private void RefreshLGuiWorldMapSprite()
    {
        if (_lGuiWorldMapTab != 0 || _lGuiWorldMapImage == null)
            return;
        var sprite = _modules.WorldMap.BuildWorldSprite(EnsureLGuiWorldMapZones());
        if (sprite != null)
            _lGuiWorldMapImage.sprite = sprite;
    }

    private void UpdateLGuiWorldMapMarkers()
    {
        if (_lGuiWorldMapViewport == null || !TryGetLGuiWorldMapSize(out var cellsX, out var cellsY))
            return;
        var zoom = GetLGuiWorldMapZoom(cellsX, cellsY);
        UpdateLGuiWorldMapMarker(cellsX, cellsY, zoom);
        UpdateLGuiWorldMapSelectionMarker(cellsX, cellsY, zoom);
    }

    private void ClickLGuiWorldMap(PointerEventData? data)
    {
        if (data == null || _lGuiWorldMapTab != 0 || data.dragging)
            return;
        if (!TryGetLGuiWorldMapCell(data.position, out var cellX, out var cellY))
            return;
        var terrain = _modules.WorldMap.Terrain;
        if (terrain == null)
            return;
        var gridX = cellX + terrain.MinX;
        var gridY = cellY + terrain.MinY;
        var exact = _modules.WorldMap.FindZoneAt(EnsureLGuiWorldMapZones(), gridX, gridY);
        var zone = exact ?? _modules.WorldMap.FindZoneNear(
            EnsureLGuiWorldMapZones(),
            gridX,
            gridY,
            GetLGuiWorldMapClickTolerance());
        if (zone != null)
        {
            SelectLGuiWorldMapZone(zone, exact != null);
            return;
        }
        if (_lGuiWorldMapInfoLocked &&
            gridX == _lGuiWorldMapFocusGridX &&
            gridY == _lGuiWorldMapFocusGridY)
        {
            ClearLGuiWorldMapSelection();
            return;
        }
        if (_lGuiWorldMapSelectedGridX != int.MinValue)
        {
            _lGuiWorldMapSelectedGridX = int.MinValue;
            _lGuiWorldMapSelectedGridY = int.MinValue;
            _lGuiWorldMapZoneList?.RefreshBoundRows();
            if (_lGuiWorldMapShowNpcs)
                RefreshLGuiWorldMapNpcItems();
            ApplyLGuiWorldMapTransform();
        }
        _lGuiWorldMapFocusGridX = gridX;
        _lGuiWorldMapFocusGridY = gridY;
        _lGuiWorldMapInfoLocked = true;
        SyncLGuiWorldMapNavigationTarget();
        SetLGuiWorldMapInfo(BuildLGuiWorldMapCellInfo(cellX, cellY));
        SyncLGuiWorldMapPinInput();
    }

    private bool TryGetLGuiWorldMapCell(Vector2 screenPosition, out int cellX, out int cellY)
    {
        cellX = -1;
        cellY = -1;
        if (_lGuiWorldMapViewport == null || !TryGetLGuiWorldMapSize(out var cellsX, out var cellsY))
            return false;
        var camera = _lGuiCanvas == null ? null : _lGuiCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_lGuiWorldMapViewport, screenPosition, camera, out var local))
            return false;
        var rect = _lGuiWorldMapViewport.rect;
        var zoom = GetLGuiWorldMapZoom(cellsX, cellsY);
        var mapX = (local.x - rect.xMin - _lGuiWorldMapPan.x) / zoom;
        var mapY = (rect.yMax - local.y + _lGuiWorldMapPan.y) / zoom;
        if (mapX < 0f || mapY < 0f || mapX >= cellsX || mapY >= cellsY)
            return false;
        cellX = (int)mapX;
        cellY = cellsY - 1 - (int)mapY;
        return true;
    }

    private void UpdateLGuiWorldMapHover(Vector2 screenPosition)
    {
        var camera = _lGuiCanvas == null ? null : _lGuiCanvas.worldCamera;
        for (var i = 0; i < _lGuiWorldMapBlockRects.Count; i++)
        {
            var blocker = _lGuiWorldMapBlockRects[i];
            if (blocker != null && RectTransformUtility.RectangleContainsScreenPoint(blocker, screenPosition, camera))
                return;
        }
        if (!TryGetLGuiWorldMapCell(screenPosition, out var cellX, out var cellY))
            return;

        var packed = cellX * 100003 + cellY;
        if (packed == _lGuiWorldMapHoverCell)
            return;
        _lGuiWorldMapHoverCell = packed;

        if (_lGuiWorldMapTab == 0)
        {
            SetLGuiWorldMapInfo(BuildLGuiWorldMapCellInfo(cellX, cellY));
        }
        else
        {
            SetLGuiWorldMapInfo(BuildLGuiLocalMapCellInfo(cellX, cellY));
        }
    }
}
