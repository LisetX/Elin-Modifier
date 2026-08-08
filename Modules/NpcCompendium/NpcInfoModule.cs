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

    private readonly ElinModifierPlugin _host;
    private readonly List<NpcRecord> _npcs = new List<NpcRecord>();
    private readonly Dictionary<string, NpcRecord> _npcById =
        new Dictionary<string, NpcRecord>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpawnListSnapshot?> _spawnListCache =
        new Dictionary<string, SpawnListSnapshot?>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, double>> _distributionCache =
        new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _biomeDisplayNameCache =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<string> _spawnListIds = new List<string>();
    private List<BiomeProfile>? _biomes;
    private object? _sourceIdentity;

    internal NpcInfoModule(ElinModifierPlugin host)
    {
        _host = host;
    }

    internal string Filter { get; set; } = "";
    internal string ZoneFilter { get; set; } = "";
    internal bool RandomOnly { get; set; }
    internal int ZoneSpawnMode { get; set; }
    internal int NpcPage { get; set; }
    internal int ZonePage { get; set; }
    internal bool ShowCurrentZone { get; set; }
    internal string Log { get; private set; } = "";

    internal IReadOnlyList<NpcRecord> GetFilteredNpcs()
    {
        EnsureData();
        var filter = (Filter ?? "").Trim();
        var result = new List<NpcRecord>();
        for (var i = 0; i < _npcs.Count; i++)
        {
            var npc = _npcs[i];
            if (RandomOnly && npc.Chance <= 0)
                continue;
            if (filter.Length > 0 &&
                npc.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                npc.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                npc.Race.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                npc.Job.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                npc.Biome.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                FormatBiomeName(npc.Biome).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            result.Add(npc);
        }
        return result;
    }

    internal void Refresh()
    {
        _sourceIdentity = null;
        _npcs.Clear();
        _npcById.Clear();
        _spawnListCache.Clear();
        _distributionCache.Clear();
        _biomeDisplayNameCache.Clear();
        _spawnListIds.Clear();
        _biomes = null;
        EnsureData();
        Log = T("NPC图鉴数据已刷新，共 ", "NPC compendium refreshed: ") +
              _npcs.Count.ToString(CultureInfo.InvariantCulture);
    }

    internal NpcAnalysis? AnalyzeNpc(
        string id,
        int additionalLevel = 0,
        int startingDangerLevel = 1,
        NpcTemplateInfo? templateOverride = null)
    {
        EnsureData();
        if (!_npcById.TryGetValue(id ?? "", out var npc))
            return null;

        additionalLevel = Math.Max(0, additionalLevel);
        startingDangerLevel = Math.Max(1, startingDangerLevel);

        var analysis = new NpcAnalysis { Npc = npc };
        var routes = BuildAllBiomeRoutes(false);
        var relevantRoutes = new List<BiomeRoute>();
        for (var i = 0; i < routes.Count; i++)
        {
            var route = routes[i];
            if (route.Lists.Any(item => ContainsNpc(item.Snapshot, npc.Id)))
                relevantRoutes.Add(route);
        }

        AddSpecialDungeonRoutes(npc, relevantRoutes);
        var dangerLevels = BuildRelevantDangerLevels(relevantRoutes, startingDangerLevel);
        var targetProbabilityCache = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var routeIndex = 0; routeIndex < relevantRoutes.Count; routeIndex++)
        {
            var route = relevantRoutes[routeIndex];
            var result = new LocationResult
            {
                Name = route.Name,
                Route = string.Join(", ", route.Lists.Select(item => item.Snapshot.Id).Distinct(StringComparer.OrdinalIgnoreCase))
            };
            for (var levelIndex = 0; levelIndex < dangerLevels.Count; levelIndex++)
            {
                var dangerLevel = dangerLevels[levelIndex];
                var probability = GetTargetRouteProbability(route, npc.Id, dangerLevel, targetProbabilityCache);
                if (probability <= 0d)
                    continue;
                if (result.MinimumDangerLevel == 0)
                    result.MinimumDangerLevel = dangerLevel;
                if (probability > result.PeakProbability + 0.0000000001d)
                {
                    result.PeakProbability = probability;
                    result.PeakDangerLevel = dangerLevel;
                }
            }
            if (result.PeakProbability > 0d)
                analysis.Locations.Add(result);
        }

        analysis.Locations.Sort((left, right) =>
        {
            var probabilityOrder = right.PeakProbability.CompareTo(left.PeakProbability);
            return probabilityOrder != 0
                ? probabilityOrder
                : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        if (analysis.Locations.Count > 0)
        {
            var highest = analysis.Locations[0];
            analysis.HighestLocation = highest.Name;
            analysis.PeakDangerLevel = highest.PeakDangerLevel;
            analysis.PeakProbability = highest.PeakProbability;
            analysis.MinimumDangerLevel = analysis.Locations
                .Where(item => item.MinimumDangerLevel > 0)
                .Select(item => item.MinimumDangerLevel)
                .DefaultIfEmpty(0)
                .Min();
        }

        analysis.SpawnLists.AddRange(FindSpawnListsForNpc(npc.Id));
        analysis.Loot.AddRange(BuildLootEntries(npc));
        analysis.Template = templateOverride ?? BuildTemplateInfo(npc, additionalLevel);
        var zone = AnalyzeCurrentZone();
        var current = zone?.Npcs.FirstOrDefault(item =>
            string.Equals(item.Npc.Id, npc.Id, StringComparison.OrdinalIgnoreCase));
        analysis.CurrentZoneProbability = current?.Probability ?? 0d;
        Log = T("已分析 NPC：", "Analyzed NPC: ") + npc.Name;
        return analysis;
    }
}
