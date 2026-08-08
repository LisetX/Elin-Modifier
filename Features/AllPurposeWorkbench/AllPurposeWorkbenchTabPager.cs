using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal static class AllPurposeWorkbenchTabPager
{
    private const int CategoriesPerPage = 13;

    private enum TabType
    {
        ItemCategory,
        Workbench
    }

    private sealed class GroupEntry
    {
        internal string Key = "";
        internal string Name = "";
        internal int Count;
    }

    private sealed class PagerState
    {
        internal bool Initialized;
        internal int Page;
        internal TabType Type;
        internal string SearchText = "";
        internal string SelectedGroup = "";
        internal bool ContentOnlyRefresh;
        internal UIButton? TypeButton;
        internal UIButton? PageButton;
        internal UIButton? SearchButton;
        internal readonly List<UIButton> GroupButtons = new List<UIButton>();
        internal readonly List<RecipeSource> AvailableSources = new List<RecipeSource>();
    }

    private static readonly ConditionalWeakTable<LayerCraft, PagerState> States =
        new ConditionalWeakTable<LayerCraft, PagerState>();

    private static PagerState GetState(LayerCraft layer)
    {
        var state = States.GetOrCreateValue(layer);
        if (!state.Initialized)
        {
            state.Type = AllPurposeWorkbenchPatchContext.Current?.DefaultByWorkbench == true
                ? TabType.Workbench
                : TabType.ItemCategory;
            state.Initialized = true;
        }
        return state;
    }

    internal static void FilterSources(
        LayerCraft? layer,
        Thing? factory,
        ref List<RecipeSource> sources)
    {
        if (!AllPurposeWorkbenchPatchContext.IsTarget(layer) ||
            factory == null || layer?.factory != factory || sources == null)
            return;

        var state = GetState(layer);
        state.AvailableSources.Clear();
        var filtered = new List<RecipeSource>(sources.Count);

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (source == null || !MatchesSearch(source, state.SearchText))
                continue;

            state.AvailableSources.Add(source);
            if (state.SelectedGroup.Length == 0 ||
                MatchesGroup(source, state.Type, state.SelectedGroup))
            {
                filtered.Add(source);
            }
        }

        sources = filtered;
    }

    internal static void Refresh(LayerCraft layer)
    {
        if (layer == null || layer.windowList == null)
            return;

        var tabs = layer.windowList.setting?.tabs;
        if (tabs == null || tabs.Count == 0)
            return;

        var state = GetState(layer);
        if (!AllPurposeWorkbenchPatchContext.IsTarget(layer))
        {
            RestoreAll(layer, tabs, state);
            LayoutRebuilder.ForceRebuildLayoutImmediate(layer.windowList.rectTab);
            return;
        }

        if (state.ContentOnlyRefresh)
            return;

        var allButton = tabs[0].button;
        if (allButton == null)
            return;

        EnsureControls(layer, allButton, state);
        ApplyPage(layer, tabs, state);
    }

    internal static void Cleanup(LayerCraft layer)
    {
        if (layer == null)
            return;

        if (!States.TryGetValue(layer, out var state))
            return;

        DestroyControls(state);
        States.Remove(layer);
    }

    private static void EnsureControls(
        LayerCraft layer,
        UIButton allButton,
        PagerState state)
    {
        var parent = allButton.transform.parent;

        if (state.TypeButton == null)
        {
            state.TypeButton = CreateButton(
                allButton,
                parent,
                "ElinModifier_AllPurposeWorkbenchTabType",
                () => SwitchType(layer));
        }

        if (state.PageButton == null)
        {
            state.PageButton = CreateButton(
                allButton,
                parent,
                "ElinModifier_AllPurposeWorkbenchTabPager",
                () => NextPage(layer));
        }

        if (state.SearchButton == null)
        {
            state.SearchButton = CreateButton(
                allButton,
                parent,
                "ElinModifier_AllPurposeWorkbenchSearch",
                () => OpenSearchDialog(layer));
        }
    }

    private static UIButton CreateButton(
        UIButton template,
        Transform parent,
        string name,
        UnityEngine.Events.UnityAction action)
    {
        var button = UnityEngine.Object.Instantiate(template, parent);
        button.name = name;
        button.group = null;
        button.selected = false;
        if (button.mainText != null)
            button.mainText.SetText("");
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
        return button;
    }

    private static void OpenSearchDialog(LayerCraft layer)
    {
        if (!AllPurposeWorkbenchPatchContext.IsTarget(layer) || layer.windowList == null)
            return;

        var state = GetState(layer);
        Dialog.InputName(
            AllPurposeWorkbenchPatchContext.GetSearchPlaceholder(),
            state.SearchText,
            (cancel, value) =>
            {
                if (!cancel)
                    ApplySearch(layer, value);
            });
    }

    private static void SwitchType(LayerCraft layer)
    {
        if (!AllPurposeWorkbenchPatchContext.IsTarget(layer) || layer.windowList == null)
            return;

        var state = GetState(layer);
        state.Type = state.Type == TabType.ItemCategory
            ? TabType.Workbench
            : TabType.ItemCategory;
        state.Page = 0;
        state.SelectedGroup = "";
        layer.windowList.SwitchContent(0);
    }

    private static void NextPage(LayerCraft layer)
    {
        if (!AllPurposeWorkbenchPatchContext.IsTarget(layer) || layer.windowList == null)
            return;

        var state = GetState(layer);
        var pageCount = GetPageCount(BuildGroups(state).Count);
        state.Page = (state.Page + 1) % pageCount;

        var tabs = layer.windowList.setting?.tabs;
        if (tabs != null && tabs.Count > 0)
            ApplyPage(layer, tabs, state);
    }

    private static void ApplySearch(LayerCraft layer, string value)
    {
        if (!AllPurposeWorkbenchPatchContext.IsTarget(layer) || layer.windowList == null)
            return;

        var state = GetState(layer);
        var search = (value ?? "").Trim();
        if (string.Equals(search, state.SearchText, StringComparison.Ordinal))
            return;

        state.SearchText = search;
        state.Page = 0;
        state.SelectedGroup = "";
        layer.windowList.SwitchContent(0);
    }

    private static void SelectAll(LayerCraft layer)
    {
        if (!AllPurposeWorkbenchPatchContext.IsTarget(layer) || layer.windowList == null)
            return;

        var state = GetState(layer);
        state.SelectedGroup = "";
        RefreshSelectedContent(layer, state);
    }

    private static void SelectGroup(LayerCraft layer, string key)
    {
        if (!AllPurposeWorkbenchPatchContext.IsTarget(layer) || layer.windowList == null)
            return;

        var state = GetState(layer);
        state.SelectedGroup = key;
        RefreshSelectedContent(layer, state);
    }

    private static void RefreshSelectedContent(LayerCraft layer, PagerState state)
    {
        state.ContentOnlyRefresh = true;
        try
        {
            layer.RefreshCategory("all");
        }
        finally
        {
            state.ContentOnlyRefresh = false;
        }

        var tabs = layer.windowList?.setting?.tabs;
        if (tabs != null && tabs.Count > 0)
            UpdateSelectionVisual(layer, tabs[0].button, state);
    }

    private static void ApplyPage(
        LayerCraft layer,
        List<Window.Setting.Tab> tabs,
        PagerState state)
    {
        var allButton = tabs[0].button;
        if (allButton == null)
            return;

        for (var i = 1; i < tabs.Count; i++)
        {
            if (tabs[i].button != null)
                tabs[i].button.gameObject.SetActive(false);
        }

        allButton.gameObject.SetActive(true);
        allButton.onClick.RemoveAllListeners();
        allButton.onClick.AddListener(() => SelectAll(layer));

        var groups = BuildGroups(state);
        EnsureGroupButtons(layer, allButton, state, groups.Count);

        var pageCount = GetPageCount(groups.Count);
        if (state.Page >= pageCount)
            state.Page = 0;

        var first = state.Page * CategoriesPerPage;
        var last = Math.Min(first + CategoriesPerPage, groups.Count);
        for (var i = 0; i < state.GroupButtons.Count; i++)
        {
            var button = state.GroupButtons[i];
            var visible = i >= first && i < last && i < groups.Count;
            button.gameObject.SetActive(visible);
            if (!visible)
                continue;

            var group = groups[i];
            var key = group.Key;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectGroup(layer, key));
            button.selected = false;
            if (button.mainText != null)
                button.mainText.SetText(group.Name + "(" + group.Count + ")");
            button.refStr = group.Key;
        }

        if (state.TypeButton != null)
        {
            state.TypeButton.gameObject.SetActive(true);
            if (state.TypeButton.mainText != null)
            {
                state.TypeButton.mainText.SetText(
                    AllPurposeWorkbenchPatchContext.GetTypeText(state.Type == TabType.Workbench));
            }
        }

        if (state.PageButton != null)
        {
            state.PageButton.gameObject.SetActive(pageCount > 1);
            if (state.PageButton.mainText != null)
            {
                state.PageButton.mainText.SetText(
                    AllPurposeWorkbenchPatchContext.GetPagerText(state.Page, pageCount));
            }
        }

        if (state.SearchButton != null)
        {
            state.SearchButton.gameObject.SetActive(true);
            if (state.SearchButton.mainText != null)
            {
                state.SearchButton.mainText.SetText(
                    state.SearchText.Length == 0
                        ? AllPurposeWorkbenchPatchContext.GetSearchPlaceholder()
                        : AllPurposeWorkbenchPatchContext.Translate("搜索", "Search") +
                          ": " + state.SearchText);
            }
        }

        UpdateSelectionVisual(layer, allButton, state);
        ApplySiblingOrder(allButton, state);
        LayoutRebuilder.ForceRebuildLayoutImmediate(layer.windowList.rectTab);
    }

    private static void UpdateSelectionVisual(
        LayerCraft layer,
        UIButton? allButton,
        PagerState state)
    {
        if (allButton == null)
            return;

        UIButton? selectedButton = null;
        for (var i = 0; i < state.GroupButtons.Count; i++)
        {
            var button = state.GroupButtons[i];
            var selected = state.SelectedGroup.Length > 0 &&
                           string.Equals(
                               button.refStr,
                               state.SelectedGroup,
                               StringComparison.Ordinal);
            button.selected = selected;
            button.DoNormalTransition();
            if (selected)
                selectedButton = button;
        }

        var group = layer.windowList?.groupTab;
        if (selectedButton != null)
        {
            allButton.selected = false;
            allButton.DoNormalTransition();
            if (group != null)
            {
                group.selected = selectedButton;
                group.RefreshButtons();
            }
            selectedButton.selected = true;
            selectedButton.DoNormalTransition();
        }
        else
        {
            allButton.selected = true;
            allButton.DoNormalTransition();
            if (group != null)
                group.Select(allButton);
        }
    }

    private static void EnsureGroupButtons(
        LayerCraft layer,
        UIButton template,
        PagerState state,
        int count)
    {
        while (state.GroupButtons.Count < count)
        {
            var index = state.GroupButtons.Count;
            var button = CreateButton(
                template,
                template.transform.parent,
                "ElinModifier_AllPurposeWorkbenchGroup_" + index,
                () => { });
            state.GroupButtons.Add(button);
        }

        while (state.GroupButtons.Count > count)
        {
            var last = state.GroupButtons.Count - 1;
            var button = state.GroupButtons[last];
            state.GroupButtons.RemoveAt(last);
            if (button != null)
                UnityEngine.Object.Destroy(button.gameObject);
        }
    }

    private static void ApplySiblingOrder(UIButton allButton, PagerState state)
    {
        var sibling = 0;
        if (state.SearchButton != null)
            state.SearchButton.transform.SetSiblingIndex(sibling++);
        if (state.TypeButton != null)
            state.TypeButton.transform.SetSiblingIndex(sibling++);
        if (state.PageButton != null)
            state.PageButton.transform.SetSiblingIndex(sibling++);

        allButton.transform.SetSiblingIndex(sibling++);
        for (var i = 0; i < state.GroupButtons.Count; i++)
            state.GroupButtons[i].transform.SetSiblingIndex(sibling++);
    }

    private static List<GroupEntry> BuildGroups(PagerState state)
    {
        var groups = new List<GroupEntry>();
        var byKey = new Dictionary<string, GroupEntry>(StringComparer.Ordinal);

        for (var i = 0; i < state.AvailableSources.Count; i++)
        {
            var source = state.AvailableSources[i];
            if (source == null)
                continue;

            if (state.Type == TabType.Workbench)
            {
                var workbenches = GetWorkbenchKeys(source);
                for (var keyIndex = 0; keyIndex < workbenches.Count; keyIndex++)
                {
                    AddGroup(
                        groups,
                        byKey,
                        source,
                        workbenches[keyIndex],
                        state.Type);
                }
            }
            else
            {
                AddGroup(
                    groups,
                    byKey,
                    source,
                    GetItemCategoryKey(source),
                    state.Type);
            }
        }

        return groups;
    }

    private static void AddGroup(
        List<GroupEntry> groups,
        Dictionary<string, GroupEntry> byKey,
        RecipeSource source,
        string key,
        TabType type)
    {
        if (key.Length == 0)
            return;

        if (!byKey.TryGetValue(key, out var group))
        {
            group = new GroupEntry
            {
                Key = key,
                Name = GetGroupName(source, key, type),
                Count = 0
            };
            byKey.Add(key, group);
            groups.Add(group);
        }

        group.Count++;
    }

    private static bool MatchesGroup(RecipeSource source, TabType type, string key)
    {
        if (type == TabType.ItemCategory)
        {
            return string.Equals(
                GetItemCategoryKey(source),
                key,
                StringComparison.Ordinal);
        }

        var workbenches = GetWorkbenchKeys(source);
        for (var i = 0; i < workbenches.Count; i++)
        {
            if (string.Equals(workbenches[i], key, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string GetItemCategoryKey(RecipeSource source)
    {
        try
        {
            var row = source.row.Category.GetSecondRoot();
            if (row.id != "lightsource" && row.IsChildOf("armor") &&
                GameAccess.Sources.Categories.map.TryGetValue("armor", out var armor))
            {
                row = armor;
            }
            return row.id ?? "";
        }
        catch
        {
            return source.recipeCat ?? "";
        }
    }

    private static List<string> GetWorkbenchKeys(RecipeSource source)
    {
        var result = new List<string>(4);
        try
        {
            AddWorkbenchKey(result, source.idFactory);
            var factories = source.row?.factory;
            if (factories != null)
            {
                for (var i = 0; i < factories.Length; i++)
                    AddWorkbenchKey(result, factories[i]);
            }

            if (result.Contains("bonfire"))
            {
                AddWorkbenchKeyIfPresent(result, "hearth");
                AddWorkbenchKeyIfPresent(result, "bbq");
            }
            if (result.Contains("camppot"))
            {
                AddWorkbenchKeyIfPresent(result, "cauldron");
                AddWorkbenchKeyIfPresent(result, "stove");
            }
            if (result.Contains("factory_sculpture"))
                AddWorkbenchKeyIfPresent(result, "tool_sculpture");
        }
        catch
        {
        }
        return result;
    }

    private static void AddWorkbenchKey(List<string> result, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        key = key.Trim();
        if (key == "self" || key == "x" || key == "none" || key == "None")
            return;
        if (!result.Contains(key))
            result.Add(key);
    }

    private static void AddWorkbenchKeyIfPresent(List<string> result, string key)
    {
        try
        {
            if (GameAccess.Sources.Cards.map.ContainsKey(key))
                AddWorkbenchKey(result, key);
        }
        catch
        {
        }
    }

    internal static bool HasWorkbenchFactory(RecipeSource source)
    {
        return source != null && GetWorkbenchKeys(source).Count > 0;
    }

    private static string GetGroupName(
        RecipeSource source,
        string key,
        TabType type)
    {
        try
        {
            if (type == TabType.Workbench)
            {
                if (GameAccess.Sources.Cards.map.TryGetValue(key, out var factory))
                    return factory.GetName();
                return source.NameFactory;
            }

            if (GameAccess.Sources.Categories.map.TryGetValue(key, out var category))
                return category.GetName();
        }
        catch
        {
        }

        return key;
    }

    private static bool MatchesSearch(RecipeSource source, string search)
    {
        if (search.Length == 0)
            return true;

        try
        {
            if (Contains(source.Name, search) ||
                Contains(source.id, search) ||
                Contains(source.row?.idString, search) ||
                Contains(source.row?.name, search) ||
                Contains(source.row?.name_JP, search))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool Contains(string? value, string search)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void RestoreAll(
        LayerCraft layer,
        List<Window.Setting.Tab> tabs,
        PagerState state)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            var button = tabs[i].button;
            if (button != null)
                button.gameObject.SetActive(!tabs[i].disable);
        }

        if (tabs.Count > 0 && tabs[0].button != null)
        {
            var allButton = tabs[0].button;
            allButton.onClick.RemoveAllListeners();
            allButton.onClick.AddListener(() =>
            {
                if (layer.windowList != null)
                    layer.windowList.SwitchContent(0);
            });
        }

        DestroyControls(state);
        state.Page = 0;
        state.Type = TabType.ItemCategory;
        state.SearchText = "";
        state.SelectedGroup = "";
        state.AvailableSources.Clear();
    }

    private static void DestroyControls(PagerState state)
    {
        if (state.TypeButton != null)
            UnityEngine.Object.Destroy(state.TypeButton.gameObject);
        if (state.PageButton != null)
            UnityEngine.Object.Destroy(state.PageButton.gameObject);
        if (state.SearchButton != null)
            UnityEngine.Object.Destroy(state.SearchButton.gameObject);

        for (var i = 0; i < state.GroupButtons.Count; i++)
        {
            var button = state.GroupButtons[i];
            if (button != null)
                UnityEngine.Object.Destroy(button.gameObject);
        }

        state.TypeButton = null;
        state.PageButton = null;
        state.SearchButton = null;
        state.GroupButtons.Clear();
    }

    private static int GetPageCount(int categoryCount)
    {
        return Math.Max(1, (categoryCount + CategoriesPerPage - 1) / CategoriesPerPage);
    }
}

