using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal readonly struct ProjectedCellOverlay
{
    internal ProjectedCellOverlay(Point point, Color32 fill, Color32 border)
    {
        Point = new Point(point);
        Fill = fill;
        Border = border;
    }

    internal Point Point { get; }
    internal Color32 Fill { get; }
    internal Color32 Border { get; }
}

internal sealed class ProjectedCellOverlayRenderer : IDisposable
{
    private sealed class CellVisual
    {
        internal GameObject Root = null!;
        internal ProjectedCellGraphic Graphic = null!;
    }

    private readonly string _name;
    private readonly int _sortingOrder;
    private readonly List<CellVisual> _visuals = new List<CellVisual>();
    private GameObject? _root;
    private Canvas? _canvas;
    private RectTransform? _canvasRect;
    private Canvas? _gameUiCanvas;
    private RenderMode _gameUiRenderMode;
    private Camera? _gameUiCamera;
    private int _gameUiSortingLayerId = int.MinValue;
    private int _gameUiTargetDisplay = -1;
    private bool _disposed;

    internal ProjectedCellOverlayRenderer(string name, int sortingOrder)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "ProjectedCellOverlay" : name;
        _sortingOrder = sortingOrder;
    }

    internal void Render(IReadOnlyList<ProjectedCellOverlay> cells)
    {
        if (_disposed)
            return;
        if (cells == null || cells.Count == 0)
        {
            Clear();
            return;
        }

        try
        {
            EnsureInitialized();
            SyncCanvasBehindGameUi();
            if (_root == null || _canvasRect == null)
                return;
            if (!_root.activeSelf)
                _root.SetActive(true);

            var camera = GetSceneCamera();
            if (camera == null)
            {
                HideFrom(0);
                return;
            }

            var viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            var cameraPixelRect = camera.pixelRect;
            var canvasCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
            var visibleCount = 0;
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (!TryProjectCell(cell.Point, viewProjection, cameraPixelRect, canvasCamera,
                        out var left, out var top, out var right, out var bottom))
                    continue;
                var visual = GetOrCreateVisual(visibleCount++);
                visual.Graphic.SetColors(cell.Fill, cell.Border);
                visual.Graphic.SetQuad(left, top, right, bottom);
                if (!visual.Root.activeSelf)
                    visual.Root.SetActive(true);
            }
            HideFrom(visibleCount);
        }
        catch
        {
            Clear();
        }
    }

    internal void Clear()
    {
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        _canvas = null;
        _canvasRect = null;
        _gameUiCanvas = null;
        _gameUiCamera = null;
        _visuals.Clear();
    }

    private void EnsureInitialized()
    {
        if (_root != null)
            return;
        _root = new GameObject(_name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.Object.DontDestroyOnLoad(_root);
        _canvas = _root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = _sortingOrder;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560f, 1440f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = (RectTransform)_root.transform;
    }

    private void SyncCanvasBehindGameUi()
    {
        if (_canvas == null)
            return;

        Canvas? gameCanvas;
        try { gameCanvas = GameAccess.Ui.Root?.canvas; }
        catch { gameCanvas = null; }
        if (gameCanvas == null || ReferenceEquals(gameCanvas, _canvas))
            return;

        var renderMode = gameCanvas.renderMode;
        var worldCamera = gameCanvas.worldCamera;
        var sortingLayerId = gameCanvas.sortingLayerID;
        var targetDisplay = gameCanvas.targetDisplay;
        var changed = !ReferenceEquals(_gameUiCanvas, gameCanvas) ||
                      _gameUiRenderMode != renderMode ||
                      !ReferenceEquals(_gameUiCamera, worldCamera) ||
                      _gameUiSortingLayerId != sortingLayerId ||
                      _gameUiTargetDisplay != targetDisplay;
        if (!changed)
            return;

        _gameUiCanvas = gameCanvas;
        _gameUiRenderMode = renderMode;
        _gameUiCamera = worldCamera;
        _gameUiSortingLayerId = sortingLayerId;
        _gameUiTargetDisplay = targetDisplay;
        if (renderMode != RenderMode.WorldSpace)
        {
            _canvas.renderMode = renderMode;
            _canvas.worldCamera = worldCamera;
            _canvas.planeDistance = gameCanvas.planeDistance;
        }
        else
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = null;
        }
        _canvas.sortingLayerID = sortingLayerId;
        _canvas.sortingOrder = _sortingOrder;
        _canvas.targetDisplay = targetDisplay;
    }

    private CellVisual GetOrCreateVisual(int index)
    {
        if (index < _visuals.Count)
            return _visuals[index];

        var root = new GameObject("ProjectedCell", typeof(RectTransform), typeof(CanvasRenderer));
        root.transform.SetParent(_canvasRect, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var graphic = root.AddComponent<ProjectedCellGraphic>();
        graphic.color = Color.white;
        graphic.raycastTarget = false;
        var visual = new CellVisual
        {
            Root = root,
            Graphic = graphic
        };
        _visuals.Add(visual);
        return visual;
    }

    private void HideFrom(int firstHidden)
    {
        for (var i = firstHidden; i < _visuals.Count; i++)
        {
            var visual = _visuals[i];
            visual.Graphic.ClearQuad();
            if (visual.Root.activeSelf)
                visual.Root.SetActive(false);
        }
    }

    private bool TryProjectCell(
        Point point,
        Matrix4x4 viewProjection,
        Rect cameraPixelRect,
        Camera? canvasCamera,
        out Vector2 left,
        out Vector2 top,
        out Vector2 right,
        out Vector2 bottom)
    {
        left = top = right = bottom = default;
        if (point == null || !point.IsValid || _canvasRect == null)
            return false;
        var screen = GameAccess.Ui.Screen;
        if (screen == null)
            return false;
        var origin = point.Position();
        var tileSize = screen.tileWorldSize;
        var z = origin.z;
        var leftWorld = new Vector3(origin.x, origin.y + tileSize.y * 0.5f, z);
        var topWorld = new Vector3(origin.x + tileSize.x * 0.5f, origin.y + tileSize.y, z);
        var rightWorld = new Vector3(origin.x + tileSize.x, origin.y + tileSize.y * 0.5f, z);
        var bottomWorld = new Vector3(origin.x + tileSize.x * 0.5f, origin.y, z);
        return TryProjectPoint(leftWorld, viewProjection, cameraPixelRect, out var leftScreen) &&
               TryProjectPoint(topWorld, viewProjection, cameraPixelRect, out var topScreen) &&
               TryProjectPoint(rightWorld, viewProjection, cameraPixelRect, out var rightScreen) &&
               TryProjectPoint(bottomWorld, viewProjection, cameraPixelRect, out var bottomScreen) &&
               TryGuiPointToCanvas(leftScreen, canvasCamera, out left) &&
               TryGuiPointToCanvas(topScreen, canvasCamera, out top) &&
               TryGuiPointToCanvas(rightScreen, canvasCamera, out right) &&
               TryGuiPointToCanvas(bottomScreen, canvasCamera, out bottom);
    }

    private bool TryGuiPointToCanvas(Vector2 guiPoint, Camera? canvasCamera, out Vector2 localPoint)
    {
        var screenPoint = new Vector2(guiPoint.x, Screen.height - guiPoint.y);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            screenPoint,
            canvasCamera,
            out localPoint);
    }

    private static bool TryProjectPoint(Vector3 world, Matrix4x4 viewProjection, Rect pixelRect, out Vector2 guiPoint)
    {
        var clip = viewProjection * new Vector4(world.x, world.y, world.z, 1f);
        if (clip.w <= 0.00001f)
        {
            guiPoint = default;
            return false;
        }
        var inverseW = 1f / clip.w;
        var screenX = pixelRect.x + (clip.x * inverseW + 1f) * 0.5f * pixelRect.width;
        var screenY = pixelRect.y + (clip.y * inverseW + 1f) * 0.5f * pixelRect.height;
        guiPoint = new Vector2(screenX, Screen.height - screenY);
        return true;
    }

    private static Camera? GetSceneCamera()
    {
        try
        {
            var scene = GameAccess.Ui.Scene;
            if (scene != null && scene.cam != null)
                return scene.cam;
        }
        catch
        {
        }
        try { return Camera.main; }
        catch { return null; }
    }
}
