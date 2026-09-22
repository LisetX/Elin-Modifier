using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private const int LGuiWorldMapMaxNpcRows = 200;

    private void BuildLGuiWorldMapNpcPanel(RectTransform column, float bottom, float top)
    {
        var panel = CreateLGuiWorldMapSidePanel(column, "WorldMapNpcPanel", bottom, top);
        _lGuiWorldMapNpcHeader = CreateLGuiText(panel, "WorldMapNpcHeader", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiWorldMapNpcHeader.rectTransform, 14f, 6f, 402f, 28f);

        _lGuiWorldMapNpcSubtitle = CreateLGuiText(panel, "WorldMapNpcSubtitle", "", 14, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiWorldMapNpcSubtitle.rectTransform, 14f, 38f, 286f, 24f);

        if (_lGuiWorldMapReadSaves)
            CreateLGuiButton(
                panel,
                "WorldMapNpcReload",
                T("\u91cd\u8bfb", "Reload"),
                304f,
                36f,
                112f,
                28f,
                () => LoadLGuiWorldMapZoneNpcs(ResolveLGuiWorldMapNpcZone(), true));

        var scroll = CreateLGuiScroll(panel, "WorldMapNpcScroll", 70f);
        _lGuiWorldMapNpcList = new VirtualList<WorldMapNpcEntry>(
            scroll,
            LGuiWorldMapRowHeight,
            LGuiWorldMapRowPool,
            CreateLGuiWorldMapRow,
            BindLGuiWorldMapNpcRow);
        RefreshLGuiWorldMapNpcItems();
    }

    private string DescribeLGuiWorldMapNpcOrigin(WorldMapNpcOrigin origin)
    {
        switch (origin)
        {
            case WorldMapNpcOrigin.Live: return T("在场角色", "characters on map");
            case WorldMapNpcOrigin.Snapshot: return T("存档角色(未进入区块)", "characters from save (zone not entered)");
            case WorldMapNpcOrigin.Citizens: return T("登记居民(区块未载入)", "registered citizens (zone not loaded)");
            default: return "";
        }
    }

    private void LoadLGuiWorldMapZoneNpcs(Zone? zone, bool force)
    {
        if (zone == null || !_lGuiWorldMapReadSaves)
            return;
        var module = _modules.WorldMap;
        var result = module.TryLoadZoneNpcSnapshot(zone, force);
        switch (result)
        {
            case WorldMapNpcLoadResult.Loaded:
                module.Log = T("已读取区块存档", "Loaded zone save") + ": " + SafeText(() => zone.Name, "");
                RefreshLGuiWorldMapNpcItems();
                break;
            case WorldMapNpcLoadResult.Failed:
                module.Log = T("读取区块存档失败", "Failed to read the zone save");
                break;
            case WorldMapNpcLoadResult.Unavailable:
                module.Log = T("此区块没有可读取的存档", "This zone has no readable save");
                break;
        }
    }

    private Zone? ResolveLGuiWorldMapNpcZone()
    {
        if (_lGuiWorldMapTab != 0)
        {
            try { return GameAccess.World.CurrentZone; }
            catch { return null; }
        }
        var selected = ResolveLGuiWorldMapSelectedZone();
        if (selected != null)
            return selected.Zone;
        try { return GameAccess.World.CurrentZone; }
        catch { return null; }
    }

    private string BuildLGuiWorldMapNpcRowLabel(WorldMapNpcEntry npc)
    {
        var sb = new StringBuilder();
        sb.Append(LGuiWorldMapNpcMark(npc)).Append(' ').Append(npc.Name);
        if (npc.IsLive)
        {
            sb.Append("  Lv").Append(npc.Level.ToString(CultureInfo.InvariantCulture));
            var role = string.IsNullOrEmpty(npc.JobName) ? npc.RaceName : npc.JobName;
            if (!string.IsNullOrEmpty(role))
                sb.Append("  ").Append(role);
        }
        return sb.ToString();
    }

    private string LGuiWorldMapNpcMark(WorldMapNpcEntry npc)
    {
        if (npc.IsPlayer || npc.IsPlayerFaction)
            return LGuiSwatch(LGuiWorldMapSwatchHome, "*");
        if (!npc.IsLive)
            return LGuiSwatch(LGuiWorldMapSwatchUnknown, "*");
        if (string.Equals(npc.HostilityName, "Enemy", StringComparison.Ordinal))
            return LGuiSwatch(LGuiWorldMapSwatchDungeon, "*");
        if (npc.IsCitizen)
            return LGuiSwatch(LGuiWorldMapSwatchLandmark, "*");
        return LGuiSwatch(LGuiWorldMapSwatchOther, "*");
    }

    private void SelectLGuiWorldMapNpc(WorldMapNpcEntry npc)
    {
        var sb = new StringBuilder();
        sb.Append("NPC: ").Append(npc.Name);
        if (!npc.IsLive)
        {
            AppendLGuiWorldMapLine(sb, T("来源", "Source"), T("登记居民(区块未载入)", "registered citizens (zone not loaded)"));
            AppendLGuiWorldMapLine(sb, "uid", npc.Uid.ToString(CultureInfo.InvariantCulture));
            SetLGuiWorldMapInfo(sb.ToString());
            return;
        }

        AppendLGuiWorldMapLine(sb, "id", npc.Id);
        AppendLGuiWorldMapLine(sb, T("等级", "Level"), npc.Level.ToString(CultureInfo.InvariantCulture));
        AppendLGuiWorldMapLine(sb, T("种族", "Race"), npc.RaceName);
        AppendLGuiWorldMapLine(sb, T("职业", "Job"), npc.JobName);
        AppendLGuiWorldMapLine(sb, T("阵营", "Faction"), npc.FactionId);
        AppendLGuiWorldMapLine(sb, T("态度", "Hostility"), DescribeLGuiWorldMapHostility(npc.HostilityName));
        if (npc.PosX >= 0 && npc.PosZ >= 0)
            AppendLGuiWorldMapLine(
                sb,
                T("位置", "Location"),
                npc.PosX.ToString(CultureInfo.InvariantCulture) + ", " + npc.PosZ.ToString(CultureInfo.InvariantCulture));
        var tags = new List<string>();
        if (npc.IsPlayer)
            tags.Add(T("玩家", "player"));
        if (npc.IsPlayerFaction)
            tags.Add(T("玩家阵营", "player faction"));
        if (npc.IsCitizen)
            tags.Add(T("登记居民", "registered citizen"));
        if (npc.IsGlobal)
            tags.Add(T("全局角色", "global"));
        AppendLGuiWorldMapLine(sb, T("标记", "Flags"), string.Join(", ", tags));

        if (npc.Chara != null)
            AppendLGuiWorldMapLine(sb, T("归属区块", "Home zone"), SafeText(() => npc.Chara.homeZone.Name, ""));
        if (npc.IsSnapshot)
            AppendLGuiWorldMapLine(sb, T("来源", "Source"), T("存档角色(未进入区块)", "characters from save (zone not entered)"));

        SetLGuiWorldMapInfo(sb.ToString());
        _modules.WorldMap.Log = "NPC: " + npc.Name;
    }

    private string DescribeLGuiWorldMapHostility(string hostility)
    {
        switch (hostility)
        {
            case "Enemy": return T("敌对", "enemy");
            case "Neutral": return T("中立", "neutral");
            case "Friend": return T("友好", "friend");
            case "Ally": return T("同伴", "ally");
            default: return hostility;
        }
    }
}
