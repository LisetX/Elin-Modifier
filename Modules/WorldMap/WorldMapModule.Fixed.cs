using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using LZ4;

internal sealed partial class WorldMapModule
{
    private const int WorldMapMaxFixedRowsPerZone = 400;
    private const long WorldMapMaxExportBytes = 16L * 1024L * 1024L;

    private readonly Dictionary<string, List<WorldMapFixedEntry>> _fixedIndex =
        new Dictionary<string, List<WorldMapFixedEntry>>(StringComparer.OrdinalIgnoreCase);

    internal string GetZoneExportId(Zone? zone)
    {
        if (zone == null)
            return "";
        try
        {
            return zone.idExport ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal List<WorldMapFixedEntry> EnsureFixedIndex(string exportId)
    {
        if (_fixedIndex.TryGetValue(exportId, out var cached))
            return cached;
        var entries = BuildFixedIndex(exportId);
        _fixedIndex[exportId] = entries;
        return entries;
    }

    internal bool MatchesFixedEntry(WorldMapFixedEntry entry) =>
        ScoreFixedEntry(entry) > 0;

    internal int ScoreFixedEntry(WorldMapFixedEntry entry) =>
        ScoreAny(entry.ThingName, entry.ThingId, entry.Detail);

    internal string DescribeFixedEntry(WorldMapFixedEntry entry, string zoneName)
    {
        var product = entry.ThingName.Length > 0 ? entry.ThingName : entry.ThingId;
        if (entry.IsChara && entry.Detail.Length > 0)
            product += " (" + entry.Detail + ")";
        return zoneName.Length == 0 ? product : product + " @ " + zoneName;
    }

    private static List<WorldMapFixedEntry> BuildFixedIndex(string exportId)
    {
        var entries = new List<WorldMapFixedEntry>();
        var json = ReadZoneExportJson(exportId);
        if (json.Length == 0)
            return entries;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        CollectExportIds(json, ids, WorldMapMaxFixedRowsPerZone * 4);
        foreach (var id in ids)
        {
            if (entries.Count >= WorldMapMaxFixedRowsPerZone)
                break;
            var entry = ResolveFixedEntry(id);
            if (entry != null)
                entries.Add(entry);
        }
        entries.Sort((a, b) =>
        {
            if (a.IsChara != b.IsChara)
                return a.IsChara ? 1 : -1;
            return string.Compare(a.ThingName, b.ThingName, StringComparison.Ordinal);
        });
        return entries;
    }

    private static WorldMapFixedEntry? ResolveFixedEntry(string id)
    {
        try
        {
            var thing = EClass.sources.things.GetRow(id);
            if (thing != null)
                return new WorldMapFixedEntry(id, SafeName(() => thing.GetName()), "", false);
        }
        catch
        {
        }

        try
        {
            var chara = EClass.sources.charas.GetRow(id);
            if (chara != null)
                return new WorldMapFixedEntry(
                    id,
                    SafeName(() => chara.GetName()),
                    DescribeCharaRow(chara),
                    true);
        }
        catch
        {
        }

        return null;
    }

    private static string DescribeCharaRow(SourceChara.Row row)
    {
        var job = ResolveSubRowName(() => EClass.sources.jobs.GetRow(row.job));
        var race = ResolveSubRowName(() => EClass.sources.races.GetRow(row.race));
        if (job.Length == 0)
            return race;
        return race.Length == 0 ? job : job + " / " + race;
    }

    private static string ResolveSubRowName(Func<SourceData.BaseRow?> read)
    {
        try
        {
            var row = read();
            return row == null ? "" : SafeName(() => row.GetName());
        }
        catch
        {
            return "";
        }
    }

    private static string ReadZoneExportJson(string exportId)
    {
        string path;
        try
        {
            path = CorePath.ZoneSave + exportId + ".z";
            if (!File.Exists(path))
                return "";
        }
        catch
        {
            return "";
        }

        try
        {
            using var file = File.OpenRead(path);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            var entry = archive.GetEntry("export");
            if (entry == null || entry.Length > WorldMapMaxExportBytes)
                return "";
            using var packed = entry.Open();
            using var buffer = new MemoryStream();
            packed.CopyTo(buffer);
            buffer.Position = 0;
            using var lz4 = new LZ4Stream(buffer, LZ4StreamMode.Decompress, LZ4StreamFlags.None, 1 << 20);
            using var reader = new StreamReader(lz4, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    private static void CollectExportIds(string json, HashSet<string> ids, int limit)
    {
        CollectExportIdsAfter(json, "\"B\":", false, ids, limit);
        CollectExportIdsAfter(json, "\"strs\":", true, ids, limit);
    }

    private static void CollectExportIdsAfter(
        string json,
        string key,
        bool insideArray,
        HashSet<string> ids,
        int limit)
    {
        var index = 0;
        while (ids.Count < limit)
        {
            var at = json.IndexOf(key, index, StringComparison.Ordinal);
            if (at < 0)
                return;
            index = at + key.Length;
            var cursor = SkipExportWhitespace(json, index);
            if (insideArray)
            {
                if (cursor >= json.Length || json[cursor] != '[')
                    continue;
                cursor = SkipExportWhitespace(json, cursor + 1);
            }
            if (cursor >= json.Length || json[cursor] != '"')
                continue;
            var end = json.IndexOf('"', cursor + 1);
            if (end < 0)
                return;
            if (end > cursor + 1)
                ids.Add(json.Substring(cursor + 1, end - cursor - 1));
            index = end + 1;
        }
    }

    private static int SkipExportWhitespace(string json, int index)
    {
        while (index < json.Length)
        {
            var c = json[index];
            if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                break;
            index++;
        }
        return index;
    }
}

internal sealed class WorldMapFixedEntry
{
    internal readonly string ThingId;
    internal readonly string ThingName;
    internal readonly string Detail;
    internal readonly bool IsChara;

    internal WorldMapFixedEntry(string thingId, string thingName, string detail, bool isChara)
    {
        ThingId = thingId ?? "";
        ThingName = thingName ?? "";
        Detail = detail ?? "";
        IsChara = isChara;
    }
}
