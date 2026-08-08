using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{

    private float CreateLGuiNpcEquipmentGrid(
        RectTransform content,
        IReadOnlyList<NpcEquipmentEntry> equipment,
        float y,
        LGuiNpcTooltipView tooltip)
    {
        if (equipment == null || equipment.Count == 0)
            return CreateLGuiNpcInfoEmptyState(
                content,
                "NpcEquipmentEmpty",
                T("无固有装备", "No fixed equipment"),
                y);

        const int columnCount = 4;
        const float rowHeight = 42f;
        const float rowStep = 46f;
        for (var start = 0; start < equipment.Count; start += columnCount)
        {
            var rowIndex = start / columnCount;
            var row = CreateLGuiNpcInfoRow(content, "NpcEquipmentRow" + rowIndex, rowIndex, y, rowHeight, false);
            for (var column = 0; column < columnCount && start + column < equipment.Count; column++)
            {
                var entry = equipment[start + column];
                var cellX = Mathf.Round(1360f * column / columnCount);
                var cellRight = Mathf.Round(1360f * (column + 1) / columnCount);
                var cellWidth = cellRight - cellX;
                var hasIcon = CreateLGuiNpcEquipmentIcon(row, entry, cellX + 10f, 5f, 32f);
                var displayText = entry.Name;
                if (entry.Quantity > 1)
                    displayText += " ×" + entry.Quantity.ToString(CultureInfo.InvariantCulture);
                var slotName = GetLGuiNpcEquipmentSlotName(entry);
                if (!string.IsNullOrWhiteSpace(slotName))
                    displayText += " [" + slotName + "]";
                var valueText = CreateLGuiNpcInfoCell(
                    row,
                    displayText,
                    cellX + 50f,
                    0f,
                    cellWidth - 60f,
                    rowHeight,
                    TextAnchor.MiddleLeft,
                    14);
                var textWidth = Mathf.Clamp(
                    Mathf.Ceil(valueText.preferredWidth) + 2f,
                    1f,
                    cellWidth - 60f);
                var textHeight = Mathf.Clamp(
                    Mathf.Ceil(valueText.preferredHeight) + 4f,
                    18f,
                    rowHeight);
                BindLGuiNpcTemplateTooltip(
                    row,
                    tooltip,
                    entry.Name,
                    BuildLGuiNpcEquipmentTooltip(entry),
                    hasIcon,
                    cellX + 10f,
                    5f,
                    32f,
                    cellX + 50f,
                    (rowHeight - textHeight) * 0.5f,
                    textWidth,
                    textHeight,
                    start + column);
            }
            y += rowStep;
        }
        return y;
    }

    private bool CreateLGuiNpcEquipmentIcon(
        RectTransform row,
        NpcEquipmentEntry entry,
        float x,
        float y,
        float size)
    {
        try
        {
            if (entry.Item == null)
                return false;
            var iconRect = CreateLGuiRect(row, "NpcEquipmentIcon" + entry.Id);
            PlaceLGuiRect(iconRect, x, y, size, size);
            var image = iconRect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
            image.sprite = entry.Item.GetSprite(0);
            return image.sprite != null;
        }
        catch
        {
            return false;
        }
    }

    private string BuildLGuiNpcEquipmentTooltip(NpcEquipmentEntry entry)
    {
        var item = entry.Item;
        if (item == null)
            return T("未提供描述", "No description is available.");

        var lines = new List<string>();
        var slotName = GetLGuiNpcEquipmentSlotName(entry);
        if (!string.IsNullOrWhiteSpace(slotName))
            lines.Add(T("装备位置", "Equipment position") + " : " + slotName);
        lines.Add("ID : " + entry.Id);
        lines.Add(
            T("等级", "Level") + " : " + SafeInt(() => item.LV).ToString(CultureInfo.InvariantCulture) +
            "  |  " + T("稀有度", "Rarity") + " : " + GetLGuiNpcEquipmentRarity(item));
        var material = SafeText(() => item.material.name_L, "-");
        var weight = SafeText(
            () => Lang._weight(item.SelfWeight, true, 0),
            item.SelfWeight.ToString(CultureInfo.InvariantCulture));
        lines.Add(
            T("材质", "Material") + " : " + material +
            "  |  " + T("重量", "Weight") + " : " + weight);

        var stats = new List<string>();
        AddLGuiNpcEquipmentStat(stats, "HIT", SafeInt(() => item.HIT));
        AddLGuiNpcEquipmentStat(stats, "DMG", SafeInt(() => item.DMG));
        AddLGuiNpcEquipmentStat(stats, "DV", SafeInt(() => item.DV));
        AddLGuiNpcEquipmentStat(stats, "PV", SafeInt(() => item.PV));
        var enchantmentLevel = SafeInt(() => item.encLV);
        if (enchantmentLevel != 0)
            stats.Add(T("强化", "Enhancement") + " : " + enchantmentLevel.ToString(CultureInfo.InvariantCulture));
        if (stats.Count > 0)
            lines.Add(string.Join("  |  ", stats));

        var detail = SafeText(() => item.source.detail_L, "");
        if (!string.IsNullOrWhiteSpace(detail))
            lines.Add(detail);

        var effects = new List<(int Sort, int Id, string Text)>();
        try
        {
            foreach (var element in item.elements.dict.Values)
            {
                if (element == null || element.id == 64 || element.id == 65 ||
                    element.id == 66 || element.id == 67)
                    continue;
                var value = item.elements.ValueWithoutLink(element.id);
                if (value == 0)
                    continue;
                SourceElement.Row source;
                try { source = element.source; }
                catch { continue; }
                if (source == null)
                    continue;
                var name = GetElementDisplayName(source);
                if (string.IsNullOrWhiteSpace(name))
                    name = source.alias ?? element.id.ToString(CultureInfo.InvariantCulture);
                effects.Add((source.sort, element.id,
                    name + " : " + value.ToString(CultureInfo.InvariantCulture)));
            }
        }
        catch
        {
        }
        if (effects.Count > 0)
        {
            lines.Add(T("属性与效果", "Stats and effects") + " :");
            effects.Sort((left, right) =>
            {
                var bySort = left.Sort.CompareTo(right.Sort);
                return bySort != 0 ? bySort : left.Id.CompareTo(right.Id);
            });
            for (var i = 0; i < effects.Count; i++)
                lines.Add("• " + effects[i].Text);
        }
        return string.Join("\n", lines);
    }

    private string GetLGuiNpcEquipmentSlotName(NpcEquipmentEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SlotName))
            return entry.SlotName;
        if (entry.IsRanged)
            return T("远程武器", "Ranged weapon");
        if (entry.IsCarried)
            return T("持有装备", "Carried equipment");
        return "";
    }

    private static void AddLGuiNpcEquipmentStat(List<string> stats, string name, int value)
    {
        if (value != 0)
            stats.Add(name + " : " + value.ToString(CultureInfo.InvariantCulture));
    }

    private string GetLGuiNpcEquipmentRarity(Thing item)
    {
        switch (SafeInt(() => (int)item.rarity))
        {
            case int value when value <= (int)Rarity.Crude:
                return T("低级", "Poor");
            case (int)Rarity.Superior:
                return T("高级", "Superior");
            case (int)Rarity.Legendary:
                return T("奇迹", "Miracle");
            case (int)Rarity.Mythical:
                return T("神器", "Godly");
            case int value when value >= (int)Rarity.Artifact:
                return T("古遗物", "Artifact");
            default:
                return T("普通", "Standard");
        }
    }
}
