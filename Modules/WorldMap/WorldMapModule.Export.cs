using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

internal sealed partial class WorldMapModule
{
    private const int WorldMapExportMaxSide = 4096;
    internal const int WorldMapLabelFontSize = 13;
    private const float WorldMapLabelScreenBaselineOffset = 7.5f;

    internal string ExportWorldMapPng(
        string directory,
        Font? font,
        IReadOnlyList<WorldMapExportLabel>? labels,
        WorldMapExportCrop crop) =>
        ExportTexturePng(directory, _worldTexture, "ElinWorldMap_", font, labels, crop);

    internal string ExportLocalMapPng(string directory, WorldMapExportCrop crop) =>
        ExportTexturePng(directory, _localTexture, "ElinZoneMap_", null, null, crop);

    private static string ExportTexturePng(
        string directory,
        Texture2D? texture,
        string prefix,
        Font? font,
        IReadOnlyList<WorldMapExportLabel>? labels,
        WorldMapExportCrop crop)
    {
        var bytes = EncodeScaledPng(texture, font, labels, crop);
        if (bytes == null)
            return "";
        try
        {
            Directory.CreateDirectory(directory);
            var name = prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png";
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch
        {
            return "";
        }
    }

    private static byte[]? EncodeScaledPng(
        Texture2D? source,
        Font? font,
        IReadOnlyList<WorldMapExportLabel>? labels,
        WorldMapExportCrop crop)
    {
        if (source == null || source.width <= 0 || source.height <= 0)
            return null;
        Texture2D? scaled = null;
        try
        {
            var sourceWidth = source.width;
            var sourceHeight = source.height;
            var cropX = crop.IsEmpty ? 0 : Mathf.Clamp(crop.X, 0, sourceWidth - 1);
            var cropY = crop.IsEmpty ? 0 : Mathf.Clamp(crop.Y, 0, sourceHeight - 1);
            var width = crop.IsEmpty ? sourceWidth : Mathf.Clamp(crop.Width, 1, sourceWidth - cropX);
            var height = crop.IsEmpty ? sourceHeight : Mathf.Clamp(crop.Height, 1, sourceHeight - cropY);
            var scale = Math.Max(1, Math.Min(16, WorldMapExportMaxSide / Math.Max(width, height)));
            var targetWidth = width * scale;
            var targetHeight = height * scale;
            var src = source.GetPixels32();
            var dst = new Color32[targetWidth * targetHeight];
            for (var y = 0; y < targetHeight; y++)
            {
                var sourceRow = (cropY + y / scale) * sourceWidth + cropX;
                var targetRow = y * targetWidth;
                for (var x = 0; x < targetWidth; x++)
                    dst[targetRow + x] = src[sourceRow + x / scale];
            }

            if (font != null && labels != null && labels.Count > 0)
                DrawExportLabels(dst, targetWidth, targetHeight, scale, font, labels);

            scaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            scaled.SetPixels32(dst);
            scaled.Apply(false, false);
            return scaled.EncodeToPNG();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (scaled != null)
            {
                try { UnityEngine.Object.Destroy(scaled); }
                catch { }
            }
        }
    }

    private static void DrawExportLabels(
        Color32[] pixels,
        int width,
        int height,
        int scale,
        Font font,
        IReadOnlyList<WorldMapExportLabel> labels)
    {
        var fontSize = WorldMapLabelFontSize;
        var request = new StringBuilder();
        for (var i = 0; i < labels.Count; i++)
            request.Append(labels[i].Text);
        try
        {
            font.RequestCharactersInTexture(request.ToString(), fontSize, FontStyle.Normal);
        }
        catch
        {
            return;
        }

        var atlas = font.material == null ? null : font.material.mainTexture as Texture2D;
        if (atlas == null)
            return;
        var readable = CopyTextureToReadable(atlas);
        if (readable == null)
            return;

        try
        {
            var atlasPixels = readable.GetPixels32();
            var outline = 1;
            var shadow = new Color32(0, 0, 0, 235);
            var ink = new Color32(255, 255, 255, 255);
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < labels.Count; i++)
                {
                    DrawExportLabel(
                        pixels,
                        width,
                        height,
                        scale,
                        font,
                        fontSize,
                        atlasPixels,
                        readable.width,
                        readable.height,
                        labels[i],
                        pass == 0 ? shadow : ink,
                        pass == 0 ? outline : 0);
                }
            }
        }
        catch
        {
        }
        finally
        {
            try { UnityEngine.Object.Destroy(readable); }
            catch { }
        }
    }

    private static void DrawExportLabel(
        Color32[] pixels,
        int width,
        int height,
        int scale,
        Font font,
        int fontSize,
        Color32[] atlasPixels,
        int atlasWidth,
        int atlasHeight,
        WorldMapExportLabel label,
        Color32 color,
        int spread)
    {
        var text = label.Text;
        if (string.IsNullOrEmpty(text))
            return;

        var advance = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (font.GetCharacterInfo(text[i], out var measure, fontSize, FontStyle.Normal))
                advance += measure.advance;
        }
        if (advance <= 0)
            return;

        var penX = Mathf.RoundToInt(label.CellX * scale + scale * 0.5f - advance * 0.5f);
        var baseline = Mathf.RoundToInt((label.CellY + 1) * scale + WorldMapLabelScreenBaselineOffset);

        for (var i = 0; i < text.Length; i++)
        {
            if (!font.GetCharacterInfo(text[i], out var info, fontSize, FontStyle.Normal))
                continue;
            var glyphWidth = info.maxX - info.minX;
            var glyphHeight = info.maxY - info.minY;
            if (glyphWidth > 0 && glyphHeight > 0)
            {
                for (var gy = 0; gy < glyphHeight; gy++)
                {
                    var ty = (gy + 0.5f) / glyphHeight;
                    for (var gx = 0; gx < glyphWidth; gx++)
                    {
                        var tx = (gx + 0.5f) / glyphWidth;
                        var uv = info.uvBottomLeft +
                                 (info.uvBottomRight - info.uvBottomLeft) * tx +
                                 (info.uvTopLeft - info.uvBottomLeft) * ty;
                        var ax = Mathf.Clamp((int)(uv.x * atlasWidth), 0, atlasWidth - 1);
                        var ay = Mathf.Clamp((int)(uv.y * atlasHeight), 0, atlasHeight - 1);
                        var coverage = atlasPixels[ay * atlasWidth + ax].a;
                        if (coverage < 24)
                            continue;
                        var px = penX + info.minX + gx;
                        var py = baseline + info.minY + gy;
                        if (spread <= 0)
                        {
                            BlendExportPixel(pixels, width, height, px, py, color, coverage);
                            continue;
                        }
                        for (var oy = -spread; oy <= spread; oy++)
                        {
                            for (var ox = -spread; ox <= spread; ox++)
                                BlendExportPixel(pixels, width, height, px + ox, py + oy, color, coverage);
                        }
                    }
                }
            }
            penX += info.advance;
        }
    }

    private static void BlendExportPixel(
        Color32[] pixels,
        int width,
        int height,
        int x,
        int y,
        Color32 color,
        byte coverage)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;
        var index = y * width + x;
        var alpha = coverage * color.a / 255f / 255f;
        if (alpha <= 0f)
            return;
        var current = pixels[index];
        pixels[index] = new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(current.r + (color.r - current.r) * alpha), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(current.g + (color.g - current.g) * alpha), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(current.b + (color.b - current.b) * alpha), 0, 255),
            255);
    }

    private static Texture2D? CopyTextureToReadable(Texture2D source)
    {
        RenderTexture? temporary = null;
        var previous = RenderTexture.active;
        try
        {
            temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            readable.Apply(false, false);
            return readable;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { RenderTexture.active = previous; }
            catch { }
            if (temporary != null)
            {
                try { RenderTexture.ReleaseTemporary(temporary); }
                catch { }
            }
        }
    }
}

internal readonly struct WorldMapExportCrop
{
    internal static readonly WorldMapExportCrop Full = new WorldMapExportCrop(0, 0, 0, 0);

    internal readonly int X;
    internal readonly int Y;
    internal readonly int Width;
    internal readonly int Height;

    internal WorldMapExportCrop(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    internal bool IsEmpty => Width <= 0 || Height <= 0;
}

internal readonly struct WorldMapExportLabel
{
    internal readonly int CellX;
    internal readonly int CellY;
    internal readonly string Text;

    internal WorldMapExportLabel(int cellX, int cellY, string text)
    {
        CellX = cellX;
        CellY = cellY;
        Text = text ?? "";
    }
}
