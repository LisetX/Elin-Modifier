using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed partial class WorldMapModule
{
    private const int WorldMapMaxSampleSide = 64;

    private Color32[]? _tileColors;

    internal void InvalidateTileColors() => _tileColors = null;

    private Color32[] ResolveTileColors(WorldMapTerrain terrain)
    {
        if (_tileColors != null && _tileColors.Length == terrain.SourceCount)
            return _tileColors;

        var count = Math.Max(1, terrain.SourceCount);
        var colors = new Color32[count];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = ColorLand;

        var groups = new Dictionary<Texture2D, List<int>>();
        for (var slot = 0; slot < terrain.SourceCount; slot++)
        {
            colors[slot] = FallbackTileColor(terrain, slot);
            var sprite = terrain.GetSpriteAt(slot);
            Texture2D? texture = null;
            try
            {
                texture = sprite == null ? null : sprite.texture;
            }
            catch
            {
            }
            if (texture == null)
                continue;
            if (!groups.TryGetValue(texture, out var slots))
            {
                slots = new List<int>();
                groups.Add(texture, slots);
            }
            slots.Add(slot);
        }

        foreach (var pair in groups)
            SampleTileGroup(terrain, pair.Key, pair.Value, colors);

        _tileColors = colors;
        return colors;
    }

    private static void SampleTileGroup(WorldMapTerrain terrain, Texture2D texture, List<int> slots, Color32[] colors)
    {
        RenderTexture? previous = null;
        try
        {
            previous = RenderTexture.active;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var sprite = terrain.GetSpriteAt(slot);
                if (sprite == null)
                    continue;
                if (TrySampleSprite(texture, sprite, out var sampled))
                    colors[slot] = sampled;
            }
        }
        catch
        {
        }
        finally
        {
            try
            {
                RenderTexture.active = previous;
            }
            catch
            {
            }
        }
    }

    private static bool TrySampleSprite(Texture2D texture, Sprite sprite, out Color32 color)
    {
        color = default;
        RenderTexture? temporary = null;
        Texture2D? readable = null;
        try
        {
            var area = sprite.textureRect;
            var width = Mathf.Clamp(Mathf.RoundToInt(area.width), 1, WorldMapMaxSampleSide);
            var height = Mathf.Clamp(Mathf.RoundToInt(area.height), 1, WorldMapMaxSampleSide);
            if (texture.width <= 0 || texture.height <= 0)
                return false;

            var scale = new Vector2(area.width / texture.width, area.height / texture.height);
            var offset = new Vector2(area.x / texture.width, area.y / texture.height);
            temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, temporary, scale, offset);
            RenderTexture.active = temporary;
            readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            readable.Apply(false, false);

            var pixels = readable.GetPixels32();
            long r = 0;
            long g = 0;
            long b = 0;
            var counted = 0;
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 128)
                    continue;
                r += pixels[i].r;
                g += pixels[i].g;
                b += pixels[i].b;
                counted++;
            }
            if (counted == 0)
                return false;
            color = new Color32((byte)(r / counted), (byte)(g / counted), (byte)(b / counted), 255);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (readable != null)
            {
                try { UnityEngine.Object.Destroy(readable); }
                catch { }
            }
            if (temporary != null)
            {
                try { RenderTexture.ReleaseTemporary(temporary); }
                catch { }
            }
        }
    }

    private static Color32 FallbackTileColor(WorldMapTerrain terrain, int slot)
    {
        var row = terrain.GetSourceAt(slot);
        if (row == null)
            return ColorLand;
        try
        {
            var id = row.idBiome;
            if (string.IsNullOrEmpty(id))
                return ColorLand;
            var biomes = EClass.core?.refs?.biomes?.dict;
            if (biomes == null || !biomes.TryGetValue(id, out var biome) || biome == null)
                return ColorLand;
            return MaterialColor(biome.MatFloor, ColorLand);
        }
        catch
        {
            return ColorLand;
        }
    }
}
