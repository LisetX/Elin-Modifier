using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed partial class WorldMapModule
{
    private const int WorldMapNavMaxMarkers = 320;
    private const int WorldMapNavMaxStrikes = 8;
    private const float WorldMapNavMarkerScale = 0.32f;
    private const float WorldMapNavTargetScale = 1.2f;
    private const float WorldMapNavDepthBias = 0.02f;
    private const float WorldMapNavTargetDepthBias = 0.03f;

    private readonly List<SpriteRenderer> _navMarkers = new List<SpriteRenderer>();
    private GameObject? _navRoot;
    private Texture2D? _navDotTexture;
    private Sprite? _navDotSprite;
    private Texture2D? _navBoxTexture;
    private Sprite? _navBoxSprite;
    private int _navOverlayVersion = -1;
    private int _navOverlayStrikes;

    internal void LateTick()
    {
        if (!_navigationEnabled || _navOverlayStrikes >= WorldMapNavMaxStrikes)
            return;
        try
        {
            UpdateNavigationOverlay();
            _navOverlayStrikes = 0;
        }
        catch
        {
            _navOverlayStrikes++;
            try
            {
                HideNavigationOverlay();
            }
            catch
            {
            }
        }
    }

    private void UpdateNavigationOverlay()
    {
        if (!_navigationEnabled || !HasNavigationTarget)
        {
            HideNavigationOverlay();
            return;
        }

        RefreshNavigationPath();
        if (!IsOnRegionMap())
        {
            HideNavigationOverlay();
            return;
        }
        var path = _navPath;
        if (path == null || path.Count == 0)
        {
            HideNavigationOverlay();
            return;
        }
        if (_navOverlayVersion == _navVersion)
            return;
        RebuildNavigationMarkers(path);
        _navOverlayVersion = _navVersion;
    }

    private static bool IsOnRegionMap()
    {
        try
        {
            var zone = GameAccess.World.CurrentZone;
            return zone != null && zone.IsRegion;
        }
        catch
        {
            return false;
        }
    }

    private void RebuildNavigationMarkers(List<int> path)
    {
        var terrain = _terrain;
        EnsureNavigationRoot();
        EnsureNavigationSprites();
        if (terrain == null || _navRoot == null || _navDotSprite == null || _navBoxSprite == null)
        {
            HideNavigationOverlay();
            return;
        }

        var unit = ResolveNavigationUnit();
        var layer = ResolveNavigationLayer();
        _navRoot.layer = layer;
        var stride = Math.Max(1, (path.Count + WorldMapNavMaxMarkers - 1) / WorldMapNavMaxMarkers);
        var used = 0;

        for (var i = 0; i < path.Count; i += stride)
        {
            var cell = path[i];
            var cellX = cell % terrain.Width;
            var cellY = cell / terrain.Width;
            if (!TryGetNavigationWorldPosition(cellX, cellY, out var position))
                continue;
            var marker = GetNavigationMarker(used);
            if (marker == null)
                break;
            used++;
            marker.sprite = _navDotSprite;
            marker.color = ColorRoute;
            marker.gameObject.layer = layer;
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * (unit * WorldMapNavMarkerScale);
            marker.enabled = true;
        }

        if (TryGetNavigationTargetCell(out var targetX, out var targetY) &&
            TryGetNavigationWorldPosition(targetX, targetY, out var targetPosition))
        {
            var marker = GetNavigationMarker(used);
            if (marker != null)
            {
                used++;
                marker.sprite = _navBoxSprite;
                marker.color = ColorRouteTarget;
                marker.gameObject.layer = layer;
                marker.transform.position = new Vector3(
                    targetPosition.x,
                    targetPosition.y,
                    targetPosition.z - WorldMapNavTargetDepthBias + WorldMapNavDepthBias);
                marker.transform.localScale = Vector3.one * (unit * WorldMapNavTargetScale);
                marker.enabled = true;
            }
        }

        for (var i = used; i < _navMarkers.Count; i++)
        {
            var marker = _navMarkers[i];
            if (marker != null)
                marker.enabled = false;
        }
    }

    private void HideNavigationOverlay()
    {
        for (var i = 0; i < _navMarkers.Count; i++)
        {
            var marker = _navMarkers[i];
            if (marker != null)
                marker.enabled = false;
        }
        _navOverlayVersion = -1;
    }

    private void EnsureNavigationRoot()
    {
        if (_navRoot != null)
            return;
        try
        {
            _navRoot = new GameObject("ElinModifierWorldMapRoute");
            UnityEngine.Object.DontDestroyOnLoad(_navRoot);
        }
        catch
        {
            _navRoot = null;
        }
    }

    private SpriteRenderer? GetNavigationMarker(int index)
    {
        while (_navMarkers.Count <= index)
        {
            if (_navRoot == null)
                return null;
            SpriteRenderer renderer;
            try
            {
                var host = new GameObject("RouteMarker");
                host.transform.SetParent(_navRoot.transform, false);
                renderer = host.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = 30000;
            }
            catch
            {
                return null;
            }
            _navMarkers.Add(renderer);
        }
        var marker = _navMarkers[index];
        return marker == null ? null : marker;
    }

    private void EnsureNavigationSprites()
    {
        try
        {
            if (_navDotSprite == null)
            {
                _navDotTexture = CreateNavigationDotTexture(16);
                _navDotSprite = Sprite.Create(
                    _navDotTexture,
                    new Rect(0f, 0f, 16f, 16f),
                    new Vector2(0.5f, 0.5f),
                    16f);
            }
            if (_navBoxSprite == null)
            {
                _navBoxTexture = CreateNavigationBoxTexture(32, 4);
                _navBoxSprite = Sprite.Create(
                    _navBoxTexture,
                    new Rect(0f, 0f, 32f, 32f),
                    new Vector2(0.5f, 0.5f),
                    32f);
            }
        }
        catch
        {
            _navDotSprite = null;
            _navBoxSprite = null;
        }
    }

    private static Texture2D CreateNavigationDotTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        var center = (size - 1) * 0.5f;
        var radius = size * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp01((radius - distance) / Mathf.Max(1f, radius * 0.35f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static Texture2D CreateNavigationBoxTexture(int size, int border)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        var clear = new Color32(255, 255, 255, 0);
        var solid = new Color32(255, 255, 255, 255);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var edge = x < border || y < border || x >= size - border || y >= size - border;
                pixels[y * size + x] = edge ? solid : clear;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static int ResolveNavigationLayer()
    {
        try
        {
            var tileMap = EClass.screen.tileMap;
            return tileMap == null ? 0 : tileMap.gameObject.layer;
        }
        catch
        {
            return 0;
        }
    }

    private static float ResolveNavigationUnit()
    {
        try
        {
            var align = EClass.screen.tileAlign;
            return Mathf.Max(0.05f, Mathf.Max(Mathf.Abs(align.x), Mathf.Abs(align.y)));
        }
        catch
        {
            return 1f;
        }
    }

    private static bool TryGetNavigationWorldPosition(int cellX, int cellY, out Vector3 position)
    {
        position = Vector3.zero;
        try
        {
            var point = new Point(cellX, cellY);
            var value = point.PositionCenter();
            position = new Vector3(value.x, value.y, value.z - WorldMapNavDepthBias);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ReleaseNavigationOverlay()
    {
        for (var i = 0; i < _navMarkers.Count; i++)
        {
            try
            {
                if (_navMarkers[i] != null)
                    UnityEngine.Object.Destroy(_navMarkers[i].gameObject);
            }
            catch
            {
            }
        }
        _navMarkers.Clear();
        try
        {
            if (_navRoot != null)
                UnityEngine.Object.Destroy(_navRoot);
        }
        catch
        {
        }
        _navRoot = null;
        DestroyTexture(ref _navDotTexture, ref _navDotSprite);
        DestroyTexture(ref _navBoxTexture, ref _navBoxSprite);
        _navOverlayVersion = -1;
    }
}
