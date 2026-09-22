using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

internal sealed partial class WorldMapModule
{
    private const int WorldMapMaxSearchHits = 400;
    private const int WorldMapMaxHarvestHits = 200;
    private const int WorldMapMaxNpcHits = 120;
    private const int WorldMapMaxFixedHits = 120;

    private string _worldToken = "";
    private string _searchQuery = "";
    private string[] _searchTerms = Array.Empty<string>();
    private int _searchVersion;
    private HashSet<int>? _searchCells;
    private List<WorldMapSearchHit>? _searchHits;

    internal string SearchQuery => _searchQuery;
    internal IReadOnlyList<WorldMapSearchHit> SearchHits =>
        _searchHits ?? (IReadOnlyList<WorldMapSearchHit>)Array.Empty<WorldMapSearchHit>();

    internal bool EnsureWorldIdentity()
    {
        var token = BuildWorldToken();
        if (token.Length == 0)
            return false;
        if (string.Equals(token, _worldToken, StringComparison.Ordinal))
            return false;
        _worldToken = token;
        _terrain = null;
        ClearNavigationTarget();
        InvalidateHarvestIndex();
        _searchQuery = "";
        _searchCells = null;
        _searchHits = null;
        InvalidateTileColors();
        ClearNpcSnapshots();
        ReleaseWorldTexture();
        ReleaseLocalTexture();
        return true;
    }

    internal string WorldToken => _worldToken;

    private static string BuildWorldToken()
    {
        try
        {
            var game = EClass.game;
            if (game == null)
                return "";
            return (Game.id ?? "") + "|" + game.seed.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }

    internal string BuildZoneStateSignature(List<WorldMapZoneEntry> zones) => BuildZoneSignature(zones);

    internal string DescribeZoneExpiry(Zone? zone)
    {
        if (zone == null)
            return "";
        try
        {
            var expire = zone.dateExpire;
            if (expire <= 0)
                return "";
            var remaining = expire - EClass.world.date.GetRaw(0);
            if (remaining <= 0)
                return Text("已过期", "expired");
            return DescribeMinutes(remaining);
        }
        catch
        {
            return "";
        }
    }

    internal string DescribeZoneRegenerate(Zone? zone)
    {
        if (zone == null)
            return "";
        try
        {
            var target = zone.dateRegenerate;
            if (target <= 0)
                return "";
            var remaining = target - EClass.world.date.GetRaw(0);
            return remaining <= 0 ? Text("可重置", "ready") : DescribeMinutes(remaining);
        }
        catch
        {
            return "";
        }
    }

    private string DescribeMinutes(int minutes)
    {
        if (minutes >= 1440)
            return (minutes / 1440).ToString(CultureInfo.InvariantCulture) + Text(" 天", " d");
        if (minutes >= 60)
            return (minutes / 60).ToString(CultureInfo.InvariantCulture) + Text(" 小时", " h");
        return minutes.ToString(CultureInfo.InvariantCulture) + Text(" 分钟", " min");
    }

    internal int GetRoadDistance(int gridX, int gridY)
    {
        try
        {
            var elomap = GetEloMap();
            return elomap == null ? -1 : elomap.GetRoadDist(gridX, gridY);
        }
        catch
        {
            return -1;
        }
    }

    internal string DescribeTravelCost(WorldMapZoneEntry zone)
    {
        try
        {
            var source = zone.Zone.source;
            if (source == null)
                return "";
            var parts = new List<string>();
            if (source.cost > 0)
                parts.Add(Text("旅行", "travel") + " " + source.cost.ToString(CultureInfo.InvariantCulture));
            if (source.costSkyTravel > 0)
                parts.Add(Text("空路", "sky") + " " + source.costSkyTravel.ToString(CultureInfo.InvariantCulture));
            return string.Join(" / ", parts);
        }
        catch
        {
            return "";
        }
    }

    internal void SetSearchQuery(string? query)
    {
        var normalized = (query ?? "").Trim();
        if (string.Equals(normalized, _searchQuery, StringComparison.Ordinal))
            return;
        _searchQuery = normalized;
        _searchTerms = normalized.Length == 0
            ? Array.Empty<string>()
            : normalized.Split(new[] { ' ', '\t', '\u3000' }, StringSplitOptions.RemoveEmptyEntries);
        RebuildSearchResults();
    }

    internal void RefreshSearch()
    {
        RebuildSearchResults();
    }

    internal bool IsSearchCell(int cellIndex) =>
        _searchCells != null && _searchCells.Contains(cellIndex);

    private void RebuildSearchResults()
    {
        _searchVersion++;
        _searchCells = null;
        _searchHits = null;
        if (_searchQuery.Length == 0 || _terrain == null)
            return;

        var terrain = _terrain;
        var cells = new HashSet<int>();
        var hits = new List<WorldMapSearchHit>();

        var matchedSlots = new bool[Math.Max(1, terrain.SourceCount)];
        var slotLabels = new string[matchedSlots.Length];
        var anySlot = false;
        for (var slot = 0; slot < terrain.SourceCount; slot++)
        {
            var row = terrain.GetSourceAt(slot);
            if (row == null)
                continue;
            var label = DescribeTileMatch(row);
            if (label == null)
                continue;
            matchedSlots[slot] = true;
            slotLabels[slot] = label;
            anySlot = true;
        }

        if (anySlot)
        {
            var reported = new HashSet<int>();
            for (var y = 0; y < terrain.Height; y++)
            {
                for (var x = 0; x < terrain.Width; x++)
                {
                    var slot = terrain.GetSourceIndex(x, y);
                    if (slot < 0 || slot >= matchedSlots.Length || !matchedSlots[slot])
                        continue;
                    cells.Add(y * terrain.Width + x);
                    if (reported.Add(slot) && hits.Count < WorldMapMaxSearchHits)
                        hits.Add(new WorldMapSearchHit(
                            x + terrain.MinX,
                            y + terrain.MinY,
                            slotLabels[slot] ?? "",
                            false,
                            ScoreValue(slotLabels[slot])));
                }
            }
        }

        var zones = EnumerateZones();
        AppendHarvestMatches(terrain, zones, cells, hits);

        foreach (var zone in zones)
        {
            var label = DescribeZoneMatch(zone);
            if (label == null)
                continue;
            var x = zone.X - terrain.MinX;
            var y = zone.Y - terrain.MinY;
            if (terrain.Contains(x, y))
                cells.Add(y * terrain.Width + x);
            hits.Add(new WorldMapSearchHit(zone.X, zone.Y, label, true, ScoreValue(zone.Name) + 5));
        }

        AppendNpcMatches(terrain, zones, cells, hits);
        AppendFixedMatches(terrain, zones, cells, hits);

        hits.Sort((a, b) =>
        {
            if (a.Score != b.Score)
                return b.Score.CompareTo(a.Score);
            if (a.IsZone != b.IsZone)
                return a.IsZone ? -1 : 1;
            return string.Compare(a.Label, b.Label, StringComparison.Ordinal);
        });
        _searchCells = cells;
        _searchHits = hits;
    }

    private void AppendNpcMatches(
        WorldMapTerrain terrain,
        List<WorldMapZoneEntry> zones,
        HashSet<int> cells,
        List<WorldMapSearchHit> hits)
    {
        var budget = WorldMapMaxNpcHits;
        for (var z = 0; z < zones.Count && budget > 0 && hits.Count < WorldMapMaxSearchHits; z++)
        {
            var zone = zones[z];
            var npcs = ListZoneNpcs(zone.Zone);
            if (npcs.Count == 0)
                continue;
            var x = zone.X - terrain.MinX;
            var y = zone.Y - terrain.MinY;
            var inside = terrain.Contains(x, y);
            for (var i = 0; i < npcs.Count && budget > 0 && hits.Count < WorldMapMaxSearchHits; i++)
            {
                var npc = npcs[i];
                var score = ScoreAny(npc.Name, npc.Id, npc.RaceName, npc.JobName, npc.FactionId);
                if (score <= 0)
                    continue;
                if (inside)
                    cells.Add(y * terrain.Width + x);
                hits.Add(new WorldMapSearchHit(
                    zone.X,
                    zone.Y,
                    DescribeNpcHit(npc, zone.Name),
                    false,
                    score + 3));
                budget--;
            }
        }
    }

    private static string DescribeNpcHit(WorldMapNpcEntry npc, string zoneName)
    {
        var label = npc.Name.Length > 0 ? npc.Name : npc.Id;
        var detail = npc.JobName.Length > 0 ? npc.JobName : npc.RaceName;
        if (detail.Length > 0)
            label += " (" + detail + ")";
        return zoneName.Length == 0 ? label : label + " @ " + zoneName;
    }

    private void AppendFixedMatches(
        WorldMapTerrain terrain,
        List<WorldMapZoneEntry> zones,
        HashSet<int> cells,
        List<WorldMapSearchHit> hits)
    {
        var budget = WorldMapMaxFixedHits;
        for (var z = 0; z < zones.Count && budget > 0 && hits.Count < WorldMapMaxSearchHits; z++)
        {
            var zone = zones[z];
            var exportId = GetZoneExportId(zone.Zone);
            if (exportId.Length == 0)
                continue;
            var entries = EnsureFixedIndex(exportId);
            if (entries.Count == 0)
                continue;
            var x = zone.X - terrain.MinX;
            var y = zone.Y - terrain.MinY;
            var inside = terrain.Contains(x, y);
            for (var i = 0; i < entries.Count && budget > 0 && hits.Count < WorldMapMaxSearchHits; i++)
            {
                var entry = entries[i];
                var score = ScoreFixedEntry(entry);
                if (score <= 0)
                    continue;
                if (inside)
                    cells.Add(y * terrain.Width + x);
                hits.Add(new WorldMapSearchHit(
                    zone.X,
                    zone.Y,
                    DescribeFixedEntry(entry, zone.Name),
                    false,
                    score));
                budget--;
            }
        }
    }

    private void AppendHarvestMatches(
        WorldMapTerrain terrain,
        List<WorldMapZoneEntry> zones,
        HashSet<int> cells,
        List<WorldMapSearchHit> hits)
    {
        var index = EnsureHarvestIndex();
        if (index.Count == 0)
            return;

        var byBiome = new Dictionary<string, List<WorldMapHarvestEntry>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < index.Count; i++)
        {
            var entry = index[i];
            if (entry.BiomeId.Length == 0 || !MatchesHarvestEntry(entry))
                continue;
            if (!byBiome.TryGetValue(entry.BiomeId, out var list))
            {
                list = new List<WorldMapHarvestEntry>();
                byBiome.Add(entry.BiomeId, list);
            }
            list.Add(entry);
        }
        if (byBiome.Count == 0)
            return;

        TryGetPlayerGrid(out var playerX, out var playerY);
        var best = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bestScore = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var slotBiomes = new string[Math.Max(1, terrain.SourceCount)];
        for (var slot = 0; slot < terrain.SourceCount; slot++)
        {
            var row = terrain.GetSourceAt(slot);
            slotBiomes[slot] = row?.idBiome ?? "";
        }

        for (var y = 0; y < terrain.Height; y++)
        {
            for (var x = 0; x < terrain.Width; x++)
            {
                var slot = terrain.GetSourceIndex(x, y);
                if (slot < 0 || slot >= slotBiomes.Length)
                    continue;
                var biome = slotBiomes[slot];
                if (biome.Length == 0 || !byBiome.ContainsKey(biome))
                    continue;
                var cellIndex = y * terrain.Width + x;
                cells.Add(cellIndex);
                var gridX = x + terrain.MinX;
                var gridY = y + terrain.MinY;
                var score = Math.Max(Math.Abs(gridX - playerX), Math.Abs(gridY - playerY));
                if (!bestScore.TryGetValue(biome, out var current) || score < current)
                {
                    bestScore[biome] = score;
                    best[biome] = cellIndex;
                }
            }
        }

        for (var z = 0; z < zones.Count; z++)
        {
            var zone = zones[z];
            var biome = SafeZoneBiome(zone);
            if (biome.Length == 0 || !byBiome.ContainsKey(biome))
                continue;
            var x = zone.X - terrain.MinX;
            var y = zone.Y - terrain.MinY;
            if (!terrain.Contains(x, y))
                continue;
            var cellIndex = y * terrain.Width + x;
            cells.Add(cellIndex);
            var score = Math.Max(Math.Abs(zone.X - playerX), Math.Abs(zone.Y - playerY));
            if (!bestScore.TryGetValue(biome, out var current) || score < current)
            {
                bestScore[biome] = score;
                best[biome] = cellIndex;
            }
        }

        var budget = WorldMapMaxHarvestHits;
        foreach (var pair in byBiome)
        {
            if (budget <= 0 || hits.Count >= WorldMapMaxSearchHits)
                break;
            if (!best.TryGetValue(pair.Key, out var cellIndex))
                continue;
            var gridX = cellIndex % terrain.Width + terrain.MinX;
            var gridY = cellIndex / terrain.Width + terrain.MinY;
            for (var i = 0; i < pair.Value.Count && budget > 0 && hits.Count < WorldMapMaxSearchHits; i++)
            {
                budget--;
                var entry = pair.Value[i];
                hits.Add(new WorldMapSearchHit(
                    gridX,
                    gridY,
                    DescribeHarvestEntry(entry),
                    false,
                    ScoreAny(entry.ThingName, entry.ThingId, entry.ViaName, entry.ViaId)));
            }
        }
    }

    private static string SafeZoneBiome(WorldMapZoneEntry zone)
    {
        try
        {
            return zone.Zone.source?.idBiome ?? "";
        }
        catch
        {
            return "";
        }
    }

    private string? DescribeTileMatch(SourceGlobalTile.Row row)
    {
        var name = SafeName(() => row.GetName());
        if (Matches(name) || Matches(row.name) || Matches(row.alias))
            return string.IsNullOrEmpty(name) ? row.alias ?? "" : name;
        if (Matches(row.idBiome))
            return Text("群落", "Biome") + ": " + row.idBiome;
        if (Matches(row.zoneProfile))
            return Text("区块配置", "Zone profile") + ": " + row.zoneProfile;
        var tag = MatchArray(row.tag);
        if (tag != null)
            return Text("标签", "Tags") + ": " + tag;
        var trait = MatchArray(row.trait);
        return trait != null ? Text("特性", "Traits") + ": " + trait : null;
    }

    private string? DescribeZoneMatch(WorldMapZoneEntry zone)
    {
        if (Matches(zone.Name) || Matches(zone.TypeName))
            return zone.Name;
        SourceZone.Row? source = null;
        try
        {
            source = zone.Zone.source;
        }
        catch
        {
        }
        if (source == null)
            return null;
        if (Matches(source.idBiome) || Matches(source.faction) || Matches(source.idGen) || Matches(source.idProfile))
            return zone.Name;
        var tag = MatchArray(source.tag);
        if (tag != null)
            return zone.Name + " [" + tag + "]";
        var questTag = MatchArray(source.questTag);
        return questTag != null ? zone.Name + " [" + questTag + "]" : null;
    }

    private bool Matches(string? value) => ScoreValue(value) > 0;

    internal int ScoreValue(string? value)
    {
        if (string.IsNullOrEmpty(value) || _searchTerms.Length == 0)
            return 0;
        var total = 0;
        for (var i = 0; i < _searchTerms.Length; i++)
        {
            var score = ScoreTerm(value!, _searchTerms[i]);
            if (score <= 0)
                return 0;
            total += score;
        }
        return total;
    }

    internal int ScoreAny(params string?[] values)
    {
        if (_searchTerms.Length == 0)
            return 0;
        var total = 0;
        for (var i = 0; i < _searchTerms.Length; i++)
        {
            var best = 0;
            for (var j = 0; j < values.Length; j++)
            {
                var value = values[j];
                if (string.IsNullOrEmpty(value))
                    continue;
                var score = ScoreTerm(value!, _searchTerms[i]);
                if (score > best)
                    best = score;
            }
            if (best <= 0)
                return 0;
            total += best;
        }
        return total;
    }

    private static int ScoreTerm(string value, string term)
    {
        if (term.Length == 0)
            return 0;
        if (string.Equals(value, term, StringComparison.OrdinalIgnoreCase))
            return 100;
        var index = value.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (index == 0)
            return 80;
        if (index > 0)
            return Math.Max(40, 70 - index);
        return IsSubsequence(value, term) ? 15 : 0;
    }

    private static bool IsSubsequence(string value, string term)
    {
        var cursor = 0;
        for (var i = 0; i < term.Length; i++)
        {
            var needle = char.ToLowerInvariant(term[i]);
            var found = false;
            while (cursor < value.Length)
            {
                if (char.ToLowerInvariant(value[cursor++]) == needle)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;
        }
        return true;
    }

    private string? MatchArray(string[]? values)
    {
        if (values == null)
            return null;
        for (var i = 0; i < values.Length; i++)
        {
            if (Matches(values[i]))
                return values[i];
        }
        return null;
    }

}

internal sealed class WorldMapSearchHit
{
    internal readonly int GridX;
    internal readonly int GridY;
    internal readonly string Label;
    internal readonly bool IsZone;
    internal readonly int Score;

    internal WorldMapSearchHit(int gridX, int gridY, string label, bool isZone, int score)
    {
        GridX = gridX;
        GridY = gridY;
        Label = label;
        IsZone = isZone;
        Score = score;
    }
}
