using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private int NormalizeUiFontSize(int value)
    {
        if (value <= 0)
            return UiFontSizeDefault;
        return Clamp(value, UiFontSizeMin, UiFontSizeMax);
    }
    private int GetEffectiveUiFontSize()
    {
        return NormalizeUiFontSize(_uiFontSize);
    }
    private void SetUiFontSize(int value)
    {
        var normalized = NormalizeUiFontSize(value);
        if (_uiFontSize == normalized)
        {
            _uiFontSizeText = normalized.ToString(CultureInfo.InvariantCulture);
            return;
        }

        _uiFontSize = normalized;
        _uiFontSizeText = normalized.ToString(CultureInfo.InvariantCulture);
        _modifierSkin = null;
    }
    private string GetUiFontSizeLabel()
    {
        return GetEffectiveUiFontSize().ToString(CultureInfo.InvariantCulture);
    }
    private void SetCustomUiTextColor(Color color)
    {
        color.r = Clamp(color.r, 0f, 1f);
        color.g = Clamp(color.g, 0f, 1f);
        color.b = Clamp(color.b, 0f, 1f);
        color.a = 1f;
        _uiTextColor = color;
        _uiTextColorFollowsStyle = false;
        _uiTextColorHexText = ColorToHex(_uiTextColor);
    }
    private void UseStyleUiTextColor()
    {
        _uiTextColorFollowsStyle = true;
        _uiTextColor = GetDefaultUiTextColor();
        _uiTextColorHexText = ColorToHex(_uiTextColor);
    }
    private Color GetActiveUiTextColor()
    {
        var color = _uiTextColorFollowsStyle ? GetDefaultUiTextColor() : _uiTextColor;
        color.a = 1f;
        return color;
    }
    private Color GetDefaultUiTextColor()
    {
        if (_uiStyleIndex < 0 || _uiStyleIndex >= UiTextColors.Length)
            return Color.white;
        var color = UiTextColors[_uiStyleIndex];
        color.a = 1f;
        return color;
    }
    private static string ColorToHex(Color color)
    {
        color.a = 1f;
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }
    private static bool TryParseHexColor(string text, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (!value.StartsWith("#", StringComparison.Ordinal))
            value = "#" + value;

        if (!ColorUtility.TryParseHtmlString(value, out color))
            return false;

        color.a = 1f;
        return true;
    }
    private static bool ColorsApproximatelyEqual(Color a, Color b)
    {
        return Math.Abs(a.r - b.r) < 0.001f &&
               Math.Abs(a.g - b.g) < 0.001f &&
               Math.Abs(a.b - b.b) < 0.001f;
    }
    private string CurrentUiStyleName()
    {
        return GetUiStyleName(_uiStyleIndex);
    }
    private string GetUiStyleName(int index)
    {
        if (index < 0 || index >= UiStyleNamesZh.Length)
            index = 0;
        if (_language == "en")
            return UiStyleNamesEn[index];
        if (_language == "ja")
            return UiStyleNamesJa[index];
        if (_language == "ru")
            return UiStyleNamesRu[index];
        return UiStyleNamesZh[index];
    }
    private string CurrentLanguageName()
    {
        if (_language == "en")
            return "English";
        if (_language == "ja")
            return "日本語";
        if (_language == "ru")
            return "Русский";
        return "中文";
    }
    private string T(string zh, string en)
    {
        if (_language == "en")
            return en;
        if (_language == "ja")
        {
            string ja;
            if (UiTextJa.TryGetValue(zh, out ja) ||
                UiTextJaSupplemental.TryGetValue(zh, out ja))
                return ja;
            return en;
        }
        if (_language == "ru")
        {
            string ru;
            if (UiTextRu.TryGetValue(zh, out ru) ||
                UiTextRuSupplemental.TryGetValue(zh, out ru))
                return ru;
            return en;
        }
        return zh;
    }
    private void SetOpenKey(KeyCode key)
    {
        _openKey = key;
        _openKeyText = GetKeyLabel(_openKey);
    }
}
