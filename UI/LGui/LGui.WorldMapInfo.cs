using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed partial class ElinModifierPlugin
{
    private string BuildLGuiWorldMapCellInfo(int x, int y)
    {
        var module = _modules.WorldMap;
        var terrain = module.Terrain;
        if (terrain == null)
            return module.DescribeTerrainState();

        var gridX = x + terrain.MinX;
        var gridY = y + terrain.MinY;
        var sb = new StringBuilder();
        AppendLGuiWorldMapPosition(sb, gridX, gridY);
        sb.AppendLine();
        sb.Append(T("地形", "Terrain")).Append(": ").Append(DescribeLGuiWorldTerrain(terrain.GetKind(x, y)));
        AppendLGuiWorldTileSource(sb, terrain.GetSource(x, y));

        var road = module.GetRoadDistance(gridX, gridY);
        if (road >= 0)
            AppendLGuiWorldMapLine(
                sb,
                T("距最近道路", "Road distance"),
                road == 0
                    ? T("在道路上", "on a road")
                    : road.ToString(CultureInfo.InvariantCulture) + T(" 格", " tiles"));
        AppendLGuiWorldMapPinNote(sb, gridX, gridY);

        var zone = module.FindZoneAt(EnsureLGuiWorldMapZones(), gridX, gridY);
        if (zone == null)
        {
            sb.AppendLine();
            sb.Append(T("此格尚未生成区块(进入后生成)", "Zone not generated yet (created on entry)"));
            AppendLGuiWorldMapForecast(sb, terrain.GetSource(x, y), terrain.GetKind(x, y));
            return sb.ToString();
        }

        AppendLGuiWorldMapZoneDetails(sb, zone);
        return sb.ToString();
    }

    private void AppendLGuiWorldMapPosition(StringBuilder sb, int gridX, int gridY)
    {
        sb.Append(T("坐标", "Position")).Append(": ")
            .Append(gridX.ToString(CultureInfo.InvariantCulture)).Append(", ")
            .Append(gridY.ToString(CultureInfo.InvariantCulture));
    }

    private void AppendLGuiWorldTileSource(StringBuilder sb, SourceGlobalTile.Row? source)
    {
        if (source == null)
        {
            sb.AppendLine();
            sb.Append(T("此格没有地形数据", "No terrain data on this cell"));
            return;
        }
        AppendLGuiWorldMapLine(sb, T("地块", "Tile"), SafeText(() => source.GetName(), source.name ?? ""));
        AppendLGuiWorldMapLine(sb, T("群落", "Biome"), source.idBiome);
        AppendLGuiWorldMapLine(sb, T("区块配置", "Zone profile"), source.zoneProfile);
        if (source.dangerLv > 0)
            AppendLGuiWorldMapLine(sb, T("地块危险度", "Tile danger"), source.dangerLv.ToString(CultureInfo.InvariantCulture));
        AppendLGuiWorldMapLine(sb, T("标签", "Tags"), string.Join(", ", source.tag ?? Array.Empty<string>()));
        AppendLGuiWorldMapLine(sb, T("特性", "Traits"), string.Join(", ", source.trait ?? Array.Empty<string>()));
        AppendLGuiWorldMapLine(sb, T("说明", "Detail"), SafeText(() => source.GetDetail(), source.detail ?? ""));
    }

    private void AppendLGuiWorldMapZoneDetails(StringBuilder sb, WorldMapZoneEntry zone)
    {
        sb.AppendLine();
        sb.Append(T("区块", "Zone")).Append(": ").Append(zone.Name).Append(" [").Append(zone.TypeName).Append("]");
        sb.AppendLine();
        sb.Append(T("类别", "Category")).Append(": ").Append(DescribeLGuiZoneCategory(zone));
        sb.AppendLine();
        sb.Append("Lv ").Append(zone.Level.ToString(CultureInfo.InvariantCulture))
            .Append(" / ").Append(T("危险度", "Danger")).Append(" ")
            .Append(zone.DangerLevel.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
        sb.Append(T("状态", "State")).Append(": ")
            .Append(zone.IsKnown ? T("已知", "known") : T("未知", "unknown"));
        if (zone.IsConquered)
            sb.Append(", ").Append(T("已征服", "conquered"));
        if (zone.IsPlayerFaction)
            sb.Append(", ").Append(T("玩家领地", "player faction"));
        if (zone.IsRandomSite)
            sb.Append(", ").Append(T("随机点", "random site"));

        AppendLGuiZoneSourceInfo(sb, zone);
        AppendLGuiWorldMapLogistics(sb, zone);
    }

    private void AppendLGuiWorldMapForecast(StringBuilder sb, SourceGlobalTile.Row? tile, byte kind)
    {
        var profile = tile == null ? "" : tile.zoneProfile ?? "";
        var enterable = profile.Length > 0 && kind != WorldMapTerrain.KindBlocked;
        AppendLGuiWorldMapLine(sb, T("可进入", "Enterable"), enterable ? T("是", "yes") : T("否", "no"));
        if (!enterable)
            return;

        SourceZone.Row? row = null;
        try
        {
            row = EClass.sources.zones.GetRow("field");
        }
        catch
        {
        }
        if (row != null)
        {
            var name = SafeText(() => row.GetName(), row.name ?? "");
            var type = row.type ?? "";
            AppendLGuiWorldMapLine(
                sb,
                T("进入后生成", "Generates on entry"),
                type.Length > 0 ? name + " [" + type + "]" : name);
        }

        if (tile != null && tile.dangerLv > 0)
            AppendLGuiWorldMapLine(
                sb,
                T("预计危险度", "Expected danger"),
                tile.dangerLv.ToString(CultureInfo.InvariantCulture));

        var affixes = SafeInt(() => EClass.sources.zoneAffixes.rows.Count, 0);
        if (affixes > 0)
            AppendLGuiWorldMapLine(
                sb,
                T("名称前缀", "Name prefix"),
                T("进入时随机", "rolled on entry") + " (" +
                affixes.ToString(CultureInfo.InvariantCulture) + ")");
    }

    private void AppendLGuiWorldMapLogistics(StringBuilder sb, WorldMapZoneEntry zone)
    {
        var module = _modules.WorldMap;
        AppendLGuiWorldMapLine(sb, T("剩余时间", "Expires in"), module.DescribeZoneExpiry(zone.Zone));
        AppendLGuiWorldMapLine(sb, T("重置时间", "Regenerates in"), module.DescribeZoneRegenerate(zone.Zone));
        AppendLGuiWorldMapLine(sb, T("旅行成本", "Travel cost"), module.DescribeTravelCost(zone));
        var road = module.GetRoadDistance(zone.X, zone.Y);
        if (road >= 0)
            AppendLGuiWorldMapLine(
                sb,
                T("距最近道路", "Road distance"),
                road == 0
                    ? T("在道路上", "on a road")
                    : road.ToString(CultureInfo.InvariantCulture) + T(" 格", " tiles"));
        var playerX = GetLGuiWorldMapPlayerX();
        var playerY = GetLGuiWorldMapPlayerY();
        if (playerX != int.MinValue && playerY != int.MinValue)
            AppendLGuiWorldMapLine(
                sb,
                T("距玩家", "Distance"),
                Math.Max(Math.Abs(zone.X - playerX), Math.Abs(zone.Y - playerY))
                    .ToString(CultureInfo.InvariantCulture) + T(" 格", " tiles"));
    }

    private void AppendLGuiZoneSourceInfo(StringBuilder sb, WorldMapZoneEntry zone)
    {
        SourceZone.Row? source = null;
        try
        {
            source = zone.Zone.source;
        }
        catch
        {
        }
        if (source == null)
            return;

        AppendLGuiWorldMapLine(sb, T("阵营", "Faction"), source.faction);
        AppendLGuiWorldMapLine(sb, T("类型", "Type"), source.type);
        AppendLGuiWorldMapLine(sb, T("标签", "Tags"), string.Join(", ", source.tag ?? Array.Empty<string>()));
        AppendLGuiWorldMapLine(sb, T("任务标签", "Quest tags"), string.Join(", ", source.questTag ?? Array.Empty<string>()));
        AppendLGuiWorldMapLine(sb, T("生成器", "Generator"), source.idGen);
        AppendLGuiWorldMapLine(sb, T("文本", "Text"), SafeText(() => source.GetText("textFlavor"), source.textFlavor ?? ""));

        var npcCount = _modules.WorldMap.CountZoneNpcs(zone.Zone);
        if (npcCount >= 0)
            AppendLGuiWorldMapLine(
                sb,
                T("NPC数量", "NPC count"),
                npcCount.ToString(CultureInfo.InvariantCulture) +
                (_modules.WorldMap.IsZoneMapLoaded(zone.Zone)
                    ? ""
                    : " (" + T("登记居民", "registered citizens") + ")"));
        var branchLv = SafeInt(() => zone.Zone.branch.lv, -1);
        if (branchLv >= 0)
            AppendLGuiWorldMapLine(sb, T("据点等级", "Branch level"), branchLv.ToString(CultureInfo.InvariantCulture));
    }

    private string DescribeLGuiZoneCategory(WorldMapZoneEntry zone)
    {
        if (zone.IsPlayerFaction)
            return T("家园", "home");
        if (zone.IsLandmark)
            return T("地标", "landmark");
        if (zone.IsDungeon)
            return T("地牢", "dungeon");
        if (zone.IsRandomSite)
            return T("随机点", "random site");
        if (zone.IsField)
            return T("野外", "field");
        return T("地标", "landmark");
    }

    private static void AppendLGuiWorldMapLine(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        sb.AppendLine();
        sb.Append(label).Append(": ").Append(value);
    }

    private string DescribeLGuiWorldTerrain(byte kind)
    {
        switch (kind)
        {
            case WorldMapTerrain.KindSea: return T("海洋(不可通行)", "sea (impassable)");
            case WorldMapTerrain.KindShore: return T("浅滩", "shore");
            case WorldMapTerrain.KindLand: return T("陆地(可通行)", "land (walkable)");
            case WorldMapTerrain.KindRoad: return T("道路", "road");
            case WorldMapTerrain.KindBlocked: return T("阻挡(山岩)", "blocked (rock)");
            default: return T("未知", "unknown");
        }
    }

    private string BuildLGuiLocalMapCellInfo(int x, int z)
    {
        var map = GameAccess.World.CurrentMap;
        if (map?.cells == null)
            return T("当前区块地图不可用", "Current zone map is unavailable");
        Cell? cell = null;
        try
        {
            if (x >= 0 && z >= 0 && x < map.cells.GetLength(0) && z < map.cells.GetLength(1))
                cell = map.cells[x, z];
        }
        catch
        {
        }
        if (cell == null)
            return T("此格超出地图范围", "Cell is outside the map");

        var sb = new StringBuilder();
        sb.Append(T("坐标", "Position")).Append(": ").Append(x.ToString(CultureInfo.InvariantCulture))
            .Append(", ").Append(z.ToString(CultureInfo.InvariantCulture));
        AppendLGuiWorldMapLine(sb, T("已探索", "Explored"), cell.isSeen ? T("是", "yes") : T("否", "no"));
        AppendLGuiWorldMapLine(sb, T("地板", "Floor"), SafeText(() => cell.sourceFloor.GetName(), ""));
        AppendLGuiWorldMapLine(sb, T("地板材质", "Floor material"), SafeText(() => cell.matFloor.GetName(), ""));
        if (cell._block != 0)
        {
            AppendLGuiWorldMapLine(sb, T("墙壁", "Block"), SafeText(() => cell.sourceBlock.GetName(), ""));
            AppendLGuiWorldMapLine(sb, T("墙壁材质", "Block material"), SafeText(() => cell.matBlock.GetName(), ""));
        }
        if (cell.obj != 0)
        {
            AppendLGuiWorldMapLine(sb, T("物件", "Object"), SafeText(() => cell.sourceObj.GetName(), ""));
            AppendLGuiWorldMapLine(sb, T("物件材质", "Object material"), SafeText(() => cell.matObj.GetName(), ""));
        }
        if (cell.HasBridge)
            AppendLGuiWorldMapLine(sb, T("桥", "Bridge"), SafeText(() => cell.matBridge.GetName(), ""));
        if (cell.IsTopWater)
            AppendLGuiWorldMapLine(sb, T("水域", "Water"), T("是", "yes"));
        AppendLGuiWorldMapLine(sb, T("房间", "Room"), SafeText(() => cell.room.Name, ""));
        AppendLGuiWorldMapLine(sb, T("此格角色", "Characters here"), BuildLGuiLocalCellCharacters(map, x, z));
        AppendLGuiWorldMapLine(sb, T("此格物品", "Things here"), BuildLGuiLocalCellThings(map, x, z));
        return sb.ToString();
    }

    private string BuildLGuiLocalCellThings(Map map, int x, int z)
    {
        try
        {
            var things = map.things;
            if (things == null)
                return "";
            var names = new List<string>();
            for (var i = 0; i < things.Count && names.Count < 8; i++)
            {
                var thing = things[i];
                if (thing?.pos == null || thing.pos.x != x || thing.pos.z != z)
                    continue;
                var name = SafeText(() => thing.Name, "");
                if (SafeInt(() => thing.IsContainer ? 1 : 0, 0) != 0)
                    name += " [" + T("容器", "container") + "]";
                names.Add(name);
            }
            return string.Join(", ", names);
        }
        catch
        {
            return "";
        }
    }

    private static string BuildLGuiLocalCellCharacters(Map map, int x, int z)
    {
        try
        {
            var charas = map.charas;
            if (charas == null)
                return "";
            var names = new List<string>();
            for (var i = 0; i < charas.Count && names.Count < 6; i++)
            {
                var chara = charas[i];
                if (chara?.pos == null || chara.pos.x != x || chara.pos.z != z)
                    continue;
                names.Add(SafeText(() => chara.Name, ""));
            }
            return string.Join(", ", names);
        }
        catch
        {
            return "";
        }
    }
}
