using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiDebugPage()
    {
        if (!_debugAuthorized)
            return;
        _debugVisible = true;
        BuildLGuiDebugRoots();
        if (_lGuiDebugTarget == null && _lGuiDebugRoots.Count > 0)
        {
            _lGuiDebugRootIndex = Clamp(_lGuiDebugRootIndex, 0, _lGuiDebugRoots.Count - 1);
            _lGuiDebugTarget = _lGuiDebugRoots[_lGuiDebugRootIndex].Target;
            _lGuiDebugTargetLabel = _lGuiDebugRoots[_lGuiDebugRootIndex].Label;
            _lGuiDebugTargetPath = "debug:" + _lGuiDebugRootIndex.ToString(CultureInfo.InvariantCulture);
        }

        var toolbar = CreateLGuiRect(_lGuiPageHost!, "DebugToolbar");
        AnchorLGuiTop(toolbar, 0f, 116f, 0f, 0f);
        CreateLGuiButton(toolbar, "PrevRoot", "◀", 0f, 5f, 48f, 46f, () => CycleLGuiDebugRoot(-1));
        CreateLGuiButton(toolbar, "NextRoot", "▶", 56f, 5f, 48f, 46f, () => CycleLGuiDebugRoot(1));
        _lGuiDebugTargetText = CreateLGuiText(toolbar, "DebugTarget", _lGuiDebugTargetLabel, 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiDebugTargetText.rectTransform, 118f, 5f, 520f, 46f);
        CreateLGuiButton(toolbar, "BackDebug", T("返回", "Back"), 650f, 5f, 90f, 46f, NavigateLGuiDebugBack);
        CreateLGuiButton(toolbar, "RefreshDebug", T("刷新页面数据", "Refresh page data"), 750f, 5f, 170f, 46f, () =>
        {
            RebuildLGuiDebugRows();
            _lGuiDebugList?.Refresh(true);
        });
        var filter = CreateLGuiInput(toolbar, "DebugFilter", T("搜索", "Search"), 940f, 5f, 380f, 46f);
        filter.text = _lGuiDebugFilter;
        filter.onValueChanged.AddListener(value =>
        {
            _lGuiDebugFilter = value ?? "";
            RebuildLGuiDebugRows();
        });
        CreateLGuiButton(toolbar, "DebugTools", "Diagnostics", 0f, 63f, 150f, 46f, OpenLGuiDebugDiagnostics);
        CreateLGuiButton(toolbar, "DebugRoots", "Root selector", 164f, 63f, 150f, 46f, OpenLGuiDebugRootSelector);
        CreateLGuiButton(toolbar, "SimulateError", T("模拟Error报错", "Simulate Error"), 328f, 63f, 170f, 46f, SimulateDebugError);
        CreateLGuiButton(toolbar, "SimulateWarning", T("模拟Warning报错", "Simulate Warning"), 512f, 63f, 190f, 46f, SimulateDebugWarning);
        CreateLGuiButton(toolbar, "ClearDebugLocks", "Clear locks", 716f, 63f, 130f, 46f, ClearLGuiDebugLocks);
        CreateLGuiButton(toolbar, "ClearDebugInputs", "Clear inputs", 860f, 63f, 130f, 46f, ClearLGuiDebugInputs);
        var scroll = CreateLGuiScroll(_lGuiPageHost!, "DebugList", 116f);
        _lGuiDebugList = new VirtualList<LGuiDebugRow>(scroll, 58f, 18, CreateLGuiVirtualRow, BindLGuiDebugRow);
        RebuildLGuiDebugRows();
    }
}
