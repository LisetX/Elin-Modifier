using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal static class CraftIngredientPickerPager
{
    private const int ItemsPerPage = 40;

    private sealed class PickerState
    {
        internal Recipe.Ingredient Ingredient = null!;
        internal List<Thing> Items = null!;
        internal List<Thing> FilteredItems = null!;
        internal string Filter = "";
        internal int Page;
        internal float GridWidth;
        internal bool HasGridBounds;
        internal Bounds GridBounds;
        internal bool Rebuilding;
        internal GameObject? Controls;
        internal RectTransform? ControlsRect;
        internal Text? PageText;
        internal Button? PreviousButton;
        internal Button? NextButton;
        internal LGuiSafeInputField? FilterInput;
    }

    private static readonly Dictionary<DropdownGrid, PickerState> States =
        new Dictionary<DropdownGrid, PickerState>();

    internal static void Prepare(
        DropdownGrid picker,
        Recipe.Ingredient ingredient,
        ref List<Thing> things)
    {
        if (picker == null || ingredient == null || things == null)
            return;

        if (!ElinModifierPlugin.IsWorkbenchIngredientReadingOptimizationEnabled())
        {
            Cleanup(picker);
            return;
        }

        if (States.TryGetValue(picker, out var active) && active.Rebuilding)
            return;

        Cleanup(picker);
        if (things.Count == 0)
            return;

        var pageItems = new List<Thing>(things);
        var state = new PickerState
        {
            Ingredient = ingredient,
            Items = pageItems,
            FilteredItems = pageItems,
            Page = 0
        };
        States[picker] = state;
        things = GetPage(state);
    }

    internal static void FinishOpen(DropdownGrid picker)
    {
        if (picker == null || !States.TryGetValue(picker, out var state))
            return;
        if (state.Rebuilding)
            return;

        EnsureControls(picker, state);
        UpdateControls(state);
        ClampPickerContentToScreen(picker);
        PositionControls(picker, state);
    }

    internal static void Cleanup(DropdownGrid picker)
    {
        if (picker == null || !States.TryGetValue(picker, out var state))
            return;

        States.Remove(picker);
        if (state.Controls != null)
            UnityEngine.Object.Destroy(state.Controls);
    }

    internal static void CloseActivePickers()
    {
        if (States.Count == 0)
            return;

        var pickers = new List<DropdownGrid>(States.Keys);
        for (var i = 0; i < pickers.Count; i++)
        {
            var picker = pickers[i];
            try
            {
                if (picker != null)
                    picker.Deactivate();
            }
            catch
            {
            }
            Cleanup(picker);
        }
    }

    private static List<Thing> GetPage(PickerState state)
    {
        var items = state.FilteredItems;
        var pageCount = Math.Max(1, (items.Count + ItemsPerPage - 1) / ItemsPerPage);
        state.Page = Mathf.Clamp(state.Page, 0, pageCount - 1);
        var start = state.Page * ItemsPerPage;
        var count = Math.Min(ItemsPerPage, Math.Max(0, items.Count - start));
        return count == 0 ? new List<Thing>() : items.GetRange(start, count);
    }

    private static void ChangePage(DropdownGrid picker, PickerState state, int delta)
    {
        if (state.Rebuilding)
            return;

        var pageCount = Math.Max(1, (state.FilteredItems.Count + ItemsPerPage - 1) / ItemsPerPage);
        if (pageCount <= 1)
            return;

        var nextPage = (state.Page + delta) % pageCount;
        if (nextPage < 0)
            nextPage += pageCount;
        if (nextPage == state.Page)
            return;

        state.Page = nextPage;
        RebuildPage(picker, state);
    }

    private static void EnsureControls(DropdownGrid picker, PickerState state)
    {
        if (state.Controls != null || picker.rectDrop == null)
        {
            if (state.Controls != null)
                state.Controls.transform.SetAsLastSibling();
            return;
        }

        var font = FindPickerFont(picker);
        var panel = CreateRect(picker.rectDrop, "ElinModifier.IngredientPager");
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(540f, 48f);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.035f, 0.04f, 0.05f, 0.94f);
        ElinModifierPlugin.RegisterCraftIngredientPickerRoundedImage(panelImage);

        state.FilterInput = CreateFilterInput(
            panel,
            new Vector2(6f, 6f),
            new Vector2(286f, 36f),
            font,
            value => ApplyFilter(picker, state, value));

        state.PreviousButton = CreateButton(
            panel,
            "Previous",
            "<",
            new Vector2(298f, 6f),
            new Vector2(54f, 36f),
            font,
            () => ChangePage(picker, state, -1));

        var pageRect = CreateRect(panel, "Page");
        pageRect.anchorMin = new Vector2(0f, 0f);
        pageRect.anchorMax = new Vector2(0f, 0f);
        pageRect.pivot = new Vector2(0f, 0f);
        pageRect.anchoredPosition = new Vector2(358f, 6f);
        pageRect.sizeDelta = new Vector2(116f, 36f);
        state.PageText = pageRect.gameObject.AddComponent<Text>();
        state.PageText.font = font;
        state.PageText.fontSize = 17;
        state.PageText.alignment = TextAnchor.MiddleCenter;
        state.PageText.color = Color.white;
        state.PageText.raycastTarget = false;

        state.NextButton = CreateButton(
            panel,
            "Next",
            ">",
            new Vector2(480f, 6f),
            new Vector2(54f, 36f),
            font,
            () => ChangePage(picker, state, 1));

        state.Controls = panel.gameObject;
        state.ControlsRect = panel;
        panel.SetAsLastSibling();
        PositionControls(picker, state);
    }

    private static void UpdateControls(PickerState state)
    {
        var pageCount = Math.Max(1, (state.FilteredItems.Count + ItemsPerPage - 1) / ItemsPerPage);
        if (state.PageText != null)
            state.PageText.text = (state.Page + 1) + " / " + pageCount + "   (" + state.FilteredItems.Count + ")";
        if (state.PreviousButton != null)
            state.PreviousButton.interactable = pageCount > 1;
        if (state.NextButton != null)
            state.NextButton.interactable = pageCount > 1;
        if (state.Controls != null)
            state.Controls.transform.SetAsLastSibling();
    }

    private static void ApplyFilter(DropdownGrid picker, PickerState state, string value)
    {
        value = (value ?? "").Trim();
        if (string.Equals(state.Filter, value, StringComparison.OrdinalIgnoreCase))
            return;

        state.Filter = value;
        state.Page = 0;
        if (value.Length == 0)
        {
            state.FilteredItems = state.Items;
        }
        else
        {
            var filtered = new List<Thing>();
            for (var i = 0; i < state.Items.Count; i++)
            {
                var thing = state.Items[i];
                if (MatchesFilter(thing, value))
                    filtered.Add(thing);
            }
            state.FilteredItems = filtered;
        }

        RebuildPage(picker, state);
    }

    private static bool MatchesFilter(Thing? thing, string filter)
    {
        if (thing == null)
            return false;
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

    private static void RebuildPage(DropdownGrid picker, PickerState state)
    {
        if (state.Rebuilding)
            return;

        var page = GetPage(state);
        state.Rebuilding = true;
        try
        {
            if (page.Count == 0)
            {
                picker.listDrop.Clear();
                picker.listDrop.Refresh();
                picker.rectDrop.SetActive(true);
            }
            else
            {
                picker.Activate(state.Ingredient, page);
            }
        }
        finally
        {
            state.Rebuilding = false;
        }

        EnsureControls(picker, state);
        UpdateControls(state);
        ClampPickerContentToScreen(picker);
        PositionControls(picker, state);
    }

    private static void PositionControls(DropdownGrid picker, PickerState state)
    {
        var panel = state.ControlsRect;
        if (panel == null || picker.rectDrop == null || picker.rectDropContent == null)
            return;

        try
        {
            Bounds bounds;
            if (state.FilteredItems.Count > 0 &&
                TryGetCandidateGridBounds(picker, picker.rectDrop, out var currentBounds))
            {
                bounds = currentBounds;
                state.GridBounds = currentBounds;
                state.HasGridBounds = true;
            }
            else if (state.HasGridBounds)
            {
                bounds = state.GridBounds;
            }
            else
            {
                bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                    picker.rectDrop,
                    picker.rectDropContent);
            }

            state.GridWidth = Mathf.Max(state.GridWidth, bounds.size.x);
            var panelWidth = Mathf.Max(420f, state.GridWidth);
            panel.sizeDelta = new Vector2(panelWidth, 48f);
            LayoutControls(state, panelWidth);

            var x = bounds.center.x;
            var y = bounds.max.y + panel.rect.height * 0.5f + 2f;
            panel.localPosition = new Vector3(x, y, 0f);

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
                var below = bounds.min.y - panel.rect.height * 0.5f - 2f;
                panel.localPosition = new Vector3(x, below, 0f);
                panel.GetWorldCorners(corners);
            }
            if (corners[0].y < margin)
                delta.y += margin - corners[0].y;
            panel.position += delta;
            panel.SetAsLastSibling();
        }
        catch
        {
        }
    }

    private static void LayoutControls(PickerState state, float panelWidth)
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

    private static bool TryGetCandidateGridBounds(
        DropdownGrid picker,
        RectTransform relativeTo,
        out Bounds bounds)
    {
        bounds = default;
        if (picker.listDrop == null || relativeTo == null)
            return false;

        try
        {
            var buttons = picker.listDrop.GetComponentsInChildren<ButtonGrid>(true);
            if (buttons == null || buttons.Length == 0)
                return false;

            var corners = new Vector3[4];
            var hasBounds = false;
            var min = new Vector3(float.MaxValue, float.MaxValue, 0f);
            var max = new Vector3(float.MinValue, float.MinValue, 0f);
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null || !button.gameObject.activeInHierarchy ||
                    button.transform is not RectTransform rect)
                    continue;

                rect.GetWorldCorners(corners);
                for (var c = 0; c < corners.Length; c++)
                {
                    var local = relativeTo.InverseTransformPoint(corners[c]);
                    min.x = Mathf.Min(min.x, local.x);
                    min.y = Mathf.Min(min.y, local.y);
                    max.x = Mathf.Max(max.x, local.x);
                    max.y = Mathf.Max(max.y, local.y);
                    hasBounds = true;
                }
            }

            if (!hasBounds)
                return false;
            bounds = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ClampPickerContentToScreen(DropdownGrid picker)
    {
        if (picker.rectDropContent == null)
            return;

        try
        {
            var rects = picker.rectDropContent.GetComponentsInChildren<RectTransform>(true);
            if (rects == null || rects.Length == 0)
                return;

            var corners = new Vector3[4];
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            for (var i = 0; i < rects.Length; i++)
            {
                var rect = rects[i];
                if (rect == null || !rect.gameObject.activeInHierarchy)
                    continue;
                rect.GetWorldCorners(corners);
                for (var c = 0; c < 4; c++)
                {
                    minX = Math.Min(minX, corners[c].x);
                    maxX = Math.Max(maxX, corners[c].x);
                    minY = Math.Min(minY, corners[c].y);
                    maxY = Math.Max(maxY, corners[c].y);
                }
            }

            if (minX == float.MaxValue)
                return;
            const float margin = 8f;
            var shift = Vector3.zero;
            shift.x -= 10f;
            if (maxX > Screen.width - margin)
                shift.x -= maxX - (Screen.width - margin);
            if (minX + shift.x < margin)
                shift.x += margin - (minX + shift.x);
            if (maxY > Screen.height - margin)
                shift.y -= maxY - (Screen.height - margin);
            if (minY + shift.y < margin)
                shift.y += margin - (minY + shift.y);
            picker.rectDropContent.position += shift;
        }
        catch
        {
        }
    }

    internal static RectTransform CreateRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    internal static LGuiSafeInputField CreateFilterInput(
        Transform parent,
        Vector2 position,
        Vector2 size,
        Font font,
        Action<string> onChanged)
    {
        var rect = CreateRect(parent, "Filter");
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.015f, 0.018f, 0.024f, 1f);
        ElinModifierPlugin.RegisterCraftIngredientPickerRoundedImage(image);

        var input = rect.gameObject.AddComponent<LGuiSafeInputField>();
        input.targetGraphic = image;
        input.lineType = InputField.LineType.SingleLine;

        var textRect = CreateRect(rect, "Text");
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 3f);
        textRect.offsetMax = new Vector2(-10f, -3f);
        var text = textRect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 16;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        var placeholderRect = CreateRect(rect, "Placeholder");
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(10f, 3f);
        placeholderRect.offsetMax = new Vector2(-10f, -3f);
        var placeholder = placeholderRect.gameObject.AddComponent<Text>();
        placeholder.font = font;
        placeholder.fontSize = 16;
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.alignment = TextAnchor.MiddleLeft;
        placeholder.color = new Color(0.62f, 0.64f, 0.68f, 1f);
        placeholder.text = ElinModifierPlugin.ActiveInstance?.TranslateModuleText(
            "过滤物品名 / ID",
            "Filter item name / ID") ?? "过滤物品名 / ID";
        placeholder.raycastTarget = false;

        input.textComponent = text;
        input.EnableSafeLabelUpdates();
        input.placeholder = placeholder;
        input.onValueChanged.AddListener(value => onChanged(value));
        return input;
    }

    internal static Button CreateButton(
        Transform parent,
        string name,
        string label,
        Vector2 position,
        Vector2 size,
        Font font,
        Action action)
    {
        var rect = CreateRect(parent, name);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.16f, 0.18f, 0.22f, 1f);
        ElinModifierPlugin.RegisterCraftIngredientPickerRoundedImage(image);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());

        var textRect = CreateRect(rect, "Text");
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textRect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 20;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
        text.raycastTarget = false;
        return button;
    }

    private static Font FindPickerFont(DropdownGrid picker)
    {
        try
        {
            var existing = picker.rectDrop.GetComponentInChildren<Text>(true);
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

