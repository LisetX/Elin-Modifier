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
using static ElinModifierPlugin;

internal sealed partial class MoreInfoModule
{
    private static string BuildItemMoreInfoHoverDetailsUncached(Thing thing)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (thing == null || instance == null)
            return "";

        var lines = new List<string>();
        if (instance._showItemMoreInfoBasicInfo)
        {
            var basicInfo = new List<string>
            {
                BuildItemMoreInfoRarityField(thing),
                BuildItemMoreInfoField(Tr("物品价值", "Item value"), GetItemDataValueText(thing), ItemMoreInfoBasicInfoColor),
                BuildItemMoreInfoField(Tr("重量", "Weight"), SafeText(() => Lang._weight(thing.ChildrenAndSelfWeight), "?"), ItemMoreInfoBasicInfoColor)
            };
            lines.Add(string.Join(" ", basicInfo.ToArray()));
        }

        if (instance._showItemMoreInfoGatheringThreshold)
        {
            var gatheringThreshold = BuildThingGatheringThresholdLine(thing);
            if (!string.IsNullOrEmpty(gatheringThreshold))
                lines.Add(gatheringThreshold);
        }

        if (instance._showItemMoreInfoWeaponStats && CanEditWeaponData(thing))
        {
            var weaponStats = new List<string>
            {
                BuildItemMoreInfoField(Tr("等级", "Level"), SafeInt(() => thing.LV, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("强化", "Enhance"), SafeInt(() => thing.encLV, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("伤害骰面", "Damage dice sides"), SafeInt(() => thing.c_diceDim, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("命中", "Hit"), GetThingElementBase(thing, 66).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("伤害修正", "Damage bonus"), GetThingElementBase(thing, 67).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField("DV", GetThingElementBase(thing, 64).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField("PV", GetThingElementBase(thing, 65).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("充能", "Charges"), SafeInt(() => thing.c_charges, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("弹药", "Ammo"), SafeInt(() => thing.c_ammo, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor)
            };
            AddNpcMoreInfoLine(lines, BuildNpcMoreInfoEntryLines("", weaponStats, 5));
        }

        var enchantments = new List<string>();
        if (instance._showItemMoreInfoEnchantments)
        {
            try
            {
                var rows = new List<Element>();
                foreach (var element in thing.elements.dict.Values)
                {
                    if (element != null && element.id > 0)
                        rows.Add(element);
                }
                rows.Sort((a, b) => a.id.CompareTo(b.id));

                foreach (var element in rows)
                {
                    var value = GetThingElementEditorValue(thing, element);
                    if (value == 0)
                        continue;
                    var name = GetGeneEffectNameStatic(element.id);
                    enchantments.Add(ColorNpcMoreInfoText(name, ItemMoreInfoEnchantColor) +
                                     "(" + FormatCompactCount(value) + ")");
                }
            }
            catch { }
        }

        AddNpcMoreInfoLine(lines, BuildNpcMoreInfoEntryLines("", enchantments, 5));
        if (lines.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.Append(Environment.NewLine);
        sb.Append("<size=").Append(GetItemMoreInfoFontSize().ToString(CultureInfo.InvariantCulture)).Append('>');
        sb.Append(string.Join(Environment.NewLine, lines.ToArray()));
        sb.Append("</size>");
        return sb.ToString();
    }
    internal static string BuildMapGatheringThresholdHoverDetails(Point point)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (instance == null || !instance._showItemMoreInfoGatheringThreshold || point == null)
            return "";

        try
        {
            if (!point.IsValid || !point.HasObj)
                return "";

            var source = point.sourceObj;
            var cell = point.cell;
            var requirements = source?.reqHarvest;
            var material = cell != null && cell.isObjDyed ? source?.DefaultMaterial : cell?.matObj;
            if (source == null || material == null || requirements == null || requirements.Length < 2)
                return "";

            var hpPercent = point.growth != null ? point.growth.GetHp() : source.hp;
            var requiredHardness = GatheringThresholdPolicy.CalculateRequiredHardness(
                material.hardness,
                hpPercent,
                HasGatheringHardMaterialTag(material));
            var line = BuildGatheringThresholdLine(requirements, point.cell.CanHarvest(), requiredHardness);
            return WrapItemMoreInfoLine(line);
        }
        catch
        {
            return "";
        }
    }
    private static string BuildThingGatheringThresholdLine(Thing thing)
    {
        try
        {
            var requirementText = thing?.trait?.ReqHarvest;
            var material = thing?.material;
            if (string.IsNullOrWhiteSpace(requirementText) || material == null)
                return "";

            var requirements = requirementText.Split(',', StringSplitOptions.None);
            if (requirements.Length < 2)
                return "";

            var isHarvest = thing.pos != null && thing.pos.IsValid && thing.pos.cell.CanHarvest();
            var requiredHardness = GatheringThresholdPolicy.CalculateRequiredHardness(
                material.hardness,
                100,
                HasGatheringHardMaterialTag(material));
            return BuildGatheringThresholdLine(requirements, isHarvest, requiredHardness);
        }
        catch
        {
            return "";
        }
    }
    private static string BuildGatheringThresholdLine(string[] requirements, bool isHarvest, int requiredHardness)
    {
        if (requirements == null || requirements.Length < 2)
            return "";

        var skillAlias = isHarvest ? "gathering" : (requirements[0] ?? "").Trim();
        if (string.IsNullOrEmpty(skillAlias) ||
            GameAccess.Sources.Elements?.alias == null ||
            !GameAccess.Sources.Elements.alias.TryGetValue(skillAlias, out var skillRow) ||
            skillRow == null)
        {
            return "";
        }

        var requiredSkill = 0;
        int.TryParse(requirements[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out requiredSkill);
        requiredSkill = GatheringThresholdPolicy.NormalizeRequiredSkillLevel(requiredSkill);
        var currentSkill = Math.Max(0, SafeInt(() => GameAccess.Characters.GetPlayerElementValue(skillRow.id), 0));
        var toolRequired = !isHarvest && skillRow.id != 250;
        var tool = toolRequired ? GetCurrentGatheringTool(skillAlias) : null;
        var currentHardness = tool == null ? 0 : Math.Max(0, SafeInt(() => tool.material.hardness, 0));
        var skillName = SafeText(() => skillRow.GetName(), skillAlias);
        var toolName = GetRequiredGatheringToolName(skillAlias, toolRequired);

        var skillValue = currentSkill.ToString(CultureInfo.InvariantCulture) + "/" +
                         requiredSkill.ToString(CultureInfo.InvariantCulture);
        var hardnessValue = currentHardness.ToString(CultureInfo.InvariantCulture) + "/" +
                            (toolRequired ? requiredHardness : 0).ToString(CultureInfo.InvariantCulture);
        return BuildGatheringThresholdEntry(skillName, Tr("等级", "Level"), skillValue) + " " +
               BuildGatheringThresholdEntry(toolName, Tr("硬度", "Hardness"), hardnessValue);
    }
    private static string BuildGatheringThresholdEntry(string name, string label, string value)
    {
        return ColorNpcMoreInfoText("[", ItemMoreInfoGatheringThresholdColor) +
               ColorNpcMoreInfoText(name ?? "", ItemMoreInfoGatheringToolColor) +
               ColorNpcMoreInfoText("]" + (label ?? "") + ":", ItemMoreInfoGatheringThresholdColor) +
               ColorNpcMoreInfoText(value ?? "", ItemMoreInfoGatheringValueColor);
    }
    private static Thing? GetCurrentGatheringTool(string skillAlias)
    {
        Thing? tool;
        try
        {
            tool = GameAccess.Characters.PlayerCharacter?.Tool;
        }
        catch
        {
            return null;
        }

        if (tool == null)
            return null;

        try
        {
            if (string.Equals(skillAlias, "digging", StringComparison.OrdinalIgnoreCase))
                return tool.HasElement(230, false) ? tool : null;
            return tool.HasElement(220, false) || tool.HasElement(225, false) ? tool : null;
        }
        catch
        {
            return null;
        }
    }
    private static string GetRequiredGatheringToolName(string skillAlias, bool toolRequired)
    {
        if (!toolRequired)
            return Tr("无需工具", "No tool");
        if (string.Equals(skillAlias, "digging", StringComparison.OrdinalIgnoreCase))
            return Tr("铲子", "Shovel");
        if (string.Equals(skillAlias, "lumberjack", StringComparison.OrdinalIgnoreCase))
            return Tr("伐木斧", "Lumberjack axe");
        if (string.Equals(skillAlias, "mining", StringComparison.OrdinalIgnoreCase))
            return Tr("镐子", "Pickaxe");
        return Tr("采集工具", "Gathering tool");
    }
    private static bool HasGatheringHardMaterialTag(SourceMaterial.Row material)
    {
        var tags = material?.tag;
        if (tags == null)
            return false;
        for (var i = 0; i < tags.Length; i++)
        {
            if (string.Equals(tags[i], "hard", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
    private static string WrapItemMoreInfoLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return "";
        return Environment.NewLine +
               "<size=" + GetItemMoreInfoFontSize().ToString(CultureInfo.InvariantCulture) + ">" +
               line +
               "</size>";
    }
}
