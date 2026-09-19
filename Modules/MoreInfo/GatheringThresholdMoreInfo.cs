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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static ElinModifierPlugin;

internal sealed partial class MoreInfoModule
{
    private const int GatheringThresholdFullHpPercent = 100;
    private static readonly string[] GatheringThresholdDisassembleRequirements = { "handicraft", "1" };

    internal static string BuildMapGatheringThresholdHoverDetails(Point point)
    {
        return WrapItemMoreInfoLines(BuildMapGatheringThresholdLines(point));
    }
    internal static string BuildStandaloneGatheringThresholdHoverText(Point point)
    {
        var lines = BuildMapGatheringThresholdLines(point);
        if (lines.Count == 0)
            return "";
        return GetGatheringThresholdTileName(point) + WrapItemMoreInfoLines(lines);
    }
    private static List<string> BuildMapGatheringThresholdLines(Point point)
    {
        var lines = new List<string>();
        var instance = ElinModifierPlugin.ActiveInstance;
        if (instance == null || !instance._showItemMoreInfoGatheringThreshold || point == null)
            return lines;

        try
        {
            if (!point.IsValid)
                return lines;
            var cell = point.cell;
            if (cell == null)
                return lines;

            var canHarvest = cell.CanHarvest();
            AddGatheringThresholdLine(lines, BuildObjGatheringThresholdLine(point, cell, canHarvest));
            AddGatheringThresholdLine(lines, BuildBlockGatheringThresholdLine(point, canHarvest));
            AddGatheringThresholdLine(lines, BuildFloorGatheringThresholdLine(point, cell, canHarvest));
        }
        catch
        {
        }
        return lines;
    }
    private static void AddGatheringThresholdLine(List<string> lines, string line)
    {
        if (!string.IsNullOrEmpty(line))
            lines.Add(line);
    }
    private static string GetGatheringThresholdTileName(Point point)
    {
        try
        {
            if (point.HasBlock)
                return SafeText(() => point.sourceBlock.GetName(), "");
            if (point.cell.HasBridge)
                return SafeText(() => point.sourceBridge.GetName(), "");
            return SafeText(() => point.sourceFloor.GetName(), "");
        }
        catch
        {
            return "";
        }
    }
    private static string BuildObjGatheringThresholdLine(Point point, Cell cell, bool canHarvest)
    {
        if (!point.HasObj)
            return "";
        var source = point.sourceObj;
        if (source == null)
            return "";
        var material = cell.isObjDyed ? source.DefaultMaterial : cell.matObj;
        var hpPercent = point.growth != null ? point.growth.GetHp() : source.hp;
        return BuildGatheringThresholdTargetLine(
            Tr("物件", "Object"), source.reqHarvest, material, hpPercent, false, canHarvest);
    }
    private static string BuildBlockGatheringThresholdLine(Point point, bool canHarvest)
    {
        if (!IsGatheringThresholdTerrainEnabled() || !point.HasBlock)
            return "";
        var source = point.sourceBlock;
        if (source == null)
            return "";
        return BuildGatheringThresholdTargetLine(
            Tr("墙壁", "Wall"),
            source.reqHarvest,
            point.matBlock,
            GatheringThresholdFullHpPercent,
            true,
            canHarvest);
    }
    private static string BuildFloorGatheringThresholdLine(Point point, Cell cell, bool canHarvest)
    {
        if (!IsGatheringThresholdTerrainEnabled() || point.HasBlock)
            return "";
        var hasBridge = cell.HasBridge;
        var source = hasBridge ? point.sourceBridge : point.sourceFloor;
        if (source == null)
            return "";
        return BuildGatheringThresholdTargetLine(
            hasBridge ? Tr("桥", "Bridge") : Tr("地板", "Floor"),
            source.reqHarvest,
            hasBridge ? point.matBridge : point.matFloor,
            GatheringThresholdFullHpPercent,
            true,
            canHarvest);
    }
    private static string BuildThingGatheringThresholdLine(Thing thing)
    {
        try
        {
            var material = thing?.material;
            if (thing == null || material == null)
                return "";

            var canHarvest = thing.pos != null && thing.pos.IsValid && thing.pos.cell.CanHarvest();
            var requirementText = thing.trait?.ReqHarvest;
            if (!string.IsNullOrWhiteSpace(requirementText))
                return BuildGatheringThresholdTargetLine(
                    Tr("采集", "Harvest"),
                    requirementText.Split(',', StringSplitOptions.None),
                    material,
                    GatheringThresholdFullHpPercent,
                    false,
                    canHarvest);

            if (!IsGatheringThresholdDisassembleEnabled() || !CanDisassembleGatheringThresholdTarget(thing))
                return "";
            return BuildGatheringThresholdTargetLine(
                Tr("拆解", "Disassemble"),
                GatheringThresholdDisassembleRequirements,
                material,
                GatheringThresholdFullHpPercent,
                false,
                canHarvest);
        }
        catch
        {
            return "";
        }
    }
    private static bool IsGatheringThresholdTerrainEnabled()
    {
        return ElinModifierPlugin.ActiveInstance?._showItemMoreInfoGatheringThresholdTerrain == true;
    }
    private static bool IsGatheringThresholdDisassembleEnabled()
    {
        return ElinModifierPlugin.ActiveInstance?._showItemMoreInfoGatheringThresholdDisassemble == true;
    }
    private static bool CanDisassembleGatheringThresholdTarget(Thing thing)
    {
        try
        {
            return thing.trait != null && !thing.isHidden && !thing.isMasked && thing.trait.CanBeDisassembled;
        }
        catch
        {
            return false;
        }
    }
    private static string BuildGatheringThresholdTargetLine(
        string target,
        string[] requirements,
        SourceMaterial.Row material,
        int hpPercent,
        bool penalizeMissingMaterial,
        bool canHarvest)
    {
        if (requirements == null || requirements.Length < 2 || material == null)
            return "";

        var requiredHardness = GatheringThresholdPolicy.CalculateRequiredHardness(
            material.hardness,
            hpPercent,
            HasGatheringHardMaterialTag(material));
        if (penalizeMissingMaterial && material.id == 0)
            requiredHardness = GatheringThresholdPolicy.ApplyMissingMaterialPenalty(requiredHardness);

        var line = BuildGatheringThresholdLine(requirements, canHarvest, requiredHardness);
        if (string.IsNullOrEmpty(line))
            return "";
        return ColorNpcMoreInfoText("[" + (target ?? "") + "]", ItemMoreInfoGatheringThresholdColor) + line;
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
            if (string.Equals(skillAlias, "handicraft", StringComparison.OrdinalIgnoreCase))
                return tool.trait is TraitToolHammer ? tool : null;
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
        if (string.Equals(skillAlias, "handicraft", StringComparison.OrdinalIgnoreCase))
            return Tr("锤子", "Hammer");
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
}
