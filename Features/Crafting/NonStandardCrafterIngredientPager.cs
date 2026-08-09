using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal static class NonStandardCrafterIngredientPager
{
    private const int ItemsPerPage = 40;

    private enum RefreshMode
    {
        ExistingState,
        AfterSetInv
    }

    private sealed class IngredientState
    {
        internal readonly List<Thing> Items = new List<Thing>();
        internal readonly List<Thing> FilteredItems = new List<Thing>();
        internal string Filter = "";
        internal int Page;
        internal bool Rebuilding;
        internal bool HasGridBounds;
        internal Bounds GridBounds;
        internal float GridWidth;
        internal GameObject? Controls;
        internal RectTransform? ControlsRect;
        internal Text? PageText;
        internal Button? PreviousButton;
        internal Button? NextButton;
        internal LGuiSafeInputField? FilterInput;
    }

    private static readonly Dictionary<UIDragGridIngredients, IngredientState> States =
        new Dictionary<UIDragGridIngredients, IngredientState>();
    private static readonly HashSet<UIDragGridIngredients> PendingPositionUpdates =
        new HashSet<UIDragGridIngredients>();

    internal static bool TryRefreshFromGameRefresh(UIDragGridIngredients ingredients)
    {
        if (ingredients == null)
            return false;

        if (!States.ContainsKey(ingredients))
            return false;

        return TryRefreshCore(ingredients, RefreshMode.ExistingState);
    }

    private static bool TryRefreshCore(
        UIDragGridIngredients ingredients,
        RefreshMode mode)
    {
        if (!IsTarget(ingredients, mode))
        {
            Cleanup(ingredients);
            return false;
        }

        if (!States.TryGetValue(ingredients, out var state))
        {
            state = new IngredientState();
            States[ingredients] = state;
        }

        try
        {
            CollectCandidates(ingredients, state);
            RebuildFilteredItems(state);
            RenderPage(ingredients, state);
            EnsureControls(ingredients, state);
            UpdateControls(state);
            PositionControls(ingredients, state);
            return true;
        }
        catch
        {
            Cleanup(ingredients);
            return false;
        }
    }

    internal static void InitializeAfterSetInv(UIDragGridIngredients ingredients)
    {
        if (!ElinModifierPlugin.IsWorkbenchIngredientReadingOptimizationEnabled() ||
            ingredients == null)
            return;

        if (TryRefreshCore(ingredients, RefreshMode.AfterSetInv))
            SchedulePositionUpdate(ingredients);
    }

    private static void SchedulePositionUpdate(UIDragGridIngredients ingredients)
    {
        if (!PendingPositionUpdates.Add(ingredients))
            return;

        var host = ElinModifierPlugin.ActiveInstance;
        if (host == null)
        {
            PendingPositionUpdates.Remove(ingredients);
            return;
        }

        host.StartCoroutine(UpdatePositionWhenVisible(ingredients));
    }

    private static IEnumerator UpdatePositionWhenVisible(
        UIDragGridIngredients ingredients)
    {
        try
        {
            for (var frame = 0; frame < 120; frame++)
            {
                if (!ElinModifierPlugin.IsWorkbenchIngredientReadingOptimizationEnabled() ||
                    ingredients == null || !States.TryGetValue(ingredients, out var state))
                    yield break;

                PositionControls(ingredients, state);
                if (state.HasGridBounds)
                    yield break;

                yield return null;
            }
        }
        finally
        {
            PendingPositionUpdates.Remove(ingredients);
        }
    }

    internal static void RefreshActive()
    {
        try
        {
            var lists = UnityEngine.Object.FindObjectsOfType<UIDragGridIngredients>();
            for (var i = 0; i < lists.Length; i++)
            {
                var ingredients = lists[i];
                if (ingredients == null || !ingredients.gameObject.activeInHierarchy)
                    continue;
                if (States.ContainsKey(ingredients))
                    ingredients.Refresh();
                else
                    InitializeAfterSetInv(ingredients);
            }
        }
        catch
        {
        }
    }

    internal static void DisableAndRestoreActive()
    {
        var lists = new List<UIDragGridIngredients>(States.Keys);
        CloseAll();
        for (var i = 0; i < lists.Count; i++)
        {
            var ingredients = lists[i];
            try
            {
                if (ingredients != null && ingredients.gameObject.activeInHierarchy)
                    ingredients.Refresh();
            }
            catch
            {
            }
        }
    }

    internal static void CloseAll()
    {
        if (States.Count == 0)
            return;
        var lists = new List<UIDragGridIngredients>(States.Keys);
        for (var i = 0; i < lists.Count; i++)
            Cleanup(lists[i]);
    }

    internal static void Cleanup(UIDragGridIngredients ingredients)
    {
        if (ingredients == null || !States.TryGetValue(ingredients, out var state))
            return;
        States.Remove(ingredients);
        if (state.Controls != null)
            UnityEngine.Object.Destroy(state.Controls);
    }

    private static bool IsTarget(
        UIDragGridIngredients ingredients,
        RefreshMode mode)
    {
        if (!ElinModifierPlugin.IsWorkbenchIngredientReadingOptimizationEnabled() ||
            ingredients == null || ingredients.list == null || ingredients.layer == null)
            return false;

        try
        {
            if (ingredients.layer.owner is not InvOwnerCraft || ingredients.goList == null)
                return false;

            switch (mode)
            {
                case RefreshMode.AfterSetInv:
                    return true;
                default:
                    return States.ContainsKey(ingredients);
            }
        }
        catch
        {
            return false;
        }
    }

    private static void CollectCandidates(
        UIDragGridIngredients ingredients,
        IngredientState state)
    {
        state.Items.Clear();
        var owner = ingredients.layer.owner;
        if (!owner.AllowStockIngredients || owner.owner.c_isDisableStockUse)
            return;

        foreach (var thing in GameAccess.World.CurrentMap.Stocked.Things)
        {
            if (thing == null || !owner.ShouldShowGuide(thing) ||
                !GameAccess.World.CurrentMap.Stocked.ShouldListAsResource(thing))
                continue;

            var windowSaveData = thing.parentCard.GetWindowSaveData();
            if (windowSaveData != null && windowSaveData.excludeCraft)
                continue;

            state.Items.Add(thing);
        }
        SortCandidatesByCategory(state.Items);
    }

    private static void SortCandidatesByCategory(List<Thing> items)
    {
        items.Sort((a, b) =>
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            var aCategory = a.category == null ? int.MaxValue : a.category.sortVal;
            var bCategory = b.category == null ? int.MaxValue : b.category.sortVal;
            var result = aCategory.CompareTo(bCategory);
            if (result != 0)
                return result;

            result = string.CompareOrdinal(a.id ?? "", b.id ?? "");
            if (result != 0)
                return result;

            result = a.refVal.CompareTo(b.refVal);
            if (result != 0)
                return result;

            return a.uid.CompareTo(b.uid);
        });
    }

    private static void RebuildFilteredItems(IngredientState state)
    {
        state.FilteredItems.Clear();
        if (state.Filter.Length == 0)
        {
            state.FilteredItems.AddRange(state.Items);
        }
        else
        {
            for (var i = 0; i < state.Items.Count; i++)
            {
                var thing = state.Items[i];
                if (MatchesFilter(thing, state.Filter))
                    state.FilteredItems.Add(thing);
            }
        }

        var pageCount = GetPageCount(state);
        state.Page = Mathf.Clamp(state.Page, 0, pageCount - 1);
    }

    private static bool MatchesFilter(Thing thing, string filter)
    {
        try
        {
            if (!string.IsNullOrEmpty(thing.id) &&
                thing.id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        catch
        {
        }
        try
        {
            var name = thing.Name;
            return !string.IsNullOrEmpty(name) &&
                   name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ConfigureCallbacks(UIDragGridIngredients ingredients)
    {
        ingredients.list.callbacks = new UIList.Callback<Thing, ButtonGrid>
        {
            onClick = delegate (Thing thing, ButtonGrid button)
            {
                var layer = ingredients.layer;
                var currentIndex = layer.currentIndex;
                layer.buttons[currentIndex].SetCardGrid(thing, layer.owner);
                layer.owner.OnProcess(thing);
                layer.AddPutBack(thing, thing.parent as Thing);
            },
            onInstantiate = delegate (Thing thing, ButtonGrid button)
            {
                button.SetCard(thing, ButtonGrid.Mode.Grid);
                button.SetOnClick(delegate { });
                button.onRightClick = delegate
                {
                    ingredients.list.callbacks.OnClick(thing, button);
                };
            }
        };
    }

    private static void RenderPage(
        UIDragGridIngredients ingredients,
        IngredientState state)
    {
        if (state.Rebuilding)
            return;

        state.Rebuilding = true;
        try
        {
            ConfigureCallbacks(ingredients);
            ingredients.list.Clear();
            var start = state.Page * ItemsPerPage;
            var count = Math.Min(
                ItemsPerPage,
                Math.Max(0, state.FilteredItems.Count - start));
            for (var i = 0; i < count; i++)
                ingredients.list.Add(state.FilteredItems[start + i]);
            ingredients.list.Refresh();
        }
        finally
        {
            state.Rebuilding = false;
        }
    }

    private static int GetPageCount(IngredientState state)
    {
        return Math.Max(1, (state.FilteredItems.Count + ItemsPerPage - 1) / ItemsPerPage);
    }

    private static void ApplyFilter(
        UIDragGridIngredients ingredients,
        IngredientState state,
        string value)
    {
        value = (value ?? "").Trim();
        if (string.Equals(state.Filter, value, StringComparison.OrdinalIgnoreCase))
            return;
        state.Filter = value;
        state.Page = 0;
        RebuildFilteredItems(state);
        RenderPage(ingredients, state);
        UpdateControls(state);
        PositionControls(ingredients, state);
    }

    private static void ChangePage(
        UIDragGridIngredients ingredients,
        IngredientState state,
        int delta)
    {
        var pageCount = GetPageCount(state);
        if (pageCount <= 1 || state.Rebuilding)
            return;
        state.Page = (state.Page + delta) % pageCount;
        if (state.Page < 0)
            state.Page += pageCount;
        RenderPage(ingredients, state);
        UpdateControls(state);
        PositionControls(ingredients, state);
    }

    private static void EnsureControls(
        UIDragGridIngredients ingredients,
        IngredientState state)
    {
        if (state.Controls != null)
        {
            state.Controls.transform.SetAsLastSibling();
            return;
        }

        Transform parent = ingredients.transform;
        if (ingredients.goList != null && ingredients.goList.transform.parent != null)
            parent = ingredients.goList.transform.parent;

        var panel = CraftIngredientPickerPager.CreateRect(
            parent,
            "ElinModifier.NonStandardIngredientPager");
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(540f, 48f);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.035f, 0.04f, 0.05f, 0.96f);
        ElinModifierPlugin.RegisterCraftIngredientPickerRoundedImage(panelImage);

        var font = FindFont(ingredients);
        state.FilterInput = CraftIngredientPickerPager.CreateFilterInput(
            panel,
            new Vector2(6f, 6f),
            new Vector2(286f, 36f),
            font,
            value => ApplyFilter(ingredients, state, value));
        state.PreviousButton = CraftIngredientPickerPager.CreateButton(
            panel,
            "Previous",
            "<",
            new Vector2(298f, 6f),
            new Vector2(54f, 36f),
            font,
            () => ChangePage(ingredients, state, -1));

        var pageRect = CraftIngredientPickerPager.CreateRect(panel, "Page");
        pageRect.anchorMin = Vector2.zero;
        pageRect.anchorMax = Vector2.zero;
        pageRect.pivot = Vector2.zero;
        pageRect.anchoredPosition = new Vector2(358f, 6f);
        pageRect.sizeDelta = new Vector2(116f, 36f);
        state.PageText = pageRect.gameObject.AddComponent<Text>();
        state.PageText.font = font;
        state.PageText.fontSize = 17;
        state.PageText.alignment = TextAnchor.MiddleCenter;
        state.PageText.color = Color.white;
        state.PageText.raycastTarget = false;

        state.NextButton = CraftIngredientPickerPager.CreateButton(
            panel,
            "Next",
            ">",
            new Vector2(480f, 6f),
            new Vector2(54f, 36f),
            font,
            () => ChangePage(ingredients, state, 1));

        state.Controls = panel.gameObject;
        state.ControlsRect = panel;
        panel.SetAsLastSibling();
    }

    private static void UpdateControls(IngredientState state)
    {
        var pageCount = GetPageCount(state);
        if (state.PageText != null)
            state.PageText.text = (state.Page + 1) + " / " + pageCount +
                                  "   (" + state.FilteredItems.Count + ")";
        if (state.PreviousButton != null)
            state.PreviousButton.interactable = pageCount > 1;
        if (state.NextButton != null)
            state.NextButton.interactable = pageCount > 1;
        if (state.Controls != null)
            state.Controls.transform.SetAsLastSibling();
    }

    private static void PositionControls(
        UIDragGridIngredients ingredients,
        IngredientState state)
    {
        var panel = state.ControlsRect;
        if (panel == null || ingredients.list == null)
            return;

        try
        {
            Canvas.ForceUpdateCanvases();
            var parent = panel.parent as RectTransform;
            if (parent == null)
                return;

            Bounds bounds;
            var listRect = ingredients.list.Rect();
            if (state.FilteredItems.Count > 0 && listRect != null &&
                listRect.gameObject.activeInHierarchy)
            {
                bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, listRect);
                if (bounds.size.x > 1f && bounds.size.y > 1f)
                {
                    state.GridBounds = bounds;
                    state.HasGridBounds = true;
                }
                else if (state.HasGridBounds)
                {
                    bounds = state.GridBounds;
                }
            }
            else if (state.HasGridBounds)
            {
                bounds = state.GridBounds;
            }
            else
            {
                return;
            }

            state.GridWidth = Mathf.Max(state.GridWidth, Mathf.Max(bounds.size.x, CalculateGridWidth(ingredients.list)));
            var panelWidth = Mathf.Max(420f, state.GridWidth);
            panel.sizeDelta = new Vector2(panelWidth, 48f);
            LayoutControls(state, panelWidth);
            panel.localPosition = new Vector3(
                bounds.center.x,
                bounds.max.y + panel.rect.height * 0.5f + 2f,
                0f);
            ClampToScreen(panel, bounds);
            panel.SetAsLastSibling();
        }
        catch
        {
        }
    }

    private static float CalculateGridWidth(UIList list)
    {
        try
        {
            var grid = list.GetComponent<GridLayoutGroup>();
            if (grid == null || grid.constraintCount <= 0)
                return 0f;
            return grid.padding.left + grid.padding.right +
                   grid.constraintCount * grid.cellSize.x +
                   Math.Max(0, grid.constraintCount - 1) * grid.spacing.x;
        }
        catch
        {
            return 0f;
        }
    }

    private static void LayoutControls(IngredientState state, float panelWidth)
    {
        const float padding = 6f;
        const float gap = 6f;
        const float buttonWidth = 54f;
        const float pageWidth = 116f;
        const float height = 36f;
        var filterWidth = Mathf.Max(
            180f,
            panelWidth - padding * 2f - gap * 3f - buttonWidth * 2f - pageWidth);
        var x = padding;
        SetControlRect(state.FilterInput == null ? null : state.FilterInput.transform as RectTransform, x, filterWidth, height);
        x += filterWidth + gap;
        SetControlRect(state.PreviousButton == null ? null : state.PreviousButton.transform as RectTransform, x, buttonWidth, height);
        x += buttonWidth + gap;
        SetControlRect(state.PageText == null ? null : state.PageText.rectTransform, x, pageWidth, height);
        x += pageWidth + gap;
        SetControlRect(state.NextButton == null ? null : state.NextButton.transform as RectTransform, x, buttonWidth, height);
    }

    private static void SetControlRect(RectTransform? rect, float x, float width, float height)
    {
        if (rect == null)
            return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, 6f);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void ClampToScreen(RectTransform panel, Bounds gridBounds)
    {
        var corners = new Vector3[4];
        panel.GetWorldCorners(corners);
        var delta = Vector3.zero;
        const float margin = 10f;
        if (corners[0].x < margin)
            delta.x += margin - corners[0].x;
        if (corners[2].x > Screen.width - margin)
            delta.x -= corners[2].x - (Screen.width - margin);
        if (corners[2].y > Screen.height - margin)
        {
            panel.localPosition = new Vector3(
                panel.localPosition.x,
                gridBounds.min.y - panel.rect.height * 0.5f - 2f,
                panel.localPosition.z);
            panel.GetWorldCorners(corners);
        }
        if (corners[0].y < margin)
            delta.y += margin - corners[0].y;
        panel.position += delta;
    }

    private static Font FindFont(UIDragGridIngredients ingredients)
    {
        try
        {
            var existing = ingredients.GetComponentInChildren<Text>(true);
            if (existing != null && existing.font != null)
                return existing.font;
        }
        catch
        {
        }
        return GameUiFontResolver.ResolveCurrentUiFont() ??
               Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}

