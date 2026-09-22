using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

internal sealed partial class WorldMapModule
{
    private readonly ElinModifierPlugin _host;
    private WorldMapTerrain? _terrain;
    private Texture2D? _worldTexture;
    private Sprite? _worldSprite;
    private Texture2D? _localTexture;
    private Sprite? _localSprite;
    private Texture2D? _markerTexture;
    private Sprite? _markerSprite;
    private Texture2D? _selectionTexture;
    private Sprite? _selectionSprite;
    private string _worldSignature = "";
    private string _localSignature = "";
    private readonly Dictionary<int, string> _pins = new Dictionary<int, string>();
    private bool _pinsLoaded;

    internal WorldMapModule(ElinModifierPlugin host)
    {
        _host = host;
    }

    internal string Log { get; set; } = "";
    internal bool ShowLocalContainers { get; set; }
    internal bool ShowLocalNpcs { get; set; }
    internal WorldMapTerrain? Terrain => _terrain;
    internal bool HasTerrain => _terrain != null && _terrain.Width > 0 && _terrain.Height > 0;

    private string Text(string zh, string en) => _host.TranslateModuleText(zh, en);

    internal Region? GetRegion()
    {
        try
        {
            return GameAccess.World.CurrentWorld?.region;
        }
        catch
        {
            return null;
        }
    }

    internal EloMap? GetEloMap()
    {
        try
        {
            var map = GetRegion()?.elomap;
            if (map == null || !map.initialized || map.w <= 0 || map.h <= 0)
                return null;
            return map.GetTileInfo(map.minX, map.minY) == null ? null : map;
        }
        catch
        {
            return null;
        }
    }

    internal bool TryRefreshTerrain()
    {
        var elomap = GetEloMap();
        if (elomap == null)
            return false;
        try
        {
            var width = elomap.w;
            var height = elomap.h;
            var minX = elomap.minX;
            var minY = elomap.minY;
            if (width <= 0 || height <= 0 || (long)width * height > WorldMapMaxCells)
                return false;

            var kinds = new byte[width * height];
            var sourceIndexes = new short[width * height];
            var sources = new List<SourceGlobalTile.Row>();
            var sprites = new List<Sprite?>();
            var lookup = new Dictionary<SourceGlobalTile.Row, short>();
            var described = 0;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    EloMap.TileInfo? info = null;
                    try
                    {
                        info = elomap.GetTileInfo(x + minX, y + minY);
                    }
                    catch
                    {
                    }
                    var source = info?.source;
                    if (source == null)
                    {
                        kinds[index] = WorldMapTerrain.KindUnknown;
                        sourceIndexes[index] = -1;
                        continue;
                    }
                    described++;
                    kinds[index] = ClassifyWorldTile(info!);
                    if (!lookup.TryGetValue(source, out var slot))
                    {
                        if (sources.Count >= short.MaxValue)
                        {
                            sourceIndexes[index] = -1;
                            continue;
                        }
                        slot = (short)sources.Count;
                        sources.Add(source);
                        sprites.Add(SafeTileSprite(info!));
                        lookup.Add(source, slot);
                    }
                    sourceIndexes[index] = slot;
                }
            }

            if (described == 0)
                return false;

            _terrain = new WorldMapTerrain(width, height, minX, minY, kinds, sourceIndexes, sources, sprites);
            _navDirty = true;
            InvalidateTileColors();
            ReleaseWorldTexture();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Sprite? SafeTileSprite(EloMap.TileInfo info)
    {
        try { return info.sprite; }
        catch { return null; }
    }

    private static byte ClassifyWorldTile(EloMap.TileInfo info)
    {
        try
        {
            if (info.sea)
                return WorldMapTerrain.KindSea;
            if (info.shore)
                return WorldMapTerrain.KindShore;
            if (info.rock || info.blocked)
                return WorldMapTerrain.KindBlocked;
            if (info.isRoad)
                return WorldMapTerrain.KindRoad;
            return WorldMapTerrain.KindLand;
        }
        catch
        {
            return WorldMapTerrain.KindUnknown;
        }
    }

    internal List<WorldMapZoneEntry> EnumerateZones()
    {
        var result = new List<WorldMapZoneEntry>();
        var region = GetRegion();
        var children = region?.children;
        if (children == null)
            return result;
        foreach (var child in children)
        {
            if (child is not Zone zone)
                continue;
            try
            {
                result.Add(new WorldMapZoneEntry(zone));
            }
            catch
            {
            }
        }
        return result;
    }

    internal WorldMapZoneEntry? FindZoneAt(List<WorldMapZoneEntry> zones, int gridX, int gridY)
    {
        for (var i = 0; i < zones.Count; i++)
        {
            if (zones[i].X == gridX && zones[i].Y == gridY)
                return zones[i];
        }
        return null;
    }

    internal bool TryGetPlayerGrid(out int gridX, out int gridY)
    {
        gridX = 0;
        gridY = 0;
        try
        {
            var zone = GameAccess.World.CurrentZone;
            if (zone != null && !zone.IsRegion)
            {
                gridX = zone.x;
                gridY = zone.y;
                return true;
            }
        }
        catch
        {
        }
        try
        {
            var terrain = _terrain;
            var position = GameAccess.Characters.PlayerCharacter?.pos;
            if (terrain == null || position == null)
                return false;
            gridX = position.x + terrain.MinX;
            gridY = position.z + terrain.MinY;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal WorldMapZoneEntry? FindZoneNear(List<WorldMapZoneEntry> zones, int gridX, int gridY, int radius)
    {
        WorldMapZoneEntry? best = null;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            var distance = Math.Max(Math.Abs(zone.X - gridX), Math.Abs(zone.Y - gridY));
            if (distance > radius || distance >= bestDistance)
                continue;
            bestDistance = distance;
            best = zone;
            if (distance == 0)
                break;
        }
        return best;
    }

    internal bool TryGetLocalSize(out int sizeX, out int sizeZ)
    {
        sizeX = 0;
        sizeZ = 0;
        try
        {
            var map = GameAccess.World.CurrentMap;
            if (map?.cells == null)
                return false;
            sizeX = map.cells.GetLength(0);
            sizeZ = map.cells.GetLength(1);
            return sizeX > 0 && sizeZ > 0 && (long)sizeX * sizeZ <= WorldMapMaxCells;
        }
        catch
        {
            return false;
        }
    }

    internal void ReleaseWorldTexture()
    {
        DestroyTexture(ref _worldTexture, ref _worldSprite);
        _worldSignature = "";
    }

    internal void ReleaseLocalTexture()
    {
        DestroyTexture(ref _localTexture, ref _localSprite);
        _localSignature = "";
    }

    internal IReadOnlyDictionary<int, string> Pins => _pins;

    internal static int PackPin(int gridX, int gridY) =>
        ((gridX & 0xFFFF) << 16) | (gridY & 0xFFFF);

    internal static int UnpackPinX(int key) => (short)(key >> 16);

    internal static int UnpackPinY(int key) => (short)(key & 0xFFFF);

    internal bool TryGetPin(int gridX, int gridY, out string note) =>
        _pins.TryGetValue(PackPin(gridX, gridY), out note);

    internal void SetPin(int gridX, int gridY, string? note)
    {
        var key = PackPin(gridX, gridY);
        var text = (note ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0)
            _pins.Remove(key);
        else if (_pins.Count < WorldMapMaxPins || _pins.ContainsKey(key))
            _pins[key] = text;
        ReleaseWorldTexture();
    }

    internal void ClearPins()
    {
        if (_pins.Count == 0)
            return;
        _pins.Clear();
        ReleaseWorldTexture();
    }

    internal bool PinsLoaded => _pinsLoaded;

    internal void LoadPins(string? payload)
    {
        _pinsLoaded = true;
        _pins.Clear();
        ReleaseWorldTexture();
        if (string.IsNullOrEmpty(payload))
            return;
        var split = payload!.IndexOf('\u0001');
        if (split < 0)
            return;
        if (!string.Equals(payload.Substring(0, split), WorldToken, StringComparison.Ordinal))
            return;
        var entries = payload.Substring(split + 1).Split(new[] { ";;" }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var firstComma = entry.IndexOf(',');
            if (firstComma <= 0)
                continue;
            var secondComma = entry.IndexOf(',', firstComma + 1);
            if (secondComma <= firstComma)
                continue;
            if (!int.TryParse(entry.Substring(0, firstComma), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!int.TryParse(entry.Substring(firstComma + 1, secondComma - firstComma - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                continue;
            if (x < short.MinValue || x > short.MaxValue || y < short.MinValue || y > short.MaxValue)
                continue;
            var note = entry.Substring(secondComma + 1);
            if (note.Length > 0 && _pins.Count < WorldMapMaxPins)
                _pins[PackPin(x, y)] = note;
        }
    }

    internal string SavePins()
    {
        if (_pins.Count == 0)
            return "";
        var sb = new StringBuilder();
        sb.Append(WorldToken).Append('\u0001');
        var first = true;
        foreach (var pair in _pins)
        {
            if (!first)
                sb.Append(";;");
            first = false;
            sb.Append(UnpackPinX(pair.Key).ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(UnpackPinY(pair.Key).ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(pair.Value.Replace(";;", " "));
        }
        return sb.ToString();
    }

    internal void Shutdown()
    {
        ReleaseNavigationOverlay();
        ReleaseWorldTexture();
        ReleaseLocalTexture();
        DestroyTexture(ref _markerTexture, ref _markerSprite);
        DestroyTexture(ref _selectionTexture, ref _selectionSprite);
        _pins.Clear();
        _terrain = null;
    }

    private static void DestroyTexture(ref Texture2D? texture, ref Sprite? sprite)
    {
        try
        {
            if (sprite != null)
                UnityEngine.Object.Destroy(sprite);
        }
        catch
        {
        }
        try
        {
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
        catch
        {
        }
        sprite = null;
        texture = null;
    }

    private static Sprite CreateSprite(Texture2D texture)
    {
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.Apply(false, false);
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect);
    }

    internal string DescribeTerrainState()
    {
        if (HasTerrain)
            return Text("地形快照: ", "Terrain snapshot: ") +
                   _terrain!.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                   _terrain.Height.ToString(CultureInfo.InvariantCulture);
        return GetEloMap() != null
            ? Text("地形未扫描，点击刷新", "Terrain not scanned yet, press refresh")
            : Text("地形需要在世界地图上刷新一次", "Terrain must be refreshed once while on the world map");
    }
}

internal sealed class WorldMapTerrain
{
    internal const byte KindUnknown = 0;
    internal const byte KindSea = 1;
    internal const byte KindShore = 2;
    internal const byte KindLand = 3;
    internal const byte KindBlocked = 4;
    internal const byte KindRoad = 5;

    internal readonly int Width;
    internal readonly int Height;
    internal readonly int MinX;
    internal readonly int MinY;
    private readonly byte[] _kinds;
    private readonly short[] _sourceIndexes;
    internal readonly int LandCells;
    internal readonly int SeaCells;
    internal readonly int BlockedCells;
    internal readonly int RoadCells;
    private readonly List<SourceGlobalTile.Row> _sources;
    private readonly List<Sprite?> _sprites;

    internal WorldMapTerrain(
        int width,
        int height,
        int minX,
        int minY,
        byte[] kinds,
        short[] sourceIndexes,
        List<SourceGlobalTile.Row> sources,
        List<Sprite?> sprites)
    {
        Width = width;
        Height = height;
        MinX = minX;
        MinY = minY;
        _kinds = kinds;
        _sourceIndexes = sourceIndexes;
        _sources = sources;
        _sprites = sprites;
        for (var i = 0; i < kinds.Length; i++)
        {
            switch (kinds[i])
            {
                case KindLand: LandCells++; break;
                case KindRoad: RoadCells++; LandCells++; break;
                case KindSea:
                case KindShore: SeaCells++; break;
                case KindBlocked: BlockedCells++; break;
            }
        }
    }

    internal Sprite? GetSpriteAt(int slot) =>
        slot >= 0 && slot < _sprites.Count ? _sprites[slot] : null;

    internal bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    internal byte GetKind(int x, int y)
    {
        if (!Contains(x, y))
            return KindUnknown;
        var index = y * Width + x;
        return index >= 0 && index < _kinds.Length ? _kinds[index] : KindUnknown;
    }

    internal int SourceCount => _sources.Count;

    internal SourceGlobalTile.Row? GetSourceAt(int slot) =>
        slot >= 0 && slot < _sources.Count ? _sources[slot] : null;

    internal int GetSourceIndex(int x, int y)
    {
        if (!Contains(x, y))
            return -1;
        var index = y * Width + x;
        return index >= 0 && index < _sourceIndexes.Length ? _sourceIndexes[index] : -1;
    }

    internal SourceGlobalTile.Row? GetSource(int x, int y)
    {
        if (!Contains(x, y))
            return null;
        var index = y * Width + x;
        if (index < 0 || index >= _sourceIndexes.Length)
            return null;
        var slot = _sourceIndexes[index];
        return slot >= 0 && slot < _sources.Count ? _sources[slot] : null;
    }
}

internal sealed class WorldMapZoneEntry
{
    internal readonly Zone Zone;
    internal readonly int X;
    internal readonly int Y;
    internal readonly string Name;
    internal readonly string TypeName;
    internal readonly int Level;
    internal readonly int DangerLevel;
    internal readonly bool IsKnown;
    internal readonly bool IsConquered;
    internal readonly bool IsPlayerFaction;
    internal readonly bool IsRandomSite;
    internal readonly bool IsLandmark;
    internal readonly bool IsDungeon;
    internal readonly bool IsField;

    internal WorldMapZoneEntry(Zone zone)
    {
        Zone = zone;
        X = SafeRead(() => zone.x, 0);
        Y = SafeRead(() => zone.y, 0);
        Name = SafeReadText(() => zone.Name, zone.id ?? "");
        TypeName = zone.GetType().Name;
        Level = SafeRead(() => zone.lv, 0);
        DangerLevel = SafeRead(() => zone.DangerLv, 0);
        IsKnown = SafeRead(() => zone.isKnown ? 1 : 0, 0) != 0;
        IsConquered = SafeRead(() => zone.isConquered ? 1 : 0, 0) != 0;
        IsPlayerFaction = SafeRead(() => zone.IsPlayerFaction ? 1 : 0, 0) != 0;
        IsRandomSite = SafeRead(() => zone.isRandomSite ? 1 : 0, 0) != 0;
        var type = zone.GetType();
        IsLandmark = InheritsFrom(type, "Zone_Civilized");
        IsDungeon = InheritsFrom(type, "Zone_Dungeon");
        IsField = InheritsFrom(type, "Zone_Field");
    }

    private static bool InheritsFrom(Type? type, string baseName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (string.Equals(current.Name, baseName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static int SafeRead(Func<int> read, int fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private static string SafeReadText(Func<string> read, string fallback)
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
