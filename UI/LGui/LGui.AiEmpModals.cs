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
    private void OpenLGuiAiPluginCacheEditor()
    {
        var modal = CreateLGuiCompleteModal("RuntimeAiPluginCache", T("AI功能缓存", "AI Feature Cache"), out var content, 1540f, 980f);
        if (modal == null) return;
        var y = 4f;
        CreateLGuiButton(content, "ExportAll", T("导出AI缓存", "Export AI cache"), 0f, y, 170f, 44f, () => { _aiPluginCacheLog = ExportAiPluginCacheToWorkspace(); OpenLGuiAiPluginCacheEditor(); });
        CreateLGuiButton(content, "Clear", T("清空AI缓存", "Clear AI cache"), 182f, y, 160f, 44f, () => { ClearAiPluginCache(); OpenLGuiAiPluginCacheEditor(); });
        var status = CreateLGuiText(content, "Status", _aiPluginCacheLog, 15, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(status.rectTransform, 360f, y, 960f, 44f);
        y += 56f;
        for (var i = 0; i < _aiPluginCacheEntries.Count; i++)
        {
            var entry = _aiPluginCacheEntries[i];
            var local = entry;
            var label = entry.DisplayKind + " | " + SafeEmpText(entry.ToolName, "<empty>") + " | " + SafeEmpText(entry.DisplayTitle, "<empty>") + " | " + entry.CachedUtc.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            var text = CreateLGuiText(content, "Cache", label, 15, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(text.rectTransform, 0f, y, 1170f, 44f);
            CreateLGuiButton(content, "Export" + i, T("导出", "Export"), 1190f, y, 100f, 44f, () => { _aiPluginCacheLog = ExportAiPluginCacheToWorkspace(local); OpenLGuiAiPluginCacheEditor(); });
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 20f));
    }
    private void OpenLGuiEmpPluginDetails()
    {
        RefreshEmpPluginDefinitionsIfNeeded();
        var modal = CreateLGuiCompleteModal("RuntimeEmpDetails", T("插件详情", "Plugin details"), out var content, 1540f, 980f);
        if (modal == null) return;
        var y = 4f;
        y = AddLGuiReadOnlyRow(content, "Status", _pluginManagerLog, y, 120f);
        foreach (var plugin in _pluginDefinitions.Values.OrderBy(plugin => plugin == null ? "" : SafeEmpText(plugin.Name, plugin.Id), StringComparer.OrdinalIgnoreCase))
        {
            if (plugin == null) continue;
            y = AddLGuiSectionTitle(content, SafeEmpText(plugin.Name, plugin.Id) + " [" + plugin.Id + "]", y);
            y = AddLGuiReadOnlyRow(content, "Valid", plugin.IsValid.ToString(CultureInfo.InvariantCulture), y, 120f);
            if (!string.IsNullOrWhiteSpace(plugin.Error)) y = AddLGuiReadOnlyRow(content, "Error", plugin.Error, y, 120f);
            y = AddLGuiReadOnlyRow(content, "Functions", plugin.Functions.Count.ToString(CultureInfo.InvariantCulture), y, 120f);
            for (var i = 0; i < plugin.Functions.Count; i++)
            {
                var function = plugin.Functions[i];
                y = AddLGuiReadOnlyRow(content, GetEmpFunctionKindDisplayName(function.Kind), SafeEmpText(function.Name, function.Id) + " | " + function.Id + (function.IsValid ? "" : " | " + function.Error), y, 160f);
            }
        }
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 20f));
    }
}
