using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class ThreatOverlayModule
{
    internal void Tick()
    {
        if (!IsInitialized())
            return;

        SyncThreatCanvasBehindGameUi();

        if (!_hostileThreatMarker)
        {
            if (_threatRoot!.activeSelf)
                _threatRoot.SetActive(false);
            return;
        }

        if (!_threatRoot!.activeSelf)
            _threatRoot.SetActive(true);

        RefreshLockedDecisionPreviewScenario();
        if (_threatOverlayDirty || ShouldScanThreats())
            ScanThreats();
        if (ShouldRefreshThreatVitals())
            RefreshThreatVitals();
    }
    private void SyncThreatCanvasBehindGameUi()
    {
        if (_threatCanvas == null)
            return;

        Canvas? gameCanvas;
        try
        {
            gameCanvas = GameAccess.Ui.Root?.canvas;
        }
        catch
        {
            gameCanvas = null;
        }

        if (gameCanvas == null || ReferenceEquals(gameCanvas, _threatCanvas))
            return;

        var renderMode = gameCanvas.renderMode;
        var worldCamera = gameCanvas.worldCamera;
        var sortingLayerId = gameCanvas.sortingLayerID;
        var targetDisplay = gameCanvas.targetDisplay;
        var changed = !ReferenceEquals(_threatGameUiCanvas, gameCanvas) ||
                      _threatGameUiRenderMode != renderMode ||
                      !ReferenceEquals(_threatGameUiCamera, worldCamera) ||
                      _threatGameUiSortingLayerId != sortingLayerId ||
                      _threatGameUiTargetDisplay != targetDisplay;
        if (!changed)
            return;

        _threatGameUiCanvas = gameCanvas;
        _threatGameUiRenderMode = renderMode;
        _threatGameUiCamera = worldCamera;
        _threatGameUiSortingLayerId = sortingLayerId;
        _threatGameUiTargetDisplay = targetDisplay;

        if (renderMode != RenderMode.WorldSpace)
        {
            _threatCanvas.renderMode = renderMode;
            _threatCanvas.worldCamera = worldCamera;
            _threatCanvas.planeDistance = gameCanvas.planeDistance;
        }
        else
        {
            _threatCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _threatCanvas.worldCamera = null;
        }
        _threatCanvas.sortingLayerID = sortingLayerId;
        _threatCanvas.sortingOrder = -32000;
        _threatCanvas.targetDisplay = targetDisplay;
        _threatCameraCacheValid = false;
        for (var i = 0; i < _threatMarkers.Count; i++)
            _threatMarkers[i].HasAppliedScreenRect = false;
    }
    internal void LateTick()
    {
        if (!IsInitialized() || !_hostileThreatMarker || !_threatRoot!.activeSelf)
            return;
        var now = SchedulerNow;
        var interval = _lowPerformanceMode ? 1f / 60f : 1f / 120f;
        if (now >= _threatLastPositionRefreshTime && now - _threatLastPositionRefreshTime < interval)
            return;
        if (_threatPositionFrame == Time.frameCount)
            return;
        _threatLastPositionRefreshTime = now;
        _threatPositionFrame = Time.frameCount;
        RefreshThreatPositions();
    }
    private void ScanThreats()
    {
        _threatSeen.Clear();
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            var charas = GameAccess.World.CurrentCharacters;
            if (pc == null || charas == null)
            {
                ReleaseUnseenThreats();
                _threatOverlayDirty = false;
                MarkThreatScanClean();
                return;
            }

            for (var i = 0; i < charas.Count; i++)
            {
                var chara = charas[i];
                if (!IsHostileThreat(chara, pc) || !CanPlayerCurrentlySee(pc, chara))
                    continue;
                var uid = GetThreatUid(chara);
                if (uid <= 0)
                    continue;
                _threatSeen.Add(uid);
                if (!_threatByUid.TryGetValue(uid, out var marker))
                {
                    marker = AcquireThreatMarker();
                    marker.Uid = uid;
                    marker.Chara = chara;
                    marker.Active = true;
                    marker.Root.SetActive(true);
                    _threatByUid[uid] = marker;
                    _threatMarkers.Add(marker);
                    InvalidateThreatVitals();
                }
                else
                {
                    marker.Chara = chara;
                    marker.Active = true;
                }
                var currentName = SafeName(chara);
                if (!string.Equals(marker.Name.text, currentName, StringComparison.Ordinal))
                    marker.Name.text = currentName;
                var currentLevel = GetThreatLevelText(chara);
                if (!string.Equals(marker.Level.text, currentLevel, StringComparison.Ordinal))
                    marker.Level.text = currentLevel;
                RefreshThreatPredictionState(marker, chara);
            }
        }
        catch
        {
        }
        ReleaseUnseenThreats();
        _threatOverlayDirty = false;
        MarkThreatScanClean();
    }
    private void ReleaseUnseenThreats()
    {
        for (var i = _threatMarkers.Count - 1; i >= 0; i--)
        {
            var marker = _threatMarkers[i];
            if (_threatSeen.Contains(marker.Uid))
                continue;
            _threatMarkers.RemoveAt(i);
            _threatByUid.Remove(marker.Uid);
            _capturedThreatActions.Remove(marker.Uid);
            _threatDecisionVersions.Remove(marker.Uid);
            RemoveLockedDecision(marker.Uid);
            ReleaseThreatMarker(marker);
        }
    }
    private ThreatMarker AcquireThreatMarker()
    {
        if (_threatPool.Count > 0)
            return _threatPool.Pop();
        return CreateThreatMarker();
    }
    private void ReleaseThreatMarker(ThreatMarker marker)
    {
        marker.Uid = 0;
        marker.Chara = null;
        marker.Active = false;
        marker.HasScreenCache = false;
        marker.CachedScreenVisible = false;
        marker.CachedRendererMissing = false;
        marker.CachedRendererSkip = false;
        marker.HasObservedWorldPosition = false;
        marker.HasAppliedScreenRect = false;
        marker.LastAppliedCanvasScale = -1f;
        marker.LastAppliedCanvasSize = Vector2.zero;
        marker.LastHpRatio = -1f;
        marker.LastPrediction = string.Empty;
        marker.PredictedMovePoint = null;
        marker.PredictionVisualDirty = false;
        marker.PredictionAi = null;
        marker.PredictionCurrent = null;
        marker.PredictionStatus = default;
        marker.PredictionTurn = int.MinValue;
        marker.PredictionOwnerX = int.MinValue;
        marker.PredictionOwnerZ = int.MinValue;
        marker.PredictionTargetX = int.MinValue;
        marker.PredictionTargetZ = int.MinValue;
        marker.PredictionActionCount = -1;
        marker.PredictionDecisionVersion = -1;
        marker.PredictionCaptureSerial = -1;
        marker.PredictionLockSerial = -1;
        marker.PredictionCaptureActive = false;
        marker.MoveCell.ClearQuad();
        marker.MoveCellRoot.SetActive(false);
        marker.Root.SetActive(false);
        _threatPool.Push(marker);
    }
    private ThreatMarker CreateThreatMarker()
    {
        var marker = new ThreatMarker();
        marker.Root = new GameObject("ThreatMarker", typeof(RectTransform));
        marker.Root.transform.SetParent(_threatCanvasRect, false);
        marker.Rect = (RectTransform)marker.Root.transform;
        marker.Rect.anchorMin = new Vector2(0.5f, 0.5f);
        marker.Rect.anchorMax = new Vector2(0.5f, 0.5f);
        marker.Rect.pivot = new Vector2(0.5f, 0.5f);

        var cornerColor = new Color(1f, 0.18f, 0.16f, 0.96f);
        CreateThreatCorner(marker.Rect, "TL", new Vector2(0f, 1f), new Vector2(0f, 1f), 1f, -1f, cornerColor);
        CreateThreatCorner(marker.Rect, "TR", new Vector2(1f, 1f), new Vector2(1f, 1f), -1f, -1f, cornerColor);
        CreateThreatCorner(marker.Rect, "BL", new Vector2(0f, 0f), new Vector2(0f, 0f), 1f, 1f, cornerColor);
        CreateThreatCorner(marker.Rect, "BR", new Vector2(1f, 0f), new Vector2(1f, 0f), -1f, 1f, cornerColor);

        var hpBack = CreateThreatRect(marker.Rect, "HpBack");
        hpBack.anchorMin = new Vector2(0f, 1f);
        hpBack.anchorMax = new Vector2(1f, 1f);
        hpBack.pivot = new Vector2(0f, 0f);
        hpBack.offsetMin = new Vector2(0f, 8f);
        hpBack.offsetMax = new Vector2(0f, 17f);
        var hpBackImage = hpBack.gameObject.AddComponent<Image>();
        hpBackImage.color = new Color(0f, 0f, 0f, 0.82f);
        hpBackImage.raycastTarget = false;
        marker.HpFill = CreateThreatRect(hpBack, "HpFill");
        marker.HpFill.anchorMin = Vector2.zero;
        marker.HpFill.anchorMax = Vector2.one;
        marker.HpFill.pivot = new Vector2(0f, 0.5f);
        marker.HpFill.offsetMin = new Vector2(1f, 1f);
        marker.HpFill.offsetMax = new Vector2(-1f, -1f);
        marker.HpImage = marker.HpFill.gameObject.AddComponent<Image>();
        marker.HpImage.color = new Color(0.2f, 0.95f, 0.32f, 0.95f);
        marker.HpImage.raycastTarget = false;

        marker.Level = CreateThreatText(marker.Rect, "Level", 15, TextAnchor.MiddleCenter);
        marker.Level.rectTransform.anchorMin = new Vector2(0f, 1f);
        marker.Level.rectTransform.anchorMax = new Vector2(1f, 1f);
        marker.Level.rectTransform.offsetMin = new Vector2(0f, 18f);
        marker.Level.rectTransform.offsetMax = new Vector2(0f, 40f);
        marker.Level.color = new Color(0.16f, 1f, 0.16f, 1f);

        marker.Prediction = CreateThreatText(marker.Rect, "Prediction", 15, TextAnchor.MiddleCenter);
        marker.Prediction.rectTransform.anchorMin = new Vector2(0f, 1f);
        marker.Prediction.rectTransform.anchorMax = new Vector2(1f, 1f);
        marker.Prediction.rectTransform.offsetMin = new Vector2(-72f, 41f);
        marker.Prediction.rectTransform.offsetMax = new Vector2(72f, 63f);
        marker.Prediction.color = new Color(1f, 0.92f, 0.12f, 1f);
        marker.Prediction.gameObject.SetActive(_hostileThreatBehaviorPrediction);

        marker.Name = CreateThreatText(marker.Rect, "Name", 15, TextAnchor.MiddleCenter);
        marker.Name.rectTransform.anchorMin = new Vector2(0f, 0f);
        marker.Name.rectTransform.anchorMax = new Vector2(1f, 0f);
        marker.Name.rectTransform.offsetMin = new Vector2(-24f, -30f);
        marker.Name.rectTransform.offsetMax = new Vector2(24f, -6f);
        marker.Name.color = Color.white;

        marker.MoveCellRoot = new GameObject("PredictedMoveCell", typeof(RectTransform), typeof(CanvasRenderer));
        marker.MoveCellRoot.transform.SetParent(_threatCanvasRect, false);
        marker.MoveCellRoot.transform.SetAsFirstSibling();
        var moveRect = (RectTransform)marker.MoveCellRoot.transform;
        moveRect.anchorMin = Vector2.zero;
        moveRect.anchorMax = Vector2.one;
        moveRect.offsetMin = Vector2.zero;
        moveRect.offsetMax = Vector2.zero;
        marker.MoveCell = marker.MoveCellRoot.AddComponent<ProjectedCellGraphic>();
        marker.MoveCell.color = Color.white;
        marker.MoveCell.raycastTarget = false;
        marker.MoveCellRoot.SetActive(false);
        return marker;
    }
    private void CreateThreatCorner(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, float xDirection, float yDirection, Color color)
    {
        var root = CreateThreatRect(parent, name);
        root.anchorMin = anchor;
        root.anchorMax = anchor;
        root.pivot = pivot;
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(20f, 20f);

        var horizontal = CreateThreatRect(root, "H");
        horizontal.anchorMin = pivot;
        horizontal.anchorMax = pivot;
        horizontal.pivot = pivot;
        horizontal.anchoredPosition = Vector2.zero;
        horizontal.sizeDelta = new Vector2(20f, 2f);
        var horizontalImage = horizontal.gameObject.AddComponent<Image>();
        horizontalImage.color = color;
        horizontalImage.raycastTarget = false;

        var vertical = CreateThreatRect(root, "V");
        vertical.anchorMin = pivot;
        vertical.anchorMax = pivot;
        vertical.pivot = pivot;
        vertical.anchoredPosition = Vector2.zero;
        vertical.sizeDelta = new Vector2(2f, 20f);
        var verticalImage = vertical.gameObject.AddComponent<Image>();
        verticalImage.color = color;
        verticalImage.raycastTarget = false;
    }
    private void RefreshThreatVitals()
    {
        for (var i = 0; i < _threatMarkers.Count; i++)
        {
            var marker = _threatMarkers[i];
            var chara = marker.Chara;
            if (chara == null)
                continue;
            RefreshThreatPredictionState(marker, chara);
            var ratio = Clamp(GetThreatHpRatio(chara), 0f, 1f);
            if (Mathf.Abs(marker.LastHpRatio - ratio) <= 0.0001f)
                continue;
            marker.LastHpRatio = ratio;
            marker.HpFill.anchorMax = new Vector2(ratio, 1f);
            marker.HpImage.color = Color.Lerp(new Color(1f, 0.12f, 0.12f, 0.95f), new Color(0.18f, 0.95f, 0.28f, 0.95f), ratio);
        }
    }
}
