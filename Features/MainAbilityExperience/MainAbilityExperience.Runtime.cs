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
    private const int SpeedElementId = 79;
    private const string MainAbilityExperienceMarker = " EXP ";
    [ThreadStatic] private static Element? _mainAbilityExperienceTooltipElement;
    [ThreadStatic] private static UINote? _mainAbilityExperienceTooltipNote;
    [ThreadStatic] private static UIItem? _mainAbilityExperienceTopicItem;
    [ThreadStatic] private static bool _addingMainAbilityExperienceTopic;
    private static bool BeginMainAbilityExperienceTooltip(Element element, UINote note)
    {
        ClearMainAbilityExperienceTooltip();
        var instance = Instance;
        if (instance == null || !instance._showMainAbilityExperience || element == null || note == null)
            return false;

        try
        {
            if (!element.IsMainAttribute && element.id != SpeedElementId)
                return false;

            _mainAbilityExperienceTooltipElement = element;
            _mainAbilityExperienceTooltipNote = note;
            return true;
        }
        catch
        {
            ClearMainAbilityExperienceTooltip();
            return false;
        }
    }
    private static void AddMainAbilityExperienceBeforeCurrent(UINote note, string topicStyle, string topicId)
    {
        if (_addingMainAbilityExperienceTopic ||
            _mainAbilityExperienceTooltipElement == null ||
            !ReferenceEquals(_mainAbilityExperienceTooltipNote, note) ||
            !IsMainAbilityCurrentValueTopic(topicId))
            return;

        try
        {
            _addingMainAbilityExperienceTopic = true;
            _mainAbilityExperienceTopicItem = note.AddTopic(
                topicStyle,
                "EXP",
                FormatElementExperience(_mainAbilityExperienceTooltipElement));
        }
        catch
        {
        }
        finally
        {
            _addingMainAbilityExperienceTopic = false;
        }
    }
    private static void AlignMainAbilityExperienceWithCurrent(UINote note, string topicId, UIItem currentTopicItem)
    {
        if (_addingMainAbilityExperienceTopic ||
            _mainAbilityExperienceTopicItem == null ||
            !ReferenceEquals(_mainAbilityExperienceTooltipNote, note) ||
            !IsMainAbilityCurrentValueTopic(topicId))
            return;

        try
        {
            var experienceLabel = _mainAbilityExperienceTopicItem.text1;
            var currentLabel = currentTopicItem?.text1;
            if (experienceLabel == null || currentLabel == null)
                return;

            var preferredWidth = UnityEngine.UI.LayoutUtility.GetPreferredWidth(currentLabel.rectTransform);
            if (preferredWidth <= 0f)
                preferredWidth = currentLabel.preferredWidth;
            if (preferredWidth <= 0f)
                return;

            var layout = experienceLabel.GetComponent<UnityEngine.UI.LayoutElement>();
            if (layout == null)
                layout = experienceLabel.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            experienceLabel.alignment = GetLeftAlignedTextAnchor(
                _mainAbilityExperienceTopicItem.text2?.alignment ?? experienceLabel.alignment);
            layout.minWidth = preferredWidth;
            layout.preferredWidth = preferredWidth;

            var row = experienceLabel.rectTransform.parent as RectTransform;
            if (row != null)
                UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(row);
        }
        catch
        {
        }
        finally
        {
            _mainAbilityExperienceTopicItem = null;
        }
    }
    private static TextAnchor GetLeftAlignedTextAnchor(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft:
            case TextAnchor.UpperCenter:
            case TextAnchor.UpperRight:
                return TextAnchor.UpperLeft;
            case TextAnchor.LowerLeft:
            case TextAnchor.LowerCenter:
            case TextAnchor.LowerRight:
                return TextAnchor.LowerLeft;
            default:
                return TextAnchor.MiddleLeft;
        }
    }
    private static bool IsMainAbilityCurrentValueTopic(string topicId)
    {
        if (string.Equals(topicId, "vCurrent", StringComparison.Ordinal))
            return true;

        try
        {
            return string.Equals(topicId, "vCurrent".lang(), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
    private static void ClearMainAbilityExperienceTooltip()
    {
        _mainAbilityExperienceTooltipElement = null;
        _mainAbilityExperienceTooltipNote = null;
        _mainAbilityExperienceTopicItem = null;
        _addingMainAbilityExperienceTopic = false;
    }
    private static string FormatElementExperience(Element element)
    {
        return element.vExp.ToString(CultureInfo.InvariantCulture) + "/" +
               element.ExpToNext.ToString(CultureInfo.InvariantCulture);
    }
    private static void RefreshMainAbilityExperienceTracker(bool enabled)
    {
        try
        {
            var tracker = WidgetTracker.Instance;
            if (tracker == null)
                return;

            if (!enabled)
            {
                var text = GetWidgetTrackerText(tracker);
                if (text != null)
                    text.text = RemoveMainAbilityExperience(text.text ?? "");
            }

            tracker.Refresh();
        }
        catch
        {
        }
    }
    private static UIText? GetWidgetTrackerText(WidgetTracker tracker)
    {
        try
        {
            return AccessTools.Field(typeof(WidgetTracker), "text")?.GetValue(tracker) as UIText;
        }
        catch
        {
            return null;
        }
    }
    private static string RemoveMainAbilityExperience(string text)
    {
        return string.IsNullOrEmpty(text)
            ? text
            : Regex.Replace(text, @" EXP(?::| )-?\d+/-?\d+(?=\r?$)", "", RegexOptions.Multiline);
    }
    private static void AppendMainAbilityExperienceToTracker(WidgetTracker tracker)
    {
        var instance = Instance;
        if (instance == null ||
            !instance._showMainAbilityExperience ||
            !instance._showMainAbilityExperienceInSkillTracker ||
            tracker == null)
            return;

        try
        {
            var text = GetWidgetTrackerText(tracker);
            var pc = GameAccess.Characters.PlayerCharacter;
            var tracked = GameAccess.Runtime.Player?.trackedElements;
            if (text == null || pc == null || tracked == null || tracked.Count == 0)
                return;

            var elements = new List<Element>();
            foreach (var id in tracked)
            {
                var element = pc.elements.GetElement(id);
                if (element != null)
                    elements.Add(element);
            }

            var rendered = text.text ?? "";
            var separator = rendered.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var lines = rendered.Split(new[] { separator }, StringSplitOptions.None);
            if (elements.Count == 0 || lines.Length != elements.Count)
                return;

            var changed = false;
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                if (!element.IsMainAttribute)
                    continue;

                var line = RemoveMainAbilityExperience(lines[i]);
                var updated = line + MainAbilityExperienceMarker + FormatElementExperience(element);
                if (!string.Equals(lines[i], updated, StringComparison.Ordinal))
                {
                    lines[i] = updated;
                    changed = true;
                }
            }

            if (!changed)
                return;

            text.text = string.Join(separator, lines);
            try { tracker.RebuildLayout(); } catch { }
        }
        catch
        {
        }
    }
}
