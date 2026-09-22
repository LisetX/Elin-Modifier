using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

internal sealed partial class WorldMapModule
{
    private const int WorldMapMaxCells = 1_000_000;
    private const int WorldMapMaxPins = 200;

    private static readonly Color32 ColorSea = new Color32(28, 58, 110, 255);
    private static readonly Color32 ColorShore = new Color32(74, 122, 168, 255);
    private static readonly Color32 ColorLand = new Color32(74, 116, 66, 255);
    private static readonly Color32 ColorRoad = new Color32(158, 134, 86, 255);
    private static readonly Color32 ColorBlocked = new Color32(88, 84, 78, 255);
    private static readonly Color32 ColorUnknown = new Color32(18, 18, 20, 255);
    private static readonly Color32 ColorTown = new Color32(240, 214, 96, 255);
    private static readonly Color32 ColorDungeon = new Color32(226, 96, 88, 255);
    private static readonly Color32 ColorHome = new Color32(112, 226, 150, 255);
    private static readonly Color32 ColorSite = new Color32(198, 140, 226, 255);
    private static readonly Color32 ColorUnknownZone = new Color32(160, 160, 160, 255);
    private static readonly Color32 ColorOtherZone = new Color32(212, 212, 216, 255);
    private static readonly Color32 ColorContainer = new Color32(224, 160, 40, 255);
    private static readonly Color32 ColorHostile = new Color32(224, 72, 72, 255);
    private static readonly Color32 ColorNpc = new Color32(72, 200, 224, 255);
    private static readonly Color32 ColorAllyNpc = new Color32(112, 226, 150, 255);
    private static readonly Color32 ColorSearchHit = new Color32(255, 64, 200, 255);
    private static readonly Color32 ColorRoute = new Color32(255, 196, 64, 255);
    private static readonly Color32 ColorRouteTarget = new Color32(120, 255, 170, 255);
    private static readonly Color32 ColorPin = new Color32(255, 255, 255, 255);

    internal Sprite? BuildWorldSprite(List<WorldMapZoneEntry> zones)
    {
        if (!HasTerrain)
            return null;
        RefreshNavigationPath();
        var terrain = _terrain!;
        var signature = terrain.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                        terrain.Height.ToString(CultureInfo.InvariantCulture) + "|" +
                        BuildZoneSignature(zones) + "|" + _searchQuery + "|" +
                        _pins.Count.ToString(CultureInfo.InvariantCulture) + "|" +
                        _searchVersion.ToString(CultureInfo.InvariantCulture) + "|" +
                        (_navigationEnabled ? "n" : "-") +
                        _navVersion.ToString(CultureInfo.InvariantCulture);
        if (_worldSprite != null && string.Equals(signature, _worldSignature, StringComparison.Ordinal))
            return _worldSprite;

        var width = terrain.Width;
        var height = terrain.Height;
        var pixels = new Color32[width * height];

        var slotColors = ResolveTileColors(terrain);
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var kind = terrain.GetKind(x, y);
                if (kind == WorldMapTerrain.KindBlocked || kind == WorldMapTerrain.KindUnknown)
                {
                    pixels[row + x] = KindColor(kind);
                    continue;
                }
                var slot = terrain.GetSourceIndex(x, y);
                pixels[row + x] = slot >= 0 && slot < slotColors.Length ? slotColors[slot] : KindColor(kind);
            }
        }

        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (!TryGetZoneColor(zone, out var color))
                continue;
            var x = zone.X - terrain.MinX;
            var y = zone.Y - terrain.MinY;
            if (x < 0 || y < 0 || x >= width || y >= height)
                continue;
            pixels[y * width + x] = color;
        }

        if (_navigationEnabled && _navCells != null)
        {
            foreach (var index in _navCells)
            {
                if (index >= 0 && index < pixels.Length)
                    pixels[index] = ColorRoute;
            }
        }

        if (_searchCells != null)
        {
            foreach (var index in _searchCells)
            {
                if (index >= 0 && index < pixels.Length)
                    pixels[index] = ColorSearchHit;
            }
        }

        foreach (var pin in _pins)
        {
            var pinX = UnpackPinX(pin.Key) - terrain.MinX;
            var pinY = UnpackPinY(pin.Key) - terrain.MinY;
            if (pinX < 0 || pinY < 0 || pinX >= width || pinY >= height)
                continue;
            pixels[pinY * width + pinX] = ColorPin;
        }

        if (_navigationEnabled && _navCells != null && TryGetNavigationTargetCell(out var boxX, out var boxY))
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    var ringX = boxX + dx;
                    var ringY = boxY + dy;
                    if (ringX < 0 || ringY < 0 || ringX >= width || ringY >= height)
                        continue;
                    pixels[ringY * width + ringX] = ColorRouteTarget;
                }
            }
        }

        if (_worldTexture != null && _worldSprite != null &&
            _worldTexture.width == width && _worldTexture.height == height)
        {
            _worldTexture.SetPixels32(pixels);
            _worldTexture.Apply(false, false);
            _worldSignature = signature;
            return _worldSprite;
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        var sprite = CreateSprite(texture);
        ReleaseWorldTexture();
        _worldTexture = texture;
        _worldSprite = sprite;
        _worldSignature = signature;
        return _worldSprite;
    }

    private static string BuildZoneSignature(List<WorldMapZoneEntry> zones)
    {
        var hash = 17;
        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            unchecked
            {
                hash = hash * 31 + zone.X;
                hash = hash * 31 + zone.Y;
                hash = hash * 31 + (zone.IsKnown ? 1 : 0);
                hash = hash * 31 + (zone.IsConquered ? 2 : 0);
                hash = hash * 31 + (zone.IsPlayerFaction ? 4 : 0);
            }
        }
        return zones.Count.ToString(CultureInfo.InvariantCulture) + ":" +
               hash.ToString(CultureInfo.InvariantCulture);
    }

    private static Color32 KindColor(byte kind)
    {
        switch (kind)
        {
            case WorldMapTerrain.KindSea: return ColorSea;
            case WorldMapTerrain.KindShore: return ColorShore;
            case WorldMapTerrain.KindLand: return ColorLand;
            case WorldMapTerrain.KindRoad: return ColorRoad;
            case WorldMapTerrain.KindBlocked: return ColorBlocked;
            default: return ColorUnknown;
        }
    }

    internal static bool TryGetZoneColor(WorldMapZoneEntry zone, out Color32 color)
    {
        if (zone.IsPlayerFaction)
        {
            color = ColorHome;
            return true;
        }
        if (zone.IsField && !zone.IsLandmark && !zone.IsDungeon && !zone.IsRandomSite)
        {
            color = ColorOtherZone;
            return false;
        }
        color = ZoneCategoryColor(zone);
        return true;
    }

    private static Color32 ZoneCategoryColor(WorldMapZoneEntry zone)
    {
        if (zone.IsLandmark)
            return ColorTown;
        if (zone.IsDungeon)
            return ColorDungeon;
        if (zone.IsRandomSite)
            return ColorSite;
        return ColorTown;
    }

    internal Sprite? BuildLocalSprite()
    {
        if (!TryGetLocalSize(out var sizeX, out var sizeZ))
            return null;
        var map = GameAccess.World.CurrentMap;
        if (map?.cells == null)
            return null;

        var signature = BuildLocalSignature(sizeX, sizeZ) +
                        (ShowLocalContainers ? "|c" : "") + (ShowLocalNpcs ? "|n" : "");
        if (_localSprite != null && string.Equals(signature, _localSignature, StringComparison.Ordinal))
            return _localSprite;

        var pixels = new Color32[sizeX * sizeZ];
        for (var z = 0; z < sizeZ; z++)
        {
            var row = z * sizeX;
            for (var x = 0; x < sizeX; x++)
                pixels[row + x] = LocalCellColor(map, x, z);
        }

        if (ShowLocalContainers)
            PaintLocalContainers(map, pixels, sizeX, sizeZ);
        if (ShowLocalNpcs)
            PaintLocalCharas(map, pixels, sizeX, sizeZ);

        if (_localTexture != null && _localSprite != null &&
            _localTexture.width == sizeX && _localTexture.height == sizeZ)
        {
            _localTexture.SetPixels32(pixels);
            _localTexture.Apply(false, false);
            _localSignature = signature;
            return _localSprite;
        }

        var texture = new Texture2D(sizeX, sizeZ, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        var sprite = CreateSprite(texture);
        ReleaseLocalTexture();
        _localTexture = texture;
        _localSprite = sprite;
        _localSignature = signature;
        return _localSprite;
    }

    internal Sprite? GetMarkerSprite()
    {
        if (_markerSprite != null)
            return _markerSprite;
        const int size = 16;
        const int border = 2;
        var pixels = new Color32[size * size];
        var clear = new Color32(255, 255, 255, 0);
        var solid = new Color32(255, 255, 255, 255);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var edge = x < border || y < border || x >= size - border || y >= size - border;
                pixels[y * size + x] = edge ? solid : clear;
            }
        }
        _markerTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _markerTexture.SetPixels32(pixels);
        _markerSprite = CreateSprite(_markerTexture);
        return _markerSprite;
    }

    internal Sprite? GetSelectionSprite()
    {
        if (_selectionSprite != null)
            return _selectionSprite;
        const int size = 32;
        const int thickness = 3;
        const int arm = 11;
        var pixels = new Color32[size * size];
        var clear = new Color32(255, 255, 255, 0);
        var solid = new Color32(255, 255, 255, 255);
        for (var y = 0; y < size; y++)
        {
            var nearBottom = y < thickness;
            var nearTop = y >= size - thickness;
            var inArmY = y < arm || y >= size - arm;
            for (var x = 0; x < size; x++)
            {
                var nearLeft = x < thickness;
                var nearRight = x >= size - thickness;
                var inArmX = x < arm || x >= size - arm;
                var draw = ((nearLeft || nearRight) && inArmY) || ((nearBottom || nearTop) && inArmX);
                pixels[y * size + x] = draw ? solid : clear;
            }
        }
        _selectionTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _selectionTexture.SetPixels32(pixels);
        _selectionSprite = CreateSprite(_selectionTexture);
        return _selectionSprite;
    }

    private static void PaintLocalContainers(Map map, Color32[] pixels, int sizeX, int sizeZ)
    {
        try
        {
            var things = map.things;
            if (things == null)
                return;
            for (var i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing?.pos == null)
                    continue;
                try
                {
                    if (!thing.IsContainer)
                        continue;
                    var x = thing.pos.x;
                    var z = thing.pos.z;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    if (!map.cells[x, z].isSeen)
                        continue;
                    pixels[z * sizeX + x] = ColorContainer;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void PaintLocalCharas(Map map, Color32[] pixels, int sizeX, int sizeZ)
    {
        try
        {
            var charas = map.charas;
            if (charas == null)
                return;
            for (var i = 0; i < charas.Count; i++)
            {
                var chara = charas[i];
                if (chara?.pos == null)
                    continue;
                try
                {
                    if (chara.IsPC)
                        continue;
                    var x = chara.pos.x;
                    var z = chara.pos.z;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    pixels[z * sizeX + x] = chara.IsPCFaction
                        ? ColorAllyNpc
                        : string.Equals(chara.hostility.ToString(), "Enemy", StringComparison.Ordinal)
                            ? ColorHostile
                            : ColorNpc;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static string BuildLocalSignature(int sizeX, int sizeZ)
    {
        var zoneName = "";
        try
        {
            zoneName = GameAccess.World.CurrentZone?.Name ?? "";
        }
        catch
        {
        }
        return zoneName + "|" + sizeX.ToString(CultureInfo.InvariantCulture) + "|" +
               sizeZ.ToString(CultureInfo.InvariantCulture);
    }

    private static Color32 LocalCellColor(Map map, int x, int z)
    {
        try
        {
            var cell = map.cells[x, z];
            if (cell == null || !cell.isSeen)
                return ColorUnknown;
            if (cell.IsTopWater)
                return ColorSea;
            if (cell.isShoreSand)
                return ColorShore;
            if (cell._block != 0)
                return MaterialColor(cell.matBlock, ColorBlocked);
            if (cell.obj != 0)
                return new Color32(96, 132, 72, 255);
            return MaterialColor(cell.matFloor, ColorLand);
        }
        catch
        {
            return ColorUnknown;
        }
    }

    private static Color32 MaterialColor(SourceMaterial.Row? material, Color32 fallback)
    {
        if (material == null)
            return fallback;
        try
        {
            var color = material.matColor;
            var r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f * 0.75f + fallback.r * 0.25f), 0, 255);
            var g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f * 0.75f + fallback.g * 0.25f), 0, 255);
            var b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f * 0.75f + fallback.b * 0.25f), 0, 255);
            return new Color32(r, g, b, 255);
        }
        catch
        {
            return fallback;
        }
    }
}
