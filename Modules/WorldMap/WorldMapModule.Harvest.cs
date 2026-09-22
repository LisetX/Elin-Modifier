using System;
using System.Collections.Generic;
using System.Globalization;

internal sealed partial class WorldMapModule
{
    private static readonly Dictionary<string, string[]> HarvestGrowthDrops =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "GrowSystemTree", new[] { "log", "branch", "bark", "leaf", "resin", "throw_putit" } },
            { "GrowSystemFlower", new[] { "grass", "flower" } },
            { "GrowSystemHerb", new[] { "grass", "flower" } },
            { "GrowSystemCha", new[] { "grass", "leaf_tea" } },
            { "GrowSystemBerry", new[] { "grass" } },
            { "GrowSystemCactus", new[] { "needle" } },
            { "GrowSystemKinoko", new[] { "#mushroom" } },
            { "GrowSystemPasture", new[] { "pasture", "grass" } },
            { "GrowSystemSeaweed", new[] { "seaweed2", "grass" } },
            { "GrowSystemWheat", new[] { "grass" } },
            { "GrowSystemWeed", new[] { "grass" } },
            { "GrowSystemPlant", new[] { "grass" } },
        };

    private List<WorldMapHarvestEntry>? _harvestIndex;

    internal void InvalidateHarvestIndex() => _harvestIndex = null;

    internal int HarvestIndexCount => EnsureHarvestIndex().Count;

    private List<WorldMapHarvestEntry> EnsureHarvestIndex()
    {
        if (_harvestIndex != null)
            return _harvestIndex;
        var entries = new List<WorldMapHarvestEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var biomes = EClass.core?.refs?.biomes?.dict;
            if (biomes != null)
            {
                foreach (var pair in biomes)
                {
                    if (pair.Value == null)
                        continue;
                    CollectClusterObjects(entries, seen, pair.Key, pair.Value);
                    CollectClusterThings(entries, seen, pair.Key, pair.Value);
                    CollectSpawnThings(entries, seen, pair.Key, pair.Value);
                    CollectTileYields(entries, seen, pair.Key, pair.Value);
                }
            }
        }
        catch
        {
        }
        _harvestIndex = entries;
        return entries;
    }

    private static void CollectClusterObjects(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        BiomeProfile profile)
    {
        List<BiomeProfile.ClusterObj>? clusters = null;
        try
        {
            clusters = profile.cluster?.obj;
        }
        catch
        {
        }
        if (clusters == null)
            return;
        for (var i = 0; i < clusters.Count; i++)
        {
            var items = clusters[i]?.items;
            if (items == null)
                continue;
            for (var j = 0; j < items.Count; j++)
            {
                try
                {
                    var item = items[j];
                    if (item == null || item.idObj <= 0)
                        continue;
                    var objId = item.idObj.ToString(CultureInfo.InvariantCulture);
                    var row = ResolveObjRow(item.idObj);
                    if (row == null)
                        continue;
                    var viaName = SafeName(() => row.GetName());
                    var harvestId = NormalizeSourceId(row.growth?.idHarvestThing);
                    if (harvestId.Length == 0)
                        AddEntry(entries, seen, biomeId, "", "", objId, viaName);
                    else
                        AddEntry(entries, seen, biomeId, harvestId, ResolveThingName(harvestId), objId, viaName);
                    CollectObjYields(entries, seen, biomeId, row, objId, viaName);
                }
                catch
                {
                }
            }
        }
    }

    private static void CollectClusterThings(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        BiomeProfile profile)
    {
        List<BiomeProfile.ClusterThing>? clusters = null;
        try
        {
            clusters = profile.cluster?.thing;
        }
        catch
        {
        }
        if (clusters == null)
            return;
        for (var i = 0; i < clusters.Count; i++)
        {
            var items = clusters[i]?.items;
            if (items == null)
                continue;
            for (var j = 0; j < items.Count; j++)
            {
                try
                {
                    var id = NormalizeSourceId(items[j]?.id);
                    if (id.Length == 0)
                        continue;
                    AddEntry(entries, seen, biomeId, id, ResolveThingName(id), "", "");
                }
                catch
                {
                }
            }
        }
    }

    private static void CollectSpawnThings(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        BiomeProfile profile)
    {
        List<BiomeProfile.SpawnListThing>? spawns = null;
        try
        {
            spawns = profile.spawn?.thing;
        }
        catch
        {
        }
        if (spawns == null)
            return;
        for (var i = 0; i < spawns.Count; i++)
        {
            try
            {
                var id = NormalizeSourceId(spawns[i]?.id);
                if (id.Length == 0)
                    continue;
                AddSpawnListYields(entries, seen, biomeId, id);
            }
            catch
            {
            }
        }
    }

    private static void AddSpawnListYields(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        string listId)
    {
        SourceSpawnList.Row? row = null;
        try
        {
            row = EClass.sources.spawnLists.GetRow(listId);
        }
        catch
        {
        }
        if (row == null)
        {
            AddEntry(entries, seen, biomeId, listId, ResolveThingName(listId), "", "");
            return;
        }

        var added = false;
        var cards = row.idCard;
        if (cards != null)
        {
            for (var i = 0; i < cards.Length; i++)
            {
                var id = NormalizeSourceId(cards[i]);
                if (id.Length == 0)
                    continue;
                AddEntry(entries, seen, biomeId, id, ResolveThingName(id), listId, "");
                added = true;
            }
        }

        var categories = row.category;
        if (categories != null)
        {
            for (var i = 0; i < categories.Length; i++)
            {
                var id = NormalizeSourceId(categories[i]);
                if (id.Length == 0)
                    continue;
                var key = "#" + id;
                AddEntry(entries, seen, biomeId, key, ResolveThingName(key), listId, "");
                added = true;
            }
        }

        if (!added)
            AddEntry(entries, seen, biomeId, listId, ResolveThingName(listId), "", "");
    }

    private static void AddEntry(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        string thingId,
        string thingName,
        string viaId,
        string viaName)
    {
        if (string.IsNullOrEmpty(thingId) && string.IsNullOrEmpty(viaId))
            return;
        var key = biomeId + "|" + thingId + "|" + viaId;
        if (!seen.Add(key))
            return;
        entries.Add(new WorldMapHarvestEntry(biomeId, thingId, thingName, viaId, viaName));
    }

    private static void CollectObjYields(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        SourceObj.Row row,
        string objId,
        string viaName)
    {
        GrowSystem? growth = null;
        try
        {
            growth = row.growth;
        }
        catch
        {
        }
        if (growth == null)
        {
            AddComponentYields(entries, seen, biomeId, SafeComponents(row), objId, viaName, false);
            return;
        }
        CollectGrowthDrops(entries, seen, biomeId, row, growth);
    }

    private static void CollectTileYields(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        BiomeProfile profile)
    {
        BiomeProfile.TileGroup? exterior = null;
        BiomeProfile.TileGroup? interior = null;
        try
        {
            exterior = profile.exterior;
            interior = profile.interior;
        }
        catch
        {
        }
        CollectTileGroupYields(entries, seen, biomeId, exterior);
        if (!ReferenceEquals(interior, exterior))
            CollectTileGroupYields(entries, seen, biomeId, interior);
    }

    private static void CollectTileGroupYields(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        BiomeProfile.TileGroup? group)
    {
        if (group == null)
            return;
        try
        {
            var block = group.block;
            if (block != null)
            {
                AddTileYield(entries, seen, biomeId, ResolveBlockRow(block.id), block.mat);
                AddTileYield(entries, seen, biomeId, ResolveBlockRow(block.idSub), block.matSub);
            }
            var floor = group.floor;
            if (floor != null)
            {
                AddTileYield(entries, seen, biomeId, ResolveFloorRow(floor.id), floor.mat);
                AddTileYield(entries, seen, biomeId, ResolveFloorRow(floor.idSub), floor.matSub);
            }
        }
        catch
        {
        }
    }

    private static TileRow? ResolveBlockRow(int id)
    {
        if (id <= 0)
            return null;
        try
        {
            var source = EClass.sources.blocks;
            if (source == null)
                return null;
            if (source.map != null && source.map.TryGetValue(id, out var mapped) && mapped != null)
                return mapped;
            var rows = source.rows;
            if (rows == null || id >= rows.Count)
                return null;
            var row = rows[id];
            return row != null && row.id == id ? row : null;
        }
        catch
        {
            return null;
        }
    }

    private static TileRow? ResolveFloorRow(int id)
    {
        if (id <= 0)
            return null;
        try
        {
            var source = EClass.sources.floors;
            if (source == null)
                return null;
            if (source.map != null && source.map.TryGetValue(id, out var mapped) && mapped != null)
                return mapped;
            var rows = source.rows;
            if (rows == null || id >= rows.Count)
                return null;
            var row = rows[id];
            return row != null && row.id == id ? row : null;
        }
        catch
        {
            return null;
        }
    }

    private static void AddTileYield(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        TileRow? row,
        int matId)
    {
        if (row == null)
            return;
        var tile = row;
        var viaId = NormalizeSourceId(tile.alias);
        if (viaId.Length == 0)
            viaId = tile.id.ToString(CultureInfo.InvariantCulture);
        var viaName = SafeName(() => tile.GetName());
        var matName = ResolveMaterialName(matId);
        if (matName.Length > 0)
            viaName = viaName.Length == 0 ? matName : viaName + " (" + matName + ")";
        AddComponentYields(entries, seen, biomeId, SafeComponents(tile), viaId, viaName, true);
    }

    private static string[]? SafeComponents(RenderRow row)
    {
        try
        {
            return row.components;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveMaterialName(int matId)
    {
        if (matId <= 0)
            return "";
        try
        {
            var source = EClass.sources.materials;
            if (source == null)
                return "";
            SourceMaterial.Row? row = null;
            if (source.map != null)
                source.map.TryGetValue(matId, out row);
            if (row == null)
            {
                var rows = source.rows;
                if (rows != null && matId < rows.Count && rows[matId] != null && rows[matId].id == matId)
                    row = rows[matId];
            }
            if (row == null)
                return "";
            var resolved = row;
            return SafeName(() => resolved.GetName());
        }
        catch
        {
            return "";
        }
    }

    private static void AddComponentYields(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        string[]? components,
        string viaId,
        string viaName,
        bool firstOnly)
    {
        if (components == null || components.Length == 0)
            return;
        for (var i = 0; i < components.Length; i++)
        {
            var id = ParseComponentId(components[i]);
            if (id.Length > 0)
                AddEntry(entries, seen, biomeId, id, ResolveThingName(id), viaId, viaName);
            if (firstOnly)
                return;
        }
    }

    private static string ParseComponentId(string? component)
    {
        var raw = NormalizeSourceId(component);
        if (raw.Length == 0 || string.Equals(raw, "-", StringComparison.Ordinal))
            return "";
        var cut = raw.IndexOf('/');
        if (cut >= 0)
            raw = raw.Substring(0, cut);
        cut = raw.IndexOfAny(new[] { '|', '@' });
        if (cut >= 0)
            raw = raw.Substring(0, cut);
        raw = raw.Trim();
        if (raw.Length > 1 && (raw[0] == '$' || raw[0] == '+'))
            raw = raw.Substring(1);
        return string.Equals(raw, "-", StringComparison.Ordinal) ? "" : raw;
    }

    private static void CollectGrowthDrops(
        List<WorldMapHarvestEntry> entries,
        HashSet<string> seen,
        string biomeId,
        SourceObj.Row row,
        GrowSystem growth)
    {
        var drops = ResolveGrowthDrops(growth.GetType());
        for (var i = 0; i < drops.Length; i++)
            AddEntry(entries, seen, biomeId, drops[i], ResolveThingName(drops[i]), "", "");
        var special = ResolveSpeciesDrop(row);
        if (special.Length > 0)
            AddEntry(entries, seen, biomeId, special, ResolveThingName(special), "", "");
    }

    private static string[] ResolveGrowthDrops(Type? type)
    {
        while (type != null)
        {
            if (HarvestGrowthDrops.TryGetValue(type.Name, out var drops))
                return drops;
            type = type.BaseType;
        }
        return Array.Empty<string>();
    }

    private static string ResolveSpeciesDrop(SourceObj.Row row)
    {
        try
        {
            if (row.id == 103)
                return "bamboo_shoot";
            if (row.id == 17)
                return "leaf_palulu";
            return string.Equals(row.alias, "grape", StringComparison.Ordinal) ? "vine" : "";
        }
        catch
        {
            return "";
        }
    }

    private static SourceObj.Row? ResolveObjRow(int idObj)
    {
        try
        {
            var source = EClass.sources.objs;
            var row = source.GetRow(idObj.ToString(CultureInfo.InvariantCulture));
            if (row != null)
                return row;
            var rows = source.rows;
            if (rows == null)
                return null;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].id == idObj)
                    return rows[i];
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeSourceId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return "";
        var trimmed = id!.Trim();
        if (trimmed.Length == 0 || string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
            return "";
        return trimmed;
    }

    private static string ResolveThingName(string id)
    {
        try
        {
            if (id.Length > 1 && id[0] == '#')
            {
                var category = EClass.sources.categories.GetRow(id.Substring(1));
                return category == null ? "" : SafeName(() => category.GetName());
            }
            var row = EClass.sources.things.GetRow(id);
            if (row == null)
                return "";
            var name = SafeName(() => row.GetName());
            return name.Length > 0 ? name : id;
        }
        catch
        {
            return id;
        }
    }

    internal bool MatchesHarvestEntry(WorldMapHarvestEntry entry) =>
        ScoreAny(entry.ThingName, entry.ThingId, entry.ViaName, entry.ViaId) > 0;

    internal string DescribeHarvestEntry(WorldMapHarvestEntry entry)
    {
        var product = string.IsNullOrEmpty(entry.ThingName) ? entry.ThingId : entry.ThingName;
        if (string.IsNullOrEmpty(product))
            product = string.IsNullOrEmpty(entry.ViaName) ? entry.ViaId : entry.ViaName;
        if (entry.ThingId.Length > 1 && entry.ThingId[0] == '#')
            product += Text("(类别)", " (category)");
        var via = string.IsNullOrEmpty(entry.ViaName) ? entry.ViaId : entry.ViaName;
        if (string.IsNullOrEmpty(via) || string.Equals(via, product, StringComparison.Ordinal))
            return product + " @ " + entry.BiomeId;
        return product + " <- " + via + " @ " + entry.BiomeId;
    }
}

internal sealed class WorldMapHarvestEntry
{
    internal readonly string BiomeId;
    internal readonly string ThingId;
    internal readonly string ThingName;
    internal readonly string ViaId;
    internal readonly string ViaName;

    internal WorldMapHarvestEntry(string biomeId, string thingId, string thingName, string viaId, string viaName)
    {
        BiomeId = biomeId ?? "";
        ThingId = thingId ?? "";
        ThingName = thingName ?? "";
        ViaId = viaId ?? "";
        ViaName = viaName ?? "";
    }
}
