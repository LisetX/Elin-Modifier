using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal static class AllPurposeWorkbenchPatchContext
{
    internal static AllPurposeWorkbenchModule? Current =>
        ElinModifierPlugin.ActiveModules?.AllPurposeWorkbench;

    internal static bool IsTarget(LayerCraft? layer)
    {
        return Current?.Enabled == true && layer?.factory?.id == "workbench";
    }

    internal static string Translate(string chinese, string english)
    {
        return ElinModifierPlugin.ActiveInstance?.TranslateModuleText(chinese, english) ?? chinese;
    }

    internal static string GetPagerText(int page, int pageCount)
    {
        return Translate("切换标签页", "Switch tab page") + " " + (page + 1) + "/" + pageCount;
    }

    internal static string GetTypeText(bool byWorkbench)
    {
        var type = byWorkbench
            ? Translate("工作台", "Workbench")
            : Translate("物品分类", "Item category");
        return Translate("切换标签类型", "Switch tab type") + ": " + type;
    }

    internal static string GetSearchPlaceholder()
    {
        return Translate("搜索物品名或ID", "Search item name or ID");
    }
}

