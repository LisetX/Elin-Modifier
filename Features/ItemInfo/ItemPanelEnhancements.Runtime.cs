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
            if (bonuses == null)
                return;

            for (var rowStart = 0; rowStart < bonuses.Count; rowStart += 3)
                AddMilkBonusRow(note, bonuses, rowStart);
        }
        catch { }
    }
    private static void AddMilkBonusRow(UINote note, IReadOnlyList<MilkBonusPreviewEntry> bonuses, int rowStart)
    {
        if (note.layout == null)
            return;

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
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        for (var column = 0; column < 3; column++)
        {
            var index = rowStart + column;
            if (index < bonuses.Count)
                AddMilkBonusCell(note, rowTransform, bonuses[index]);
            else
                AddMilkBonusSpacer(rowTransform);
        }
    }
    private static void AddMilkBonusCell(UINote note, RectTransform rowTransform, MilkBonusPreviewEntry bonus)
    {
        var text = bonus.Name + " " + bonus.Value.ToString("0.00", CultureInfo.InvariantCulture);
        var item = note.AddText("NoteText_enc", text, FontColor.Good);
        item.transform.SetParent(rowTransform, false);
        item.transform.localScale = Vector3.one;

        var layoutElement = item.GetComponent<UnityEngine.UI.LayoutElement>() ?? item.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layoutElement.minWidth = 48f;
        layoutElement.flexibleWidth = 1f;
        layoutElement.minHeight = 27f;
        layoutElement.preferredHeight = 27f;
        layoutElement.flexibleHeight = 0f;

        if (item.text1 != null)
        {
            item.text1.alignment = TextAnchor.MiddleLeft;
            var textTransform = item.text1.rectTransform;
            var leftInset = Mathf.Max(22f, textTransform.offsetMin.x);
            textTransform.anchorMin = Vector2.zero;
            textTransform.anchorMax = Vector2.one;
            textTransform.offsetMin = new Vector2(leftInset, 0f);
            textTransform.offsetMax = Vector2.zero;
            item.text1.resizeTextForBestFit = true;
            item.text1.resizeTextMinSize = Mathf.Max(10, item.text1.fontSize - 4);
            item.text1.resizeTextMaxSize = item.text1.fontSize;
            item.text1.horizontalOverflow = HorizontalWrapMode.Wrap;
            item.text1.verticalOverflow = VerticalWrapMode.Truncate;
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

        var iconWidth = item.image1 != null ? Mathf.Max(20f, item.image1.rectTransform.rect.width) : 0f;
        var textWidth = item.text1 != null ? item.text1.preferredWidth : 0f;
        layoutElement.preferredWidth = iconWidth + textWidth + 6f;
    }
    private static void AddMilkBonusSpacer(RectTransform rowTransform)
    {
        var spacer = new GameObject("ElinModifierMilkBonusSpacer", typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
        spacer.transform.SetParent(rowTransform, false);
        var layoutElement = spacer.GetComponent<UnityEngine.UI.LayoutElement>();
        layoutElement.minWidth = 0f;
        layoutElement.preferredWidth = -1f;
        layoutElement.flexibleWidth = 1f;
    }
}
