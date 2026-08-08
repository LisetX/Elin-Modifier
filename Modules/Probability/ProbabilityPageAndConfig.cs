using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class ProbabilityModule
{
    internal void BuildPage()
    {
        var toolbar = CreateLGuiRect(_lGuiPageHost!, "ProbabilityToolbar");
        AnchorLGuiTop(toolbar, 0f, 64f, 0f, 0f);

        var filter = CreateLGuiInput(toolbar, "ProbabilityFilter", T("过滤", "Filter"), 0f, 7f, 460f, 46f);
        filter.text = _probabilityFilter;
        filter.onValueChanged.AddListener(value =>
        {
            _probabilityFilter = value ?? "";
            _probabilityFilterDirty = true;
            _probabilityFilterDueAt = Time.unscaledTime + 0.16f;
        });
        filter.onEndEdit.AddListener(_ =>
        {
            _probabilityFilterDirty = false;
            RebuildProbabilityRows();
        });

        CreateLGuiButton(toolbar, "ProbabilityRescan", T("重新扫描", "Rescan"), 474f, 7f, 136f, 46f, RescanProbabilityEntries);
        CreateLGuiButton(toolbar, "ProbabilityRestoreAll", T("恢复全部", "Restore all"), 620f, 7f, 144f, 46f, () =>
        {
            RestoreAll(true);
            RebuildProbabilityRows();
        });
        CreateLGuiButton(toolbar, "ProbabilitySaveModule", T("保存模块配置", "Save module config"), 774f, 7f, 176f, 46f, SaveModuleConfiguration);
        CreateLGuiButton(toolbar, "ProbabilityLoadModule", T("读取模块配置", "Load module config"), 960f, 7f, 176f, 46f, LoadModuleConfiguration);
        _probabilitySummaryText = CreateLGuiText(
            toolbar,
            "ProbabilitySummary",
            "",
            14,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(_probabilitySummaryText.rectTransform, 1150f, 1f, 360f, 58f);

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "ProbabilityList", 68f);
        _probabilityList = new VirtualList<ProbabilityRow>(scroll, 58f, 18, CreateLGuiVirtualRow, BindProbabilityRow);

        if (!_probabilityScanned || !ReferenceEquals(_probabilitySourceManager, GetCurrentProbabilitySourceManager()))
            ScanProbabilityEntries(false);
        RebuildProbabilityRows();
    }
    private object? GetCurrentProbabilitySourceManager()
    {
        try { return GameAccess.Sources.Manager; }
        catch { return null; }
    }
    private void RescanProbabilityEntries()
    {
        ScanProbabilityEntries(true);
        RebuildProbabilityRows();
    }
    private void SaveModuleConfiguration()
    {
        var json = CaptureStoredConfigurationJson();
        string error;
        if (!_host.TryWriteProbabilityModuleConfiguration(json, out error))
        {
            _probabilityLog = T("保存模块配置失败: ", "Failed to save module config: ") + error;
            RebuildProbabilityRows();
            return;
        }

        _storedConfigurationJson = json;
        _probabilityLog = T("事件概率模块配置已保存，共 ", "Event probability module config saved: ") +
                          _probabilityModifiedCount.ToString(CultureInfo.InvariantCulture) +
                          T(" 项修改", " modifications");
        RebuildProbabilityRows();
    }
    private void LoadModuleConfiguration()
    {
        string json;
        string error;
        if (!_host.TryReadProbabilityModuleConfiguration(out json, out error))
        {
            _probabilityLog = T("读取模块配置失败: ", "Failed to load module config: ") + error;
            RebuildProbabilityRows();
            return;
        }

        _storedConfigurationJson = NormalizeStoredConfigurationJson(json);
        ApplyStoredConfigurationJson(_storedConfigurationJson);
        RebuildProbabilityRows();
    }
}
