using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private void EnsureWatermarkErrorNotification()
    {
        if (_watermarkErrorRoot != null || _watermarkRoot == null)
            return;

        _watermarkErrorBar = CreateLGuiRect(_watermarkRoot.transform, "WatermarkErrorBar");
        _watermarkErrorBar.anchorMin = new Vector2(0.5f, 1f);
        _watermarkErrorBar.anchorMax = new Vector2(0.5f, 1f);
        _watermarkErrorBar.pivot = new Vector2(0.5f, 1f);
        _watermarkErrorBar.sizeDelta = new Vector2(360f, WatermarkErrorNormalHeight);
        _watermarkErrorBar.localScale = Vector3.one * GetWatermarkRelativeScale();
        _watermarkErrorRoot = _watermarkErrorBar.gameObject;
        _watermarkErrorRoot.name = "ElinModifier.RuntimeWatermarkError";
        _watermarkErrorRoot.transform.SetSiblingIndex(0);

        _watermarkErrorBackground = _watermarkErrorRoot.AddComponent<Image>();
        _watermarkErrorBackground.raycastTarget = true;
        _watermarkErrorRoot.AddComponent<RectMask2D>();
        _watermarkErrorCanvasGroup = _watermarkErrorRoot.AddComponent<CanvasGroup>();
        _watermarkErrorCanvasGroup.alpha = 0f;
        _watermarkErrorCanvasGroup.interactable = true;
        _watermarkErrorCanvasGroup.blocksRaycasts = true;

        var input = _watermarkErrorRoot.AddComponent<WatermarkErrorAlertInput>();
        input.Initialize(
            ToggleWatermarkErrorDetails,
            () => DismissWatermarkErrorNotification(false),
            hovered => _watermarkErrorHovered = hovered,
            delta => ScrollWatermarkErrorDetails(delta));

        _watermarkErrorSummaryText = CreateLGuiText(
            _watermarkErrorBar,
            "Summary",
            "",
            18,
            TextAnchor.MiddleCenter,
            FontStyle.Normal);
        StretchLGuiRect(_watermarkErrorSummaryText.rectTransform, 24f, 3f, 24f, 4f);
        _watermarkErrorSummaryText.raycastTarget = false;
        _watermarkErrorSummaryText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _watermarkErrorSummaryText.verticalOverflow = VerticalWrapMode.Truncate;
        _watermarkErrorSummaryCanvasGroup = _watermarkErrorSummaryText.gameObject.AddComponent<CanvasGroup>();
        _watermarkErrorSummaryCanvasGroup.alpha = 1f;
        _watermarkErrorSummaryCanvasGroup.interactable = false;
        _watermarkErrorSummaryCanvasGroup.blocksRaycasts = false;

        _watermarkErrorViewport = CreateLGuiRect(_watermarkErrorBar, "DetailViewport");
        StretchLGuiRect(_watermarkErrorViewport, 24f, 54f, 24f, 18f);
        _watermarkErrorViewport.gameObject.AddComponent<RectMask2D>();
        _watermarkErrorDetailCanvasGroup = _watermarkErrorViewport.gameObject.AddComponent<CanvasGroup>();
        _watermarkErrorDetailCanvasGroup.alpha = 0f;
        _watermarkErrorDetailCanvasGroup.interactable = false;
        _watermarkErrorDetailCanvasGroup.blocksRaycasts = false;
        _watermarkErrorContent = CreateLGuiRect(_watermarkErrorViewport, "DetailContent");
        _watermarkErrorContent.anchorMin = new Vector2(0f, 1f);
        _watermarkErrorContent.anchorMax = new Vector2(1f, 1f);
        _watermarkErrorContent.pivot = new Vector2(0.5f, 1f);
        _watermarkErrorContent.anchoredPosition = Vector2.zero;
        _watermarkErrorContent.sizeDelta = new Vector2(0f, 300f);
        _watermarkErrorDetailText = CreateLGuiText(
            _watermarkErrorContent,
            "DetailText",
            "",
            16,
            TextAnchor.UpperLeft,
            FontStyle.Normal);
        _watermarkErrorDetailText.rectTransform.anchorMin = new Vector2(0f, 1f);
        _watermarkErrorDetailText.rectTransform.anchorMax = new Vector2(1f, 1f);
        _watermarkErrorDetailText.rectTransform.pivot = new Vector2(0.5f, 1f);
        _watermarkErrorDetailText.rectTransform.anchoredPosition = Vector2.zero;
        _watermarkErrorDetailText.rectTransform.sizeDelta = new Vector2(0f, 300f);
        _watermarkErrorDetailText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _watermarkErrorDetailText.verticalOverflow = VerticalWrapMode.Overflow;
        _watermarkErrorDetailText.raycastTarget = false;

        _watermarkErrorScrollRect = _watermarkErrorRoot.AddComponent<ScrollRect>();
        _watermarkErrorScrollRect.viewport = _watermarkErrorViewport;
        _watermarkErrorScrollRect.content = _watermarkErrorContent;
        _watermarkErrorScrollRect.horizontal = false;
        _watermarkErrorScrollRect.vertical = true;
        _watermarkErrorScrollRect.movementType = ScrollRect.MovementType.Clamped;
        _watermarkErrorScrollRect.scrollSensitivity = 34f;
        _watermarkErrorViewport.gameObject.SetActive(false);
        _watermarkErrorRoot.SetActive(false);
    }
    private bool ShouldWatermarkErrorSlideBelow()
    {
        if (_watermarkBar == null)
            return true;
        try
        {
            var screenPoint = RectTransformUtility.WorldToScreenPoint(null, _watermarkBar.position);
            return screenPoint.y >= Screen.height * 0.5f;
        }
        catch
        {
            return true;
        }
    }
    private void UpdateWatermarkErrorDirection(float targetHeight)
    {
        if (_watermarkBar == null || _watermarkErrorBar == null || Screen.height <= 0)
            return;

        try
        {
            _watermarkBar.GetWorldCorners(_watermarkErrorScreenCorners);
            var bottomY = RectTransformUtility.WorldToScreenPoint(null, _watermarkErrorScreenCorners[0]).y;
            var topY = RectTransformUtility.WorldToScreenPoint(null, _watermarkErrorScreenCorners[1]).y;
            var canvasScale = _watermarkCanvas == null ? 1f : Mathf.Max(0.01f, _watermarkCanvas.scaleFactor);
            var requiredHeight = targetHeight * GetWatermarkRelativeScale() * canvasScale;
            var margin = 8f * canvasScale;
            var availableBelow = Mathf.Max(0f, bottomY - margin);
            var availableAbove = Mathf.Max(0f, Screen.height - topY - margin);
            var belowFits = availableBelow + 0.5f >= requiredHeight;
            var aboveFits = availableAbove + 0.5f >= requiredHeight;
            var desiredBelow = _watermarkErrorBelow;

            if (_watermarkErrorBelow && !belowFits)
                desiredBelow = !aboveFits ? availableBelow >= availableAbove : false;
            else if (!_watermarkErrorBelow && !aboveFits)
                desiredBelow = belowFits || availableBelow >= availableAbove;

            if (desiredBelow != _watermarkErrorBelow)
                SetWatermarkErrorDirection(desiredBelow);
        }
        catch
        {
        }
    }
    private void SetWatermarkErrorDirection(bool below)
    {
        if (_watermarkErrorBar == null || _watermarkErrorBelow == below)
            return;

        var worldCenter = _watermarkErrorBar.TransformPoint(_watermarkErrorBar.rect.center);
        _watermarkErrorBelow = below;
        _watermarkErrorBar.pivot = below ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
        var shiftedCenter = _watermarkErrorBar.TransformPoint(_watermarkErrorBar.rect.center);
        _watermarkErrorBar.position += worldCenter - shiftedCenter;
        var position = _watermarkErrorBar.anchoredPosition;
        _watermarkErrorCurrentX = position.x;
        _watermarkErrorCurrentY = position.y;
        _watermarkErrorVelocityY = 0f;
    }
    private float GetWatermarkErrorHiddenY(Vector2 watermarkPosition)
    {
        var watermarkHeight = _watermarkBar == null ? 42f : _watermarkBar.rect.height;
        return watermarkPosition.y - watermarkHeight * GetWatermarkRelativeScale() * 0.45f;
    }
    private float GetWatermarkErrorVisibleY(Vector2 watermarkPosition)
    {
        var watermarkHeight = _watermarkBar == null ? 42f : _watermarkBar.rect.height;
        var scaledHeight = watermarkHeight * GetWatermarkRelativeScale();
        return _watermarkErrorBelow
            ? watermarkPosition.y - scaledHeight - 8f
            : watermarkPosition.y + 8f;
    }
    private void ToggleWatermarkErrorDetails()
    {
        if (!_watermarkErrorActive || _watermarkErrorDismissing)
            return;
        _watermarkErrorExpanded = !_watermarkErrorExpanded;
        _watermarkErrorDetailLayoutDirty = true;
        _watermarkErrorLastDetailLayoutWidth = -1f;
        if (_watermarkErrorScrollRect != null && _watermarkErrorExpanded)
            _watermarkErrorScrollRect.verticalNormalizedPosition = 1f;
    }
    private void ScrollWatermarkErrorDetails(float delta)
    {
        if (!_watermarkErrorExpanded || _watermarkErrorScrollRect == null)
            return;
        _watermarkErrorScrollRect.verticalNormalizedPosition = Mathf.Clamp01(
            _watermarkErrorScrollRect.verticalNormalizedPosition + delta * 0.12f);
    }
}
