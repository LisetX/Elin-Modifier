using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{

    private const int NpcInfoRowsPerPage = 13;
    private ScrollRect? _lGuiNpcInfoScroll;

    private void BuildLGuiNpcInfoPage()
    {
        var module = _modules.NpcInfo;
        var scroll = CreateLGuiScroll(_lGuiPageHost!, "NpcInfoScroll", 0f);
        _lGuiNpcInfoScroll = scroll;
        var content = scroll.content!;
        var y = 8f;

        CreateLGuiButton(
            content,
            "NpcInfoCatalogTab",
            module.ShowCurrentZone ? T("NPC查询", "NPC catalog") : "→ " + T("NPC查询", "NPC catalog"),
            0f,
            y,
            180f,
            46f,
            () =>
            {
                module.ShowCurrentZone = false;
                module.NpcPage = 0;
                SwitchLGuiPage(LGuiPage.NpcInfo);
            });
        CreateLGuiButton(
            content,
            "NpcInfoCurrentZoneTab",
            module.ShowCurrentZone ? "→ " + T("当前区块", "Current zone") : T("当前区块", "Current zone"),
            194f,
            y,
            180f,
            46f,
            () =>
            {
                module.ShowCurrentZone = true;
                module.ZonePage = 0;
                SwitchLGuiPage(LGuiPage.NpcInfo);
            });
        var quickLookup = CreateLGuiToggle(content, "NpcInfoQuickLookup", 388f, y, 260f, 46f, out var quickLookupLabel);
        quickLookupLabel.text = T("快捷查询", "Quick lookup");
        quickLookup.isOn = module.QuickLookupEnabled;
        quickLookup.onValueChanged.AddListener(value =>
        {
            module.LoadQuickLookup(value);
            SaveConfig(false);
        });
        CreateLGuiButton(
            content,
            "RefreshNpcInformation",
            T("刷新数据", "Refresh data"),
            1180f,
            y,
            180f,
            46f,
            () =>
            {
                module.Refresh();
                SwitchLGuiPage(LGuiPage.NpcInfo);
            });
        y += 62f;

        if (module.ShowCurrentZone)
            y = BuildLGuiCurrentZoneNpcInformation(content, y);
        else
            y = BuildLGuiNpcInformationCatalog(content, y);

        content.sizeDelta = new Vector2(0f, Math.Max(900f, y + 30f));
    }

    private float BuildLGuiNpcInformationCatalog(RectTransform content, float y)
    {
        var module = _modules.NpcInfo;
        var filterLabel = CreateLGuiText(
            content,
            "NpcInfoFilterLabel",
            T("NPC名称 / ID / 种族 / 职业 / 群落", "NPC name / ID / race / job / biome"),
            15,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(filterLabel.rectTransform, 0f, y, 430f, 28f);
        y += 28f;
        var filter = CreateLGuiInput(content, "NpcInfoFilter", T("搜索NPC", "Search NPCs"), 0f, y, 500f, 44f);
        filter.text = module.Filter;
        filter.onValueChanged.AddListener(value => module.Filter = value ?? "");
        filter.onEndEdit.AddListener(value =>
        {
            module.Filter = value ?? "";
            module.NpcPage = 0;
            SwitchLGuiPage(LGuiPage.NpcInfo);
        });
        CreateLGuiButton(content, "ApplyNpcInfoFilter", T("搜索", "Search"), 514f, y, 100f, 44f, () =>
        {
            module.Filter = filter.text ?? "";
            module.NpcPage = 0;
            SwitchLGuiPage(LGuiPage.NpcInfo);
        });
        CreateLGuiButton(content, "ClearNpcInfoFilter", T("清空", "Clear"), 628f, y, 100f, 44f, () =>
        {
            module.Filter = "";
            module.NpcPage = 0;
            SwitchLGuiPage(LGuiPage.NpcInfo);
        });
        var randomOnly = CreateLGuiToggle(content, "NpcInfoRandomOnly", 754f, y, 300f, 44f, out var randomOnlyLabel);
        randomOnlyLabel.text = T("仅显示常规随机生成NPC", "Normal random-spawn NPCs only");
        randomOnly.isOn = module.RandomOnly;
        randomOnly.onValueChanged.AddListener(value =>
        {
            module.RandomOnly = value;
            module.NpcPage = 0;
            SwitchLGuiPage(LGuiPage.NpcInfo);
        });
        y += 58f;

        var rows = module.GetFilteredNpcs();
        var randomCount = rows.Count(item => item.Chance > 0);
        var summary = CreateLGuiText(
            content,
            "NpcInfoCatalogSummary",
            T("匹配NPC: ", "Matching NPCs: ") + rows.Count.ToString(CultureInfo.InvariantCulture) +
            T("    可参与常规随机生成: ", "    Normal random-spawn eligible: ") + randomCount.ToString(CultureInfo.InvariantCulture),
            16,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(summary.rectTransform, 0f, y, 1360f, 38f);
        y += 42f;

        y = CreateLGuiNpcInfoCatalogHeader(content, y);
        var pageCount = Math.Max(1, (rows.Count + NpcInfoRowsPerPage - 1) / NpcInfoRowsPerPage);
        module.NpcPage = Mathf.Clamp(module.NpcPage, 0, pageCount - 1);
        var start = module.NpcPage * NpcInfoRowsPerPage;
        var end = Math.Min(rows.Count, start + NpcInfoRowsPerPage);
        if (rows.Count == 0)
        {
            y = CreateLGuiNpcInfoEmptyState(content, "NpcInfoCatalogEmpty", T("没有符合筛选条件的NPC", "No NPCs match the current filters"), y);
        }
        else
        {
            for (var i = start; i < end; i++)
                y = CreateLGuiNpcInfoCatalogRow(content, rows[i], i - start, y);
        }
        y = CreateLGuiNpcInfoPager(content, y, module.NpcPage, pageCount, false);
        return y + 8f;
    }

    private float BuildLGuiCurrentZoneNpcInformation(RectTransform content, float y)
    {
        var module = _modules.NpcInfo;
        var analysis = module.AnalyzeCurrentZone();
        if (analysis == null)
            return CreateLGuiNpcInfoEmptyState(content, "NpcInfoNoZone", T("当前没有已加载的游戏区块", "No game zone is currently loaded"), y);

        y = AddLGuiSectionTitle(content, T("区块信息", "Zone information"), y);
        y = CreateLGuiNpcInfoKeyValue(content, y, T("区块", "Zone"), analysis.ZoneName + "  [" + analysis.ZoneType + "]");
        y = CreateLGuiNpcInfoKeyValue(content, y, T("危险度 / 缩放", "Danger / scaling"),
            analysis.DangerLevel.ToString(CultureInfo.InvariantCulture) + " / " + analysis.Scaling);
        y = CreateLGuiNpcInfoKeyValue(content, y, T("玩家所在群落", "Player-cell biome"), analysis.CurrentBiome);
        y = CreateLGuiNpcInfoKeyValue(content, y, T("可生成位置群落", "Spawnable-position biomes"), analysis.BiomeCoverage);
        y = CreateLGuiNpcInfoKeyValue(content, y, T("地图现有NPC", "Existing map NPCs"),
            analysis.ExistingNpcCount.ToString(CultureInfo.InvariantCulture) +
            T("（敌对 ", " (hostile ") + analysis.ExistingHostileCount.ToString(CultureInfo.InvariantCulture) + ")");
        y += 10f;

        var modeLabel = CreateLGuiText(content, "CurrentZoneSpawnModeLabel", T("生成模式:", "Spawn mode:"), 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(modeLabel.rectTransform, 0f, y, 120f, 44f);
        CreateAutomationDropdown(
            content,
            "CurrentZoneSpawnMode",
            new List<string>
            {
                T("敌对", "Enemy"),
                T("中立", "Neutral"),
                T("随机敌对关系", "Random hostility")
            },
            Mathf.Clamp(module.ZoneSpawnMode, 0, 2),
            124f,
            y,
            330f,
            44f,
            selectedIndex =>
            {
                module.ZoneSpawnMode = Mathf.Clamp(selectedIndex, 0, 2);
                module.ZonePage = 0;
                SwitchLGuiPage(LGuiPage.NpcInfo);
            });
        y += 58f;

        var filter = CreateLGuiInput(content, "CurrentZoneNpcFilter", T("过滤", "Filter"), 0f, y, 560f, 44f);
        filter.text = module.ZoneFilter;
        filter.onValueChanged.AddListener(value => module.ZoneFilter = value ?? "");
        filter.onEndEdit.AddListener(value =>
        {
            module.ZoneFilter = value ?? "";
            module.ZonePage = 0;
            SwitchLGuiPage(LGuiPage.NpcInfo);
        });
        CreateLGuiButton(content, "ApplyCurrentZoneNpcFilter", T("搜索", "Search"), 574f, y, 100f, 44f, () =>
        {
            module.ZoneFilter = filter.text ?? "";
            module.ZonePage = 0;
            SwitchLGuiPage(LGuiPage.NpcInfo);
        });
        CreateLGuiButton(content, "ClearCurrentZoneNpcFilter", T("清空", "Clear"), 688f, y, 100f, 44f, () =>
        {
            module.ZoneFilter = "";
            module.ZonePage = 0;
            SwitchLGuiPage(LGuiPage.NpcInfo);
        });
        y += 58f;

        var zoneFilter = (module.ZoneFilter ?? "").Trim();
        var rows = analysis.Npcs.Where(item =>
                zoneFilter.Length == 0 ||
                item.Npc.Name.IndexOf(zoneFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Npc.Id.IndexOf(zoneFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Npc.Race.IndexOf(zoneFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.MainRoute.IndexOf(zoneFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        var summary = CreateLGuiText(
            content,
            "CurrentZoneNpcSummary",
            T("常规生成候选: ", "Normal spawn candidates: ") + rows.Count.ToString(CultureInfo.InvariantCulture),
            16,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(summary.rectTransform, 0f, y, 1360f, 38f);
        y += 42f;

        y = CreateLGuiCurrentZoneNpcHeader(content, y);
        var pageCount = Math.Max(1, (rows.Count + NpcInfoRowsPerPage - 1) / NpcInfoRowsPerPage);
        module.ZonePage = Mathf.Clamp(module.ZonePage, 0, pageCount - 1);
        var start = module.ZonePage * NpcInfoRowsPerPage;
        var end = Math.Min(rows.Count, start + NpcInfoRowsPerPage);
        if (rows.Count == 0)
        {
            y = CreateLGuiNpcInfoEmptyState(content, "CurrentZoneNpcEmpty", T("当前筛选条件下没有常规生成候选", "No normal spawn candidates match the current filter"), y);
        }
        else
        {
            for (var i = start; i < end; i++)
                y = CreateLGuiCurrentZoneNpcRow(content, rows[i], i, i - start, y);
        }
        y = CreateLGuiNpcInfoPager(content, y, module.ZonePage, pageCount, true);
        return y + 8f;
    }

    private float CreateLGuiNpcInfoCatalogHeader(RectTransform content, float y)
    {
        var row = CreateLGuiNpcInfoRow(content, "NpcInfoCatalogHeader", 0, y, 42f, true);
        CreateLGuiNpcInfoCell(row, T("NPC", "NPC"), 64f, 0f, 292f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, "ID", 364f, 0f, 230f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, T("基础等级", "Base Lv"), 602f, 0f, 104f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, T("生成权重", "Weight"), 714f, 0f, 104f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, T("种族 / 职业", "Race / job"), 826f, 0f, 380f, 42f, TextAnchor.MiddleLeft, 16);
        return y + 48f;
    }

    private float CreateLGuiNpcInfoCatalogRow(RectTransform content, NpcRecord npc, int index, float y)
    {
        var row = CreateLGuiNpcInfoRow(content, "NpcInfoCatalogRow" + index, index, y, 54f, false);
        CreateLGuiNpcInfoIcon(row, npc, "NpcIcon", 20f, 8f, 38f);
        CreateLGuiNpcInfoCell(row, npc.Name, 64f, 0f, 292f, 54f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, npc.Id, 364f, 0f, 230f, 54f, TextAnchor.MiddleLeft, 14);
        CreateLGuiNpcInfoCell(row, npc.BaseLevel.ToString(CultureInfo.InvariantCulture), 602f, 0f, 104f, 54f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, npc.Chance.ToString(CultureInfo.InvariantCulture), 714f, 0f, 104f, 54f, TextAnchor.MiddleLeft, 15);
        var race = GetLGuiNpcRaceDisplayName(npc);
        var job = GetLGuiNpcJobDisplayName(npc);
        CreateLGuiNpcInfoCell(row, race + (string.IsNullOrWhiteSpace(job) ? "" : " / " + job), 826f, 0f, 380f, 54f, TextAnchor.MiddleLeft, 14);
        CreateLGuiButton(row, "View", T("查看", "View"), 1220f, 5f, 130f, 44f, () => OpenLGuiNpcInformation(npc.Id));
        return y + 60f;
    }

    private float CreateLGuiCurrentZoneNpcHeader(RectTransform content, float y)
    {
        var row = CreateLGuiNpcInfoRow(content, "CurrentZoneNpcHeader", 0, y, 42f, true);
        CreateLGuiNpcInfoCell(row, "#", 12f, 0f, 50f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, T("NPC", "NPC"), 116f, 0f, 252f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, "ID", 376f, 0f, 210f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, T("等级", "Lv"), 594f, 0f, 74f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, T("生成概率", "Probability"), 676f, 0f, 150f, 42f, TextAnchor.MiddleLeft, 16);
        CreateLGuiNpcInfoCell(row, T("主要群落", "Main biome"), 834f, 0f, 370f, 42f, TextAnchor.MiddleLeft, 16);
        return y + 48f;
    }

    private float CreateLGuiCurrentZoneNpcRow(
        RectTransform content,
        ZoneNpcResult result,
        int absoluteIndex,
        int visualIndex,
        float y)
    {
        var npc = result.Npc;
        var row = CreateLGuiNpcInfoRow(content, "CurrentZoneNpcRow" + visualIndex, visualIndex, y, 54f, false);
        CreateLGuiNpcInfoCell(row, (absoluteIndex + 1).ToString(CultureInfo.InvariantCulture), 12f, 0f, 50f, 54f, TextAnchor.MiddleLeft, 14);
        CreateLGuiNpcInfoIcon(row, npc, "NpcIcon", 72f, 8f, 38f);
        CreateLGuiNpcInfoCell(row, npc.Name, 116f, 0f, 252f, 54f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, npc.Id, 376f, 0f, 210f, 54f, TextAnchor.MiddleLeft, 14);
        CreateLGuiNpcInfoCell(row, npc.BaseLevel.ToString(CultureInfo.InvariantCulture), 594f, 0f, 74f, 54f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, _modules.NpcInfo.FormatProbability(result.Probability), 676f, 0f, 150f, 54f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, result.MainRoute, 834f, 0f, 370f, 54f, TextAnchor.MiddleLeft, 14);
        CreateLGuiButton(row, "View", T("查看", "View"), 1220f, 5f, 130f, 44f, () => OpenLGuiNpcInformation(npc.Id));
        return y + 60f;
    }

    private float CreateLGuiNpcInfoPager(RectTransform content, float y, int page, int pageCount, bool currentZone)
    {
        var module = _modules.NpcInfo;
        CreateLGuiButton(content, "PreviousNpcInfoPage", T("上一页", "Previous"), 0f, y, 130f, 44f, () =>
        {
            if (currentZone)
                module.ZonePage = Math.Max(0, module.ZonePage - 1);
            else
                module.NpcPage = Math.Max(0, module.NpcPage - 1);
            RebuildLGuiNpcInfoPagePreservingScroll();
        }).interactable = page > 0;
        CreateLGuiButton(content, "NextNpcInfoPage", T("下一页", "Next"), 144f, y, 130f, 44f, () =>
        {
            if (currentZone)
                module.ZonePage = Math.Min(pageCount - 1, module.ZonePage + 1);
            else
                module.NpcPage = Math.Min(pageCount - 1, module.NpcPage + 1);
            RebuildLGuiNpcInfoPagePreservingScroll();
        }).interactable = page + 1 < pageCount;
        var pageLabel = CreateLGuiText(
            content,
            "NpcInfoPageLabel",
            (page + 1).ToString(CultureInfo.InvariantCulture) + " / " + pageCount.ToString(CultureInfo.InvariantCulture),
            16,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(pageLabel.rectTransform, 292f, y, 220f, 44f);
        return y + 56f;
    }

    private void RebuildLGuiNpcInfoPagePreservingScroll()
    {
        var normalizedPosition = _lGuiNpcInfoScroll == null
            ? 1f
            : _lGuiNpcInfoScroll.verticalNormalizedPosition;

        SwitchLGuiPage(LGuiPage.NpcInfo);

        if (_lGuiNpcInfoScroll == null)
            return;

        Canvas.ForceUpdateCanvases();
        _lGuiNpcInfoScroll.StopMovement();
        _lGuiNpcInfoScroll.verticalNormalizedPosition = Mathf.Clamp01(normalizedPosition);
    }

    private RectTransform CreateLGuiNpcInfoRow(RectTransform content, string name, int index, float y, float height, bool header)
    {
        var row = CreateLGuiRect(content, name);
        PlaceLGuiRect(row, 0f, y, 1360f, height);
        var image = row.gameObject.AddComponent<Image>();
        image.color = GetLGuiRowColor(index, header);
        RegisterLGuiRoundedImage(image);
        return row;
    }

    private bool CreateLGuiNpcInfoIcon(
        Transform parent,
        NpcRecord npc,
        string name,
        float x,
        float y,
        float size)
    {
        var image = CreateLGuiImage(parent, name, x, y, size, size);
        image.preserveAspect = true;
        image.raycastTarget = false;
        try
        {
            var idSkin = 0;
            try
            {
                if (GameAccess.Runtime.Core?.config?.game?.antiSpider == true && npc.Row.skinAntiSpider != 0)
                    idSkin = npc.Row.skinAntiSpider;
            }
            catch
            {
            }
            image.sprite = npc.Row.GetSprite(0, idSkin, false);
        }
        catch
        {
            image.sprite = null;
        }
        var hasSprite = image.sprite != null;
        image.gameObject.SetActive(hasSprite);
        return hasSprite;
    }

    private Text CreateLGuiNpcInfoCell(
        RectTransform row,
        string value,
        float x,
        float y,
        float width,
        float height,
        TextAnchor alignment,
        int fontSize)
    {
        var text = CreateLGuiText(row, "Cell", value ?? "", fontSize, alignment, FontStyle.Normal);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        PlaceLGuiRect(text.rectTransform, x, y, width, height);
        return text;
    }

    private float CreateLGuiNpcInfoEmptyState(RectTransform content, string name, string value, float y)
    {
        var empty = CreateLGuiText(content, name, value, 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(empty.rectTransform, 18f, y, 1320f, 54f);
        return y + 62f;
    }

    private float CreateLGuiNpcInfoKeyValue(RectTransform content, float y, string key, string value)
    {
        return CreateLGuiNpcInfoKeyValue(content, y, key, value, 1360f);
    }

    private float CreateLGuiNpcInfoInlineFields(
        RectTransform content,
        float y,
        string value,
        float rowWidth)
    {
        var row = CreateLGuiNpcInfoRow(content, "NpcInfoInlineFields", (int)(y / 46f), y, 42f, false);
        rowWidth = Mathf.Clamp(rowWidth, 520f, 1360f);
        PlaceLGuiRect(row, 0f, y, rowWidth, 42f);
        CreateLGuiNpcInfoCell(row, value, 16f, 0f, rowWidth - 32f, 42f, TextAnchor.MiddleLeft, 14);
        return y + 46f;
    }

    private float CreateLGuiNpcInfoKeyValue(
        RectTransform content,
        float y,
        string key,
        string value,
        float rowWidth)
    {
        var row = CreateLGuiNpcInfoRow(content, "NpcInfoKeyValue", (int)(y / 46f), y, 42f, false);
        rowWidth = Mathf.Clamp(rowWidth, 520f, 1360f);
        PlaceLGuiRect(row, 0f, y, rowWidth, 42f);
        CreateLGuiNpcInfoCell(row, key, 16f, 0f, 220f, 42f, TextAnchor.MiddleLeft, 15);
        CreateLGuiNpcInfoCell(row, value, 244f, 0f, Math.Max(240f, rowWidth - 260f), 42f, TextAnchor.MiddleLeft, 15);
        return y + 46f;
    }

    private void ApplyCurrentLGuiFontScale(Text text, int baseFontSize)
    {
        var fontScale = GetEffectiveUiFontSize() / (float)UiFontSizeDefault;
        text.fontSize = Clamp(Mathf.RoundToInt(baseFontSize * fontScale), 1, 60);
    }
}
