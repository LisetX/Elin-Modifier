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
    private static string AppendItemPanelEnchantLevel(Thing thing, Element element, string text)
    {
        var instance = Instance;
        if (instance == null || !instance._showItemPanelEnchantLevels || thing == null || element == null)
            return text ?? "";

        try
        {
            var value = GetThingElementEditorValue(thing, element);
            var existing = text ?? "";
            var valueText = value.ToString(CultureInfo.InvariantCulture);
            if (existing.IndexOf("(" + valueText + ")", StringComparison.Ordinal) >= 0 ||
                existing.IndexOf("(" + valueText + " ", StringComparison.Ordinal) >= 0)
                return existing;
            return existing + "(" + valueText + ")";
        }
        catch
        {
            return text ?? "";
        }
    }
    [ThreadStatic] private static Thing? _itemPanelValueWriteThing;
    [ThreadStatic] private static UINote? _itemPanelValueWriteNote;
    [ThreadStatic] private static bool _itemPanelValueLineAdded;
    private static void BeginItemPanelValueWrite(Thing thing, UINote note)
    {
        ClearItemPanelValueWrite();
        var instance = Instance;
        if (instance == null || !instance._showItemPanelItemValue || thing == null || note == null)
            return;
        _itemPanelValueWriteThing = thing;
        _itemPanelValueWriteNote = note;
    }
    private static void CaptureItemPanelValueHeader(UINote note, UIItem header)
    {
        if (_itemPanelValueWriteThing != null &&
            ReferenceEquals(_itemPanelValueWriteNote, note) &&
            header != null &&
            !_itemPanelValueLineAdded)
        {
            try
            {
                note.AddText("NoteText_eqstats", Tr("价值", "Value") + ":" + GetItemDataValueText(_itemPanelValueWriteThing));
                _itemPanelValueLineAdded = true;
            }
            catch { }
        }
    }
    private static void ClearItemPanelValueWrite()
    {
        _itemPanelValueWriteThing = null;
        _itemPanelValueWriteNote = null;
        _itemPanelValueLineAdded = false;
    }
    private static void AppendItemPanelMilkBonus(Thing thing, UINote note)
    {
        var instance = Instance;
        if (instance == null || !instance._showItemPanelMilkBonus || thing == null || note == null || !GameAccess.IsInitialized)
            return;

        try
        {
            var bonuses = GameAccess.MilkBonusPreview.Calculate(thing);
            if (bonuses == null || bonuses.Count == 0)
                return;

            var mainAbilities = new List<MilkBonusPreviewEntry>();
            var skills = new List<MilkBonusPreviewEntry>();
            for (var i = 0; i < bonuses.Count; i++)
            {
                if (bonuses[i].IsMainAbility)
                    mainAbilities.Add(bonuses[i]);
                else
                    skills.Add(bonuses[i]);
            }

            AddMilkBonusSectionSpacer(note);
            var requiredWidth = AddMilkBonusRows(note, mainAbilities);
            if (mainAbilities.Count > 0 && skills.Count > 0)
                AddMilkBonusSectionSpacer(note);
            requiredWidth = Mathf.Max(requiredWidth, AddMilkBonusRows(note, skills));
            ExpandMilkBonusPanel(note, requiredWidth);
        }
        catch { }
    }
    private static float AddMilkBonusRows(UINote note, IReadOnlyList<MilkBonusPreviewEntry> bonuses)
    {
        var requiredWidth = 0f;
        for (var rowStart = 0; rowStart < bonuses.Count; rowStart += 3)
            requiredWidth = Mathf.Max(requiredWidth, AddMilkBonusRow(note, bonuses, rowStart));
        return requiredWidth;
    }
    private static void AddMilkBonusSectionSpacer(UINote note)
    {
        if (note.layout == null)
            return;

        var spacer = new GameObject(
            "ElinModifierMilkBonusSectionSpacer",
            typeof(RectTransform),
            typeof(UnityEngine.UI.LayoutElement));
        spacer.transform.SetParent(note.layout.transform, false);
        spacer.transform.localScale = Vector3.one;
        var spacerTransform = spacer.GetComponent<RectTransform>();
        spacerTransform.sizeDelta = new Vector2(spacerTransform.sizeDelta.x, 16f);
        var layoutElement = spacer.GetComponent<UnityEngine.UI.LayoutElement>();
        layoutElement.minHeight = 16f;
        layoutElement.preferredHeight = 16f;
        layoutElement.flexibleHeight = 0f;
    }
    private static float AddMilkBonusRow(UINote note, IReadOnlyList<MilkBonusPreviewEntry> bonuses, int rowStart)
    {
        if (note.layout == null)
            return 0f;

        var rowObject = new GameObject("ElinModifierMilkBonusRow", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
        var rowTransform = rowObject.GetComponent<RectTransform>();
        rowTransform.SetParent(note.layout.transform, false);
        rowTransform.localScale = Vector3.one;
        rowTransform.sizeDelta = new Vector2(rowTransform.sizeDelta.x, 27f);

        var rowElement = rowObject.GetComponent<UnityEngine.UI.LayoutElement>();
        rowElement.minHeight = 27f;
        rowElement.preferredHeight = 27f;
        rowElement.flexibleHeight = 0f;

        var rowLayout = rowObject.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        rowLayout.spacing = 2f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        var cellCount = 0;
        var requiredWidth = 0f;
        for (var column = 0; column < 3; column++)
        {
            var index = rowStart + column;
            if (index < bonuses.Count)
            {
                requiredWidth += AddMilkBonusCell(note, rowTransform, bonuses[index]);
                cellCount++;
            }
        }
        if (cellCount > 1)
            requiredWidth += rowLayout.spacing * (cellCount - 1);
        rowElement.minWidth = requiredWidth;
        rowElement.preferredWidth = requiredWidth;
        rowElement.flexibleWidth = 0f;
        rowTransform.sizeDelta = new Vector2(requiredWidth, rowTransform.sizeDelta.y);
        return requiredWidth;
    }
    private static float AddMilkBonusCell(UINote note, RectTransform rowTransform, MilkBonusPreviewEntry bonus)
    {
        var text = bonus.Name + " " + bonus.Value.ToString("0.00", CultureInfo.InvariantCulture);
        var item = note.AddText("NoteText_enc", text, FontColor.Good);
        item.transform.SetParent(rowTransform, false);
        item.transform.localScale = Vector3.one;

        var layoutElement = item.GetComponent<UnityEngine.UI.LayoutElement>() ?? item.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layoutElement.minHeight = 27f;
        layoutElement.preferredHeight = 27f;
        layoutElement.flexibleHeight = 0f;

        var requiredWidth = 0f;
        if (item.text1 != null)
        {
            item.text1.alignment = TextAnchor.MiddleLeft;
            var textTransform = item.text1.rectTransform;
            var leftInset = Mathf.Max(26f, textTransform.offsetMin.x);
            textTransform.anchorMin = Vector2.zero;
            textTransform.anchorMax = Vector2.one;
            textTransform.offsetMin = new Vector2(leftInset, 0f);
            textTransform.offsetMax = new Vector2(-8f, 0f);
            item.text1.resizeTextForBestFit = false;
            item.text1.horizontalOverflow = HorizontalWrapMode.Overflow;
            item.text1.verticalOverflow = VerticalWrapMode.Truncate;
            requiredWidth = Mathf.Ceil(leftInset + item.text1.preferredWidth + 8f);
        }

        if (item.image1 != null)
        {
            item.image1.SetActive(true);
            item.image1.sprite = bonus.Icon;
            var imageTransform = item.image1.rectTransform;
            imageTransform.anchorMin = new Vector2(imageTransform.anchorMin.x, 0.5f);
            imageTransform.anchorMax = new Vector2(imageTransform.anchorMax.x, 0.5f);
            imageTransform.anchoredPosition = new Vector2(imageTransform.anchoredPosition.x, 3f);
        }

        layoutElement.minWidth = requiredWidth;
        layoutElement.preferredWidth = requiredWidth;
        layoutElement.flexibleWidth = 0f;
        return requiredWidth;
    }
    private static void ExpandMilkBonusPanel(UINote note, float requiredContentWidth)
    {
        if (requiredContentWidth <= 0f || note.layout == null)
            return;

        var tooltip = note.GetComponentInParent<UITooltip>();
        var panel = tooltip == null ? null : tooltip.transform as RectTransform;
        var content = note.layout.transform as RectTransform;
        if (panel == null || content == null)
            return;

        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        var panelWidth = panel.rect.width;
        var contentWidth = content.rect.width;
        if (panelWidth <= 0f || contentWidth <= 0f || requiredContentWidth <= contentWidth)
            return;

        var size = panel.sizeDelta;
        size.x = Mathf.Ceil(Mathf.Max(size.x, panelWidth) + requiredContentWidth - contentWidth);
        panel.sizeDelta = size;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        Canvas.ForceUpdateCanvases();
    }
}
