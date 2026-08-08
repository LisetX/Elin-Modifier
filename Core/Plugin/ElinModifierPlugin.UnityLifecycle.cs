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

    private void Update()
    {
        if (TickTermsConfirmation())
            return;
        _modules.TickAll();
    }

    private void LateUpdate()
    {
        if (TermsConfirmationActive || TermsAcceptanceFinalizePending)
            return;

        _modules.LateTickAll();
    }

    private void OnGUI()
    {
        if (TermsConfirmationActive)
        {
            DrawTermsConfirmation();
            return;
        }

        _modules.DrawGuiAll();
    }

    private static bool HasCharacterData()
    {
        return GetSafePc() != null;
    }

    private static Chara? GetSafePc()
    {
        try { return GameAccess.Characters.PlayerCharacter; }
        catch { return null; }
    }

    private float GetAdaptiveUiScale()
    {
        if (!_adaptiveUiScale)
            return GetCustomUiScaleFactor();

        var width = Screen.width;
        var height = Screen.height;
        if (width <= 0 || height <= 0)
            return 1f;

        var scaleX = width / AdaptiveUiBaseWidth;
        var scaleY = height / AdaptiveUiBaseHeight;
        var scale = Math.Min(scaleX, scaleY);
        return Math.Max(0.1f, scale);
    }

    private static float NormalizeCustomUiScale(float value)
    {
        return Mathf.Round(Clamp(value, -4f, 4f) * 100f) / 100f;
    }

    private void SetCustomUiScale(float value)
    {
        _customUiScale = NormalizeCustomUiScale(value);
    }

    private float GetCustomUiScaleFactor()
    {
        return Math.Max(0.1f, 1f + NormalizeCustomUiScale(_customUiScale) / 10f);
    }

    private void ApplyForceGameUnfocus()
    {
        if (!_forceGameUnfocus || !IsModifierUiActuallyDrawn())
            return;

        try
        {
            GameAccess.Runtime.Core?.ConsumeInput();
        }
        catch { }
    }

    private bool IsModifierUiOpen()
    {
        return IsModifierUiActuallyDrawn();
    }

    private bool IsModifierUiActuallyDrawn()
    {
        return IsLGuiVisible();
    }

    private bool IsDebugModeActive()
    {
        return _debugAuthorized && _debugVisible;
    }

    private GUISkin GetModifierSkin(GUISkin source)
    {
        if (_modifierSkin == null)
            _modifierSkin = CreateIsolatedSkin(source);
        return _modifierSkin;
    }

    private static GUISkin CreateIsolatedSkin(GUISkin source)
    {
        var skin = UnityEngine.Object.Instantiate(source);
        skin.name = "ElinModifier_IsolatedSkin";
        CloneSkinStyles(skin);
        return skin;
    }

    private static void CloneSkinStyles(GUISkin skin)
    {
        skin.label = CloneStyle(skin.label);
        skin.button = CloneStyle(skin.button);
        skin.toggle = CloneStyle(skin.toggle);
        skin.textField = CloneStyle(skin.textField);
        skin.textArea = CloneStyle(skin.textArea);
        skin.window = CloneStyle(skin.window);
        skin.box = CloneStyle(skin.box);
        skin.horizontalSlider = CloneStyle(skin.horizontalSlider);
        skin.horizontalSliderThumb = CloneStyle(skin.horizontalSliderThumb);
        skin.verticalSlider = CloneStyle(skin.verticalSlider);
        skin.verticalSliderThumb = CloneStyle(skin.verticalSliderThumb);
        skin.horizontalScrollbar = CloneStyle(skin.horizontalScrollbar);
        skin.horizontalScrollbarThumb = CloneStyle(skin.horizontalScrollbarThumb);
        skin.horizontalScrollbarLeftButton = CloneStyle(skin.horizontalScrollbarLeftButton);
        skin.horizontalScrollbarRightButton = CloneStyle(skin.horizontalScrollbarRightButton);
        skin.verticalScrollbar = CloneStyle(skin.verticalScrollbar);
        skin.verticalScrollbarThumb = CloneStyle(skin.verticalScrollbarThumb);
        skin.verticalScrollbarUpButton = CloneStyle(skin.verticalScrollbarUpButton);
        skin.verticalScrollbarDownButton = CloneStyle(skin.verticalScrollbarDownButton);

        if (skin.customStyles == null)
            return;

        var customStyles = new GUIStyle[skin.customStyles.Length];
        for (var i = 0; i < skin.customStyles.Length; i++)
            customStyles[i] = CloneStyle(skin.customStyles[i]);
        skin.customStyles = customStyles;
    }

    private static GUIStyle CloneStyle(GUIStyle style)
    {
        return style == null ? new GUIStyle() : new GUIStyle(style);
    }

    private void ApplyUiStyle()
    {
        if (_uiStyleIndex < 0 || _uiStyleIndex >= UiStyleColors.Length)
            _uiStyleIndex = 0;
        if (_uiStyleIndex >= UiTextColors.Length)
            _uiStyleIndex = 0;
        var alpha = _uiAlpha >= 0.995f ? 1f : _uiAlpha;
        var color = UiStyleColors[_uiStyleIndex];
        color.a = alpha;
        var text = GetActiveUiTextColor();
        GUI.backgroundColor = color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.contentColor = text;
        ApplySkinTextColor(text);
        ApplySkinFontSize(GetEffectiveUiFontSize());
    }

    private static void ApplySkinTextColor(Color color)
    {
        var skin = GUI.skin;
        if (skin == null)
            return;

        color.a = 1f;
        ApplyStyleTextColor(skin.label, color);
        ApplyStyleTextColor(skin.button, color);
        ApplyStyleTextColor(skin.toggle, color);
        ApplyStyleTextColor(skin.textField, color);
        ApplyStyleTextColor(skin.textArea, color);
        ApplyStyleTextColor(skin.window, color);
        ApplyStyleTextColor(skin.box, color);
        ApplyStyleTextColor(skin.horizontalSlider, color);
        ApplyStyleTextColor(skin.horizontalSliderThumb, color);
        ApplyStyleTextColor(skin.verticalSlider, color);
        ApplyStyleTextColor(skin.verticalSliderThumb, color);
        ApplyStyleTextColor(skin.horizontalScrollbar, color);
        ApplyStyleTextColor(skin.horizontalScrollbarThumb, color);
        ApplyStyleTextColor(skin.horizontalScrollbarLeftButton, color);
        ApplyStyleTextColor(skin.horizontalScrollbarRightButton, color);
        ApplyStyleTextColor(skin.verticalScrollbar, color);
        ApplyStyleTextColor(skin.verticalScrollbarThumb, color);
        ApplyStyleTextColor(skin.verticalScrollbarUpButton, color);
        ApplyStyleTextColor(skin.verticalScrollbarDownButton, color);

        if (skin.customStyles == null)
            return;
        foreach (var style in skin.customStyles)
            ApplyStyleTextColor(style, color);
    }

    private static void ApplyStyleTextColor(GUIStyle style, Color color)
    {
        if (style == null)
            return;

        ApplyStateTextColor(style.normal, color);
        ApplyStateTextColor(style.hover, color);
        ApplyStateTextColor(style.active, color);
        ApplyStateTextColor(style.focused, color);
        ApplyStateTextColor(style.onNormal, color);
        ApplyStateTextColor(style.onHover, color);
        ApplyStateTextColor(style.onActive, color);
        ApplyStateTextColor(style.onFocused, color);
    }

    private static void ApplyStateTextColor(GUIStyleState state, Color color)
    {
        if (state != null)
            state.textColor = color;
    }

    private static void ApplySkinFontSize(int fontSize)
    {
        if (fontSize <= 0)
            return;

        var skin = GUI.skin;
        if (skin == null)
            return;

        ApplyStyleFontSize(skin.label, fontSize);
        ApplyStyleFontSize(skin.button, fontSize);
        ApplyStyleFontSize(skin.toggle, fontSize);
        ApplyStyleFontSize(skin.textField, fontSize);
        ApplyStyleFontSize(skin.textArea, fontSize);
        ApplyStyleFontSize(skin.window, fontSize);
        ApplyStyleFontSize(skin.box, fontSize);

        if (skin.customStyles == null)
            return;
        foreach (var style in skin.customStyles)
            ApplyStyleFontSize(style, fontSize);
    }

    private static void ApplyStyleFontSize(GUIStyle style, int fontSize)
    {
        if (style != null)
            style.fontSize = fontSize;
    }

}
