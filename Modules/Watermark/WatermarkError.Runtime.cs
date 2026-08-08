using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private void DismissWatermarkErrorNotification(bool immediate)
    {
        if (immediate)
        {
            _watermarkErrorActive = false;
            _watermarkErrorDismissing = false;
            _watermarkErrorExpanded = false;
            _watermarkErrorHovered = false;
            _watermarkErrorCurrentAlpha = 0f;
            if (_watermarkErrorSummaryCanvasGroup != null)
                _watermarkErrorSummaryCanvasGroup.alpha = 1f;
            if (_watermarkErrorDetailCanvasGroup != null)
                _watermarkErrorDetailCanvasGroup.alpha = 0f;
            if (_watermarkErrorViewport != null)
                _watermarkErrorViewport.gameObject.SetActive(false);
            if (_watermarkErrorRoot != null)
                _watermarkErrorRoot.SetActive(false);
            return;
        }
        if (_watermarkErrorActive)
            _watermarkErrorDismissing = true;
    }
    private void TickWatermarkErrorNotification(float now)
    {
        DrainWatermarkErrorNotification();
        if (!_watermarkErrorActive || _watermarkErrorRoot == null || _watermarkErrorBar == null)
            return;

        var deltaTime = Mathf.Clamp(now - _watermarkErrorLastTickAt, 0f, 0.1f);
        _watermarkErrorLastTickAt = now;
        if (!_watermarkErrorDismissing && !_watermarkErrorHovered && !_watermarkErrorExpanded)
        {
            _watermarkErrorRemainingSeconds = Mathf.Max(0f, _watermarkErrorRemainingSeconds - deltaTime);
            if (_watermarkErrorRemainingSeconds <= 0f)
                _watermarkErrorDismissing = true;
        }

        RefreshWatermarkErrorSummaryText(false);
        if (_watermarkErrorExpanded || _watermarkErrorCurrentHeight > WatermarkErrorNormalHeight + 1f)
            UpdateWatermarkErrorDetailLayout(false);

        var watermarkPosition = _watermarkBar == null ? Vector2.zero : _watermarkBar.anchoredPosition;
        var normalWidth = GetWatermarkErrorNormalWidth();
        var targetWidth = _watermarkErrorExpanded ? Mathf.Max(normalWidth, 1080f) : normalWidth;
        var expandedHeight = GetWatermarkErrorExpandedHeight();
        var targetHeight = _watermarkErrorExpanded ? expandedHeight : WatermarkErrorNormalHeight;
        if (!_watermarkErrorDismissing)
            UpdateWatermarkErrorDirection(targetHeight);
        var hiddenY = GetWatermarkErrorHiddenY(watermarkPosition);
        var targetY = _watermarkErrorDismissing ? hiddenY : GetWatermarkErrorVisibleY(watermarkPosition);
        var targetAlpha = _watermarkErrorDismissing ? 0f : 1f;
        var targetX = ClampWatermarkErrorX(watermarkPosition.x, targetWidth);

        _watermarkErrorCurrentX = Mathf.SmoothDamp(_watermarkErrorCurrentX, targetX, ref _watermarkErrorVelocityX, 0.14f, Mathf.Infinity, deltaTime);
        _watermarkErrorCurrentY = Mathf.SmoothDamp(_watermarkErrorCurrentY, targetY, ref _watermarkErrorVelocityY, 0.18f, Mathf.Infinity, deltaTime);
        _watermarkErrorCurrentWidth = Mathf.SmoothDamp(_watermarkErrorCurrentWidth, targetWidth, ref _watermarkErrorVelocityWidth, 0.18f, Mathf.Infinity, deltaTime);
        _watermarkErrorCurrentHeight = Mathf.SmoothDamp(_watermarkErrorCurrentHeight, targetHeight, ref _watermarkErrorVelocityHeight, 0.18f, Mathf.Infinity, deltaTime);
        _watermarkErrorCurrentAlpha = Mathf.SmoothDamp(_watermarkErrorCurrentAlpha, targetAlpha, ref _watermarkErrorVelocityAlpha, 0.14f, Mathf.Infinity, deltaTime);

        var visibleX = ClampWatermarkErrorX(_watermarkErrorCurrentX, _watermarkErrorCurrentWidth);
        if (Mathf.Abs(visibleX - _watermarkErrorCurrentX) > 0.001f)
        {
            _watermarkErrorCurrentX = visibleX;
            _watermarkErrorVelocityX = 0f;
        }

        _watermarkErrorBar.anchoredPosition = new Vector2(_watermarkErrorCurrentX, _watermarkErrorCurrentY);
        _watermarkErrorBar.sizeDelta = new Vector2(_watermarkErrorCurrentWidth, _watermarkErrorCurrentHeight);
        _watermarkErrorBar.localScale = Vector3.one * GetWatermarkRelativeScale();
        if (_watermarkErrorCanvasGroup != null)
        {
            _watermarkErrorCanvasGroup.alpha = Mathf.Clamp01(_watermarkErrorCurrentAlpha);
            _watermarkErrorCanvasGroup.blocksRaycasts = !_watermarkErrorDismissing;
            _watermarkErrorCanvasGroup.interactable = !_watermarkErrorDismissing;
        }

        var expansion = Mathf.InverseLerp(WatermarkErrorNormalHeight, Mathf.Max(WatermarkErrorNormalHeight + 1f, expandedHeight), _watermarkErrorCurrentHeight);
        if (_watermarkErrorSummaryCanvasGroup != null)
            _watermarkErrorSummaryCanvasGroup.alpha = 1f - expansion;
        if (_watermarkErrorViewport != null)
        {
            var showDetails = _watermarkErrorExpanded || expansion > 0.02f;
            if (_watermarkErrorViewport.gameObject.activeSelf != showDetails)
                _watermarkErrorViewport.gameObject.SetActive(showDetails);
        }
        if (_watermarkErrorDetailCanvasGroup != null)
            _watermarkErrorDetailCanvasGroup.alpha = expansion;
        ApplyWatermarkErrorAnimatedStyle(expansion);

        if (_watermarkErrorDismissing && _watermarkErrorCurrentAlpha <= 0.015f &&
            Mathf.Abs(_watermarkErrorCurrentY - hiddenY) <= 1f)
            DismissWatermarkErrorNotification(true);
    }
    private void RefreshWatermarkErrorSummaryText(bool force)
    {
        if (_watermarkErrorSummaryText == null)
            return;
        var seconds = Mathf.Max(0, Mathf.CeilToInt(_watermarkErrorRemainingSeconds));
        var text = T(
            "游戏出现错误，左键查看详情，右键关闭此提示 ",
            "Game error. Left-click for details, right-click to dismiss. ") +
            seconds.ToString(CultureInfo.InvariantCulture) +
            T("秒后自动关闭", "s before auto-close");
        if (!force && seconds == _watermarkErrorLastSummarySeconds &&
            string.Equals(_watermarkErrorSummaryText.text, text, StringComparison.Ordinal))
            return;

        _watermarkErrorLastSummarySeconds = seconds;
        _watermarkErrorSummaryText.text = text;
        var horizontalPadding = Mathf.Max(24f, WatermarkErrorNormalHeight * 0.62f) * 2f;
        _watermarkErrorNormalWidth = Mathf.Max(
            520f,
            Mathf.Ceil(_watermarkErrorSummaryText.preferredWidth + horizontalPadding));
    }
    private float GetWatermarkErrorNormalWidth()
    {
        return Mathf.Max(520f, _watermarkErrorNormalWidth);
    }
    private float ClampWatermarkErrorX(float desiredX, float width)
    {
        if (_watermarkErrorBar?.parent is not RectTransform parent)
            return desiredX;

        var scale = Mathf.Max(0.01f, Mathf.Abs(GetWatermarkRelativeScale()));
        var halfWidth = Mathf.Max(0f, width) * scale * 0.5f;
        var margin = 8f;
        var maxX = Mathf.Max(0f, parent.rect.width * 0.5f - halfWidth - margin);
        return Mathf.Clamp(desiredX, -maxX, maxX);
    }
    private float GetWatermarkErrorExpandedHeight()
    {
        return Mathf.Clamp(_watermarkErrorExpandedHeight, 300f, 620f);
    }
    private void UpdateWatermarkErrorDetailLayout(bool force)
    {
        if (_watermarkErrorDetailText == null || _watermarkErrorContent == null || _watermarkErrorBar == null)
            return;
        var layoutWidth = Mathf.Max(120f, _watermarkErrorBar.rect.width - 48f);
        if (!force && !_watermarkErrorDetailLayoutDirty &&
            Mathf.Abs(layoutWidth - _watermarkErrorLastDetailLayoutWidth) < 96f)
            return;

        _watermarkErrorDetailLayoutDirty = false;
        _watermarkErrorLastDetailLayoutWidth = layoutWidth;
        _watermarkErrorDetailText.rectTransform.sizeDelta = new Vector2(0f, 10000f);
        var preferredHeight = Mathf.Max(80f, _watermarkErrorDetailText.preferredHeight + 8f);
        _watermarkErrorContent.sizeDelta = new Vector2(0f, preferredHeight);
        _watermarkErrorDetailText.rectTransform.sizeDelta = new Vector2(0f, preferredHeight);
        _watermarkErrorExpandedHeight = Mathf.Clamp(Mathf.Ceil(preferredHeight + 82f), 300f, 620f);
    }
}
