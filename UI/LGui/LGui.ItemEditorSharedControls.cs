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
    private float AddLGuiRarityButtons(RectTransform content, int currentValue, Action<int> setValue, Action rebuild, float y)
    {
        y = AddLGuiSectionTitle(content, T("稀有度", "Rarity") + ": " + GetWeaponRarityLabel(currentValue) + " (" + currentValue + ")", y);
        var values = new[] { -100, 0, 100, 200, 300, 400 };
        var labels = new[] { T("低级", "Poor"), T("普通", "Standard"), T("高级", "Superior"), T("奇迹", "Miracle"), T("神器", "Godly"), T("古遗物", "Artifact") };
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var text = value == currentValue ? "→ " + labels[i] : labels[i];
            CreateLGuiButton(content, "Rarity" + value, text, i * 190f, y, 178f, 42f, () => { setValue(value); rebuild(); });
        }
        return y + 52f;
    }
    private float AddLGuiEffectEditor(RectTransform content, string title, List<GeneValueInput> values, Func<string, string> getName, Thing? target, float y, Action rebuild, Action openReference)
    {
        y = AddLGuiSectionTitle(content, title, y);
        CreateLGuiButton(content, "Reference", T("效果对应表", "Effect table"), 0f, y, 170f, 42f, openReference);
        CreateLGuiButton(content, "AddEffect", T("添加效果", "Add effect"), 184f, y, 130f, 42f, () => { values.Add(new GeneValueInput("", "0")); rebuild(); });
        y += 52f;
        for (var i = 0; i < values.Count; i++)
        {
            var index = i;
            var row = values[i];
            var name = CreateLGuiText(content, "EffectName", getName(row.ElementId), 15, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(name.rectTransform, 0f, y, 360f, 42f);
            AddLGuiInlineInput(content, T("效果ID", "Effect ID"), () => row.ElementId, value => row.ElementId = value, 370f, y, 90f, 130f);
            AddLGuiInlineInput(content, T("数值", "Value"), () => row.Value, value => row.Value = value, 610f, y, 70f, 130f);
            if (CanRestoreThingElementOriginalValue(target, row.ElementId))
                CreateLGuiButton(content, "Restore" + i, T("恢复", "Restore"), 830f, y, 90f, 42f, () => { RestoreThingElementInputToOriginal(target, row); rebuild(); });
            CreateLGuiButton(content, "Delete" + i, T("删除", "Delete"), 932f, y, 90f, 42f, () => { values.RemoveAt(index); rebuild(); });
            y += 48f;
        }
        return y;
    }
    private void OpenLGuiEffectReference(string title, Func<List<GeneEffectDef>> getRows, Func<string> readFilter, Action<string> writeFilter, int page, Action<int> setPage, Action<GeneEffectDef> select, Action returnEditor)
    {
        var modal = CreateLGuiCompleteModal("RuntimeEffectReference", title, out var content, 1480f, 930f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "Filter", T("过滤", "Filter"), 0f, y, 420f, 44f);
        filter.text = readFilter();
        filter.onValueChanged.AddListener(value => writeFilter(value ?? ""));
        CreateLGuiButton(content, "Search", T("搜索", "Search"), 434f, y, 100f, 44f, () => { setPage(0); OpenLGuiEffectReference(title, getRows, readFilter, writeFilter, 0, setPage, select, returnEditor); });
        CreateLGuiButton(content, "Back", T("返回", "Back"), 548f, y, 100f, 44f, returnEditor);
        y += 54f;
        var rows = getRows();
        var pages = Math.Max(1, (rows.Count + GameRowsPerPage - 1) / GameRowsPerPage);
        page = Clamp(page, 0, pages - 1);
        var current = page;
        y = BuildLGuiReferencePager(content, rows.Count, page, y, next => { setPage(next); OpenLGuiEffectReference(title, getRows, readFilter, writeFilter, next, setPage, select, returnEditor); });
        var start = current * GameRowsPerPage;
        var end = Math.Min(rows.Count, start + GameRowsPerPage);
        for (var i = start; i < end; i++)
        {
            var row = rows[i];
            var local = row;
            CreateLGuiButton(content, "Effect" + i, row.Name + " | " + row.Id + " | " + row.Category, 0f, y, 1280f, 44f, () => { select(local); returnEditor(); });
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(760f, y + 20f));
    }
    private float AddLGuiBlessedState(RectTransform content, float y, string controlPrefix, Func<int> readValue, Action<int> writeValue, Action refreshEditor)
    {
        var currentValue = readValue();
        y = AddLGuiSectionTitle(content, T("祝福状态", "Blessing state") + ": " + GetBlessedStateLabel(currentValue), y);
        var blessedValues = new[] { (int)BlessedState.Normal, (int)BlessedState.Blessed, (int)BlessedState.Cursed, (int)BlessedState.Doomed };
        var blessedLabels = new[] { T("普通", "Normal"), T("被祝福的", "Blessed"), T("被诅咒的", "Cursed"), T("堕落的", "Doomed") };
        for (var i = 0; i < blessedValues.Length; i++)
        {
            var value = blessedValues[i];
            CreateLGuiButton(content, controlPrefix + value, (value == currentValue ? "→ " : "") + blessedLabels[i], i * 210f, y, 198f, 42f, () =>
            {
                writeValue(value);
                refreshEditor();
            });
        }
        return y + 54f;
    }
    private float AddLGuiThingFlagToggles(RectTransform content, float y, string controlPrefix, List<Tuple<string, Func<bool>, Action<bool>>> flags)
    {
        for (var i = 0; i < flags.Count; i++)
        {
            var flag = flags[i];
            var column = i % 5;
            var line = i / 5;
            var toggle = CreateLGuiToggle(content, controlPrefix + i, column * 270f, y + line * 48f, 250f, 42f, out var label);
            label.text = flag.Item1;
            toggle.isOn = flag.Item2();
            toggle.onValueChanged.AddListener(flag.Item3.Invoke);
        }
        return y + 106f;
    }
    private float AddLGuiItemDataFlags(RectTransform content, float y)
    {
        return AddLGuiThingFlagToggles(content, y, "ItemFlag", new List<Tuple<string, Func<bool>, Action<bool>>>
        {
            Tuple.Create(T("偷窃", "Stolen"), (Func<bool>)(() => _itemDataEditorFlagStolen), (Action<bool>)(value => _itemDataEditorFlagStolen = value)),
            Tuple.Create(T("制作", "Crafted"), (Func<bool>)(() => _itemDataEditorFlagCrafted), (Action<bool>)(value => _itemDataEditorFlagCrafted = value)),
            Tuple.Create(T("赠礼", "Gifted"), (Func<bool>)(() => _itemDataEditorFlagGifted), (Action<bool>)(value => _itemDataEditorFlagGifted = value)),
            Tuple.Create(T("复制品", "Replica"), (Func<bool>)(() => _itemDataEditorFlagReplica), (Action<bool>)(value => _itemDataEditorFlagReplica = value)),
            Tuple.Create(T("复制", "Copy"), (Func<bool>)(() => _itemDataEditorFlagCopy), (Action<bool>)(value => _itemDataEditorFlagCopy = value)),
            Tuple.Create(T("耐火", "Fireproof"), (Func<bool>)(() => _itemDataEditorFlagFireproof), (Action<bool>)(value => _itemDataEditorFlagFireproof = value)),
            Tuple.Create(T("耐酸", "Acidproof"), (Func<bool>)(() => _itemDataEditorFlagAcidproof), (Action<bool>)(value => _itemDataEditorFlagAcidproof = value)),
            Tuple.Create(T("损坏", "Broken"), (Func<bool>)(() => _itemDataEditorFlagBroken), (Action<bool>)(value => _itemDataEditorFlagBroken = value)),
            Tuple.Create(T("不可出售", "No sell"), (Func<bool>)(() => _itemDataEditorFlagNoSell), (Action<bool>)(value => _itemDataEditorFlagNoSell = value)),
            Tuple.Create(T("失物", "Lost property"), (Func<bool>)(() => _itemDataEditorFlagLostProperty), (Action<bool>)(value => _itemDataEditorFlagLostProperty = value))
        });
    }
    private float AddLGuiFoodFlags(RectTransform content, float y)
    {
        return AddLGuiThingFlagToggles(content, y, "FoodFlag", new List<Tuple<string, Func<bool>, Action<bool>>>
        {
            Tuple.Create(T("偷窃", "Stolen"), (Func<bool>)(() => _foodEditorFlagStolen), (Action<bool>)(value => _foodEditorFlagStolen = value)),
            Tuple.Create(T("制作", "Crafted"), (Func<bool>)(() => _foodEditorFlagCrafted), (Action<bool>)(value => _foodEditorFlagCrafted = value)),
            Tuple.Create(T("赠礼", "Gifted"), (Func<bool>)(() => _foodEditorFlagGifted), (Action<bool>)(value => _foodEditorFlagGifted = value)),
            Tuple.Create(T("复制品", "Replica"), (Func<bool>)(() => _foodEditorFlagReplica), (Action<bool>)(value => _foodEditorFlagReplica = value)),
            Tuple.Create(T("复制", "Copy"), (Func<bool>)(() => _foodEditorFlagCopy), (Action<bool>)(value => _foodEditorFlagCopy = value)),
            Tuple.Create(T("耐火", "Fireproof"), (Func<bool>)(() => _foodEditorFlagFireproof), (Action<bool>)(value => _foodEditorFlagFireproof = value)),
            Tuple.Create(T("耐酸", "Acidproof"), (Func<bool>)(() => _foodEditorFlagAcidproof), (Action<bool>)(value => _foodEditorFlagAcidproof = value)),
            Tuple.Create(T("损坏", "Broken"), (Func<bool>)(() => _foodEditorFlagBroken), (Action<bool>)(value => _foodEditorFlagBroken = value)),
            Tuple.Create(T("不可出售", "No sell"), (Func<bool>)(() => _foodEditorFlagNoSell), (Action<bool>)(value => _foodEditorFlagNoSell = value)),
            Tuple.Create(T("失物", "Lost property"), (Func<bool>)(() => _foodEditorFlagLostProperty), (Action<bool>)(value => _foodEditorFlagLostProperty = value))
        });
    }
}
