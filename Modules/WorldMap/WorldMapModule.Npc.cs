using System;
using System.Collections.Generic;
using System.IO;

internal sealed partial class WorldMapModule
{
    internal const int WorldMapMaxNpcRows = 400;
    private const long WorldMapMaxMapFileBytes = 64L * 1024L * 1024L;

    private readonly Dictionary<int, List<WorldMapNpcEntry>> _npcSnapshots = new Dictionary<int, List<WorldMapNpcEntry>>();

    internal bool HasNpcSnapshot(Zone? zone)
    {
        var uid = SafeZoneUid(zone);
        return uid != 0 && _npcSnapshots.ContainsKey(uid);
    }

    internal void ClearNpcSnapshots() => _npcSnapshots.Clear();

    internal WorldMapNpcLoadResult TryLoadZoneNpcSnapshot(Zone? zone, bool force)
    {
        if (zone == null)
            return WorldMapNpcLoadResult.Unavailable;
        if (IsZoneMapLoaded(zone))
            return WorldMapNpcLoadResult.AlreadyLive;

        var uid = SafeZoneUid(zone);
        if (uid == 0)
            return WorldMapNpcLoadResult.Unavailable;
        if (!force && _npcSnapshots.ContainsKey(uid))
            return WorldMapNpcLoadResult.Cached;

        string path;
        try
        {
            if (!zone.isGenerated || zone.isImported)
                return WorldMapNpcLoadResult.Unavailable;
            path = zone.pathSave + "map";
            if (!File.Exists(path) || new FileInfo(path).Length > WorldMapMaxMapFileBytes)
                return WorldMapNpcLoadResult.Unavailable;
        }
        catch
        {
            return WorldMapNpcLoadResult.Unavailable;
        }

        List<Chara>? charas;
        try
        {
            var map = GameIO.LoadFile<Map>(path);
            charas = map?.serializedCharas;
            if (charas == null || charas.Count == 0)
                charas = map?.charas;
        }
        catch
        {
            return WorldMapNpcLoadResult.Failed;
        }
        if (charas == null)
            return WorldMapNpcLoadResult.Failed;

        var entries = new List<WorldMapNpcEntry>();
        for (var i = 0; i < charas.Count && entries.Count < WorldMapMaxNpcRows; i++)
        {
            var chara = charas[i];
            if (chara == null)
                continue;
            try
            {
                entries.Add(new WorldMapNpcEntry(chara, true));
            }
            catch
            {
            }
        }
        entries.Sort(CompareNpcEntries);
        _npcSnapshots[uid] = entries;
        return WorldMapNpcLoadResult.Loaded;
    }

    private static bool SafeIsLoaded(Zone zone)
    {
        try { return zone.IsLoaded; }
        catch { return false; }
    }

    private static int SafeZoneUid(Zone? zone)
    {
        if (zone == null)
            return 0;
        try { return zone.uid; }
        catch { return 0; }
    }

    internal WorldMapNpcOrigin GetZoneNpcOrigin(Zone? zone)
    {
        if (zone == null)
            return WorldMapNpcOrigin.None;
        if (IsZoneMapLoaded(zone))
            return WorldMapNpcOrigin.Live;
        return HasNpcSnapshot(zone) ? WorldMapNpcOrigin.Snapshot : WorldMapNpcOrigin.Citizens;
    }

    internal int CountZoneNpcs(Zone? zone)
    {
        if (zone == null)
            return -1;
        var live = TryGetLiveCharas(zone);
        if (live != null)
            return live.Count;
        if (SafeIsLoaded(zone))
            return 0;
        var uid = SafeZoneUid(zone);
        if (uid != 0 && _npcSnapshots.TryGetValue(uid, out var snapshot))
            return snapshot.Count;
        try
        {
            return zone.dictCitizen?.Count ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    internal bool IsZoneMapLoaded(Zone? zone) =>
        zone != null && (TryGetLiveCharas(zone) != null || SafeIsLoaded(zone));

    private static List<Chara>? TryGetLiveCharas(Zone? zone)
    {
        if (zone == null)
            return null;
        try
        {
            var charas = zone.map?.charas;
            return charas is { Count: > 0 } ? charas : null;
        }
        catch
        {
            return null;
        }
    }

    internal List<WorldMapNpcEntry> ListZoneNpcs(Zone? zone)
    {
        var result = new List<WorldMapNpcEntry>();
        if (zone == null)
            return result;

        var live = TryGetLiveCharas(zone);
        var uid = SafeZoneUid(zone);
        if (live != null)
        {
            for (var i = 0; i < live.Count && result.Count < WorldMapMaxNpcRows; i++)
            {
                var chara = live[i];
                if (chara == null)
                    continue;
                try
                {
                    result.Add(new WorldMapNpcEntry(chara, false));
                }
                catch
                {
                }
            }
        }
        else if (uid != 0 && _npcSnapshots.TryGetValue(uid, out var snapshot))
        {
            result.AddRange(snapshot);
        }
        else
        {
            Dictionary<int, string>? citizens = null;
            try
            {
                citizens = zone.dictCitizen;
            }
            catch
            {
            }
            if (citizens != null)
            {
                foreach (var pair in citizens)
                {
                    if (result.Count >= WorldMapMaxNpcRows)
                        break;
                    result.Add(new WorldMapNpcEntry(pair.Key, pair.Value ?? ""));
                }
            }
        }

        result.Sort(CompareNpcEntries);
        return result;
    }

    private static int CompareNpcEntries(WorldMapNpcEntry a, WorldMapNpcEntry b)
    {
        if (a.IsPlayer != b.IsPlayer)
            return a.IsPlayer ? -1 : 1;
        if (a.IsPlayerFaction != b.IsPlayerFaction)
            return a.IsPlayerFaction ? -1 : 1;
        if (a.IsCitizen != b.IsCitizen)
            return a.IsCitizen ? -1 : 1;
        var compare = b.Level.CompareTo(a.Level);
        return compare != 0 ? compare : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
    }
}

internal enum WorldMapNpcLoadResult
{
    Unavailable,
    AlreadyLive,
    Cached,
    Loaded,
    Failed,
}

internal enum WorldMapNpcOrigin
{
    None,
    Live,
    Snapshot,
    Citizens,
}

internal sealed class WorldMapNpcEntry
{
    internal readonly Chara? Chara;
    internal readonly int Uid;
    internal readonly string Name;
    internal readonly string Id;
    internal readonly string RaceName;
    internal readonly string JobName;
    internal readonly string FactionId;
    internal readonly int Level;
    internal readonly int PosX;
    internal readonly int PosZ;
    internal readonly string HostilityName;
    internal readonly bool IsLive;
    internal readonly bool IsSnapshot;
    internal readonly bool IsPlayer;
    internal readonly bool IsPlayerFaction;
    internal readonly bool IsCitizen;
    internal readonly bool IsGlobal;

    internal WorldMapNpcEntry(Chara chara, bool fromSnapshot)
    {
        Chara = fromSnapshot ? null : chara;
        IsLive = true;
        IsSnapshot = fromSnapshot;
        Uid = SafeRead(() => chara.uid, 0);
        Name = SafeText(() => chara.Name, chara.id ?? "");
        Id = chara.id ?? "";
        RaceName = SafeText(() => chara.race.GetName(), "");
        JobName = SafeText(() => chara.job.GetName(), "");
        FactionId = SafeText(() => chara.idFaction, "");
        Level = SafeRead(() => chara.LV, 0);
        PosX = SafeRead(() => chara.pos.x, -1);
        PosZ = SafeRead(() => chara.pos.z, -1);
        HostilityName = SafeText(() => chara.hostility.ToString(), "");
        IsPlayer = SafeRead(() => chara.IsPC ? 1 : 0, 0) != 0;
        IsPlayerFaction = SafeRead(() => chara.IsPCFaction ? 1 : 0, 0) != 0;
        IsCitizen = SafeRead(() => chara.trait.IsCitizen ? 1 : 0, 0) != 0;
        IsGlobal = SafeRead(() => chara.IsGlobal ? 1 : 0, 0) != 0;
    }

    internal WorldMapNpcEntry(int uid, string name)
    {
        Chara = null;
        IsLive = false;
        IsSnapshot = false;
        Uid = uid;
        Name = name;
        Id = "";
        RaceName = "";
        JobName = "";
        FactionId = "";
        Level = 0;
        PosX = -1;
        PosZ = -1;
        HostilityName = "";
        IsPlayer = false;
        IsPlayerFaction = false;
        IsCitizen = true;
        IsGlobal = false;
    }

    private static int SafeRead(Func<int> read, int fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private static string SafeText(Func<string> read, string fallback)
    {
        try
        {
            var value = read();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
