using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{

    private float CreateLGuiNpcLootHeader(RectTransform content, float y)
    {
        var row = CreateLGuiNpcInfoRow(content, "NpcLootHeader", 0, y, 42f, true);
        CreateLGuiNpcInfoCell(row, T("来源", "Source"), 16f, 0f, 140f, 42f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, T("掉落物", "Drop"), 164f, 0f, 330f, 42f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, T("基础概率 / 触发", "Base chance / trigger"), 502f, 0f, 174f, 42f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, T("数量", "Quantity"), 684f, 0f, 154f, 42f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, T("条件", "Conditions"), 846f, 0f, 482f, 42f, TextAnchor.MiddleLeft, 15);
        return y + 48f;
    }

    private float CreateLGuiNpcLootRow(RectTransform content, NpcLootEntry entry, int index, float y)
    {
        const float minimumHeight = 58f;
        var row = CreateLGuiNpcInfoRow(content, "NpcLootRow" + index, index, y, minimumHeight, false);
        var source = CreateLGuiNpcInfoCell(row, entry.Source, 16f, 0f, 140f, minimumHeight, TextAnchor.MiddleLeft, 14);
        var item = CreateLGuiNpcInfoCell(row, entry.Item, 164f, 0f, 330f, minimumHeight, TextAnchor.MiddleLeft, 14);
        var probability = CreateLGuiNpcInfoCell(row, entry.Probability, 502f, 0f, 174f, minimumHeight, TextAnchor.MiddleLeft, 14);
        var quantity = CreateLGuiNpcInfoCell(row, entry.Quantity, 684f, 0f, 154f, minimumHeight, TextAnchor.MiddleLeft, 14);
        var conditions = CreateLGuiNpcInfoCell(row, entry.Conditions, 846f, 0f, 482f, minimumHeight, TextAnchor.MiddleLeft, 14);
        var cells = new[] { source, item, probability, quantity, conditions };
        var preferredHeight = 0f;
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i].verticalOverflow = VerticalWrapMode.Overflow;
            ApplyCurrentLGuiFontScale(cells[i], 14);
            cells[i].rectTransform.ForceUpdateRectTransforms();
            preferredHeight = Math.Max(preferredHeight, cells[i].preferredHeight);
        }
        var height = Math.Max(minimumHeight, Mathf.Ceil((preferredHeight + 16f) / 12f) * 12f);
        PlaceLGuiRect(row, 0f, y, 1360f, height);
        PlaceLGuiRect(source.rectTransform, 16f, 0f, 140f, height);
        PlaceLGuiRect(item.rectTransform, 164f, 0f, 330f, height);
        PlaceLGuiRect(probability.rectTransform, 502f, 0f, 174f, height);
        PlaceLGuiRect(quantity.rectTransform, 684f, 0f, 154f, height);
        PlaceLGuiRect(conditions.rectTransform, 846f, 0f, 482f, height);
        return y + height + 6f;
    }
}
