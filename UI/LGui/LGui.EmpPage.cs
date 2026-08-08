using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiEmpPage()
    {
        var toolbar = CreateLGuiRect(_lGuiPageHost!, "EmpToolbar");
        AnchorLGuiTop(toolbar, 0f, 116f, 0f, 0f);
        CreateLGuiButton(toolbar, "ReloadEmp", T("重新读取配置", "Reload EMP"), 0f, 5f, 150f, 46f, () =>
        {
            ReloadEmpPluginDefinitions();
            MarkEmpPending();
            RebuildLGuiEmpRows();
        });
        CreateLGuiButton(toolbar, "SaveEmp", T("保存配置", "Save config"), 166f, 5f, 130f, 46f, () => SaveConfig(true));
        CreateLGuiButton(toolbar, "ApplyEmp", T("应用状态", "Apply states"), 310f, 5f, 130f, 46f, () => ApplySavedEmpPluginStates(true));
        CreateLGuiButton(toolbar, "AiCache", T("AI功能缓存", "AI Feature Cache"), 454f, 5f, 170f, 46f, OpenLGuiAiPluginCacheEditor);
        CreateLGuiButton(toolbar, "EmpDetails", T("插件详情", "Plugin details"), 638f, 5f, 150f, 46f, OpenLGuiEmpPluginDetails);
        _lGuiEmpStatusText = CreateLGuiText(toolbar, "EmpStatus", _pluginManagerLog, 15, TextAnchor.UpperLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiEmpStatusText.rectTransform, 0f, 63f, 1320f, 48f);

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "EmpList", 120f);
        _lGuiEmpList = new VirtualList<LGuiEmpRow>(scroll, 58f, 18, CreateLGuiVirtualRow, BindLGuiEmpRow);
        RebuildLGuiEmpRows();
    }
    private void RebuildLGuiEmpRows()
    {
        if (_lGuiEmpList == null)
            return;
        RefreshEmpPluginDefinitionsIfNeeded();
        _lGuiEmpRows.Clear();
        foreach (var plugin in _pluginDefinitions.Values)
        {
            if (plugin == null || !plugin.IsValid)
                continue;
            for (var i = 0; i < plugin.Functions.Count; i++)
            {
                var function = plugin.Functions[i];
                if (function == null || !function.IsValid)
                    continue;
                _lGuiEmpRows.Add(new LGuiEmpRow(plugin, function, GetEmpFunctionState(plugin, function)));
            }
        }
        _lGuiEmpList.SetItems(_lGuiEmpRows);
    }
}
