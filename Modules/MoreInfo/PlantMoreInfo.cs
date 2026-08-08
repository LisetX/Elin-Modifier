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
    internal static string BuildPlantMoreInfoHoverDetails(Point point)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        var map = SafeObject(() => GameAccess.World.CurrentMap) as Map;
        if (instance == null || map == null || point == null)
            return "";

        var x = SafeInt(() => point.x, int.MinValue);
        var z = SafeInt(() => point.z, int.MinValue);
        var mask = GetItemMoreInfoDisplayMask(instance);
        var language = instance._language ?? "";
        var frame = Time.frameCount;
        var now = instance.SchedulerNow;
        var interval = instance._lowPerformanceMode ? 0.25f : 0.1f;
        var sameTarget = ReferenceEquals(instance._plantMoreInfoHoverCacheMap, map) &&
                         instance._plantMoreInfoHoverCacheX == x &&
                         instance._plantMoreInfoHoverCacheZ == z &&
                         instance._plantMoreInfoHoverCacheMask == mask &&
                         string.Equals(instance._plantMoreInfoHoverCacheLanguage, language, StringComparison.Ordinal);
        if (sameTarget &&
            (instance._plantMoreInfoHoverCacheFrame == frame ||
             (now >= instance._plantMoreInfoHoverCacheTime && now - instance._plantMoreInfoHoverCacheTime < interval)))
        {
            return instance._plantMoreInfoHoverCacheValue;
        }

        var value = BuildPlantMoreInfoHoverDetailsUncached(point);
        instance._plantMoreInfoHoverCacheMap = map;
        instance._plantMoreInfoHoverCacheX = x;
        instance._plantMoreInfoHoverCacheZ = z;
        instance._plantMoreInfoHoverCacheMask = mask;
        instance._plantMoreInfoHoverCacheLanguage = language;
        instance._plantMoreInfoHoverCacheFrame = frame;
        instance._plantMoreInfoHoverCacheTime = now;
        instance._plantMoreInfoHoverCacheValue = value;
        return value;
    }
    private static string BuildPlantMoreInfoHoverDetailsUncached(Point point)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        var map = GameAccess.World.CurrentMap;
        if (instance == null ||
            (!instance._showItemMoreInfoPlantStats && !instance._showItemMoreInfoPlantStatsExtended) ||
            map == null || point == null || !point.IsValid)
            return "";

        PlantData plant;
        Cell cell;
        SourceObj.Row source;
        GrowSystem growth;
        try
        {
            plant = map.TryGetPlant(point);
            cell = point.cell;
            source = cell?.sourceObj;
            growth = source?.growth;
        }
        catch
        {
            return "";
        }
        if (plant == null || cell == null || source == null || growth == null)
            return "";

        var previousCell = GrowSystem.cell;
        try
        {
            GrowSystem.cell = cell;
            var stageIndex = Clamp(cell.objVal / 30, 0, Math.Max(0, growth.StageLength - 1));
            var stageProgress = cell.objVal % 30;
            var canHarvest = growth.CanHarvest();
            var canReapSeed = growth.CanReapSeed();
            var isWithered = growth.IsWithered();
            var isMature = growth.IsMature;

            var firstLine = new List<string>
            {
                BuildItemMoreInfoField(Tr("种子遗传", "Seed genetics"), BuildPlantSeedGeneticsText(plant.seed), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("累计浇水", "Total watering"), plant.water.ToString(CultureInfo.InvariantCulture), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("肥料", "Fertilizer"), plant.fert.ToString(CultureInfo.InvariantCulture), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("尺寸", "Size"), GetPlantSizeText(plant.size), ItemMoreInfoPlantStatsColor)
            };

            var secondLine = new List<string>
            {
                BuildItemMoreInfoField(Tr("植物品种", "Plant variety"), SafeText(() => source.GetName(), source.alias ?? source.id.ToString(CultureInfo.InvariantCulture)), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("生长阶段", "Growth stage"), GetPlantStageText(stageIndex, growth.StageLength, canHarvest, isWithered, isMature), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("阶段进度", "Stage progress"), stageProgress.ToString(CultureInfo.InvariantCulture) + "/30 (" + Mathf.FloorToInt(stageProgress * 100f / 30f).ToString(CultureInfo.InvariantCulture) + "%)", ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("当前浇水", "Currently watered"), cell.isWatered ? Tr("已浇水", "Watered") : Tr("未浇水", "Not watered"), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("收获状态", "Harvest status"), GetPlantHarvestStateText(cell, canHarvest, canReapSeed, isWithered), ItemMoreInfoPlantStatsColor)
            };

            var materialLine = new List<string>
            {
                BuildItemMoreInfoField(Tr("材质", "Material"), SafeText(() => cell.matObj.GetName(), "?"), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("外观", "Appearance"), "obj " + cell.obj.ToString(CultureInfo.InvariantCulture) + " / dir " + cell.objDir.ToString(CultureInfo.InvariantCulture), ItemMoreInfoPlantStatsColor)
            };

            var plantRuleLine = BuildItemMoreInfoField(Tr("植物规则", "Plant rules"), GetPlantRuleText(growth), ItemMoreInfoPlantStatsColor);
            var stageDefinitionLine = BuildItemMoreInfoField(Tr("阶段定义", "Stage definition"), GetPlantStageDefinitionText(growth), ItemMoreInfoPlantStatsColor);
            var harvestLine = new List<string>
            {
                BuildItemMoreInfoField(Tr("收获物", "Harvest item"), GetPlantHarvestItemText(growth.idHarvestThing), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("环境需求", "Environment requirements"), GetPlantEnvironmentText(source, growth), ItemMoreInfoPlantStatsColor)
            };

            var lines = new List<string>();
            if (instance._showItemMoreInfoPlantStats)
            {
                lines.Add(string.Join(" ", firstLine.ToArray()));
                lines.Add(string.Join(" ", secondLine.ToArray()));
                lines.Add(string.Join(" ", harvestLine.ToArray()));
            }
            if (instance._showItemMoreInfoPlantStatsExtended)
            {
                lines.Add(string.Join(" ", materialLine.ToArray()));
                lines.Add(plantRuleLine);
                lines.Add(stageDefinitionLine);
            }
            if (lines.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.Append(Environment.NewLine);
            sb.Append("<size=").Append(GetItemMoreInfoFontSize().ToString(CultureInfo.InvariantCulture)).Append('>');
            sb.Append(string.Join(Environment.NewLine, lines.ToArray()));
            sb.Append("</size>");
            return sb.ToString();
        }
        catch
        {
            return "";
        }
        finally
        {
            GrowSystem.cell = previousCell;
        }
    }
    internal static string BuildPlantMoreInfoHoverDetails(Thing thing)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (thing == null || instance == null ||
            (!instance._showItemMoreInfoPlantStats && !instance._showItemMoreInfoPlantStatsExtended))
            return "";

        try
        {
            var mouseTarget = GameAccess.Ui.Scene?.mouseTarget;
            if (thing.trait is TraitFertilizer && mouseTarget != null && mouseTarget.card == thing && mouseTarget.pos != null && mouseTarget.pos.IsValid)
            {
                var hoveredPlantDetails = BuildPlantMoreInfoHoverDetails(mouseTarget.pos);
                if (!string.IsNullOrEmpty(hoveredPlantDetails))
                    return hoveredPlantDetails;

                var thingsAtPoint = mouseTarget.pos.Things;
                if (thingsAtPoint != null)
                {
                    for (var i = 0; i < thingsAtPoint.Count; i++)
                    {
                        var underlyingSeed = thingsAtPoint[i];
                        if (underlyingSeed == null || underlyingSeed == thing || !(underlyingSeed.trait is TraitSeed))
                            continue;
                        var seedDetails = BuildPlantMoreInfoHoverDetails(underlyingSeed);
                        if (!string.IsNullOrEmpty(seedDetails))
                            return seedDetails;
                    }
                }
            }
        }
        catch { }

        if (thing.ExistsOnMap || thing.IsInstalled)
        {
            var plantedDetails = BuildPlantMoreInfoHoverDetails(thing.pos);
            if (!string.IsNullOrEmpty(plantedDetails))
                return plantedDetails;
        }

        TraitSeed seedTrait;
        SourceObj.Row source;
        try
        {
            seedTrait = thing.trait as TraitSeed;
            source = seedTrait?.row;
        }
        catch
        {
            return "";
        }
        if (seedTrait == null || source == null || source.growth == null)
            return "";

        try
        {
            var firstLine = new List<string>
            {
                BuildItemMoreInfoField(Tr("种子遗传", "Seed genetics"), BuildPlantSeedGeneticsText(thing), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("累计浇水", "Total watering"), "0", ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("肥料", "Fertilizer"), "0", ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("尺寸", "Size"), GetPlantSizeText(0), ItemMoreInfoPlantStatsColor)
            };

            var secondLine = new List<string>
            {
                BuildItemMoreInfoField(Tr("植物品种", "Plant variety"), SafeText(() => source.GetName(), source.alias ?? source.id.ToString(CultureInfo.InvariantCulture)), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("生长阶段", "Growth stage"), Tr("未种植", "Not planted"), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("阶段进度", "Stage progress"), "0/30 (0%)", ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("当前浇水", "Currently watered"), Tr("未浇水", "Not watered"), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("收获状态", "Harvest status"), Tr("未种植", "Not planted"), ItemMoreInfoPlantStatsColor)
            };

            var materialLine = new List<string>
            {
                BuildItemMoreInfoField(Tr("材质", "Material"), SafeText(() => source.DefaultMaterial.GetName(), source.defMat ?? "?"), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("外观", "Appearance"), "skin " + thing.idSkin.ToString(CultureInfo.InvariantCulture) + " / obj " + source.id.ToString(CultureInfo.InvariantCulture), ItemMoreInfoPlantStatsColor)
            };

            var plantRuleLine = BuildItemMoreInfoField(Tr("植物规则", "Plant rules"), GetPlantRuleText(source.growth), ItemMoreInfoPlantStatsColor);
            var stageDefinitionLine = BuildItemMoreInfoField(Tr("阶段定义", "Stage definition"), GetPlantStageDefinitionText(source.growth), ItemMoreInfoPlantStatsColor);
            var harvestLine = new List<string>
            {
                BuildItemMoreInfoField(Tr("收获物", "Harvest item"), GetPlantHarvestItemText(source.growth.idHarvestThing), ItemMoreInfoPlantStatsColor),
                BuildItemMoreInfoField(Tr("环境需求", "Environment requirements"), GetPlantEnvironmentText(source, source.growth), ItemMoreInfoPlantStatsColor)
            };

            var lines = new List<string>();
            if (instance._showItemMoreInfoPlantStats)
            {
                lines.Add(string.Join(" ", firstLine.ToArray()));
                lines.Add(string.Join(" ", secondLine.ToArray()));
                lines.Add(string.Join(" ", harvestLine.ToArray()));
            }
            if (instance._showItemMoreInfoPlantStatsExtended)
            {
                lines.Add(string.Join(" ", materialLine.ToArray()));
                lines.Add(plantRuleLine);
                lines.Add(stageDefinitionLine);
            }
            if (lines.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.Append(Environment.NewLine);
            sb.Append("<size=").Append(GetItemMoreInfoFontSize().ToString(CultureInfo.InvariantCulture)).Append('>');
            sb.Append(string.Join(Environment.NewLine, lines.ToArray()));
            sb.Append("</size>");
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }
    private static string BuildPlantSeedGeneticsText(Thing? seed)
    {
        if (seed == null)
            return Tr("无", "None");

        var parts = new List<string>
        {
            "L" + SafeInt(() => seed.encLV, 0).ToString(CultureInfo.InvariantCulture)
        };
        try
        {
            var traits = seed.elements?.dict?.Values
                .Where(element => element != null && element.IsFoodTrait)
                .OrderBy(element => element.id)
                .Take(8);
            if (traits != null)
            {
                foreach (var element in traits)
                {
                    var value = GetThingElementEditorValue(seed, element);
                    if (value == 0)
                        continue;
                    parts.Add(GetGeneEffectNameStatic(element.id) + "(" + FormatCompactCount(value) + ")");
                }
            }
        }
        catch { }
        return string.Join(",", parts.ToArray());
    }
    private static string GetPlantSizeText(int size)
    {
        if (size <= 0)
            return Tr("普通", "Normal") + "(0)";
        try
        {
            var names = Lang.GetList("plant_size");
            if (names != null && size - 1 < names.Length && !string.IsNullOrWhiteSpace(names[size - 1]))
                return names[size - 1] + "(" + size.ToString(CultureInfo.InvariantCulture) + ")";
        }
        catch { }
        return size.ToString(CultureInfo.InvariantCulture);
    }
    private static string GetPlantStageText(int stageIndex, int stageLength, bool canHarvest, bool isWithered, bool isMature)
    {
        var state = isWithered
            ? Tr("凋零", "Withered")
            : canHarvest
                ? Tr("可收获", "Harvestable")
                : isMature
                    ? Tr("成熟", "Mature")
                    : Tr("生长中", "Growing");
        return state + "(" + (stageIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + Math.Max(1, stageLength).ToString(CultureInfo.InvariantCulture) + ")";
    }
    private static string GetPlantHarvestStateText(Cell cell, bool canHarvest, bool canReapSeed, bool isWithered)
    {
        if (isWithered)
            return Tr("凋零", "Withered");
        if (canHarvest)
            return Tr("可收获", "Harvestable");
        if (cell.isHarvested)
            return Tr("已收获", "Harvested");
        if (canReapSeed)
            return Tr("可采种", "Seed harvestable");
        return Tr("生长中", "Growing");
    }
    private static string GetPlantRuleText(GrowSystem growth)
    {
        var rules = new List<string> { growth.GetType().Name };
        if (growth.IsTree) rules.Add(Tr("树木", "Tree"));
        if (growth.CanLevelSeed) rules.Add(Tr("种子可升级", "Upgradeable seed"));
        if (growth.NeedSunlight) rules.Add(Tr("需要日照", "Needs sunlight"));
        if (growth.GrowOnLand) rules.Add(Tr("陆生", "Land"));
        if (growth.GrowUndersea) rules.Add(Tr("水下生长", "Undersea"));
        return string.Join("/", rules.ToArray());
    }
    private static string GetPlantStageDefinitionText(GrowSystem growth)
    {
        return Tr("阶段", "Stages") + "=" + growth.StageLength.ToString(CultureInfo.InvariantCulture) +
               "," + Tr("步进", "Step") + "=" + growth.Step.ToString(CultureInfo.InvariantCulture) +
               "," + Tr("收获", "Harvest") + "=" + growth.HarvestStage.ToString(CultureInfo.InvariantCulture) +
               "," + Tr("自动", "Auto") + "=" + growth.AutoMineStage.ToString(CultureInfo.InvariantCulture);
    }
    private static string GetPlantHarvestItemText(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Tr("无", "None");
        try
        {
            if (id.StartsWith("#", StringComparison.Ordinal))
                return id;
            if (GameAccess.Sources.Things?.map != null && GameAccess.Sources.Things.map.TryGetValue(id, out var row) && row != null)
                return row.GetName() + "(" + id + ")";
        }
        catch { }
        return id;
    }
    private static string GetPlantEnvironmentText(SourceObj.Row source, GrowSystem growth)
    {
        var requirements = new List<string>();
        if (growth.NeedSunlight) requirements.Add(Tr("日照", "Sunlight"));
        if (growth.GrowOnLand) requirements.Add(Tr("陆地", "Land"));
        if (growth.GrowUndersea) requirements.Add(Tr("水下", "Undersea"));
        if (source.tag != null && source.tag.Contains("flood")) requirements.Add(Tr("水田", "Flooded field"));
        requirements.Add(Tr("土壤", "Soil") + "=" + source.costSoil.ToString(CultureInfo.InvariantCulture));
        return string.Join("/", requirements.ToArray());
    }
    private static string BuildItemMoreInfoField(string label, string value, string color)
    {
        return ColorNpcMoreInfoText(label ?? "", color) + ":" + FormatCompactNumericText(value);
    }
    private static string BuildItemMoreInfoRarityField(Thing thing)
    {
        var rarity = SafeInt(() => (int)thing.rarity, 0);
        string value;
        string color;
        switch (rarity)
        {
            case int n when n <= (int)Rarity.Crude:
                value = Tr("低级", "Poor");
                color = ItemMoreInfoRarityCrudeColor;
                break;
            case (int)Rarity.Superior:
                value = Tr("高级", "Superior");
                color = ItemMoreInfoRaritySuperiorColor;
                break;
            case (int)Rarity.Legendary:
                value = Tr("奇迹", "Miracle");
                color = ItemMoreInfoRarityLegendaryColor;
                break;
            case (int)Rarity.Mythical:
                value = Tr("神器", "Godly");
                color = ItemMoreInfoRarityMythicalColor;
                break;
            case int n when n >= (int)Rarity.Artifact:
                value = Tr("古遗物", "Artifact");
                color = ItemMoreInfoRarityArtifactColor;
                break;
            default:
                value = Tr("普通", "Standard");
                color = ItemMoreInfoRarityNormalColor;
                break;
        }

        return ColorNpcMoreInfoText(Tr("稀有度", "Rarity"), ItemMoreInfoBasicInfoColor) +
               ":" +
               ColorNpcMoreInfoText(value, color);
    }
    private static int GetItemMoreInfoFontSize()
    {
        return Clamp(14 + (ElinModifierPlugin.ActiveInstance?._showItemMoreInfoFontSizeOffset ?? 0), 6, 22);
    }
}
