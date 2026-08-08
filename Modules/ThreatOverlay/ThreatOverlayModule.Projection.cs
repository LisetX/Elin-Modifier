using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class ThreatOverlayModule
{
    private void RefreshThreatPositions()
    {
        _threatPositionFrame = Time.frameCount;
        var camera = GetSceneCamera();
        if (camera == null || _threatCanvasRect == null)
            return;
        var viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
        var cameraPixelRect = camera.pixelRect;
        var canvasScale = _threatCanvas == null ? 1f : Math.Max(0.0001f, _threatCanvas.scaleFactor);
        var canvasSize = _threatCanvasRect.rect.size;
        var cameraChanged = !_threatCameraCacheValid ||
                            !ReferenceEquals(camera, _threatCachedCamera) ||
                            _threatCachedScreenWidth != Screen.width ||
                            _threatCachedScreenHeight != Screen.height ||
                            !ThreatRectApproximatelyEqual(_threatCachedCameraPixelRect, cameraPixelRect) ||
                            !ThreatMatrixApproximatelyEqual(_threatCachedViewProjection, viewProjection);
        if (cameraChanged)
        {
            _threatCachedCamera = camera;
            _threatCachedViewProjection = viewProjection;
            _threatCachedScreenWidth = Screen.width;
            _threatCachedScreenHeight = Screen.height;
            _threatCachedCameraPixelRect = cameraPixelRect;
            _threatCameraCacheValid = true;
        }
        var layoutChanged = false;
        for (var i = 0; i < _threatMarkers.Count; i++)
        {
            var marker = _threatMarkers[i];
            if (marker.PredictionVisualDirty ||
                !marker.HasScreenCache ||
                (marker.HasAppliedScreenRect &&
                 (Mathf.Abs(marker.LastAppliedCanvasScale - canvasScale) > 0.0001f ||
                  !ThreatVector2ApproximatelyEqual(marker.LastAppliedCanvasSize, canvasSize))))
            {
                layoutChanged = true;
                break;
            }
        }
        if (!cameraChanged && !layoutChanged && !HaveThreatEnemyPositionsChanged())
            return;
        for (var i = 0; i < _threatMarkers.Count; i++)
        {
            var marker = _threatMarkers[i];
            var chara = marker.Chara;
            if (chara == null)
            {
                if (marker.Root.activeSelf)
                    marker.Root.SetActive(false);
                HideThreatMoveCell(marker);
                continue;
            }
            if (!TryGetThreatScreenRect(marker, chara, viewProjection, cameraPixelRect, cameraChanged, out var screenRect))
            {
                if (marker.Root.activeSelf)
                    marker.Root.SetActive(false);
                HideThreatMoveCell(marker);
                continue;
            }
            if (marker.HasAppliedScreenRect &&
                ThreatRectApproximatelyEqual(marker.LastAppliedScreenRect, screenRect) &&
                Mathf.Abs(marker.LastAppliedCanvasScale - canvasScale) <= 0.0001f &&
                ThreatVector2ApproximatelyEqual(marker.LastAppliedCanvasSize, canvasSize))
            {
                if (!marker.Root.activeSelf)
                    marker.Root.SetActive(true);
                RefreshThreatMoveCell(marker, viewProjection, cameraPixelRect);
                continue;
            }
            var bottomLeftScreen = new Vector2(screenRect.xMin, Screen.height - screenRect.yMax);
            var topRightScreen = new Vector2(screenRect.xMax, Screen.height - screenRect.yMin);
            var canvasCamera = _threatCanvas != null && _threatCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _threatCanvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_threatCanvasRect, bottomLeftScreen, canvasCamera, out var bottomLeft) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(_threatCanvasRect, topRightScreen, canvasCamera, out var topRight))
            {
                if (marker.Root.activeSelf)
                    marker.Root.SetActive(false);
                HideThreatMoveCell(marker);
                continue;
            }
            if (!marker.Root.activeSelf)
                marker.Root.SetActive(true);
            var size = topRight - bottomLeft;
            var position = (bottomLeft + topRight) * 0.5f;
            var targetSize = new Vector2(Math.Max(22f, Math.Abs(size.x)), Math.Max(28f, Math.Abs(size.y)));
            if ((marker.Rect.anchoredPosition - position).sqrMagnitude > 0.0001f)
                marker.Rect.anchoredPosition = position;
            if ((marker.Rect.sizeDelta - targetSize).sqrMagnitude > 0.0001f)
                marker.Rect.sizeDelta = targetSize;
            marker.HasAppliedScreenRect = true;
            marker.LastAppliedScreenRect = screenRect;
            marker.LastAppliedCanvasScale = canvasScale;
            marker.LastAppliedCanvasSize = canvasSize;
            RefreshThreatMoveCell(marker, viewProjection, cameraPixelRect);
        }
    }
    private void RefreshThreatMoveCell(ThreatMarker marker, Matrix4x4 viewProjection, Rect cameraPixelRect)
    {
        marker.PredictionVisualDirty = false;
        if (marker.PredictedMovePoint == null || _threatCanvasRect == null)
        {
            HideThreatMoveCell(marker);
            return;
        }

        try
        {
            var canvasCamera = _threatCanvas != null && _threatCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _threatCanvas.worldCamera
                : null;
            if (!TryProjectThreatMoveQuad(
                    marker.PredictedMovePoint,
                    viewProjection,
                    cameraPixelRect,
                    canvasCamera,
                    out var left,
                    out var top,
                    out var right,
                    out var bottom))
            {
                HideThreatMoveCell(marker);
                return;
            }
            if (!marker.MoveCellRoot.activeSelf)
                marker.MoveCellRoot.SetActive(true);
            marker.MoveCell.SetQuad(left, top, right, bottom);
        }
        catch
        {
            HideThreatMoveCell(marker);
        }
    }
    private bool TryProjectThreatMoveQuad(
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
        var origin = point.Position();
        var tileSize = GameAccess.Ui.Screen.tileWorldSize;
        var z = origin.z;
        var leftWorld = new Vector3(origin.x, origin.y + tileSize.y * 0.5f, z);
        var topWorld = new Vector3(origin.x + tileSize.x * 0.5f, origin.y + tileSize.y, z);
        var rightWorld = new Vector3(origin.x + tileSize.x, origin.y + tileSize.y * 0.5f, z);
        var bottomWorld = new Vector3(origin.x + tileSize.x * 0.5f, origin.y, z);
        return TryProjectThreatPoint(leftWorld, viewProjection, cameraPixelRect, out var leftScreen) &&
               TryProjectThreatPoint(topWorld, viewProjection, cameraPixelRect, out var topScreen) &&
               TryProjectThreatPoint(rightWorld, viewProjection, cameraPixelRect, out var rightScreen) &&
               TryProjectThreatPoint(bottomWorld, viewProjection, cameraPixelRect, out var bottomScreen) &&
               TryThreatGuiPointToCanvas(leftScreen, canvasCamera, out left) &&
               TryThreatGuiPointToCanvas(topScreen, canvasCamera, out top) &&
               TryThreatGuiPointToCanvas(rightScreen, canvasCamera, out right) &&
               TryThreatGuiPointToCanvas(bottomScreen, canvasCamera, out bottom);
    }
    private bool TryThreatGuiPointToCanvas(Vector2 guiPoint, Camera? canvasCamera, out Vector2 localPoint)
    {
        var screenPoint = new Vector2(guiPoint.x, Screen.height - guiPoint.y);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _threatCanvasRect,
            screenPoint,
            canvasCamera,
            out localPoint);
    }
    private static void HideThreatMoveCell(ThreatMarker marker)
    {
        marker.PredictionVisualDirty = false;
        marker.MoveCell.ClearQuad();
        if (marker.MoveCellRoot.activeSelf)
            marker.MoveCellRoot.SetActive(false);
    }
    private static bool TryGetThreatScreenRect(ThreatMarker marker, Chara chara, Matrix4x4 viewProjection, Rect cameraPixelRect, bool cameraChanged, out Rect rect)
    {
        rect = default;
        try
        {
            var cardRenderer = chara.renderer;
            marker.CachedRendererMissing = cardRenderer == null;
            marker.CachedRendererSkip = cardRenderer != null && cardRenderer.skip;
            if (marker.CachedRendererMissing || marker.CachedRendererSkip)
                return CacheThreatScreenResult(marker, false, default);
            marker.HasObservedWorldPosition = true;
            marker.ObservedWorldPosition = cardRenderer!.position;

            var actor = cardRenderer.actor;
            var hasBounds = false;
            var bounds = default(Bounds);
            if (actor != null)
            {
                var sr = actor.sr;
                if (sr != null && sr.enabled)
                {
                    bounds = sr.bounds;
                    hasBounds = true;
                }
                var sr2 = actor.sr2;
                if (sr2 != null && sr2.enabled)
                {
                    if (hasBounds)
                        bounds.Encapsulate(sr2.bounds);
                    else
                    {
                        bounds = sr2.bounds;
                        hasBounds = true;
                    }
                }
            }

            if (hasBounds)
            {
                if (!cameraChanged && marker.HasScreenCache && marker.CachedUsesBounds &&
                    ThreatVectorApproximatelyEqual(marker.CachedBoundsCenter, bounds.center) &&
                    ThreatVectorApproximatelyEqual(marker.CachedBoundsSize, bounds.size))
                {
                    rect = marker.CachedScreenRect;
                    return marker.CachedScreenVisible;
                }

                var visible = TryBoundsToScreenRect(bounds, viewProjection, cameraPixelRect, out rect);
                if (visible)
                    TightenMarkerRect(ref rect, 0.78f, 0.96f);
                marker.CachedUsesBounds = true;
                marker.CachedBoundsCenter = bounds.center;
                marker.CachedBoundsSize = bounds.size;
                return CacheThreatScreenResult(marker, visible, rect);
            }

            var center = cardRenderer.PositionCenter();
            var basePosition = cardRenderer.position;
            if (!cameraChanged && marker.HasScreenCache && !marker.CachedUsesBounds &&
                ThreatVectorApproximatelyEqual(marker.CachedApproxCenter, center) &&
                ThreatVectorApproximatelyEqual(marker.CachedApproxBase, basePosition))
            {
                rect = marker.CachedScreenRect;
                return marker.CachedScreenVisible;
            }

            var fallbackVisible = TryApproxCharaScreenRect(cardRenderer, viewProjection, cameraPixelRect, out rect);
            marker.CachedUsesBounds = false;
            marker.CachedApproxCenter = center;
            marker.CachedApproxBase = basePosition;
            return CacheThreatScreenResult(marker, fallbackVisible, rect);
        }
        catch
        {
            return CacheThreatScreenResult(marker, false, default);
        }
    }
    private static bool CacheThreatScreenResult(ThreatMarker marker, bool visible, Rect rect)
    {
        marker.HasScreenCache = true;
        marker.CachedScreenVisible = visible;
        marker.CachedScreenRect = rect;
        return visible;
    }
    private bool HaveThreatEnemyPositionsChanged()
    {
        for (var i = 0; i < _threatMarkers.Count; i++)
        {
            var marker = _threatMarkers[i];
            var chara = marker.Chara;
            if (chara == null)
                return true;
            try
            {
                var cardRenderer = chara.renderer;
                var missing = cardRenderer == null;
                if (missing != marker.CachedRendererMissing)
                    return true;
                if (missing)
                    continue;
                var skip = cardRenderer!.skip;
                if (skip != marker.CachedRendererSkip)
                    return true;
                if (skip)
                    continue;
                if (!marker.HasObservedWorldPosition ||
                    !ThreatVectorApproximatelyEqual(marker.ObservedWorldPosition, cardRenderer!.position))
                    return true;
            }
            catch
            {
                return true;
            }
        }
        return false;
    }
    private static bool ThreatVectorApproximatelyEqual(Vector3 left, Vector3 right)
    {
        return (left - right).sqrMagnitude <= 0.000001f;
    }
    private static bool ThreatVector2ApproximatelyEqual(Vector2 left, Vector2 right)
    {
        return (left - right).sqrMagnitude <= 0.000001f;
    }
    private static bool ThreatRectApproximatelyEqual(Rect left, Rect right)
    {
        return Mathf.Abs(left.x - right.x) <= 0.001f &&
               Mathf.Abs(left.y - right.y) <= 0.001f &&
               Mathf.Abs(left.width - right.width) <= 0.001f &&
               Mathf.Abs(left.height - right.height) <= 0.001f;
    }
    private static bool TryBoundsToScreenRect(Bounds bounds, Matrix4x4 viewProjection, Rect pixelRect, out Rect rect)
    {
        rect = default;
        var min = bounds.min;
        var max = bounds.max;
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var count = 0;
        AccumulateThreatScreenPoint(new Vector3(min.x, min.y, min.z), viewProjection, pixelRect, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateThreatScreenPoint(new Vector3(min.x, min.y, max.z), viewProjection, pixelRect, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateThreatScreenPoint(new Vector3(min.x, max.y, min.z), viewProjection, pixelRect, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateThreatScreenPoint(new Vector3(min.x, max.y, max.z), viewProjection, pixelRect, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateThreatScreenPoint(new Vector3(max.x, min.y, min.z), viewProjection, pixelRect, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateThreatScreenPoint(new Vector3(max.x, min.y, max.z), viewProjection, pixelRect, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateThreatScreenPoint(new Vector3(max.x, max.y, min.z), viewProjection, pixelRect, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateThreatScreenPoint(new Vector3(max.x, max.y, max.z), viewProjection, pixelRect, ref minX, ref minY, ref maxX, ref maxY, ref count);
        if (count == 0)
            return false;
        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return NormalizeMarkerRect(ref rect);
    }
    private static bool TryApproxCharaScreenRect(CardRenderer cardRenderer, Matrix4x4 viewProjection, Rect pixelRect, out Rect rect)
    {
        rect = default;
        var center = cardRenderer.PositionCenter();
        var basePosition = cardRenderer.position;
        if (!TryProjectThreatPoint(center + Vector3.up * 0.55f, viewProjection, pixelRect, out var top) ||
            !TryProjectThreatPoint(basePosition + Vector3.down * 0.1f, viewProjection, pixelRect, out var bottom) ||
            !TryProjectThreatPoint(center + Vector3.left * 0.45f, viewProjection, pixelRect, out var left) ||
            !TryProjectThreatPoint(center + Vector3.right * 0.45f, viewProjection, pixelRect, out var right))
        {
            return false;
        }

        rect = Rect.MinMaxRect(Mathf.Min(left.x, right.x), Mathf.Min(top.y, bottom.y), Mathf.Max(left.x, right.x), Mathf.Max(top.y, bottom.y));
        if (!NormalizeMarkerRect(ref rect))
            return false;
        TightenMarkerRect(ref rect, 0.82f, 0.98f);
        return true;
    }
    private static void AccumulateThreatScreenPoint(Vector3 world, Matrix4x4 viewProjection, Rect pixelRect,
        ref float minX, ref float minY, ref float maxX, ref float maxY, ref int count)
    {
        if (!TryProjectThreatPoint(world, viewProjection, pixelRect, out var screen))
            return;
        minX = Mathf.Min(minX, screen.x);
        maxX = Mathf.Max(maxX, screen.x);
        minY = Mathf.Min(minY, screen.y);
        maxY = Mathf.Max(maxY, screen.y);
        count++;
    }
    private static bool TryProjectThreatPoint(Vector3 world, Matrix4x4 viewProjection, Rect pixelRect, out Vector2 guiPoint)
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
    private static bool ThreatMatrixApproximatelyEqual(Matrix4x4 left, Matrix4x4 right)
    {
        for (var i = 0; i < 16; i++)
            if (Mathf.Abs(left[i] - right[i]) > 0.000001f)
                return false;
        return true;
    }
    private static int GetThreatUid(Chara chara)
    {
        try { return chara.uid; }
        catch { return 0; }
    }
    private RectTransform CreateThreatRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
    private Text CreateThreatText(Transform parent, string name, int fontSize, TextAnchor anchor)
    {
        var rect = CreateThreatRect(parent, name);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = _lGuiFont ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
