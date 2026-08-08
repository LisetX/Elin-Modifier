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

    private string GetCardName(string id)
    {
        try
        {
            if (GameAccess.Sources.Cards?.map != null && GameAccess.Sources.Cards.map.TryGetValue(id, out var row) && row != null)
            {
                var name = row.GetName();
                if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    return name;
            }
        }
        catch
        {
        }
        return id;
    }

    private static string SafeName(SourceChara.Row row)
    {
        try { return row.GetName() ?? row.id ?? ""; }
        catch { return row.id ?? ""; }
    }

    private static string SafeZoneName(Zone zone)
    {
        try
        {
            var name = zone.Name;
            return string.IsNullOrWhiteSpace(name) ? zone.GetType().Name : name;
        }
        catch { return zone.GetType().Name; }
    }

    private string GetBiomeDisplayName(BiomeProfile biome) => FormatBiomeName(biome?.name ?? "");

    internal string FormatBiomeName(string id)
    {
        var raw = (id ?? "").Trim();
        var uiLanguage = _host.TranslateModuleText("CN", "EN");
        if (raw.Length == 0 || uiLanguage == "EN")
            return raw;

        var gameLanguage = (Lang.langCode ?? "").Trim().ToUpperInvariant();
        var cacheKey = uiLanguage + "|" + gameLanguage + "|" + raw;
        if (_biomeDisplayNameCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var localized = string.Equals(gameLanguage, "CN", StringComparison.Ordinal)
            ? GetOriginalBiomeTranslation(raw)
            : "";
        if (string.IsNullOrWhiteSpace(localized))
        {
            switch (raw.ToLowerInvariant())
            {
                case "default": localized = "默认"; break;
                case "plain": localized = "平原"; break;
                case "forest": localized = "森林"; break;
                case "forest_cherry": localized = "森林"; break;
                case "sand": localized = "沙地"; break;
                case "mud": localized = "泥地"; break;
                case "water": localized = "海"; break;
                case "factory": localized = "工厂"; break;
                case "snow": localized = "雪地"; break;
                case "barren": localized = "荒地"; break;
                case "undersea": localized = "海底"; break;
                case "cave": localized = "洞窟"; break;
                case "machine": localized = "机械群落"; break;
                case "undead": localized = "不死群落"; break;
                case "shore": localized = "海滨"; break;
                case "mountain": localized = "山"; break;
                case "dungeon": localized = "地牢"; break;
                case "mine": localized = "矿井"; break;
                case "ruin": localized = "遗迹"; break;
                default: localized = raw; break;
            }
        }
        var result = string.Equals(localized, raw, StringComparison.OrdinalIgnoreCase)
            ? raw
            : localized + " (" + GetBiomeIdentifierDisplayName(raw) + ")";
        _biomeDisplayNameCache[cacheKey] = result;
        return result;
    }

    private static string GetBiomeIdentifierDisplayName(string id)
    {
        switch (id.ToLowerInvariant())
        {
            case "default": return "Default";
            case "plain": return "Plain";
            case "forest": return "Forest";
            case "forest_cherry": return "Forest_cherry";
            case "sand": return "Sand";
            case "mud": return "Mud";
            case "water": return "Water";
            case "factory": return "Factory";
            case "snow": return "Snow";
            case "barren": return "Barren";
            case "undersea": return "Undersea";
            case "cave": return "Cave";
            case "machine": return "Machine";
            case "undead": return "Undead";
            case "shore": return "Shore";
            case "mountain": return "Mountain";
            case "dungeon": return "Dungeon";
            case "mine": return "Mine";
            case "ruin": return "Ruin";
            default: return id;
        }
    }

    private static string GetOriginalBiomeTranslation(string id)
    {
        string key;
        switch (id.ToLowerInvariant())
        {
            case "plain": key = "zone_R_Plain"; break;
            case "forest": key = "zone_R_Forest"; break;
            case "forest_cherry": key = "zone_R_Forest_cherry"; break;
            case "snow": key = "zone_R_Snow"; break;
            case "water": key = "zone_R_Water"; break;
            case "undersea": key = "zone_R_Undersea"; break;
            case "shore": key = "zone_R_Shore"; break;
            case "mountain": key = "zone_R_Mountain"; break;
            default: return "";
        }
        try
        {
            var values = Lang.GetList(key);
            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(values[i]))
                        return values[i].Trim();
                }
            }
        }
        catch
        {
        }
        return "";
    }

    private string T(string chinese, string english) => _host.TranslateModuleText(chinese, english);

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
