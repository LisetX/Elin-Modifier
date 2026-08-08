using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

internal sealed partial class NpcInfoModule
{

    internal ZoneAnalysis? AnalyzeCurrentZone()
    {
        EnsureData();
        var zone = GameAccess.World.CurrentZone;
        var map = GameAccess.World.CurrentMap;
        if (zone == null || map == null)
        {
            Log = T("当前没有可分析的区块", "No active zone to analyze");
            return null;
        }

        var result = new ZoneAnalysis
        {
            ZoneName = SafeZoneName(zone),
            ZoneType = zone.GetType().Name,
            DangerLevel = Math.Max(1, zone.DangerLv),
            Scaling = zone.ScaleType.ToString(),
            IsEstimate = true
        };
        try
        {
            result.ExistingNpcCount = map.charas?.Count ?? 0;
            result.ExistingHostileCount = map.CountHostile();
        }
        catch
        {
        }

        var biomeWeights = GetCurrentZoneBiomeWeights(map, zone, out var coverage, out var currentBiome);
        result.BiomeCoverage = coverage;
        result.CurrentBiome = currentBiome;
        if (biomeWeights.Count == 0)
        {
            Log = T("当前区块没有原版随机刷怪可用的位置", "No original random-spawn position is currently available");
            return result;
        }
        var filterLevels = GetZoneFilterLevelWeights(zone, result.DangerLevel);
        var accumulators = new Dictionary<string, ProbabilityAccumulator>(StringComparer.OrdinalIgnoreCase);
        var neutralChance = Math.Max(0d, Math.Min(1d, zone.ChanceSpawnNeutral));
        var enemyRouteWeight = ZoneSpawnMode == 1 ? 0d : ZoneSpawnMode == 2 ? 1d - neutralChance : 1d;
        var neutralRouteWeight = ZoneSpawnMode == 0 ? 0d : ZoneSpawnMode == 2 ? neutralChance : 1d;

        if (enemyRouteWeight > 0d)
        {
            foreach (var biomePair in biomeWeights)
            {
                foreach (var levelPair in filterLevels)
                {
                    var distribution = GetBiomeDistribution(biomePair.Key, levelPair.Key, zone.IsInstance);
                    var routeWeight = biomePair.Value * levelPair.Value * enemyRouteWeight;
                    foreach (var probabilityPair in distribution)
                    {
                        AddProbability(
                            accumulators,
                            probabilityPair.Key,
                            probabilityPair.Value * routeWeight,
                            GetBiomeDisplayName(biomePair.Key));
                    }
                }
            }
        }

        if (neutralRouteWeight > 0d)
        {
            var neutralListId = zone.IsInstance ? "c_neutral_war" : "c_neutral";
            var neutralSnapshot = GetSpawnListSnapshot(neutralListId, false);
            if (neutralSnapshot != null)
            {
                foreach (var levelPair in filterLevels)
                {
                    var distribution = GetSpawnDistribution(neutralSnapshot, levelPair.Key);
                    foreach (var probabilityPair in distribution)
                    {
                        AddProbability(
                            accumulators,
                            probabilityPair.Key,
                            probabilityPair.Value * levelPair.Value * neutralRouteWeight,
                            T("中立列表", "Neutral list") + " [" + neutralListId + "]");
                    }
                }
            }
        }

        ApplyCurrentSpecialDungeonOverride(zone, filterLevels, accumulators);
        ApplySeasonalSantaOverride(zone, accumulators);
        foreach (var pair in accumulators)
        {
            if (!_npcById.TryGetValue(pair.Key, out var npc) || pair.Value.Total <= 0d)
                continue;
            result.Npcs.Add(new ZoneNpcResult
            {
                Npc = npc,
                Probability = pair.Value.Total,
                MainRoute = pair.Value.MainRoute
            });
        }
        result.Npcs.Sort((left, right) =>
        {
            var probabilityOrder = right.Probability.CompareTo(left.Probability);
            return probabilityOrder != 0
                ? probabilityOrder
                : string.Compare(left.Npc.Name, right.Npc.Name, StringComparison.OrdinalIgnoreCase);
        });

        Log = T("已分析当前区块，可生成NPC：", "Current zone analyzed; possible NPCs: ") +
              result.Npcs.Count.ToString(CultureInfo.InvariantCulture);
        return result;
    }

    internal string FormatProbability(double probability)
    {
        if (probability <= 0d)
            return "0%";
        var percent = probability * 100d;
        if (percent >= 10d)
            return percent.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        if (percent >= 0.1d)
            return percent.ToString("0.000", CultureInfo.InvariantCulture) + "%";
        if (percent >= 0.001d)
            return percent.ToString("0.00000", CultureInfo.InvariantCulture) + "%";
        return "≈1/" + Math.Max(1d, Math.Round(1d / probability)).ToString("0", CultureInfo.InvariantCulture);
    }
}
