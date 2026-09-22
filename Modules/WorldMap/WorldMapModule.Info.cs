using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

internal sealed partial class WorldMapModule
{
    internal string BuildWorldSummary(List<WorldMapZoneEntry> zones, int playerX, int playerY)
    {
        var sb = new StringBuilder();
        sb.Append(Text("当前位置", "Current location")).Append(": ");
        var current = SafeName(() => GameAccess.World.CurrentZone?.Name);
        sb.Append(string.IsNullOrEmpty(current) ? Text("未知", "unknown") : current);
        if (playerX >= 0 && playerY >= 0)
            sb.Append(" (").Append(playerX.ToString(CultureInfo.InvariantCulture)).Append(", ")
                .Append(playerY.ToString(CultureInfo.InvariantCulture)).Append(')');

        var regionName = SafeName(() => GetRegion()?.Name);
        if (!string.IsNullOrEmpty(regionName))
            sb.AppendLine().Append(Text("所在区域", "Region")).Append(": ").Append(regionName);

        if (HasTerrain)
        {
            var terrain = _terrain!;
            sb.AppendLine().Append(Text("地图尺寸", "Map size")).Append(": ")
                .Append(terrain.Width.ToString(CultureInfo.InvariantCulture)).Append(" x ")
                .Append(terrain.Height.ToString(CultureInfo.InvariantCulture));

            var total = terrain.Width * terrain.Height;
            var land = terrain.LandCells;
            var sea = terrain.SeaCells;
            var blocked = terrain.BlockedCells;
            var road = terrain.RoadCells;
            sb.AppendLine().Append(Text("地形构成", "Terrain mix")).Append(": ")
                .Append(Text("可达", "reachable")).Append(' ').Append(Percent(land, total)).Append(" / ")
                .Append(Text("水域", "water")).Append(' ').Append(Percent(sea, total)).Append(" / ")
                .Append(Text("不可达", "blocked")).Append(' ').Append(Percent(blocked, total)).Append(" / ")
                .Append(Text("道路", "road")).Append(' ').Append(road.ToString(CultureInfo.InvariantCulture));
        }

        var known = 0;
        var conquered = 0;
        var owned = 0;
        var landmarks = 0;
        var dungeons = 0;
        var sites = 0;
        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (zone.IsKnown) known++;
            if (zone.IsConquered) conquered++;
            if (zone.IsPlayerFaction) owned++;
            else if (zone.IsLandmark) landmarks++;
            else if (zone.IsDungeon) dungeons++;
            else if (zone.IsRandomSite) sites++;
        }
        sb.AppendLine().Append(Text("区块", "Zones")).Append(": ")
            .Append(Text("总计", "total")).Append(' ').Append(zones.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" / ").Append(Text("已知", "known")).Append(' ').Append(known.ToString(CultureInfo.InvariantCulture))
            .Append(" / ").Append(Text("已征服", "conquered")).Append(' ').Append(conquered.ToString(CultureInfo.InvariantCulture))
            .Append(" / ").Append(Text("玩家领地", "owned")).Append(' ').Append(owned.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine().Append(Text("分类", "Breakdown")).Append(": ")
            .Append(Text("地标", "landmark")).Append(' ').Append(landmarks.ToString(CultureInfo.InvariantCulture))
            .Append(" / ").Append(Text("地牢", "dungeon")).Append(' ').Append(dungeons.ToString(CultureInfo.InvariantCulture))
            .Append(" / ").Append(Text("随机点", "random site")).Append(' ').Append(sites.ToString(CultureInfo.InvariantCulture));

        AppendNavigationSummary(sb);
        return sb.ToString();
    }

    internal string BuildLocalSummary()
    {
        var map = GameAccess.World.CurrentMap;
        if (map?.cells == null)
            return Text("当前区块地图不可用", "Current zone map is unavailable");

        int sizeX;
        int sizeZ;
        try
        {
            sizeX = map.cells.GetLength(0);
            sizeZ = map.cells.GetLength(1);
        }
        catch
        {
            return Text("当前区块地图不可用", "Current zone map is unavailable");
        }

        var sb = new StringBuilder();
        sb.Append(Text("当前区块", "Current zone")).Append(": ")
            .Append(SafeName(() => GameAccess.World.CurrentZone?.Name));
        sb.AppendLine().Append(Text("地图尺寸", "Map size")).Append(": ")
            .Append(sizeX.ToString(CultureInfo.InvariantCulture)).Append(" x ")
            .Append(sizeZ.ToString(CultureInfo.InvariantCulture));

        var total = sizeX * sizeZ;
        if (total > 0 && total <= WorldMapMaxCells)
        {
            var seen = 0;
            var water = 0;
            var wall = 0;
            var obj = 0;
            var room = 0;
            for (var z = 0; z < sizeZ; z++)
            {
                for (var x = 0; x < sizeX; x++)
                {
                    try
                    {
                        var cell = map.cells[x, z];
                        if (cell == null)
                            continue;
                        if (cell.isSeen) seen++;
                        if (cell.IsTopWater) water++;
                        if (cell.HasBlock) wall++;
                        if (cell.HasObj) obj++;
                        if (cell.room != null) room++;
                    }
                    catch
                    {
                    }
                }
            }
            sb.AppendLine().Append(Text("已探索", "Explored")).Append(": ").Append(Percent(seen, total));
            sb.AppendLine().Append(Text("地形构成", "Terrain mix")).Append(": ")
                .Append(Text("水域", "water")).Append(' ').Append(Percent(water, total)).Append(" / ")
                .Append(Text("墙壁", "wall")).Append(' ').Append(Percent(wall, total)).Append(" / ")
                .Append(Text("物件", "object")).Append(' ').Append(Percent(obj, total)).Append(" / ")
                .Append(Text("室内", "indoor")).Append(' ').Append(Percent(room, total));
        }

        var charas = -1;
        try
        {
            charas = map.charas?.Count ?? -1;
        }
        catch
        {
        }
        if (charas >= 0)
            sb.AppendLine().Append(Text("角色数量", "Characters")).Append(": ")
                .Append(charas.ToString(CultureInfo.InvariantCulture));

        var things = -1;
        try
        {
            things = map.things?.Count ?? -1;
        }
        catch
        {
        }
        if (things >= 0)
            sb.AppendLine().Append(Text("物品数量", "Things")).Append(": ")
                .Append(things.ToString(CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    private static string Percent(int value, int total)
    {
        if (total <= 0)
            return "0%";
        return (value * 100 / total).ToString(CultureInfo.InvariantCulture) + "%";
    }

    private void AppendNavigationSummary(StringBuilder sb)
    {
        if (!_navigationEnabled)
            return;
        RefreshNavigationPath();
        sb.AppendLine().Append(Text("导航", "Route")).Append(": ");
        if (!HasNavigationTarget)
        {
            sb.Append(Text("未选择目的地", "no destination"));
            return;
        }
        sb.Append('(').Append(_navTargetX.ToString(CultureInfo.InvariantCulture)).Append(", ")
            .Append(_navTargetY.ToString(CultureInfo.InvariantCulture)).Append(") ");
        var steps = NavigationSteps;
        if (steps <= 0)
        {
            sb.Append(Text("无法到达", "unreachable"));
            return;
        }
        sb.Append(steps.ToString(CultureInfo.InvariantCulture)).Append(Text(" 格", " tiles"));
    }

    private static string SafeName(Func<string?> read)
    {
        try
        {
            return read() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
