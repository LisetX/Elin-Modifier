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

    private sealed class SpawnEntry
    {
        internal string Id = "";
        internal int Level;
        internal int Weight;
        internal SourceCategory.Row? Category;
    }

    private sealed class SpawnListSnapshot
    {
        internal string Key = "";
        internal string Id = "";
        internal readonly List<SpawnEntry> Entries = new List<SpawnEntry>();
        internal readonly List<SourceCategory.Row> IncludedCategories = new List<SourceCategory.Row>();
        internal int MaxLevel;
    }

    private sealed class RouteList
    {
        internal SpawnListSnapshot Snapshot = null!;
        internal double Weight;
    }

    private sealed class BiomeRoute
    {
        internal string Name = "";
        internal readonly List<RouteList> Lists = new List<RouteList>();
    }

    private sealed class ProbabilityAccumulator
    {
        internal double Total;
        internal string MainRoute = "";
        internal double MainContribution;
        internal readonly Dictionary<string, double> RouteContributions =
            new Dictionary<string, double>(StringComparer.Ordinal);
    }

    private List<BiomeProfile> GetBiomes()
    {
        if (_biomes != null)
            return _biomes;
        var result = new Dictionary<string, BiomeProfile>(StringComparer.OrdinalIgnoreCase);
        try
        {
            AddBiomes(result, GameAccess.Runtime.Core?.refs?.biomes?.dict?.Values);
        }
        catch
        {
        }
        try
        {
            AddBiomes(result, Resources.FindObjectsOfTypeAll<BiomeProfile>());
        }
        catch
        {
        }
        try
        {
            if (GameAccess.World.CurrentMap?.biomes != null)
            {
                foreach (var biome in GameAccess.World.CurrentMap.biomes)
                    AddBiome(result, biome);
            }
        }
        catch
        {
        }
        _biomes = result.Values.OrderBy(GetBiomeDisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        return _biomes;
    }

    private static void AddBiomes(IDictionary<string, BiomeProfile> result, IEnumerable<BiomeProfile>? biomes)
    {
        if (biomes == null)
            return;
        foreach (var biome in biomes)
            AddBiome(result, biome);
    }

    private static void AddBiome(IDictionary<string, BiomeProfile> result, BiomeProfile? biome)
    {
        if (biome == null || string.IsNullOrWhiteSpace(biome.name))
            return;
        if (!result.ContainsKey(biome.name))
            result.Add(biome.name, biome);
    }

    private List<BiomeRoute> BuildAllBiomeRoutes(bool instance)
    {
        var routes = new List<BiomeRoute>();
        var biomes = GetBiomes();
        for (var i = 0; i < biomes.Count; i++)
        {
            var route = BuildBiomeRoute(biomes[i], instance);
            if (route.Lists.Count > 0)
                routes.Add(route);
        }
        return routes;
    }

    private BiomeRoute BuildBiomeRoute(BiomeProfile biome, bool instance)
    {
        var route = new BiomeRoute { Name = GetBiomeDisplayName(biome) };
        var spawnEntries = biome.spawn?.chara;
        if (spawnEntries != null && spawnEntries.Count > 0)
        {
            var totalWeight = spawnEntries.Where(item => item != null && item.chance > 0f).Sum(item => (double)item.chance);
            if (totalWeight <= 0d)
                totalWeight = spawnEntries.Count;
            for (var i = 0; i < spawnEntries.Count; i++)
            {
                var item = spawnEntries[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;
                var snapshot = GetSpawnListSnapshot(item.id, instance);
                if (snapshot == null || snapshot.Entries.Count == 0)
                    continue;
                route.Lists.Add(new RouteList
                {
                    Snapshot = snapshot,
                    Weight = totalWeight <= 0d ? 1d / spawnEntries.Count : Math.Max(0d, item.chance) / totalWeight
                });
            }
            return route;
        }

        var fallback = CreateFallbackBiomeSnapshot(biome, instance);
        if (fallback.Entries.Count > 0)
            route.Lists.Add(new RouteList { Snapshot = fallback, Weight = 1d });
        return route;
    }

    private SpawnListSnapshot CreateFallbackBiomeSnapshot(BiomeProfile biome, bool instance)
    {
        try
        {
            var filter = new CharaFilter
            {
                ShouldPass = row =>
                    string.IsNullOrEmpty(row.hostility) &&
                    (string.IsNullOrEmpty(row.biome) ||
                     string.Equals(row.biome, biome.name, StringComparison.OrdinalIgnoreCase))
            };
            var list = SpawnList.Get(biome.name, "chara", filter);
            return CreateSnapshotFromRuntimeList(
                list,
                "biome:" + biome.name + (instance ? ":instance" : ""),
                T("群落回退:", "Biome fallback:") + GetBiomeDisplayName(biome));
        }
        catch
        {
            return new SpawnListSnapshot
            {
                Key = "biome:" + biome.name + (instance ? ":instance" : ""),
                Id = T("群落回退:", "Biome fallback:") + GetBiomeDisplayName(biome)
            };
        }
    }

    private SpawnListSnapshot? GetSpawnListSnapshot(string id, bool instance)
    {
        var key = (instance ? "instance:" : "normal:") + id;
        if (_spawnListCache.TryGetValue(key, out var cached))
            return cached;
        try
        {
            var list = instance
                ? SpawnList.Get("instance_" + id, id, new CharaFilter { ShouldPass = PassesInstanceFilter })
                : SpawnList.Get(id, null, null);
            var snapshot = CreateSnapshotFromRuntimeList(list, key, id);
            _spawnListCache[key] = snapshot;
            return snapshot;
        }
        catch
        {
            _spawnListCache[key] = null;
            return null;
        }
    }

    private static SpawnListSnapshot? CreateSnapshotFromRuntimeList(SpawnList? list, string key, string id)
    {
        if (list?.rows == null)
            return null;
        var snapshot = new SpawnListSnapshot { Key = key, Id = id };
        for (var i = 0; i < list.rows.Count; i++)
        {
            var row = list.rows[i];
            if (row == null || !row.isChara || row.chance <= 0)
                continue;
            AddSpawnEntry(snapshot, row);
        }
        if (list.filter?.categoriesInclude != null)
        {
            for (var i = 0; i < list.filter.categoriesInclude.Count; i++)
            {
                var category = list.filter.categoriesInclude[i];
                if (category != null)
                    snapshot.IncludedCategories.Add(category);
            }
        }
        return snapshot;
    }

    private static void AddSpawnEntry(SpawnListSnapshot snapshot, CardRow row)
    {
        SourceCategory.Row? category = null;
        try { category = row.Category; }
        catch { }
        snapshot.Entries.Add(new SpawnEntry
        {
            Id = row.id ?? "",
            Level = row.LV,
            Weight = Math.Max(0, row.chance),
            Category = category
        });
        snapshot.MaxLevel = Math.Max(snapshot.MaxLevel, row.LV);
    }

    private static bool PassesInstanceFilter(SourceChara.Row row)
    {
        if (string.IsNullOrEmpty(row.hostility))
            return true;
        if (ContainsTag(row.tag, "cat"))
            return false;
        try
        {
            return !ContainsTag(row.race_row?.tag, "cat");
        }
        catch
        {
            return true;
        }
    }

    private static bool ContainsTag(string[]? values, string value)
    {
        if (values == null)
            return false;
        for (var i = 0; i < values.Length; i++)
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private IReadOnlyDictionary<string, double> GetSpawnDistribution(SpawnListSnapshot snapshot, int dangerLevel)
    {
        var cacheKey = snapshot.Key + "|" + dangerLevel.ToString(CultureInfo.InvariantCulture);
        if (_distributionCache.TryGetValue(cacheKey, out var cached))
            return cached;
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var offsets = NpcInfoProbabilityMath.GetDefaultSpawnLevelOffsets(dangerLevel);
        foreach (var offsetPair in offsets)
        {
            var effectiveLevel = Math.Max(1, dangerLevel) + 2 + offsetPair.Key;
            var eligible = snapshot.Entries.Where(item => item.Level <= effectiveLevel && item.Weight > 0).ToList();
            if (eligible.Count == 0)
                eligible = snapshot.Entries.Where(item => item.Weight > 0).ToList();
            var distribution = GetWeightedDistribution(eligible, snapshot.IncludedCategories);
            foreach (var pair in distribution)
            {
                result.TryGetValue(pair.Key, out var current);
                result[pair.Key] = current + pair.Value * offsetPair.Value;
            }
        }
        _distributionCache[cacheKey] = result;
        return result;
    }

    private static Dictionary<string, double> GetWeightedDistribution(
        IReadOnlyList<SpawnEntry> entries,
        IReadOnlyList<SourceCategory.Row> categories)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var total = 0d;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Weight <= 0 || string.IsNullOrEmpty(entry.Id))
                continue;
            total += entry.Weight;
            weights.TryGetValue(entry.Id, out var current);
            weights[entry.Id] = current + entry.Weight;
        }
        if (total <= 0d)
            return result;
        if (categories.Count == 0)
        {
            foreach (var pair in weights)
                result[pair.Key] = pair.Value / total;
            return result;
        }

        for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
        {
            var category = categories[categoryIndex];
            var matchingWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var matchingTotal = 0d;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                if (entry.Weight <= 0 || !IsCategoryMatch(entry.Category, category))
                    continue;
                matchingTotal += entry.Weight;
                matchingWeights.TryGetValue(entry.Id, out var current);
                matchingWeights[entry.Id] = current + entry.Weight;
            }
            var matchingRatio = matchingTotal / total;
            var fallbackProbability = Math.Pow(Math.Max(0d, 1d - matchingRatio), 100d);
            foreach (var pair in weights)
            {
                matchingWeights.TryGetValue(pair.Key, out var matchingWeight);
                var accepted = matchingTotal > 0d ? matchingWeight / matchingTotal * (1d - fallbackProbability) : 0d;
                var fallback = pair.Value / total * fallbackProbability;
                result.TryGetValue(pair.Key, out var current);
                result[pair.Key] = current + (accepted + fallback) / categories.Count;
            }
        }
        return result;
    }

    private static bool IsCategoryMatch(SourceCategory.Row? rowCategory, SourceCategory.Row required)
    {
        if (rowCategory == null || required == null)
            return false;
        try { return rowCategory.IsChildOf(required); }
        catch { return ReferenceEquals(rowCategory, required); }
    }

    private IReadOnlyDictionary<string, double> GetBiomeDistribution(BiomeProfile biome, int dangerLevel, bool instance)
    {
        var route = BuildBiomeRoute(biome, instance);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < route.Lists.Count; i++)
        {
            var routeList = route.Lists[i];
            var distribution = GetSpawnDistribution(routeList.Snapshot, dangerLevel);
            foreach (var pair in distribution)
            {
                result.TryGetValue(pair.Key, out var current);
                result[pair.Key] = current + pair.Value * routeList.Weight;
            }
        }
        return result;
    }

    private static bool ContainsNpc(SpawnListSnapshot snapshot, string id)
    {
        for (var i = 0; i < snapshot.Entries.Count; i++)
            if (string.Equals(snapshot.Entries[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private double GetTargetRouteProbability(
        BiomeRoute route,
        string id,
        int dangerLevel,
        IDictionary<string, double> cache)
    {
        var total = 0d;
        for (var i = 0; i < route.Lists.Count; i++)
        {
            var routeList = route.Lists[i];
            var key = routeList.Snapshot.Key + "|" + id + "|" + dangerLevel.ToString(CultureInfo.InvariantCulture);
            if (!cache.TryGetValue(key, out var probability))
            {
                var distribution = GetSpawnDistribution(routeList.Snapshot, dangerLevel);
                distribution.TryGetValue(id, out probability);
                cache[key] = probability;
            }
            total += probability * routeList.Weight;
        }
        return total;
    }

    private List<int> BuildRelevantDangerLevels(IReadOnlyList<BiomeRoute> routes, int startingDangerLevel)
    {
        startingDangerLevel = Math.Max(1, startingDangerLevel);
        var levels = new SortedSet<int>();
        for (var offset = 0; offset < 12; offset++)
        {
            var level = (long)startingDangerLevel + offset;
            if (level <= int.MaxValue)
                levels.Add((int)level);
        }
        for (var routeIndex = 0; routeIndex < routes.Count; routeIndex++)
        {
            var route = routes[routeIndex];
            for (var listIndex = 0; listIndex < route.Lists.Count; listIndex++)
            {
                var entries = route.Lists[listIndex].Snapshot.Entries;
                for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    var rowLevel = Math.Max(1, entries[entryIndex].Level);
                    var lowerLevel = Math.Max(startingDangerLevel, Math.Max(1, rowLevel - 23));
                    var upperLevel = rowLevel == int.MaxValue ? int.MaxValue : rowLevel + 1;
                    for (var level = lowerLevel; level <= upperLevel; level++)
                    {
                        levels.Add(level);
                        if (level == int.MaxValue)
                            break;
                    }
                }
            }
        }
        return levels.ToList();
    }

    private void AddSpecialDungeonRoutes(NpcRecord npc, ICollection<BiomeRoute> routes)
    {
        if (string.Equals(npc.Race, "yeek", StringComparison.OrdinalIgnoreCase) && npc.Quality == 0)
            routes.Add(CreateSpecialRaceRoute(
                T("耶鲁士地牢（专属分支）", "Yeek dungeon (special branch)"),
                "dungeon_yeek",
                row => string.Equals(row.race, "yeek", StringComparison.OrdinalIgnoreCase) && row.quality == 0,
                0.8d));
        if (npc.Quality == 0 && new[] { "dragon", "drake", "wyvern", "lizardman", "dinosaur" }
            .Contains(npc.Race, StringComparer.OrdinalIgnoreCase))
            routes.Add(CreateSpecialRaceRoute(
                T("龙窟（专属分支）", "Dragon dungeon (special branch)"),
                "dungeon_dragon",
                IsDragonDungeonNpc,
                0.8d));
        if (string.Equals(npc.Race, "minotaur", StringComparison.OrdinalIgnoreCase) && npc.Quality == 0)
            routes.Add(CreateSpecialRaceRoute(
                T("米诺陶地牢（专属分支）", "Minotaur dungeon (special branch)"),
                "dungeon_mino",
                row => string.Equals(row.race, "minotaur", StringComparison.OrdinalIgnoreCase) && row.quality == 0,
                0.8d));
    }

    private BiomeRoute CreateSpecialRaceRoute(
        string name,
        string listId,
        Func<SourceChara.Row, bool> predicate,
        double routeWeight = 1d)
    {
        var route = new BiomeRoute { Name = name };
        try
        {
            var snapshot = CreateSnapshotFromRuntimeList(
                SpawnListChara.Get(listId, predicate),
                "special:" + listId,
                listId);
            if (snapshot != null && snapshot.Entries.Count > 0)
                route.Lists.Add(new RouteList { Snapshot = snapshot, Weight = routeWeight });
        }
        catch
        {
        }
        return route;
    }

    private static bool IsDragonDungeonNpc(SourceChara.Row row)
    {
        if (row == null || row.quality != 0)
            return false;
        return string.Equals(row.race, "dragon", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(row.race, "drake", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(row.race, "wyvern", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(row.race, "lizardman", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(row.race, "dinosaur", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyCurrentSpecialDungeonOverride(
        Zone zone,
        IReadOnlyDictionary<int, double> filterLevels,
        IDictionary<string, ProbabilityAccumulator> normal)
    {
        BiomeRoute? route = null;
        if (zone is Zone_DungeonYeek)
            route = CreateSpecialRaceRoute(
                T("耶鲁士地牢专属", "Yeek dungeon special"),
                "dungeon_yeek",
                row => string.Equals(row.race, "yeek", StringComparison.OrdinalIgnoreCase) && row.quality == 0);
        else if (zone is Zone_DungeonDragon)
            route = CreateSpecialRaceRoute(
                T("龙窟专属", "Dragon dungeon special"),
                "dungeon_dragon",
                IsDragonDungeonNpc);
        else if (zone is Zone_DungeonMino)
            route = CreateSpecialRaceRoute(
                T("米诺陶地牢专属", "Minotaur dungeon special"),
                "dungeon_mino",
                row => string.Equals(row.race, "minotaur", StringComparison.OrdinalIgnoreCase) && row.quality == 0);
        if (route == null || route.Lists.Count == 0)
            return;

        foreach (var accumulator in normal.Values)
            ScaleProbability(accumulator, 0.2d);
        var special = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var levelPair in filterLevels)
        {
            var distribution = GetSpawnDistribution(route.Lists[0].Snapshot, levelPair.Key);
            foreach (var pair in distribution)
            {
                special.TryGetValue(pair.Key, out var current);
                special[pair.Key] = current + pair.Value * levelPair.Value;
            }
        }
        foreach (var pair in special)
            AddProbability(normal, pair.Key, pair.Value * 0.8d, route.Name);
    }

    private static void AddProbability(
        IDictionary<string, ProbabilityAccumulator> accumulators,
        string id,
        double contribution,
        string route)
    {
        if (contribution <= 0d || string.IsNullOrEmpty(id))
            return;
        if (!accumulators.TryGetValue(id, out var accumulator))
        {
            accumulator = new ProbabilityAccumulator();
            accumulators.Add(id, accumulator);
        }
        accumulator.Total += contribution;
        accumulator.RouteContributions.TryGetValue(route, out var routeContribution);
        routeContribution += contribution;
        accumulator.RouteContributions[route] = routeContribution;
        if (routeContribution > accumulator.MainContribution)
        {
            accumulator.MainContribution = routeContribution;
            accumulator.MainRoute = route;
        }
    }

    private static void ScaleProbability(ProbabilityAccumulator accumulator, double factor)
    {
        accumulator.Total *= factor;
        accumulator.MainContribution *= factor;
        foreach (var route in accumulator.RouteContributions.Keys.ToList())
            accumulator.RouteContributions[route] *= factor;
    }

    private void ApplySeasonalSantaOverride(
        Zone zone,
        IDictionary<string, ProbabilityAccumulator> probabilities)
    {
        try
        {
            var date = GameAccess.World.CurrentWorld?.date;
            if (date == null || date.month != 12 || date.day < 24 || date.day > 26 || !zone.IsNefia || !zone.isRandomSite)
                return;
            var santaCount = Math.Max(0, GameAccess.Runtime.Player?.flags?.santa ?? 0);
            var denominator = 50d * (1d + santaCount) * (1d + santaCount);
            var probability = denominator <= 0d ? 0d : 1d / denominator;
            if (probability <= 0d)
                return;
            foreach (var accumulator in probabilities.Values)
                ScaleProbability(accumulator, 1d - probability);
            AddProbability(probabilities, "santa", probability, T("圣诞事件覆盖", "Christmas event override"));
        }
        catch
        {
        }
    }

    private Dictionary<BiomeProfile, double> GetCurrentZoneBiomeWeights(
        Map map,
        Zone zone,
        out string coverage,
        out string currentBiome)
    {
        var counts = new Dictionary<BiomeProfile, int>();
        var total = 0;
        var inspectedSpawnGrid = false;
        try
        {
            var bounds = map.bounds;
            var cells = map.cells;
            if (bounds != null && cells != null)
            {
                var minX = Math.Max(0, bounds.x);
                var minZ = Math.Max(0, bounds.z);
                var maxX = Math.Min(bounds.maxX, cells.GetLength(0) - 1);
                var maxZ = Math.Min(bounds.maxZ, cells.GetLength(1) - 1);
                inspectedSpawnGrid = minX <= maxX && minZ <= maxZ;
                for (var x = minX; x <= maxX; x++)
                {
                    for (var z = minZ; z <= maxZ; z++)
                    {
                        var cell = cells[x, z];
                        if (cell == null ||
                            !NpcInfoProbabilityMath.IsDefaultSpawnCandidate(cell.blocked, cell.hasDoor, cell.pcSync))
                            continue;
                        var biome = cell.biome;
                        if (biome == null)
                            continue;
                        counts.TryGetValue(biome, out var count);
                        counts[biome] = count + 1;
                        total++;
                    }
                }
            }
        }
        catch
        {
            counts.Clear();
            total = 0;
            inspectedSpawnGrid = false;
        }

        BiomeProfile? playerBiome = null;
        try { playerBiome = GameAccess.Characters.PlayerCharacter?.pos?.cell?.biome; }
        catch { }
        if (counts.Count == 0 && !inspectedSpawnGrid && playerBiome != null)
        {
            counts[playerBiome] = 1;
            total = 1;
        }
        currentBiome = playerBiome == null ? "-" : GetBiomeDisplayName(playerBiome);
        var weights = new Dictionary<BiomeProfile, double>();
        foreach (var pair in counts)
            weights[pair.Key] = total > 0 ? pair.Value / (double)total : 0d;

        if (weights.Count > 0 && zone.IsUnderwater)
        {
            foreach (var key in weights.Keys.ToList())
                weights[key] /= 15d;
            try
            {
                var sand = GameAccess.Runtime.Core?.refs?.biomes?.Sand;
                var water = GameAccess.Runtime.Core?.refs?.biomes?.Water;
                if (sand != null)
                    AddWeight(weights, sand, 14d / 60d);
                if (water != null)
                    AddWeight(weights, water, 42d / 60d);
            }
            catch
            {
            }
        }

        coverage = weights.Count == 0
            ? "-"
            : string.Join(" / ", weights
                .OrderByDescending(pair => pair.Value)
                .Take(6)
                .Select(pair => GetBiomeDisplayName(pair.Key) + " " +
                                (pair.Value * 100d).ToString("0.0", CultureInfo.InvariantCulture) + "%"));
        return weights;
    }

    private static void AddWeight(IDictionary<BiomeProfile, double> weights, BiomeProfile biome, double value)
    {
        weights.TryGetValue(biome, out var current);
        weights[biome] = current + value;
    }

    private static Dictionary<int, double> GetZoneFilterLevelWeights(Zone zone, int dangerLevel)
    {
        var result = new Dictionary<int, double>();
        if (zone.ScaleType != ZoneScaleType.Void)
        {
            result[dangerLevel] = 1d;
            return result;
        }
        var scaled = ((dangerLevel - 1) % 50 + 5) * 150 / 100;
        if (scaled < 20)
        {
            result[Math.Max(1, scaled)] = 1d;
            return result;
        }
        var dangerProbability = Math.Min(100, scaled) / 100d;
        result[dangerLevel] = dangerProbability;
        if (dangerProbability < 1d)
            result[Math.Max(1, scaled)] = 1d - dangerProbability;
        return result;
    }

    private List<string> FindSpawnListsForNpc(string id)
    {
        var result = new List<string>();
        for (var i = 0; i < _spawnListIds.Count; i++)
        {
            var snapshot = GetSpawnListSnapshot(_spawnListIds[i], false);
            if (snapshot != null && ContainsNpc(snapshot, id))
                result.Add(snapshot.Id);
        }
        return result;
    }
}
