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
    private void OpenLGuiGeneItemEditor()
    {
        var modal = CreateLGuiCompleteModal("RuntimeGeneItemEditor", T("基因编辑", "Gene editor") + " | " + _geneEditorName, out var content, 1540f, 1010f);
        if (modal == null) return;
        var y = 4f;
        AddLGuiInlineInput(content, T("源ID", "Source ID"), () => _geneEditorSourceId, value => _geneEditorSourceId = value, 0f, y, 120f, 200f);
        CreateLGuiButton(content, "SourceTable", T("源ID对应表", "Source ID table"), 340f, y, 170f, 42f, OpenLGuiGeneSourceReference);
        y += 52f;
        AddLGuiInlineInput(content, T("等级", "Level"), () => _geneEditorLv, value => _geneEditorLv = value, 0f, y);
        AddLGuiInlineInput(content, T("种子", "Seed"), () => _geneEditorSeed, value => _geneEditorSeed = value, 340f, y);
        AddLGuiInlineInput(content, T("费用", "Cost"), () => _geneEditorCost, value => _geneEditorCost = value, 680f, y);
        AddLGuiInlineInput(content, T("占用槽位", "Required slots"), () => _geneEditorSlot, value => _geneEditorSlot = value, 1020f, y);
        y += 60f;
        y = AddLGuiEffectEditor(content, T("基因效果", "Gene effects"), _geneEditorValues, GetGeneEffectName, null, y, OpenLGuiGeneItemEditor, () =>
            OpenLGuiEffectReference(T("基因效果对应表", "Gene effect table"), GetFilteredGeneEffectIds, () => _geneEffectFilter, value => _geneEffectFilter = value, _geneEffectPage, value => _geneEffectPage = value, row => _geneEditorValues.Add(new GeneValueInput(row.Id.ToString(CultureInfo.InvariantCulture), "0")), OpenLGuiGeneItemEditor));
        y += 10f;
        CreateLGuiButton(content, "Apply", T("确认", "Confirm"), 0f, y, 120f, 44f, ApplyLGuiGeneDataChange);
        CreateLGuiButton(content, "Cancel", T("取消", "Cancel"), 134f, y, 120f, 44f, CloseLGuiEditorModal);
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 70f));
    }
    private void OpenLGuiGeneSourceReference()
    {
        var modal = CreateLGuiCompleteModal("RuntimeGeneSources", T("源ID对应表", "Source ID table"), out var content, 1480f, 930f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "Filter", T("过滤", "Filter"), 0f, y, 420f, 44f);
        filter.text = _geneSourceFilter;
        filter.onValueChanged.AddListener(value => _geneSourceFilter = value ?? "");
        CreateLGuiButton(content, "Search", T("搜索", "Search"), 434f, y, 100f, 44f, () => { _geneSourcePage = 0; OpenLGuiGeneSourceReference(); });
        CreateLGuiButton(content, "Back", T("返回", "Back"), 548f, y, 100f, 44f, OpenLGuiGeneItemEditor);
        y += 54f;
        var rows = GetFilteredGeneSourceIds();
        _geneSourcePage = Clamp(_geneSourcePage, 0, Math.Max(0, (rows.Count + GameRowsPerPage - 1) / GameRowsPerPage - 1));
        y = BuildLGuiReferencePager(content, rows.Count, _geneSourcePage, y, next => { _geneSourcePage = next; OpenLGuiGeneSourceReference(); });
        var start = _geneSourcePage * GameRowsPerPage;
        var end = Math.Min(rows.Count, start + GameRowsPerPage);
        for (var i = start; i < end; i++)
        {
            var row = rows[i];
            var local = row;
            CreateLGuiButton(content, "Source" + i, row.DisplayName + " | " + row.Id, 0f, y, 1280f, 44f, () => { _geneEditorSourceId = local.Id; OpenLGuiGeneItemEditor(); });
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(760f, y + 20f));
    }
    private void ApplyLGuiItemAmountChange()
    {
        _itemAmountWindowVisible = true;
        ApplyItemAmountChange();
        var completed = !_itemAmountWindowVisible;
        _itemAmountWindowVisible = false;
        if (completed) CloseLGuiEditorModal(); else OpenLGuiItemAmountEditor();
    }
    private void ApplyLGuiItemDataChange()
    {
        _itemDataEditorWindowVisible = true;
        ApplyItemDataEditorChange();
        var completed = !_itemDataEditorWindowVisible;
        _itemDataEditorWindowVisible = false;
        if (completed) CloseLGuiEditorModal(); else OpenLGuiItemDataEditor();
    }
    private void ApplyLGuiFoodDataChange()
    {
        _foodEditorWindowVisible = true;
        ApplyFoodEditorChange();
        var completed = !_foodEditorWindowVisible;
        _foodEditorWindowVisible = false;
        if (completed) CloseLGuiEditorModal(); else OpenLGuiFoodEditor();
    }
    private void ApplyLGuiWeaponDataChange()
    {
        _weaponEditorWindowVisible = true;
        ApplyWeaponEditorChange();
        var completed = !_weaponEditorWindowVisible;
        _weaponEditorWindowVisible = false;
        if (completed) CloseLGuiEditorModal(); else OpenLGuiWeaponEditor();
    }
    private void ApplyLGuiGeneDataChange()
    {
        _geneEditorWindowVisible = true;
        ApplyGeneEditorChange();
        var completed = !_geneEditorWindowVisible;
        _geneEditorWindowVisible = false;
        if (completed) CloseLGuiEditorModal(); else OpenLGuiGeneItemEditor();
    }
}
