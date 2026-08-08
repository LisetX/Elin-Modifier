using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private float BuildLGuiTextColorSettings(RectTransform host, float y)
    {
        var color = GetActiveUiTextColor();
        var r = CreateLGuiSlider(host, "TextR", 130f, y, 300f, 24f, 0f, 1f, color.r);
        CreateLGuiFieldLabel(host, "R", 0f, y - 8f, 100f);
        var g = CreateLGuiSlider(host, "TextG", 580f, y, 300f, 24f, 0f, 1f, color.g);
        CreateLGuiFieldLabel(host, "G", 450f, y - 8f, 100f);
        var b = CreateLGuiSlider(host, "TextB", 1030f, y, 300f, 24f, 0f, 1f, color.b);
        CreateLGuiFieldLabel(host, "B", 900f, y - 8f, 100f);
        y += 44f;

        var paletteY = y;
        y += 48f;
        var colorLabel = CreateLGuiText(host, "TextColorLabel", T("字体颜色", "Text color"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(colorLabel.rectTransform, 0f, y, 120f, 46f);
        var colorInput = CreateLGuiInput(host, "TextColor", "#RRGGBB", 130f, y, 180f, 44f);
        colorInput.text = ColorToHex(color);
        var syncing = false;

        void SyncControls(Color next)
        {
            syncing = true;
            r.value = next.r;
            g.value = next.g;
            b.value = next.b;
            colorInput.text = ColorToHex(next);
            syncing = false;
        }

        void ApplyColor(Color next)
        {
            if (syncing)
                return;
            SetCustomUiTextColor(next);
            SyncControls(GetActiveUiTextColor());
            ApplyLGuiVisualSettings();
        }

        r.onValueChanged.AddListener(value =>
        {
            if (syncing) return;
            var next = GetActiveUiTextColor();
            next.r = value;
            ApplyColor(next);
        });
        g.onValueChanged.AddListener(value =>
        {
            if (syncing) return;
            var next = GetActiveUiTextColor();
            next.g = value;
            ApplyColor(next);
        });
        b.onValueChanged.AddListener(value =>
        {
            if (syncing) return;
            var next = GetActiveUiTextColor();
            next.b = value;
            ApplyColor(next);
        });

        for (var i = 0; i < UiTextColorPalette.Length; i++)
        {
            var paletteColor = UiTextColorPalette[i];
            var local = paletteColor;
            var button = CreateLGuiButton(host, "Palette" + i, ColorToHex(paletteColor), i * 154f, paletteY, 144f, 40f, () => ApplyColor(local));
            var swatch = CreateLGuiImage(button.transform, "Swatch", 6f, 7f, 26f, 26f);
            swatch.color = paletteColor;
            RegisterLGuiRoundedImage(swatch);
            swatch.raycastTarget = false;
        }
        CreateLGuiButton(host, "ResetTextColor", T("重置", "Reset"), 1232f, paletteY, 110f, 40f, () =>
        {
            UseStyleUiTextColor();
            SyncControls(GetActiveUiTextColor());
            ApplyLGuiVisualSettings();
        });

        colorInput.onValueChanged.AddListener(value =>
        {
            if (!syncing && TryParseHexColor(value, out var parsed))
                ApplyColor(parsed);
        });
        CreateLGuiButton(host, "ApplyTextColor", T("应用", "Apply"), 322f, y, 100f, 44f, () =>
        {
            if (TryParseHexColor(colorInput.text, out var parsed))
                ApplyColor(parsed);
        });
        CreateLGuiButton(host, "StyleTextColor", T("跟随风格", "Use style"), 434f, y, 140f, 44f, () =>
        {
            UseStyleUiTextColor();
            SyncControls(GetActiveUiTextColor());
            ApplyLGuiVisualSettings();
        });
        return y + 56f;
    }
    private void BuildLGuiExtendedSettings(RectTransform host, float y)
    {
        var path = CreateLGuiText(host, "ConfigPath", T("配置文件: ", "Config file: ") + GetConfigPath(), 14, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(path.rectTransform, 0f, y, 1290f, 42f);
    }
}
