using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiSettingsPage()
    {
        var scroll = CreateLGuiScroll(_lGuiPageHost!, "SettingsScroll", 0f);
        var host = scroll.content!;
        CreateLGuiToggleControl(host, T("Elin Modifier 游戏主界面信息", "Elin Modifier title-screen information"), ShowMainMenuInfo, 0f, SetShowMainMenuInfo);
        CreateLGuiToggleControl(host, T("Elin Modifier 水印", "Elin Modifier watermark"), _modules.Watermark.Enabled, 64f, SetWatermarkEnabled);
        CreateLGuiButton(host, "WatermarkSettings", T("设置", "Settings"), 580f, 66f, 110f, 44f, OpenWatermarkSettings);
        CreateLGuiToggleControl(host, T("关闭CWL报错提醒", "Disable CWL error notifications"), DisableCwlErrorNotification, 128f, SetDisableCwlErrorNotification);
        CreateLGuiToggleControl(host, T("自适应UI比例", "Adaptive UI scale"), _adaptiveUiScale, 192f, value =>
        {
            _adaptiveUiScale = value;
            ApplyLGuiVisualSettings();
        });

        var customScaleLabel = CreateLGuiText(host, "CustomUiScaleLabel", T("自定义UI比例", "Custom UI scale"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(customScaleLabel.rectTransform, 0f, 256f, 120f, 48f);
        var customScaleValue = CreateLGuiText(host, "CustomUiScaleValue", _customUiScale.ToString("0.00", CultureInfo.InvariantCulture), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(customScaleValue.rectTransform, 750f, 256f, 70f, 48f);
        var customScaleSlider = CreateLGuiSlider(host, "CustomUiScaleSlider", 130f, 267f, 600f, 26f, -4f, 4f, _customUiScale, 0.01f);
        var customScaleInput = CreateLGuiInput(host, "CustomUiScaleInput", "-4.00 ~ 4.00", 830f, 258f, 90f, 44f);
        customScaleInput.contentType = InputField.ContentType.DecimalNumber;
        customScaleInput.text = _customUiScale.ToString("0.00", CultureInfo.InvariantCulture);
        CreateLGuiButton(host, "ApplyCustomUiScale", T("应用", "Apply"), 930f, 258f, 90f, 44f, () =>
        {
            var raw = (customScaleInput.text ?? "").Trim().Replace(',', '.');
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                SetCustomUiScale(parsed);
                customScaleSlider.SetValueWithoutNotify(_customUiScale);
                customScaleValue.text = _customUiScale.ToString("0.00", CultureInfo.InvariantCulture);
                ApplyLGuiVisualSettings();
            }

            customScaleInput.text = _customUiScale.ToString("0.00", CultureInfo.InvariantCulture);
        });
        customScaleSlider.onValueChanged.AddListener(value =>
        {
            SetCustomUiScale(value);
            customScaleValue.text = _customUiScale.ToString("0.00", CultureInfo.InvariantCulture);
            customScaleInput.text = _customUiScale.ToString("0.00", CultureInfo.InvariantCulture);
            ApplyLGuiVisualSettings();
        });
        var customScaleHint = CreateLGuiText(host, "CustomUiScaleHint", T("(仅在自适应UI比例关闭后生效)", "(Only applies when Adaptive UI scale is off)"), 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(customScaleHint.rectTransform, 1030f, 256f, 440f, 48f);

        CreateLGuiToggleControl(host, T("强制游戏失焦", "Force game unfocus"), _forceGameUnfocus, 320f, value =>
        {
            _forceGameUnfocus = value;
            ApplyLGuiVisualSettings();
        });
        CreateLGuiToggleControl(host, T("边框圆角", "Rounded corners"), _uiRoundedCorners, 384f, value =>
        {
            _uiRoundedCorners = value;
            ApplyLGuiVisualSettings();
        });
        CreateLGuiToggleControl(host, T("低性能模式", "Low performance mode"), _lowPerformanceMode, 448f, SetLowPerformanceMode);

        var languageLabel = CreateLGuiText(host, "LanguageLabel", T("语言", "Language"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(languageLabel.rectTransform, 0f, 516f, 120f, 48f);
        CreateLGuiButton(host, "Zh", "中文", 130f, 516f, 110f, 46f, () => SetLGuiLanguage("zh"));
        CreateLGuiButton(host, "En", "English", 250f, 516f, 120f, 46f, () => SetLGuiLanguage("en"));
        CreateLGuiButton(host, "Ru", "Русский", 380f, 516f, 120f, 46f, () => SetLGuiLanguage("ru"));
        CreateLGuiButton(host, "Ja", "日本語", 510f, 516f, 110f, 46f, () => SetLGuiLanguage("ja"));

        var styleLabel = CreateLGuiText(host, "StyleLabel", T("颜色风格", "Color style"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(styleLabel.rectTransform, 0f, 576f, 120f, 46f);
        for (var i = 0; i < UiStyleNamesZh.Length; i++)
        {
            var styleIndex = i;
            CreateLGuiButton(host, "Style" + i.ToString(CultureInfo.InvariantCulture), GetUiStyleName(i), 130f + i * 158f, 576f, 148f, 44f, () =>
            {
                _uiStyleIndex = styleIndex;
                if (_uiTextColorFollowsStyle)
                    _uiTextColor = GetDefaultUiTextColor();
                SwitchLGuiPage(LGuiPage.Settings);
            });
        }

        var opacityLabel = CreateLGuiText(host, "OpacityLabel", T("透明度", "Opacity"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(opacityLabel.rectTransform, 0f, 636f, 120f, 46f);
        var opacityValue = CreateLGuiText(host, "OpacityValue", _uiAlpha.ToString("0.00", CultureInfo.InvariantCulture), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(opacityValue.rectTransform, 510f, 636f, 90f, 46f);
        var opacitySlider = CreateLGuiSlider(host, "OpacitySlider", 130f, 646f, 360f, 26f, 0.2f, 1f, _uiAlpha);
        opacitySlider.onValueChanged.AddListener(value =>
        {
            _uiAlpha = Mathf.Round(value * 100f) / 100f;
            _uiAlphaText = _uiAlpha.ToString("0.00", CultureInfo.InvariantCulture);
            opacityValue.text = _uiAlphaText;
            ApplyLGuiVisualSettings();
        });

        var fontLabel = CreateLGuiText(host, "FontLabel", T("字体大小", "Font size"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(fontLabel.rectTransform, 0f, 692f, 120f, 46f);
        var fontValue = CreateLGuiText(host, "FontValue", GetUiFontSizeLabel(), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(fontValue.rectTransform, 510f, 692f, 90f, 46f);
        var fontSlider = CreateLGuiSlider(host, "FontSlider", 130f, 702f, 360f, 26f, UiFontSizeMin, UiFontSizeMax, GetEffectiveUiFontSize());
        fontSlider.wholeNumbers = true;
        fontSlider.onValueChanged.AddListener(value =>
        {
            SetUiFontSize(Mathf.RoundToInt(value));
            fontValue.text = GetUiFontSizeLabel();
            ApplyLGuiVisualSettings();
        });

        var colorSettingsEnd = BuildLGuiTextColorSettings(host, 748f);
        var hotkeyY = colorSettingsEnd + 8f;

        var hotkeyLabel = CreateLGuiText(host, "HotkeyLabel", T("开启键位", "Hotkey"), 18, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(hotkeyLabel.rectTransform, 0f, hotkeyY, 120f, 46f);
        var hotkeyValue = CreateLGuiText(host, "HotkeyValue", GetKeyLabel(_openKey), 18, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(hotkeyValue.rectTransform, 186f, hotkeyY, 170f, 44f);
        CreateLGuiButton(host, "PrevHotkey", "◀", 130f, hotkeyY, 48f, 44f, () => CycleLGuiOpenKey(-1, hotkeyValue));
        CreateLGuiButton(host, "NextHotkey", "▶", 364f, hotkeyY, 48f, 44f, () => CycleLGuiOpenKey(1, hotkeyValue));

        var saveY = hotkeyY + 74f;
        CreateLGuiButton(host, "Save", T("保存配置", "Save configuration"), 0f, saveY, 190f, 48f, SaveLGuiGlobalConfig);
        CreateLGuiButton(host, "Reload", T("重新读取配置", "Reload configuration"), 204f, saveY, 210f, 48f, LoadLGuiGlobalConfig);
        BuildLGuiExtendedSettings(host, saveY + 72f);
        host.sizeDelta = new Vector2(0f, saveY + 136f);
    }
    private void CycleLGuiOpenKey(int direction, Text label)
    {
        var index = 0;
        for (var i = 0; i < KeyOptions.Length; i++)
        {
            if (KeyOptions[i].Key != _openKey)
                continue;
            index = i;
            break;
        }
        index = (index + direction) % KeyOptions.Length;
        if (index < 0)
            index += KeyOptions.Length;
        SetOpenKey(KeyOptions[index].Key);
        label.text = GetKeyLabel(_openKey);
    }
    private void SetLGuiLanguage(string language)
    {
        _language = language;
        RefreshWatermarkText();
        RebuildLGuiAll();
    }
    private void RebuildLGuiAll()
    {
        if (!IsLGuiInitialized())
            return;
        SwitchLGuiPage(_lGuiPage);
    }
}
