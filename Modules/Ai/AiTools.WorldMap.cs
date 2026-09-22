using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed partial class ElinModifierPlugin
{
    private const int AiWorldMapDefaultLimit = 30;
    private const int AiWorldMapMaxLimit = 200;

    private string AiToolWorldMapOverview(string args)
    {
        var module = _modules.WorldMap;
        if (!module.EnsureTerrain())
            return "failed: world map terrain unavailable (open the world map once, or load a save first)";
        var terrain = module.Terrain!;

        var sb = new StringBuilder("ok: world map");
        var region = SafeText(() => module.GetRegion()?.Name, "");
        AppendAiQueryField(sb, "region", region);
        AppendAiQueryField(sb, "size", terrain.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                                       terrain.Height.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "origin", terrain.MinX.ToString(CultureInfo.InvariantCulture) + "," +
                                         terrain.MinY.ToString(CultureInfo.InvariantCulture));

        var total = Math.Max(1, terrain.Width * terrain.Height);
        AppendAiQueryField(sb, "terrain", "reachable " + AiWorldMapPercent(terrain.LandCells, total) +
                                          ", water " + AiWorldMapPercent(terrain.SeaCells, total) +
                                          ", blocked " + AiWorldMapPercent(terrain.BlockedCells, total) +
                                          ", road cells " + terrain.RoadCells.ToString(CultureInfo.InvariantCulture));

        if (module.TryGetPlayerGrid(out var playerX, out var playerY))
        {
            AppendAiQueryField(sb, "player", AiWorldMapCoord(playerX, playerY));
            var roadDistance = module.GetRoadDistance(playerX, playerY);
            if (roadDistance >= 0)
                AppendAiQueryField(sb, "roadDistance", roadDistance.ToString(CultureInfo.InvariantCulture));
        }

        var zones = module.EnumerateZones();
        var known = 0;
        var landmarks = 0;
        var dungeons = 0;
        var owned = 0;
        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (zone.IsKnown) known++;
            if (zone.IsLandmark) landmarks++;
            if (zone.IsDungeon) dungeons++;
            if (zone.IsPlayerFaction) owned++;
        }
        AppendAiQueryField(sb, "zones", "total " + zones.Count.ToString(CultureInfo.InvariantCulture) +
                                        ", known " + known.ToString(CultureInfo.InvariantCulture) +
                                        ", landmark " + landmarks.ToString(CultureInfo.InvariantCulture) +
                                        ", dungeon " + dungeons.ToString(CultureInfo.InvariantCulture) +
                                        ", owned " + owned.ToString(CultureInfo.InvariantCulture));

        AppendAiQueryField(sb, "routeGuide", module.NavigationEnabled ? "on" : "off");
        if (module.HasNavigationTarget)
            AppendAiQueryField(sb, "routeTarget", AiWorldMapCoord(module.NavigationTargetX, module.NavigationTargetY) +
                                                  " steps " + module.NavigationSteps.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private string AiToolWorldMapZones(string args)
    {
        var module = _modules.WorldMap;
        if (!module.EnsureTerrain())
            return "failed: world map terrain unavailable (open the world map once, or load a save first)";

        var filter = AiArgString(args, "filter");
        var category = NormalizeAiKey(AiArgString(args, "category", "all"));
        var knownOnly = AiArgBool(args, "known_only", false);
        var limit = AiWorldMapLimit(args);
        var hasPlayer = module.TryGetPlayerGrid(out var playerX, out var playerY);

        var zones = module.EnumerateZones();
        var rows = new List<WorldMapZoneEntry>();
        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (knownOnly && !zone.IsKnown)
                continue;
            if (!AiWorldMapZoneMatchesCategory(zone, category))
                continue;
            if (filter.Length > 0 &&
                (zone.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                (zone.TypeName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            rows.Add(zone);
        }

        if (hasPlayer)
            rows.Sort((a, b) => AiWorldMapDistance(a, playerX, playerY)
                .CompareTo(AiWorldMapDistance(b, playerX, playerY)));
        else
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        if (rows.Count == 0)
            return "ok: no zone matched";

        var sb = new StringBuilder("ok: zones ")
            .Append(Math.Min(limit, rows.Count).ToString(CultureInfo.InvariantCulture))
            .Append('/').Append(rows.Count.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < rows.Count && i < limit; i++)
        {
            var zone = rows[i];
            sb.AppendLine();
            sb.Append("  ").Append(zone.Name).Append(' ').Append(AiWorldMapCoord(zone.X, zone.Y))
                .Append(" type=").Append(zone.TypeName)
                .Append(" lv=").Append(zone.Level.ToString(CultureInfo.InvariantCulture))
                .Append(" danger=").Append(zone.DangerLevel.ToString(CultureInfo.InvariantCulture));
            if (hasPlayer)
                sb.Append(" dist=").Append(AiWorldMapDistance(zone, playerX, playerY)
                    .ToString(CultureInfo.InvariantCulture));
            sb.Append(AiWorldMapZoneFlags(zone));
        }
        return sb.ToString();
    }

    private string AiToolWorldMapSearch(string args)
    {
        var module = _modules.WorldMap;
        if (!module.EnsureTerrain())
            return "failed: world map terrain unavailable (open the world map once, or load a save first)";

        var query = AiArgString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
            return "failed: query is required";

        var limit = AiWorldMapLimit(args);
        var hits = module.RunDetachedSearch(query, limit);
        if (hits.Count == 0)
            return "ok: no world map match for " + query;

        var hasPlayer = module.TryGetPlayerGrid(out var playerX, out var playerY);
        var sb = new StringBuilder("ok: world map hits ")
            .Append(hits.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" for ").Append(query);
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            sb.AppendLine();
            sb.Append("  ").Append(hit.Label).Append(' ').Append(AiWorldMapCoord(hit.GridX, hit.GridY));
            if (hasPlayer)
                sb.Append(" dist=").Append(
                    Math.Max(Math.Abs(hit.GridX - playerX), Math.Abs(hit.GridY - playerY))
                        .ToString(CultureInfo.InvariantCulture));
            if (hit.IsZone)
                sb.Append(" zone");
        }
        return sb.ToString();
    }

    private string AiToolWorldMapRoute(string args)
    {
        var module = _modules.WorldMap;
        if (!module.EnsureTerrain())
            return "failed: world map terrain unavailable (open the world map once, or load a save first)";
        if (!module.TryGetPlayerGrid(out var playerX, out var playerY))
            return "failed: player position unavailable";

        var terrain = module.Terrain!;
        int targetX;
        int targetY;
        var zoneKey = AiArgString(args, "zone");
        WorldMapZoneEntry? target = null;
        if (zoneKey.Length > 0)
        {
            target = AiWorldMapFindZone(module, zoneKey);
            if (target == null)
                return "failed: zone not found on the world map: " + zoneKey;
            targetX = target.X;
            targetY = target.Y;
        }
        else
        {
            targetX = AiArgInt(args, "x", int.MinValue);
            targetY = AiArgInt(args, "y", int.MinValue);
            if (targetX == int.MinValue || targetY == int.MinValue)
                return "failed: provide zone, or both x and y";
        }

        if (!terrain.Contains(targetX - terrain.MinX, targetY - terrain.MinY))
            return "failed: target is outside the world map: " + AiWorldMapCoord(targetX, targetY);

        var sb = new StringBuilder("ok: route");
        AppendAiQueryField(sb, "from", AiWorldMapCoord(playerX, playerY));
        AppendAiQueryField(sb, "to", (target == null ? "" : target.Name + " ") + AiWorldMapCoord(targetX, targetY));
        AppendAiQueryField(sb, "straightDistance",
            Math.Max(Math.Abs(targetX - playerX), Math.Abs(targetY - playerY)).ToString(CultureInfo.InvariantCulture));

        if (!module.TryPlanRoutePreview(targetX, targetY, out var steps, out var road, out var land, out var shore, out var sea))
        {
            AppendAiQueryField(sb, "route", "unreachable");
            return sb.ToString();
        }

        AppendAiQueryField(sb, "steps", steps.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "cells", "road " + road.ToString(CultureInfo.InvariantCulture) +
                                        ", land " + land.ToString(CultureInfo.InvariantCulture) +
                                        ", shore " + shore.ToString(CultureInfo.InvariantCulture) +
                                        ", water " + sea.ToString(CultureInfo.InvariantCulture));
        if (target != null)
        {
            var travel = module.DescribeTravelCost(target);
            if (!string.IsNullOrEmpty(travel))
                AppendAiQueryField(sb, "travelCost", travel);
        }

        if (!AiArgBool(args, "set_destination", true))
            return sb.ToString();

        module.SetNavigationTarget(targetX, targetY);
        _lGuiWorldMapNavigation = true;
        module.NavigationEnabled = true;
        _lGuiWorldMapRebuildPending = true;
        SaveConfig(false);
        AppendAiQueryField(sb, "destinationSet", "yes, route guide enabled");
        return sb.ToString();
    }

    private static WorldMapZoneEntry? AiWorldMapFindZone(WorldMapModule module, string key)
    {
        var zones = module.EnumerateZones();
        WorldMapZoneEntry? partial = null;
        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            var name = zone.Name ?? "";
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                return zone;
            if (partial == null && name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                partial = zone;
        }
        return partial;
    }

    private static bool AiWorldMapZoneMatchesCategory(WorldMapZoneEntry zone, string category)
    {
        switch (category)
        {
            case "landmark": return zone.IsLandmark;
            case "dungeon": return zone.IsDungeon;
            case "field": return zone.IsField;
            case "site": return zone.IsRandomSite;
            case "owned": return zone.IsPlayerFaction;
            default: return true;
        }
    }

    private static string AiWorldMapZoneFlags(WorldMapZoneEntry zone)
    {
        var sb = new StringBuilder();
        if (zone.IsKnown) sb.Append(" known");
        if (zone.IsConquered) sb.Append(" conquered");
        if (zone.IsPlayerFaction) sb.Append(" owned");
        if (zone.IsLandmark) sb.Append(" landmark");
        if (zone.IsDungeon) sb.Append(" dungeon");
        if (zone.IsRandomSite) sb.Append(" site");
        return sb.ToString();
    }

    private static int AiWorldMapDistance(WorldMapZoneEntry zone, int playerX, int playerY) =>
        Math.Max(Math.Abs(zone.X - playerX), Math.Abs(zone.Y - playerY));

    private static string AiWorldMapCoord(int x, int y) =>
        "(" + x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture) + ")";

    private static string AiWorldMapPercent(int value, int total) =>
        (total <= 0 ? 0 : value * 100 / total).ToString(CultureInfo.InvariantCulture) + "%";

    private static int AiWorldMapLimit(string args)
    {
        var limit = AiArgInt(args, "limit", AiWorldMapDefaultLimit);
        if (limit <= 0)
            return AiWorldMapMaxLimit;
        return Math.Min(limit, AiWorldMapMaxLimit);
    }
}
