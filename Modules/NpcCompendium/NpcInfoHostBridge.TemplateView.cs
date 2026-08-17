using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{

    private float CreateLGuiNpcTemplateValueGrid(
        RectTransform content,
        string rowPrefix,
        IReadOnlyList<NpcTemplateValue> values,
        float y,
        LGuiNpcTooltipView? tooltip,
        bool enableTooltip,
        int fixedColumnCount,
        bool displayRandomRangeOnly = false)
    {
        if (values == null || values.Count == 0)
            return CreateLGuiNpcInfoEmptyState(content, rowPrefix + "Empty", T("无", "None"), y);

        var columnCount = fixedColumnCount > 0
            ? Mathf.Clamp(fixedColumnCount, 1, 4)
            : values.Any(entry => entry.HasRandomRange) ? 2 : 4;
        const float rowHeight = 42f;
        const float rowStep = 46f;
        for (var start = 0; start < values.Count; start += columnCount)
        {
            var rowIndex = start / columnCount;
            var row = CreateLGuiNpcInfoRow(content, rowPrefix + "Row" + rowIndex, rowIndex, y, rowHeight, false);
            for (var column = 0; column < columnCount && start + column < values.Count; column++)
            {
                var entry = values[start + column];
                var cellX = Mathf.Round(1360f * column / columnCount);
                var cellRight = Mathf.Round(1360f * (column + 1) / columnCount);
                var cellWidth = cellRight - cellX;
                var hasIcon = CreateLGuiNpcTemplateValueIcon(row, entry, cellX + 10f, 6f, 30f);
                var label = entry.Name;
                var formattedValue = FormatLGuiNpcTemplateDisplayValue(entry, displayRandomRangeOnly);
                if (entry.IsResistance)
                {
                    if (_language == "zh")
                    {
                        if (!label.EndsWith("抗性", StringComparison.Ordinal))
                            label += "抗性";
                    }
                    else if (label.IndexOf("resistance", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        label += " resistance";
                    }
                }
                var displayText = label + " : " + formattedValue;
                var valueText = CreateLGuiNpcInfoCell(
                    row,
                    displayText,
                    cellX + 48f,
                    0f,
                    cellWidth - 58f,
                    rowHeight,
                    TextAnchor.MiddleLeft,
                    14);
                if (enableTooltip && tooltip != null)
                {
                    var textWidth = Mathf.Clamp(
                        Mathf.Ceil(valueText.preferredWidth) + 2f,
                        1f,
                        cellWidth - 58f);
                    var textHeight = Mathf.Clamp(
                        Mathf.Ceil(valueText.preferredHeight) + 4f,
                        18f,
                        rowHeight);
                    var body = string.IsNullOrWhiteSpace(entry.TooltipText)
                        ? T("未提供描述", "No description is available.")
                        : entry.TooltipText;
                    BindLGuiNpcTemplateTooltip(
                        row,
                        tooltip,
                        entry.Name,
                        body,
                        hasIcon,
                        cellX + 10f,
                        6f,
                        30f,
                        cellX + 48f,
                        (rowHeight - textHeight) * 0.5f,
                        textWidth,
                        textHeight,
                        start + column);
                }
            }
            y += rowStep;
        }
        return y;
    }

    private static NpcTemplateValue CreateLGuiNpcTemplateValue(
        NpcTemplateInfo template,
        int id,
        string name,
        int value,
        bool isWeight = false)
    {
        var result = new NpcTemplateValue
        {
            Id = id,
            Name = name,
            Value = value,
            IsWeight = isWeight,
            TooltipText = GetLGuiNpcElementDescription(id)
        };
        if (isWeight)
        {
            result.HasRandomRange = template.WeightLimitHasRandomRange;
            result.RandomMinimum = template.WeightLimitRandomMinimum;
            result.RandomMaximum = template.WeightLimitRandomMaximum;
        }
        else if (template.RandomRanges.TryGetValue(id, out var range))
        {
            result.HasRandomRange = true;
            result.RandomMinimum = range.Minimum;
            result.RandomMaximum = range.Maximum;
        }
        return result;
    }

    private LGuiNpcTooltipView CreateLGuiNpcTemplateTooltip(RectTransform modal)
    {
        var panel = CreateLGuiRect(modal, "NpcTemplateTooltip");
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(180f, 70f);
        var background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.025f, 0.03f, 0.04f, 1f);
        background.raycastTarget = false;
        RegisterLGuiRoundedImage(background);
        var group = panel.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        var fade = panel.gameObject.AddComponent<LGuiFadeDriver>();
        fade.Initialize(group);
        var title = CreateLGuiText(panel, "Title", "", 15, TextAnchor.UpperLeft, FontStyle.Normal);
        title.raycastTarget = false;
        title.color = new Color(0.78f, 0.94f, 0.91f, 1f);
        var body = CreateLGuiText(panel, "Body", "", 13, TextAnchor.UpperLeft, FontStyle.Normal);
        body.raycastTarget = false;
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Truncate;
        var view = panel.gameObject.AddComponent<LGuiNpcTooltipView>();
        view.Initialize(modal, panel, _lGuiCanvas, group, fade, title, body);
        panel.gameObject.SetActive(false);
        return view;
    }

    private void BindLGuiNpcTemplateTooltip(
        RectTransform row,
        LGuiNpcTooltipView tooltip,
        string title,
        string body,
        bool hasIcon,
        float iconX,
        float iconY,
        float iconSize,
        float textX,
        float textY,
        float textWidth,
        float textHeight,
        int index)
    {
        var suffix = index.ToString(CultureInfo.InvariantCulture);
        if (hasIcon)
        {
            CreateLGuiNpcTemplateTooltipTarget(
                row,
                tooltip,
                title,
                body,
                "NpcTemplateTooltipIconTarget" + suffix,
                iconX,
                iconY,
                iconSize,
                iconSize);
        }
        CreateLGuiNpcTemplateTooltipTarget(
            row,
            tooltip,
            title,
            body,
            "NpcTemplateTooltipTextTarget" + suffix,
            textX,
            textY,
            textWidth,
            textHeight);
    }

    private void CreateLGuiNpcTemplateTooltipTarget(
        RectTransform row,
        LGuiNpcTooltipView tooltip,
        string title,
        string body,
        string name,
        float x,
        float y,
        float width,
        float height)
    {
        var target = CreateLGuiRect(row, name);
        PlaceLGuiRect(target, x, y, width, height);
        var image = target.gameObject.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;
        var hover = target.gameObject.AddComponent<LGuiNpcTooltipTarget>();
        hover.Initialize(tooltip, title, body);
    }

    private static string GetLGuiNpcElementDescription(int id)
    {
        try
        {
            if (GameAccess.Sources.Elements?.map != null &&
                GameAccess.Sources.Elements.map.TryGetValue(id, out var source))
                return source.GetDetail() ?? "";
        }
        catch
        {
        }
        return "";
    }

    private static string FormatLGuiNpcTemplateRangeValue(NpcTemplateValue entry, int value)
    {
        return entry.IsWeight
            ? (value / 1000f).ToString("0.0", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private string FormatLGuiNpcTemplateDisplayValue(NpcTemplateValue entry, bool displayRandomRangeOnly)
    {
        if (displayRandomRangeOnly && entry.HasRandomRange)
        {
            var minimum = FormatLGuiNpcTemplateRangeValue(entry, entry.RandomMinimum);
            var maximum = FormatLGuiNpcTemplateRangeValue(entry, entry.RandomMaximum);
            return entry.RandomMinimum == entry.RandomMaximum ? minimum : minimum + "~" + maximum;
        }
        var value = entry.IsResistance
            ? FormatLGuiNpcResistance(entry.Value)
            : entry.IsWeight
                ? (entry.Value / 1000f).ToString("0.0", CultureInfo.InvariantCulture)
                : entry.Value.ToString(CultureInfo.InvariantCulture);
        if (!entry.HasRandomRange)
            return value;
        var rangeMinimum = FormatLGuiNpcTemplateRangeValue(entry, entry.RandomMinimum);
        var rangeMaximum = FormatLGuiNpcTemplateRangeValue(entry, entry.RandomMaximum);
        return value + " [" + rangeMinimum + "~" + rangeMaximum + "]";
    }

    private bool CreateLGuiNpcTemplateValueIcon(
        RectTransform parent,
        NpcTemplateValue entry,
        float x,
        float y,
        float size)
    {
        var image = CreateLGuiImage(parent, "TemplateElementIcon" + entry.Id, x, y, size, size);
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
        try
        {
            var element = Element.Create(entry.Id, entry.Value);
            image.sprite = element?.GetIcon("");
            if (image.sprite == null && GameAccess.Sources.Elements?.map != null &&
                GameAccess.Sources.Elements.map.TryGetValue(entry.Id, out var source))
                image.sprite = source.GetSprite();
        }
        catch
        {
            image.sprite = null;
        }
        var hasSprite = image.sprite != null;
        image.gameObject.SetActive(hasSprite);
        return hasSprite;
    }

    private string FormatLGuiNpcResistance(int value)
    {
        var level = Element.GetResistLv(value);
        try
        {
            var labels = Lang.GetList(level > 0 ? "resist" : "resistNeg");
            var index = level > 0 ? Math.Min(level, 5) : -level;
            if (labels != null && index >= 0 && index < labels.Length && !string.IsNullOrWhiteSpace(labels[index]))
                return labels[index] + "(" + value.ToString(CultureInfo.InvariantCulture) + ")";
        }
        catch
        {
        }
        var fallback = level == 0 ? T("无", "None") : T("等级", "Level") + " " + level.ToString(CultureInfo.InvariantCulture);
        return fallback + "(" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
