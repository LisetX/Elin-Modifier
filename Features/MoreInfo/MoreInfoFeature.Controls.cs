using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private void SetShowItemMoreInfo(bool enabled)
    {
        _showItemMoreInfo = enabled;
        InvalidateItemMoreInfoCache();
        try
        {
            if (WidgetMouseover.Instance != null)
                ConfigureNpcMoreInfoHoverDirection(WidgetMouseover.Instance, enabled: false);
        }
        catch { }
        _log = enabled
            ? T("显示物品更多信息已开启", "Show more item info enabled")
            : T("显示物品更多信息已关闭", "Show more item info disabled");
    }
    private void SetShowBuffSpecificValues(bool enabled)
    {
        _showBuffSpecificValues = enabled;
        try
        {
            WidgetStats.RefreshAll();
        }
        catch { }
        _log = enabled
            ? T("显示Buff具体信息已开启", "Detailed Buff information enabled")
            : T("显示Buff具体信息已关闭", "Detailed Buff information disabled");
    }
    private void SetShowBuffSpecificValuesIconFontSizeOffset(int value)
    {
        value = Clamp(value, -8, 8);
        if (_showBuffSpecificValuesIconFontSizeOffset == value)
            return;
        _showBuffSpecificValuesIconFontSizeOffset = value;
        try { WidgetStats.RefreshAll(); }
        catch { }
    }
    private void SetShowBuffSpecificValuesTextFontSizeOffset(int value)
    {
        value = Clamp(value, -8, 8);
        if (_showBuffSpecificValuesTextFontSizeOffset == value)
            return;
        _showBuffSpecificValuesTextFontSizeOffset = value;
        try { WidgetStats.RefreshAll(); }
        catch { }
    }
    private void SetShowItemPanelEnchantLevels(bool enabled)
    {
        _showItemPanelEnchantLevels = enabled;
        _log = enabled
            ? T("显示物品面板附魔等级已开启", "Item panel enchantment levels enabled")
            : T("显示物品面板附魔等级已关闭", "Item panel enchantment levels disabled");
    }
    private void SetShowItemPanelItemValue(bool enabled)
    {
        _showItemPanelItemValue = enabled;
        _log = enabled
            ? T("显示物品面板物品价值已开启", "Item panel item value enabled")
            : T("显示物品面板物品价值已关闭", "Item panel item value disabled");
    }
    private void SetShowItemPanelMilkBonus(bool enabled)
    {
        _showItemPanelMilkBonus = enabled;
        _log = enabled
            ? T("显示物品面板奶的加成已开启", "Item panel milk bonus enabled")
            : T("显示物品面板奶的加成已关闭", "Item panel milk bonus disabled");
    }
    private void SetShowMainAbilityExperience(bool enabled)
    {
        _showMainAbilityExperience = enabled;
        RefreshMainAbilityExperienceTracker(enabled && _showMainAbilityExperienceInSkillTracker);
        _log = enabled
            ? T("显示主能力经验值已开启", "Main ability experience display enabled")
            : T("显示主能力经验值已关闭", "Main ability experience display disabled");
    }
    private void SetShowMainAbilityExperienceInSkillTracker(bool enabled)
    {
        _showMainAbilityExperienceInSkillTracker = enabled;
        RefreshMainAbilityExperienceTracker(_showMainAbilityExperience && enabled);
        _log = enabled
            ? T("技能追踪器经验值显示已开启", "Skill tracker experience display enabled")
            : T("技能追踪器经验值显示已关闭", "Skill tracker experience display disabled");
    }
    private void SetEquipmentComparison(bool enabled)
    {
        _equipmentComparison = enabled;
        if (!enabled)
            DestroyEquipmentComparisonTooltip();
        _log = enabled
            ? T("装备对比已开启", "Equipment comparison enabled")
            : T("装备对比已关闭", "Equipment comparison disabled");
    }
}
