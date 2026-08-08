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
    private void SetItemMoreInfoFontSizeOffset(int value)
    {
        _showItemMoreInfoFontSizeOffset = Clamp(value, -8, 8);
        InvalidateItemMoreInfoCache();
    }
    private static bool TryNormalizeHoverInfoColor(string? value, out string normalized)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("#", StringComparison.Ordinal))
            text = text.Substring(1);
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            normalized = "#" + text.ToUpperInvariant();
            return true;
        }
        normalized = "";
        return false;
    }
    private static string NormalizeHoverInfoColor(string? value, string fallback)
    {
        return TryNormalizeHoverInfoColor(value, out var normalized) ? normalized : fallback;
    }
    internal static string FormatCompactCount(long value)
    {
        return FormatCompactCount((decimal)value);
    }
    internal static string FormatCompactCount(decimal value)
    {
        var absolute = value < 0m ? -value : value;
        if (absolute < 1000m)
            return value.ToString("0.############################", CultureInfo.InvariantCulture);

        decimal divisor;
        string suffix;
        if (absolute >= 1000000000000m)
        {
            divisor = 1000000000000m;
            suffix = "T";
        }
        else if (absolute >= 1000000000m)
        {
            divisor = 1000000000m;
            suffix = "B";
        }
        else if (absolute >= 1000000m)
        {
            divisor = 1000000m;
            suffix = "M";
        }
        else
        {
            divisor = 1000m;
            suffix = "K";
        }

        return (value / divisor).ToString("0.000", CultureInfo.InvariantCulture) + suffix;
    }
    internal static string FormatCompactNumericText(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return text;

        var index = 0;
        if (text[index] == '+' || text[index] == '-')
            index++;
        var hasDigit = false;
        var hasDecimalPoint = false;
        while (index < text.Length)
        {
            var c = text[index];
            if (c >= '0' && c <= '9')
            {
                hasDigit = true;
                index++;
                continue;
            }
            if (c == ',')
            {
                index++;
                continue;
            }
            if (c == '.' && !hasDecimalPoint)
            {
                hasDecimalPoint = true;
                index++;
                continue;
            }
            break;
        }

        if (!hasDigit)
            return text;
        var numberText = text.Substring(0, index);
        if (!decimal.TryParse(numberText, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return text;
        var absolute = number < 0m ? -number : number;
        if (absolute < 1000m)
            return text;
        return FormatCompactCount(number) + text.Substring(index);
    }
    private void SyncNpcMoreInfoColorInputs()
    {
        _npcMoreInfoLevelColorText = _npcMoreInfoLevelColor;
        _npcMoreInfoIdentityColorText = _npcMoreInfoIdentityColor;
        _npcMoreInfoRelationColorText = _npcMoreInfoRelationColor;
        _npcMoreInfoHpColorText = _npcMoreInfoHpColor;
        _npcMoreInfoMpColorText = _npcMoreInfoMpColor;
        _npcMoreInfoSpColorText = _npcMoreInfoSpColor;
        _npcMoreInfoExpColorText = _npcMoreInfoExpColor;
        _npcMoreInfoSpeedColorText = _npcMoreInfoSpeedColor;
        _npcMoreInfoDvColorText = _npcMoreInfoDvColor;
        _npcMoreInfoPvColorText = _npcMoreInfoPvColor;
        _npcMoreInfoSkillColorText = _npcMoreInfoSkillColor;
        _npcMoreInfoAbilityColorText = _npcMoreInfoAbilityColor;
        _npcMoreInfoFeatColorText = _npcMoreInfoFeatColor;
        _npcMoreInfoCombatColorText = _npcMoreInfoCombatColor;
        _npcMoreInfoResistColorText = _npcMoreInfoResistColor;
        _npcMoreInfoAttributeColorText = _npcMoreInfoAttributeColor;
        _npcMoreInfoBuffColorText = _npcMoreInfoBuffColor;
    }
    private void SyncItemMoreInfoColorInputs()
    {
        _itemMoreInfoBasicInfoColorText = _itemMoreInfoBasicInfoColor;
        _itemMoreInfoGatheringToolColorText = _itemMoreInfoGatheringToolColor;
        _itemMoreInfoGatheringThresholdColorText = _itemMoreInfoGatheringThresholdColor;
        _itemMoreInfoWeaponStatsColorText = _itemMoreInfoWeaponStatsColor;
        _itemMoreInfoEnchantColorText = _itemMoreInfoEnchantColor;
        _itemMoreInfoPlantStatsColorText = _itemMoreInfoPlantStatsColor;
        _itemMoreInfoRarityCrudeColorText = _itemMoreInfoRarityCrudeColor;
        _itemMoreInfoRarityNormalColorText = _itemMoreInfoRarityNormalColor;
        _itemMoreInfoRaritySuperiorColorText = _itemMoreInfoRaritySuperiorColor;
        _itemMoreInfoRarityLegendaryColorText = _itemMoreInfoRarityLegendaryColor;
        _itemMoreInfoRarityMythicalColorText = _itemMoreInfoRarityMythicalColor;
        _itemMoreInfoRarityArtifactColorText = _itemMoreInfoRarityArtifactColor;
    }
    private bool TryApplyNpcMoreInfoColors(out string status)
    {
        var inputs = new[]
        {
            _npcMoreInfoLevelColorText, _npcMoreInfoIdentityColorText, _npcMoreInfoRelationColorText,
            _npcMoreInfoHpColorText, _npcMoreInfoMpColorText, _npcMoreInfoSpColorText,
            _npcMoreInfoExpColorText, _npcMoreInfoSpeedColorText, _npcMoreInfoDvColorText, _npcMoreInfoPvColorText,
            _npcMoreInfoSkillColorText, _npcMoreInfoAbilityColorText, _npcMoreInfoFeatColorText,
            _npcMoreInfoCombatColorText, _npcMoreInfoResistColorText, _npcMoreInfoAttributeColorText, _npcMoreInfoBuffColorText
        };
        var labels = new[]
        {
            T("等级", "Level"), T("身份信息", "Identity"), T("更多身份信息", "Additional identity info"),
            "HP", "MP", "SP", "EXP", T("速度", "Speed"), "DV", "PV", T("技能", "Skills"), T("能力", "Abilities"),
            T("专长", "Feats"), T("交战推演", "Combat Simulation"), T("抗性", "Resistances"),
            T("主属性", "Main Attributes"), "Buff"
        };
        var colors = new string[inputs.Length];
        for (var i = 0; i < inputs.Length; i++)
        {
            if (TryNormalizeHoverInfoColor(inputs[i], out colors[i]))
                continue;
            status = T("颜色格式无效: ", "Invalid color format: ") + labels[i];
            _log = status;
            return false;
        }

        _npcMoreInfoLevelColor = colors[0];
        _npcMoreInfoIdentityColor = colors[1];
        _npcMoreInfoRelationColor = colors[2];
        _npcMoreInfoHpColor = colors[3];
        _npcMoreInfoMpColor = colors[4];
        _npcMoreInfoSpColor = colors[5];
        _npcMoreInfoExpColor = colors[6];
        _npcMoreInfoSpeedColor = colors[7];
        _npcMoreInfoDvColor = colors[8];
        _npcMoreInfoPvColor = colors[9];
        _npcMoreInfoSkillColor = colors[10];
        _npcMoreInfoAbilityColor = colors[11];
        _npcMoreInfoFeatColor = colors[12];
        _npcMoreInfoCombatColor = colors[13];
        _npcMoreInfoResistColor = colors[14];
        _npcMoreInfoAttributeColor = colors[15];
        _npcMoreInfoBuffColor = colors[16];
        SyncNpcMoreInfoColorInputs();
        InvalidateNpcMoreInfoCaches();
        status = T("字体颜色已应用", "Font colors applied");
        _log = status;
        return true;
    }
    private bool TryApplyItemMoreInfoColors(out string status)
    {
        if (!TryNormalizeHoverInfoColor(_itemMoreInfoBasicInfoColorText, out var basicInfoColor))
        {
            status = T("颜色格式无效: ", "Invalid color format: ") + T("基础信息", "Basic info");
            _log = status;
            return false;
        }
        if (!TryNormalizeHoverInfoColor(_itemMoreInfoGatheringToolColorText, out var gatheringToolColor))
        {
            status = T("颜色格式无效: ", "Invalid color format: ") + T("采集工具", "Gathering tool");
            _log = status;
            return false;
        }
        if (!TryNormalizeHoverInfoColor(_itemMoreInfoGatheringThresholdColorText, out var gatheringThresholdColor))
        {
            status = T("颜色格式无效: ", "Invalid color format: ") + T("采集门槛", "Gathering threshold");
            _log = status;
            return false;
        }
        if (!TryNormalizeHoverInfoColor(_itemMoreInfoWeaponStatsColorText, out var weaponStatsColor))
        {
            status = T("颜色格式无效: ", "Invalid color format: ") + T("武器属性", "Weapon stats");
            _log = status;
            return false;
        }
        if (!TryNormalizeHoverInfoColor(_itemMoreInfoEnchantColorText, out var enchantColor))
        {
            status = T("颜色格式无效: ", "Invalid color format: ") + T("附魔内容", "Enchantments");
            _log = status;
            return false;
        }
        if (!TryNormalizeHoverInfoColor(_itemMoreInfoPlantStatsColorText, out var plantStatsColor))
        {
            status = T("颜色格式无效: ", "Invalid color format: ") + T("种植作物属性", "Planted crop stats");
            _log = status;
            return false;
        }
        var rarityInputs = new[]
        {
            _itemMoreInfoRarityCrudeColorText,
            _itemMoreInfoRarityNormalColorText,
            _itemMoreInfoRaritySuperiorColorText,
            _itemMoreInfoRarityLegendaryColorText,
            _itemMoreInfoRarityMythicalColorText,
            _itemMoreInfoRarityArtifactColorText
        };
        var rarityLabels = new[]
        {
            T("低级", "Poor"),
            T("普通", "Standard"),
            T("高级", "Superior"),
            T("奇迹", "Miracle"),
            T("神器", "Godly"),
            T("古遗物", "Artifact")
        };
        var rarityColors = new string[rarityInputs.Length];
        for (var i = 0; i < rarityInputs.Length; i++)
        {
            if (TryNormalizeHoverInfoColor(rarityInputs[i], out rarityColors[i]))
                continue;
            status = T("颜色格式无效: ", "Invalid color format: ") + rarityLabels[i];
            _log = status;
            return false;
        }

        _itemMoreInfoBasicInfoColor = basicInfoColor;
        _itemMoreInfoGatheringToolColor = gatheringToolColor;
        _itemMoreInfoGatheringThresholdColor = gatheringThresholdColor;
        _itemMoreInfoWeaponStatsColor = weaponStatsColor;
        _itemMoreInfoEnchantColor = enchantColor;
        _itemMoreInfoPlantStatsColor = plantStatsColor;
        _itemMoreInfoRarityCrudeColor = rarityColors[0];
        _itemMoreInfoRarityNormalColor = rarityColors[1];
        _itemMoreInfoRaritySuperiorColor = rarityColors[2];
        _itemMoreInfoRarityLegendaryColor = rarityColors[3];
        _itemMoreInfoRarityMythicalColor = rarityColors[4];
        _itemMoreInfoRarityArtifactColor = rarityColors[5];
        SyncItemMoreInfoColorInputs();
        InvalidateItemMoreInfoCache();
        status = T("字体颜色已应用", "Font colors applied");
        _log = status;
        return true;
    }
    private void ResetNpcMoreInfoColors(bool updateLog = true)
    {
        _npcMoreInfoLevelColor = DefaultNpcMoreInfoLevelColor;
        _npcMoreInfoIdentityColor = DefaultNpcMoreInfoIdentityColor;
        _npcMoreInfoRelationColor = DefaultNpcMoreInfoRelationColor;
        _npcMoreInfoHpColor = DefaultNpcMoreInfoHpColor;
        _npcMoreInfoMpColor = DefaultNpcMoreInfoMpColor;
        _npcMoreInfoSpColor = DefaultNpcMoreInfoSpColor;
        _npcMoreInfoExpColor = DefaultNpcMoreInfoExpColor;
        _npcMoreInfoSpeedColor = DefaultNpcMoreInfoSpeedColor;
        _npcMoreInfoDvColor = DefaultNpcMoreInfoDvColor;
        _npcMoreInfoPvColor = DefaultNpcMoreInfoPvColor;
        _npcMoreInfoSkillColor = DefaultNpcMoreInfoSkillColor;
        _npcMoreInfoAbilityColor = DefaultNpcMoreInfoAbilityColor;
        _npcMoreInfoFeatColor = DefaultNpcMoreInfoFeatColor;
        _npcMoreInfoCombatColor = DefaultNpcMoreInfoCombatColor;
        _npcMoreInfoResistColor = DefaultNpcMoreInfoResistColor;
        _npcMoreInfoAttributeColor = DefaultNpcMoreInfoAttributeColor;
        _npcMoreInfoBuffColor = DefaultNpcMoreInfoBuffColor;
        SyncNpcMoreInfoColorInputs();
        InvalidateNpcMoreInfoCaches();
        if (updateLog)
            _log = T("字体颜色已重置", "Font colors reset");
    }
    private void ResetItemMoreInfoColors(bool updateLog = true)
    {
        _itemMoreInfoBasicInfoColor = DefaultItemMoreInfoBasicInfoColor;
        _itemMoreInfoGatheringToolColor = DefaultItemMoreInfoGatheringToolColor;
        _itemMoreInfoGatheringThresholdColor = DefaultItemMoreInfoGatheringThresholdColor;
        _itemMoreInfoWeaponStatsColor = DefaultItemMoreInfoWeaponStatsColor;
        _itemMoreInfoEnchantColor = DefaultItemMoreInfoEnchantColor;
        _itemMoreInfoPlantStatsColor = DefaultItemMoreInfoPlantStatsColor;
        _itemMoreInfoRarityCrudeColor = DefaultItemMoreInfoRarityCrudeColor;
        _itemMoreInfoRarityNormalColor = DefaultItemMoreInfoRarityNormalColor;
        _itemMoreInfoRaritySuperiorColor = DefaultItemMoreInfoRaritySuperiorColor;
        _itemMoreInfoRarityLegendaryColor = DefaultItemMoreInfoRarityLegendaryColor;
        _itemMoreInfoRarityMythicalColor = DefaultItemMoreInfoRarityMythicalColor;
        _itemMoreInfoRarityArtifactColor = DefaultItemMoreInfoRarityArtifactColor;
        SyncItemMoreInfoColorInputs();
        InvalidateItemMoreInfoCache();
        if (updateLog)
            _log = T("字体颜色已重置", "Font colors reset");
    }
    private void SetNpcMoreInfoFontSizeOffset(int value)
    {
        value = Clamp(value, -8, 8);
        if (_showNpcMoreInfoFontSizeOffset == value)
            return;
        _showNpcMoreInfoFontSizeOffset = value;
        InvalidateNpcMoreInfoCaches();
    }
}
