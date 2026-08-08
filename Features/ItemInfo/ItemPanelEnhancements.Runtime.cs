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
}
