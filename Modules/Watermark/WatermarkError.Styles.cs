using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private void ApplyWatermarkErrorVisualSettings()
    {
        if (_watermarkErrorRoot == null)
            return;
        if (_watermarkErrorSummaryText != null)
        {
            _watermarkErrorSummaryText.font = _lGuiFont;
            var fontScale = GetEffectiveUiFontSize() / (float)BaseUiFontSize;
            _watermarkErrorSummaryText.fontSize = Clamp(Mathf.RoundToInt(18f * fontScale), 1, 60);
            _watermarkErrorSummaryText.color = GetActiveUiTextColor();
        }
        if (_watermarkErrorDetailText != null)
        {
            _watermarkErrorDetailText.font = _lGuiFont;
            var fontScale = GetEffectiveUiFontSize() / (float)BaseUiFontSize;
            _watermarkErrorDetailText.fontSize = Clamp(Mathf.RoundToInt(16f * fontScale), 1, 60);
            var lightTheme = _uiStyleIndex == 5;
            _watermarkErrorDetailText.color = _watermarkErrorIsWarning
                ? (lightTheme ? new Color(0.20f, 0.13f, 0.01f, 1f) : new Color(1f, 0.96f, 0.78f, 1f))
                : (lightTheme ? new Color(0.25f, 0.035f, 0.04f, 1f) : new Color(1f, 0.94f, 0.94f, 1f));
        }
        _watermarkErrorDetailLayoutDirty = true;
        var expandedHeight = GetWatermarkErrorExpandedHeight();
        var expansion = Mathf.InverseLerp(
            WatermarkErrorNormalHeight,
            Mathf.Max(WatermarkErrorNormalHeight + 1f, expandedHeight),
            _watermarkErrorCurrentHeight);
        ApplyWatermarkErrorAnimatedStyle(expansion);
    }
    private void ApplyWatermarkErrorAnimatedStyle(float expansion)
    {
        expansion = Mathf.Clamp01(expansion);
        var lightTheme = _uiStyleIndex == 5;
        var alpha = Clamp(_uiAlpha * 0.92f, 0.45f, 0.92f);
        var collapsedBackground = _watermarkErrorIsWarning
            ? (lightTheme
                ? new Color(0.95f, 0.70f, 0.12f, alpha)
                : new Color(0.38f, 0.25f, 0.025f, alpha))
            : (lightTheme
                ? new Color(0.82f, 0.20f, 0.17f, alpha)
                : new Color(0.24f, 0.045f, 0.055f, alpha));
        var expandedBackground = _watermarkErrorIsWarning
            ? (lightTheme
                ? new Color(0.98f, 0.76f, 0.18f, alpha)
                : new Color(0.31f, 0.19f, 0.018f, alpha))
            : (lightTheme
                ? new Color(0.88f, 0.24f, 0.20f, alpha)
                : new Color(0.18f, 0.025f, 0.035f, alpha));
        if (_watermarkErrorBackground != null)
            _watermarkErrorBackground.color = Color.Lerp(collapsedBackground, expandedBackground, expansion);

        ApplyWatermarkErrorCornerStyle(expansion > 0.35f);
    }
    private void ApplyWatermarkErrorCornerStyle(bool expanded)
    {
        if (_watermarkErrorBackground == null)
            return;
        if (!_uiRoundedCorners)
        {
            if (_watermarkErrorBackground.sprite != null)
                _watermarkErrorBackground.sprite = null;
            if (_watermarkErrorBackground.type != Image.Type.Simple)
                _watermarkErrorBackground.type = Image.Type.Simple;
            return;
        }
        Sprite? sprite;
        if (expanded)
        {
            sprite = GetStandardRoundedSprite();
        }
        else
        {
            EnsureWatermarkErrorCapsuleSprite();
            sprite = _watermarkErrorCapsuleSprite;
        }
        if (sprite == null)
            return;
        if (_watermarkErrorBackground.sprite != sprite)
            _watermarkErrorBackground.sprite = sprite;
        if (_watermarkErrorBackground.type != Image.Type.Sliced)
            _watermarkErrorBackground.type = Image.Type.Sliced;
        if (!_watermarkErrorBackground.fillCenter)
            _watermarkErrorBackground.fillCenter = true;
    }
    private void EnsureWatermarkErrorCapsuleSprite()
    {
        if (_watermarkErrorCapsuleSprite != null)
            return;

        var diameter = Mathf.Max(24, Mathf.RoundToInt(WatermarkErrorNormalHeight));
        if ((diameter & 1) != 0)
            diameter++;
        var radius = diameter * 0.5f;
        var width = diameter + 4;
        var height = diameter + 2;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "ElinModifier.WatermarkErrorCapsule";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.DontSave;

        var pixels = new Color[width * height];
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var px = Mathf.Abs(x + 0.5f - halfWidth) - (halfWidth - radius);
                var py = Mathf.Abs(y + 0.5f - halfHeight) - (halfHeight - radius);
                var outsideX = Mathf.Max(px, 0f);
                var outsideY = Mathf.Max(py, 0f);
                var distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY) +
                               Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - distance));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);

        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        sprite.name = "ElinModifier.WatermarkErrorCapsule";
        sprite.hideFlags = HideFlags.DontSave;
        _watermarkErrorCapsuleTexture = texture;
        _watermarkErrorCapsuleSprite = sprite;
    }
}
