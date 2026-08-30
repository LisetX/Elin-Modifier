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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private static void ConfigureNpcMoreInfoHoverDirection(WidgetMouseover widget, bool enabled)
    {
        if (widget == null)
            return;

        try
        {
            var rootRect = widget.Rect();
            var layoutRect = widget.layout != null ? widget.layout.transform as RectTransform : null;
            if (!ReferenceEquals(_npcMoreInfoHoverWidget, widget))
            {
                try
                {
                    if (_npcMoreInfoHoverRootRect != null)
                        _npcMoreInfoHoverRootRect.pivot = _npcMoreInfoHoverOriginalRootPivot;
                    if (_npcMoreInfoHoverLayoutRect != null && !ReferenceEquals(_npcMoreInfoHoverLayoutRect, _npcMoreInfoHoverRootRect))
                        _npcMoreInfoHoverLayoutRect.pivot = _npcMoreInfoHoverOriginalLayoutPivot;
                }
                catch { }

                _npcMoreInfoHoverWidget = widget;
                _npcMoreInfoHoverRootRect = rootRect;
                _npcMoreInfoHoverLayoutRect = layoutRect;
                _npcMoreInfoHoverOriginalRootPivot = rootRect != null ? rootRect.pivot : new Vector2(0.5f, 0.5f);
                _npcMoreInfoHoverOriginalLayoutPivot = layoutRect != null ? layoutRect.pivot : new Vector2(0.5f, 0.5f);
            }

            if (rootRect != null)
                rootRect.pivot = enabled
                    ? new Vector2(_npcMoreInfoHoverOriginalRootPivot.x, 1f)
                    : _npcMoreInfoHoverOriginalRootPivot;
            if (layoutRect != null && !ReferenceEquals(layoutRect, rootRect))
                layoutRect.pivot = enabled
                    ? new Vector2(_npcMoreInfoHoverOriginalLayoutPivot.x, 1f)
                    : _npcMoreInfoHoverOriginalLayoutPivot;
        }
        catch { }
    }
    private static bool ConsumeExpectedNpcMoreInfoHover(string text)
    {
        var expectedBlock = _npcMoreInfoExpectedHoverBlock;
        var expectedFrame = _npcMoreInfoExpectedHoverFrame;
        _npcMoreInfoExpectedHoverBlock = "";
        _npcMoreInfoExpectedHoverFrame = -1;
        return expectedFrame == Time.frameCount &&
               !string.IsNullOrEmpty(expectedBlock) &&
               !string.IsNullOrEmpty(text) &&
               text.IndexOf(expectedBlock, StringComparison.Ordinal) >= 0;
    }
    internal static bool ShouldSkipNpcMoreInfo(Chara? chara)
    {
        if (chara == null)
            return true;

        try
        {
            return chara.mimicry != null && chara.mimicry.IsThing;
        }
        catch
        {
            return false;
        }
    }
}
