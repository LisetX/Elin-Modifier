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
    private float BuildLGuiReferencePager(RectTransform content, int count, int page, float y, Action<int> setPage, int perPage = GameRowsPerPage)
    {
        perPage = Math.Max(1, perPage);
        var pages = Math.Max(1, (count + perPage - 1) / perPage);
        page = Clamp(page, 0, pages - 1);
        var currentPage = page;
        CreateLGuiButton(content, "Prev", "◀", 0f, y, 48f, 42f, () => setPage(Math.Max(0, currentPage - 1)));
        var label = CreateLGuiText(content, "Page", (page + 1) + " / " + pages + "  (" + count + ")", 16, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(label.rectTransform, 58f, y, 220f, 42f);
        CreateLGuiButton(content, "Next", "▶", 288f, y, 48f, 42f, () => setPage(Math.Min(pages - 1, currentPage + 1)));
        return y + 52f;
    }
    private void OpenLGuiMaterialReference()
    {
        EnsureMaterialRows();
        var modal = CreateLGuiCompleteModal("RuntimeMaterialReference", T("材质ID", "Material IDs"), out var content, 1480f, 930f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "MaterialFilter", T("过滤", "Filter"), 0f, y, 420f, 44f);
        filter.text = _lGuiMaterialFilter;
        filter.onValueChanged.AddListener(value => _lGuiMaterialFilter = value ?? "");
        CreateLGuiButton(content, "Refresh", T("刷新", "Refresh"), 434f, y, 100f, 44f, () => { _lGuiMaterialPage = 0; OpenLGuiMaterialReference(); });
        y += 54f;
        var rows = _materialRows.Where(row => string.IsNullOrWhiteSpace(_lGuiMaterialFilter) ||
            row.Name.IndexOf(_lGuiMaterialFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            row.Category.IndexOf(_lGuiMaterialFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            row.Id.ToString(CultureInfo.InvariantCulture).Contains(_lGuiMaterialFilter)).ToList();
        const int perPage = 36;
        var pages = Math.Max(1, (rows.Count + perPage - 1) / perPage);
        _lGuiMaterialPage = Clamp(_lGuiMaterialPage, 0, pages - 1);
        var current = _lGuiMaterialPage;
        y = BuildLGuiReferencePager(content, rows.Count, _lGuiMaterialPage, y, next => { _lGuiMaterialPage = next; OpenLGuiMaterialReference(); }, perPage);
        var start = current * perPage;
        var end = Math.Min(rows.Count, start + perPage);
        for (var i = start; i < end; i++)
        {
            var row = rows[i];
            var column = (i - start) % 3;
            var line = (i - start) / 3;
            CreateLGuiButton(content, "Material" + i, row.Id + ": " + row.Name, column * 440f, y + line * 48f, 426f, 42f, () => { _itemMat = row.Id.ToString(CultureInfo.InvariantCulture); CloseLGuiEditorModal(true); });
        }
        y += Math.Max(1, (end - start + 2) / 3) * 48f;
        content.sizeDelta = new Vector2(0f, Math.Max(760f, y + 20f));
    }
}
