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

internal sealed class GuiSkinTextColorSnapshot
{
    private readonly StyleTextColorSnapshot[] _styles;

    public GuiSkinTextColorSnapshot(GUISkin skin)
    {
        var styles = new List<StyleTextColorSnapshot>();
        AddStyle(styles, skin.label);
        AddStyle(styles, skin.button);
        AddStyle(styles, skin.toggle);
        AddStyle(styles, skin.textField);
        AddStyle(styles, skin.textArea);
        AddStyle(styles, skin.window);
        AddStyle(styles, skin.box);
        AddStyle(styles, skin.horizontalSlider);
        AddStyle(styles, skin.horizontalSliderThumb);
        AddStyle(styles, skin.verticalSlider);
        AddStyle(styles, skin.verticalSliderThumb);
        AddStyle(styles, skin.horizontalScrollbar);
        AddStyle(styles, skin.horizontalScrollbarThumb);
        AddStyle(styles, skin.horizontalScrollbarLeftButton);
        AddStyle(styles, skin.horizontalScrollbarRightButton);
        AddStyle(styles, skin.verticalScrollbar);
        AddStyle(styles, skin.verticalScrollbarThumb);
        AddStyle(styles, skin.verticalScrollbarUpButton);
        AddStyle(styles, skin.verticalScrollbarDownButton);

        if (skin.customStyles != null)
        {
            foreach (var style in skin.customStyles)
                AddStyle(styles, style);
        }

        _styles = styles.ToArray();
    }

    public void Restore()
    {
        foreach (var style in _styles)
            style.Restore();
    }

    private static void AddStyle(List<StyleTextColorSnapshot> styles, GUIStyle style)
    {
        if (style != null)
            styles.Add(new StyleTextColorSnapshot(style));
    }
}

internal sealed class StyleTextColorSnapshot
{
    private readonly GUIStyle _style;
    private readonly Color _normal;
    private readonly Color _hover;
    private readonly Color _active;
    private readonly Color _focused;
    private readonly Color _onNormal;
    private readonly Color _onHover;
    private readonly Color _onActive;
    private readonly Color _onFocused;

    public StyleTextColorSnapshot(GUIStyle style)
    {
        _style = style;
        _normal = GetTextColor(style.normal);
        _hover = GetTextColor(style.hover);
        _active = GetTextColor(style.active);
        _focused = GetTextColor(style.focused);
        _onNormal = GetTextColor(style.onNormal);
        _onHover = GetTextColor(style.onHover);
        _onActive = GetTextColor(style.onActive);
        _onFocused = GetTextColor(style.onFocused);
    }

    public void Restore()
    {
        RestoreState(_style.normal, _normal);
        RestoreState(_style.hover, _hover);
        RestoreState(_style.active, _active);
        RestoreState(_style.focused, _focused);
        RestoreState(_style.onNormal, _onNormal);
        RestoreState(_style.onHover, _onHover);
        RestoreState(_style.onActive, _onActive);
        RestoreState(_style.onFocused, _onFocused);
    }

    private static Color GetTextColor(GUIStyleState state)
    {
        return state == null ? Color.white : state.textColor;
    }

    private static void RestoreState(GUIStyleState state, Color color)
    {
        if (state != null)
            state.textColor = color;
    }
}

