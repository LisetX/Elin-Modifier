using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private Color GetLGuiRowColor(int index, bool header = false)
    {
        if (_uiStyleIndex == 5)
        {
            if (header)
                return index % 2 == 0 ? new Color(0.79f, 0.80f, 0.77f, 1f) : new Color(0.72f, 0.74f, 0.71f, 1f);
            return index % 2 == 0 ? new Color(0.92f, 0.92f, 0.89f, 1f) : new Color(0.84f, 0.85f, 0.81f, 1f);
        }
        var accent = _uiStyleIndex >= 0 && _uiStyleIndex < UiStyleColors.Length ? UiStyleColors[_uiStyleIndex] : Color.white;
        if (header)
        {
            var headerBase = index % 2 == 0
                ? new Color(0.095f, 0.108f, 0.132f, 1f)
                : new Color(0.135f, 0.15f, 0.18f, 1f);
            return Color.Lerp(headerBase, accent, index % 2 == 0 ? 0.17f : 0.22f);
        }
        var baseColor = index % 2 == 0
            ? new Color(0.055f, 0.062f, 0.075f, 1f)
            : new Color(0.095f, 0.105f, 0.126f, 1f);
        return Color.Lerp(baseColor, accent, index % 2 == 0 ? 0.06f : 0.10f);
    }
    private void ApplyLGuiRowVisual(LGuiRowView view, int index, bool header = false)
    {
        view.Background.color = GetLGuiRowColor(index, header);
        var lightTheme = _uiStyleIndex == 5;
        var accent = _uiStyleIndex >= 0 && _uiStyleIndex < UiStyleColors.Length
            ? UiStyleColors[_uiStyleIndex]
            : Color.white;
        view.Accent.gameObject.SetActive(header);
        if (header)
            view.Accent.color = lightTheme
                ? Color.Lerp(accent, new Color(0.24f, 0.27f, 0.25f, 1f), 0.32f)
                : Color.Lerp(accent, Color.white, 0.12f);
        view.Separator.color = lightTheme
            ? new Color(0.48f, 0.50f, 0.47f, 0.72f)
            : Color.Lerp(new Color(0.18f, 0.20f, 0.24f, 0.78f), accent, 0.16f);
    }
    private RectTransform CreateLGuiVirtualRow(RectTransform parent)
    {
        return CreateLGuiVirtualRow(parent, false);
    }
    private RectTransform CreateLGuiCharacterVirtualRow(RectTransform parent)
    {
        return CreateLGuiVirtualRow(parent, true);
    }
    private RectTransform CreateLGuiVirtualRow(RectTransform parent, bool includeChoices)
    {
        var rect = CreateLGuiRect(parent, "VirtualRow");
        var background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(0.09f, 0.095f, 0.11f, 0.98f);
        RegisterLGuiRoundedImage(background);
        var view = rect.gameObject.AddComponent<LGuiRowView>();
        view.Rect = rect;
        view.Background = background;

        view.Accent = CreateLGuiImage(rect, "RowAccent", 0f, 6f, 4f, 46f);
        view.Accent.raycastTarget = false;
        var separatorRect = CreateLGuiRect(rect, "RowSeparator");
        separatorRect.anchorMin = new Vector2(0f, 0f);
        separatorRect.anchorMax = new Vector2(1f, 0f);
        separatorRect.pivot = new Vector2(0.5f, 0f);
        separatorRect.offsetMin = new Vector2(10f, 0f);
        separatorRect.offsetMax = new Vector2(-10f, 1f);
        view.Separator = separatorRect.gameObject.AddComponent<Image>();
        view.Separator.raycastTarget = false;

        view.Favorite = CreateLGuiButton(rect, "Favorite", "☆", 8f, 8f, 42f, 42f, null);
        view.FavoriteText = view.Favorite.GetComponentInChildren<Text>();
        var favoriteBackground = view.Favorite.GetComponent<Image>();
        if (favoriteBackground != null)
            favoriteBackground.enabled = false;
        view.Favorite.targetGraphic = view.FavoriteText;
        view.FavoriteText.raycastTarget = true;
        view.FavoriteText.fontSize = 24;
        var favoriteTextProfile = view.FavoriteText.GetComponent<LGuiTextProfile>();
        if (favoriteTextProfile != null)
            favoriteTextProfile.BaseFontSize = 24;
        view.Favorite.gameObject.SetActive(false);
        view.Icon = CreateLGuiImage(rect, "Icon", 8f, 8f, 42f, 42f);
        view.Label = CreateLGuiText(rect, "Label", "", 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(view.Label.rectTransform, 60f, 4f, 410f, 50f);
        view.Secondary = CreateLGuiText(rect, "Secondary", "", 15, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(view.Secondary.rectTransform, 480f, 4f, 250f, 50f);
        view.Input = CreateLGuiInput(rect, "Input", "", 742f, 8f, 160f, 42f);
        view.Toggle = CreateLGuiToggle(rect, "Toggle", 914f, 8f, 150f, 42f, out var toggleLabel);
        view.ToggleLabel = toggleLabel;
        view.Primary = CreateLGuiButton(rect, "Primary", T("应用", "Apply"), 1080f, 8f, 130f, 42f, null);
        view.PrimaryText = view.Primary.GetComponentInChildren<Text>();
        view.Auxiliary = CreateLGuiButton(rect, "Auxiliary", T("锁定", "Lock"), 1220f, 8f, 130f, 42f, null);
        view.AuxiliaryText = view.Auxiliary.GetComponentInChildren<Text>();
        if (includeChoices)
        {
            view.Dropdown = CreateAutomationDropdown(rect, "RowDropdown", new[] { "", "", "", "", "" }, 0, 830f, 8f, 320f, 42f, view.HandleDropdownSelection);
            view.Dropdown.gameObject.SetActive(false);
            view.Choices = new Button[4];
            view.ChoiceTexts = new Text[4];
            for (var i = 0; i < view.Choices.Length; i++)
            {
                var choice = CreateLGuiButton(rect, "Choice" + i.ToString(CultureInfo.InvariantCulture), "", 480f + i * 214f, 8f, 202f, 42f, null);
                view.Choices[i] = choice;
                view.ChoiceTexts[i] = choice.GetComponentInChildren<Text>();
                choice.gameObject.SetActive(false);
            }
        }
        view.Initialize(this);
        return rect;
    }
    private static void HideLGuiRowChoices(LGuiRowView view)
    {
        for (var i = 0; i < view.Choices.Length; i++)
            view.Choices[i].gameObject.SetActive(false);
    }
    private static void ConfigureLGuiRowChoice(LGuiRowView view, int choiceIndex, string label, float x, float width)
    {
        if (choiceIndex < 0 || choiceIndex >= view.Choices.Length)
            return;
        var button = view.Choices[choiceIndex];
        PlaceLGuiRect((RectTransform)button.transform, x, 8f, width, 42f);
        view.ChoiceTexts[choiceIndex].text = label ?? "";
        button.gameObject.SetActive(true);
    }
    private void BindLGuiCharacterChoiceRow(LGuiRowView view, LGuiCharacterRow model, int index)
    {
        ApplyLGuiRowVisual(view, index);
        view.Label.fontStyle = FontStyle.Normal;
        view.Label.text = IndentLGuiText(model.Header, model.Depth);
        view.Secondary.gameObject.SetActive(false);
        view.Input.gameObject.SetActive(false);
        view.Toggle.gameObject.SetActive(false);
        view.Primary.gameObject.SetActive(false);
        view.Auxiliary.gameObject.SetActive(false);
        HideLGuiRowChoices(view);

        var target = model.Target;
        if (target == null)
            return;

        if (model.Action == LGuiCharacterAction.NpcRelationshipChoices)
        {
            var selected = GetRelationshipIndex(target);
            for (var i = 0; i < RelationshipOptions.Length && i < view.Choices.Length; i++)
            {
                var label = GetRelationshipLabel(RelationshipOptions[i]);
                ConfigureLGuiRowChoice(view, i, (i == selected ? "-> " : "") + label, 480f + i * 214f, 202f);
            }
            return;
        }

        if (model.Action == LGuiCharacterAction.NpcPartyChoices)
        {
            var labels = new[]
            {
                T("加入队伍", "Join party"),
                T("离开队伍", "Leave party"),
                T("仅加入阵营", "Join faction"),
                T("仅退出阵营", "Leave faction")
            };
            var selected = -1;
            try
            {
                selected = target.IsPCParty ? 0 : (target.IsPCFaction ? 2 : 3);
            }
            catch { }
            for (var i = 0; i < labels.Length && i < view.Choices.Length; i++)
                ConfigureLGuiRowChoice(view, i, (i == selected ? "-> " : "") + labels[i], 480f + i * 214f, 202f);
            return;
        }

        if (model.Action == LGuiCharacterAction.NpcFaithChoices)
        {
            EnsureFaithRows();
            var faiths = _faithRows ?? new List<FaithDef>();
            var currentFaithId = SafeText(() => target.idFaith, "");
            const int perRow = 3;
            for (var i = 0; i < perRow && i < view.Choices.Length; i++)
            {
                var faithIndex = model.ActionIndex + i;
                if (faithIndex < 0 || faithIndex >= faiths.Count)
                    break;
                var faith = faiths[faithIndex];
                var selected = string.Equals(currentFaithId, faith.Id, StringComparison.OrdinalIgnoreCase);
                ConfigureLGuiRowChoice(view, i, (selected ? "-> " : "") + GetLGuiFaithButtonLabel(faith), 480f + i * 284f, 272f);
            }
        }
    }
}
