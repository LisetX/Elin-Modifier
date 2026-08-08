using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiItemsPage()
    {
        EnsureItemRows();
        var toolbar = CreateLGuiRect(_lGuiPageHost!, "ItemToolbar");
        AnchorLGuiTop(toolbar, 0f, 100f, 0f, 0f);
        CreateLGuiFieldLabel(toolbar, T("物品过滤", "Item filter"), 0f, 0f, 360f);
        CreateLGuiFieldLabel(toolbar, T("生成数量", "Spawn count"), 374f, 0f, 120f);
        CreateLGuiFieldLabel(toolbar, T("物品等级", "Item level"), 506f, 0f, 120f);
        CreateLGuiFieldLabel(toolbar, T("材质ID", "Material ID"), 638f, 0f, 150f);
        var filter = CreateLGuiInput(toolbar, "ItemFilter", T("过滤", "Filter"), 0f, 28f, 360f, 46f);
        filter.text = _itemFilter;
        filter.onValueChanged.AddListener(value => { _itemFilter = value ?? ""; RebuildLGuiItemRows(); });
        var count = CreateLGuiInput(toolbar, "Count", T("数量", "Count"), 374f, 28f, 120f, 46f);
        count.text = _itemCount;
        count.onValueChanged.AddListener(value => _itemCount = value ?? "1");
        var level = CreateLGuiInput(toolbar, "Level", T("等级", "Level"), 506f, 28f, 120f, 46f);
        level.text = _itemLv;
        level.onValueChanged.AddListener(value => _itemLv = value ?? "1");
        var material = CreateLGuiInput(toolbar, "Material", T("材质ID", "Material ID"), 638f, 28f, 150f, 46f);
        material.text = _itemMat;
        material.onValueChanged.AddListener(value => _itemMat = value ?? "-1");
        CreateLGuiButton(toolbar, "MaterialReference", T("材质ID表", "Material IDs"), 806f, 28f, 140f, 46f, OpenLGuiMaterialReference);

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "ItemList", 104f);
        _lGuiItemList = new VirtualList<ItemDef>(scroll, 58f, 18, CreateLGuiVirtualRow, BindLGuiItemRow);
        RebuildLGuiItemRows();
    }
    private void RebuildLGuiItemRows()
    {
        if (_lGuiItemList == null)
            return;
        EnsureItemRows();
        _lGuiFilteredItems.Clear();
        for (var i = 0; i < _itemRows.Count; i++)
        {
            var item = _itemRows[i];
            if (LGuiFilterMatches(item.DisplayName, item.Name, item.Id, _itemFilter))
                _lGuiFilteredItems.Add(item);
        }
        _lGuiItemList.SetItems(_lGuiFilteredItems);
    }
}
