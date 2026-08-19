using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{

    internal bool OpenLGuiNpcInformationFromInteraction(string npcId)
    {
        if (!IsLGuiInitialized() || !_modules.NpcInfo.HasNpc(npcId))
            return false;

        _modules.NpcInfo.ShowCurrentZone = false;
        var alreadyOnNpcInfoPage = _lGuiPage == LGuiPage.NpcInfo;
        ShowLGui();
        if (!alreadyOnNpcInfoPage)
            SwitchLGuiPage(LGuiPage.NpcInfo);
        OpenLGuiNpcInformation(npcId, restoreMainOnClose: false);
        return true;
    }

    private void OpenLGuiNpcInformation(
        string npcId,
        int additionalLevel = 0,
        int startingDangerLevel = 1,
        NpcTemplateInfo? templateOverride = null,
        float? restoredScrollPosition = null,
        bool restoreMainOnClose = true)
    {
        additionalLevel = Math.Max(0, additionalLevel);
        startingDangerLevel = Math.Max(1, startingDangerLevel);
        var analysis = _modules.NpcInfo.AnalyzeNpc(
            npcId,
            additionalLevel,
            startingDangerLevel,
            templateOverride);
        if (analysis == null)
            return;
        var npc = analysis.Npc;
        var modal = CreateLGuiCompleteModal(
            "NpcInformationDetails",
            npc.Name + "  [" + npc.Id + "]",
            out var content,
            1660f,
            1040f);
        if (modal == null)
            return;
        var detailScroll = content.GetComponentInParent<ScrollRect>();
        if (CreateLGuiNpcInfoIcon(modal, npc, "NpcTitleIcon", 24f, 17f, 42f))
        {
            var title = modal.Find("Title") as RectTransform;
            if (title != null)
                PlaceLGuiRect(title, 78f, 14f, 1486f, 48f);
        }
        _lGuiModalRestoreMainOnClose = restoreMainOnClose;
        var templateTooltip = CreateLGuiNpcTemplateTooltip(modal);
        var y = 6f;

        CreateLGuiNpcPlacementModels(content, npc, y);

        y = AddLGuiSectionTitle(content, T("基础信息", "Basic information"), y);
        y = CreateLGuiNpcInfoInlineFields(
            content,
            y,
            T("ID", "ID") + " : " + npc.Id + "  |  " +
            T("名称", "Name") + " : " + npc.Name + "  |  " +
            T("基础等级", "Base level") + " : " + npc.BaseLevel.ToString(CultureInfo.InvariantCulture) + "  |  " +
            T("种族", "Race") + " : " + GetLGuiNpcRaceDisplayName(npc) + "  |  " +
            T("职业", "Job") + " : " + GetLGuiNpcJobDisplayName(npc),
            750f);
        y = CreateLGuiNpcInfoInlineFields(
            content,
            y,
            T("生成权重", "Spawn weight") + " : " + npc.Chance.ToString(CultureInfo.InvariantCulture) + "  |  " +
            T("敌对配置", "Hostility") + " : " + (string.IsNullOrWhiteSpace(npc.Hostility) ? "-" : npc.Hostility) + "  |  " +
            T("品质", "Quality") + " : " + npc.Quality.ToString(CultureInfo.InvariantCulture) + "  |  " +
            T("类别", "Category") + " : " + npc.Category,
            750f);
        var configuredBiome = string.IsNullOrWhiteSpace(npc.Biome)
            ? T("无指定（通用池）", "None specified (generic pool)")
            : _modules.NpcInfo.FormatBiomeName(npc.Biome);
        y = CreateLGuiNpcInfoInlineFields(
            content,
            y,
            T("NPC配置群落", "NPC-configured biome") + " : " + configuredBiome + "  |  " +
            T("当前区块生成概率", "Current-zone probability") + " : " +
            _modules.NpcInfo.FormatProbability(analysis.CurrentZoneProbability),
            750f);
        y = CreateLGuiNpcCalculationControls(
            content,
            "NpcAdditionalLevel",
            T("额外等级修正", "Additional level modifier"),
            additionalLevel,
            y,
            750f,
            out var additionalLevelInput,
            out var additionalLevelButton);
        additionalLevelButton.onClick.AddListener(() =>
        {
            var nextAdditionalLevel = ReadLGuiNpcCalculationValue(additionalLevelInput, 0, 0);
            var scrollPosition = detailScroll == null ? 1f : detailScroll.verticalNormalizedPosition;
            OpenLGuiNpcInformation(
                npc.Id,
                nextAdditionalLevel,
                startingDangerLevel,
                null,
                scrollPosition,
                restoreMainOnClose);
        });

        y = Math.Max(y + 10f, 334f);
        y = AddLGuiSectionTitle(content, T("肢体", "Body parts"), y);
        y = CreateLGuiNpcBodySlotGrid(content, analysis.Template.BodySlots, y);

        y += 10f;
        y = AddLGuiSectionTitle(content, T("装备", "Equipment"), y);
        y = CreateLGuiNpcEquipmentGrid(content, analysis.Template.Equipment, y, templateTooltip);

        y += 10f;
        y = AddLGuiSectionTitle(content, T("主能力", "Main abilities"), y);
        var mainAbilities = new List<NpcTemplateValue>(analysis.Template.MainAbilities);
        if (analysis.Template.Loaded)
        {
            mainAbilities.Add(CreateLGuiNpcTemplateValue(analysis.Template, SKILL.life, T("生命力", "Life"), analysis.Template.Life));
            mainAbilities.Add(CreateLGuiNpcTemplateValue(analysis.Template, SKILL.mana, T("玛那", "Mana"), analysis.Template.Mana));
            mainAbilities.Add(CreateLGuiNpcTemplateValue(analysis.Template, SKILL.vigor, T("活力", "Vigor"), analysis.Template.Vigor));
            mainAbilities.Add(CreateLGuiNpcTemplateValue(analysis.Template, SKILL.SPD, T("速度", "Speed"), analysis.Template.Speed));
            mainAbilities.Add(CreateLGuiNpcTemplateValue(analysis.Template, SKILL.DV, "DV", analysis.Template.DV));
            mainAbilities.Add(CreateLGuiNpcTemplateValue(analysis.Template, SKILL.PV, "PV", analysis.Template.PV));
            mainAbilities.Add(CreateLGuiNpcTemplateValue(
                analysis.Template,
                SKILL.weightlifting,
                T("负重上限", "Weight limit"),
                analysis.Template.WeightLimit,
                true));
        }
        y = CreateLGuiNpcTemplateValueGrid(
            content,
            "NpcMainAbility",
            mainAbilities,
            y,
            templateTooltip,
            true,
            4,
            true);

        y += 10f;
        y = AddLGuiSectionTitle(content, T("技能", "Skills"), y);
        y = CreateLGuiNpcTemplateValueGrid(
            content,
            "NpcSkill",
            analysis.Template.Skills,
            y,
            templateTooltip,
            true,
            3,
            true);

        y += 10f;
        y = AddLGuiSectionTitle(content, T("专长", "Feats"), y);
        y = CreateLGuiNpcTemplateValueGrid(
            content,
            "NpcFeat",
            analysis.Template.Feats,
            y,
            templateTooltip,
            true,
            4,
            true);

        y += 10f;
        y = AddLGuiSectionTitle(content, T("能力", "Abilities"), y);
        y = CreateLGuiNpcTemplateValueGrid(
            content,
            "NpcAbility",
            analysis.Template.Spells,
            y,
            templateTooltip,
            true,
            4,
            false,
            true);

        y += 10f;
        y = AddLGuiSectionTitle(content, T("抗性", "Resistances"), y);
        y = CreateLGuiNpcTemplateValueGrid(
            content,
            "NpcResistance",
            analysis.Template.Resistances,
            y,
            null,
            false,
            4);

        y += 10f;
        y = AddLGuiSectionTitle(content, T("附魔", "Enchantments"), y);
        y = CreateLGuiNpcTemplateValueGrid(
            content,
            "NpcEnchantment",
            analysis.Template.Enchantments,
            y,
            templateTooltip,
            true,
            4);
        if (!string.IsNullOrWhiteSpace(analysis.Template.Error))
            y = CreateLGuiNpcInfoEmptyState(content, "NpcTemplateError",
                T("部分模板数据读取失败：", "Some template data could not be read: ") + analysis.Template.Error, y);

        y += 10f;
        y = AddLGuiSectionTitle(content, T("刷新群落与地牢等级", "Spawn biomes and dungeon levels"), y);
        y = CreateLGuiNpcCalculationControls(
            content,
            "NpcStartingDangerLevel",
            T("起始危险度", "Starting danger level"),
            startingDangerLevel,
            y,
            1360f,
            out var startingDangerInput,
            out var startingDangerButton);
        startingDangerButton.onClick.AddListener(() =>
        {
            var nextStartingDangerLevel = ReadLGuiNpcCalculationValue(startingDangerInput, 1, 1);
            var scrollPosition = detailScroll == null ? 1f : detailScroll.verticalNormalizedPosition;
            OpenLGuiNpcInformation(
                npc.Id,
                additionalLevel,
                nextStartingDangerLevel,
                analysis.Template,
                scrollPosition,
                restoreMainOnClose);
        });
        if (analysis.Locations.Count == 0)
        {
            y = CreateLGuiNpcInfoEmptyState(content, "NpcInfoNoLocations", T("没有找到常规随机生成群落", "No normal random-spawn biome was found"), y);
        }
        else
        {
            y = CreateLGuiNpcInfoKeyValue(content, y, T("最高概率地点", "Highest-probability location"),
                analysis.HighestLocation + " · " + _modules.NpcInfo.FormatProbability(analysis.PeakProbability));
            y = CreateLGuiNpcInfoKeyValue(content, y, T("最高概率危险度", "Peak danger level"),
                analysis.PeakDangerLevel.ToString(CultureInfo.InvariantCulture));
            y = CreateLGuiNpcInfoKeyValue(content, y, T("常规生成危险度区间", "Normal spawn danger range"),
                analysis.MinimumDangerLevel > 0
                    ? analysis.MinimumDangerLevel.ToString(CultureInfo.InvariantCulture) + "+"
                    : "-");

            var header = CreateLGuiNpcInfoRow(content, "NpcLocationHeader", 0, y, 42f, true);
            CreateLGuiNpcInfoCell(header, T("群落 / 特殊地点", "Biome / special location"), 16f, 0f, 440f, 42f, TextAnchor.MiddleLeft, 16);
            CreateLGuiNpcInfoCell(header, T("峰值概率", "Peak probability"), 464f, 0f, 180f, 42f, TextAnchor.MiddleLeft, 16);
            CreateLGuiNpcInfoCell(header, T("峰值危险度", "Peak danger"), 652f, 0f, 150f, 42f, TextAnchor.MiddleLeft, 16);
            CreateLGuiNpcInfoCell(header, T("最低危险度", "Minimum danger"), 810f, 0f, 150f, 42f, TextAnchor.MiddleLeft, 16);
            CreateLGuiNpcInfoCell(header, T("生成列表", "Spawn lists"), 968f, 0f, 360f, 42f, TextAnchor.MiddleLeft, 16);
            y += 48f;
            for (var i = 0; i < analysis.Locations.Count; i++)
            {
                var location = analysis.Locations[i];
                var row = CreateLGuiNpcInfoRow(content, "NpcLocationRow" + i, i, y, 54f, false);
                CreateLGuiNpcInfoCell(row, (i == 0 ? "★ " : "") + location.Name, 16f, 0f, 440f, 54f, TextAnchor.MiddleLeft, 15);
                CreateLGuiNpcInfoCell(row, _modules.NpcInfo.FormatProbability(location.PeakProbability), 464f, 0f, 180f, 54f, TextAnchor.MiddleLeft, 15);
                CreateLGuiNpcInfoCell(row, location.PeakDangerLevel.ToString(CultureInfo.InvariantCulture), 652f, 0f, 150f, 54f, TextAnchor.MiddleLeft, 15);
                CreateLGuiNpcInfoCell(row, location.MinimumDangerLevel.ToString(CultureInfo.InvariantCulture) + "+", 810f, 0f, 150f, 54f, TextAnchor.MiddleLeft, 15);
                CreateLGuiNpcInfoCell(row, location.Route, 968f, 0f, 360f, 54f, TextAnchor.MiddleLeft, 14);
                y += 60f;
            }
        }

        y += 10f;
        y = AddLGuiSectionTitle(content, T("所属生成列表", "Containing spawn lists"), y);
        var listText = analysis.SpawnLists.Count == 0
            ? T("无（可能为剧情、地图预放置或脚本直接生成）", "None (possibly quest, map-preplaced, or script-spawned)")
            : string.Join(" / ", analysis.SpawnLists);
        var spawnLists = CreateLGuiText(content, "NpcSpawnLists", listText, 15, TextAnchor.UpperLeft, FontStyle.Normal);
        spawnLists.horizontalOverflow = HorizontalWrapMode.Wrap;
        spawnLists.verticalOverflow = VerticalWrapMode.Overflow;
        ApplyCurrentLGuiFontScale(spawnLists, 15);
        PlaceLGuiRect(spawnLists.rectTransform, 12f, y, 1320f, 52f);
        spawnLists.rectTransform.ForceUpdateRectTransforms();
        var spawnListHeight = Math.Max(52f, Mathf.Ceil((spawnLists.preferredHeight + 16f) / 12f) * 12f);
        PlaceLGuiRect(spawnLists.rectTransform, 12f, y, 1320f, spawnListHeight);
        y += spawnListHeight + 10f;

        y = AddLGuiSectionTitle(content, T("掉落物", "Drops"), y);
        if (analysis.Loot.Count == 0)
        {
            y = CreateLGuiNpcInfoEmptyState(content, "NpcLootEmpty", T("未找到可展示的掉落规则", "No displayable drop rule was found"), y);
        }
        else
        {
            y = CreateLGuiNpcLootHeader(content, y);
            for (var i = 0; i < analysis.Loot.Count; i++)
                y = CreateLGuiNpcLootRow(content, analysis.Loot[i], i, y);
        }

        content.sizeDelta = new Vector2(0f, Math.Max(900f, y + 20f));
        ApplyLGuiVisualSettings();
        if (restoredScrollPosition.HasValue && detailScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            detailScroll.StopMovement();
            detailScroll.verticalNormalizedPosition = Mathf.Clamp01(restoredScrollPosition.Value);
        }
    }

    private float CreateLGuiNpcCalculationControls(
        RectTransform content,
        string name,
        string label,
        int value,
        float y,
        float rowWidth,
        out InputField input,
        out Button calculateButton)
    {
        var row = CreateLGuiNpcInfoRow(content, name + "Row", (int)(y / 46f), y, 42f, false);
        rowWidth = Mathf.Clamp(rowWidth, 520f, 1360f);
        PlaceLGuiRect(row, 0f, y, rowWidth, 42f);
        CreateLGuiNpcInfoCell(row, label, 16f, 0f, 220f, 42f, TextAnchor.MiddleLeft, 15);
        input = CreateLGuiInput(row, name + "Input", "", 244f, 4f, 180f, 34f);
        input.contentType = InputField.ContentType.IntegerNumber;
        input.characterLimit = 10;
        input.SetTextWithoutNotify(value.ToString(CultureInfo.InvariantCulture));
        calculateButton = CreateLGuiButton(
            row,
            name + "Calculate",
            T("计算", "Calculate"),
            440f,
            4f,
            120f,
            34f,
            null);
        return y + 46f;
    }

    private static int ReadLGuiNpcCalculationValue(InputField input, int minimum, int fallback)
    {
        var valueText = (input.text ?? "").Trim();
        if (!int.TryParse(valueText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum)
            value = fallback;
        input.SetTextWithoutNotify(value.ToString(CultureInfo.InvariantCulture));
        return value;
    }
}
