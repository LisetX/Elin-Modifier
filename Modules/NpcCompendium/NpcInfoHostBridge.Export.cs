using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    internal void AppendNpcCompendiumWorkbookEntry(
        NpcCompendiumWorkbookWriter workbook,
        NpcAnalysis analysis,
        IDictionary<string, byte[]> imageCache)
    {
        var npc = analysis.Npc;
        var npcSprite = GetLGuiNpcPlacementSprite(npc);
        var npcIcon = GetNpcWorkbookSpritePng(npcSprite, imageCache, null);
        var cardIcon = GetNpcWorkbookSpritePng(npcSprite, imageCache, GetLGuiNpcCardPlacementColor());

        workbook.AddTitle(npc.Name + "  [" + npc.Id + "]", npcIcon, npc.Name, npc.Id);
        workbook.AddSection(T("基础信息", "Basic information"));
        workbook.AddFullWidthText(
            T("ID", "ID") + " : " + npc.Id + "  |  " +
            T("名称", "Name") + " : " + npc.Name + "  |  " +
            T("基础等级", "Base level") + " : " + npc.BaseLevel.ToString(CultureInfo.InvariantCulture) + "  |  " +
            T("种族", "Race") + " : " + GetLGuiNpcRaceDisplayName(npc) + "  |  " +
            T("职业", "Job") + " : " + GetLGuiNpcJobDisplayName(npc));
        workbook.AddFullWidthText(
            T("生成权重", "Spawn weight") + " : " + npc.Chance.ToString(CultureInfo.InvariantCulture) + "  |  " +
            T("敌对配置", "Hostility") + " : " + (string.IsNullOrWhiteSpace(npc.Hostility) ? "-" : npc.Hostility) + "  |  " +
            T("品质", "Quality") + " : " + npc.Quality.ToString(CultureInfo.InvariantCulture) + "  |  " +
            T("类别", "Category") + " : " + npc.Category,
            true);
        var configuredBiome = string.IsNullOrWhiteSpace(npc.Biome)
            ? T("无指定（通用池）", "None specified (generic pool)")
            : _modules.NpcInfo.FormatBiomeName(npc.Biome);
        workbook.AddFullWidthText(
            T("NPC配置群落", "NPC-configured biome") + " : " + configuredBiome + "  |  " +
            T("当前区块生成概率", "Current-zone probability") + " : " +
            _modules.NpcInfo.FormatProbability(analysis.CurrentZoneProbability));
        workbook.AddPreview(T("标本", "Figure"), npcIcon, T("卡片", "Card"), cardIcon);

        AppendNpcWorkbookBodyParts(workbook, analysis.Template.BodySlots, imageCache);
        AppendNpcWorkbookEquipment(workbook, analysis.Template.Equipment, imageCache);

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

        AppendNpcWorkbookTemplateSection(
            workbook,
            T("主能力", "Main abilities"),
            mainAbilities,
            4,
            true,
            false,
            imageCache);
        AppendNpcWorkbookTemplateSection(
            workbook,
            T("技能", "Skills"),
            analysis.Template.Skills,
            3,
            true,
            false,
            imageCache);
        AppendNpcWorkbookTemplateSection(
            workbook,
            T("专长", "Feats"),
            analysis.Template.Feats,
            4,
            true,
            false,
            imageCache);
        AppendNpcWorkbookTemplateSection(
            workbook,
            T("能力", "Abilities"),
            analysis.Template.Spells,
            4,
            false,
            true,
            imageCache);
        AppendNpcWorkbookTemplateSection(
            workbook,
            T("抗性", "Resistances"),
            analysis.Template.Resistances,
            4,
            false,
            false,
            imageCache);
        AppendNpcWorkbookTemplateSection(
            workbook,
            T("附魔", "Enchantments"),
            analysis.Template.Enchantments,
            4,
            false,
            false,
            imageCache);

        if (!string.IsNullOrWhiteSpace(analysis.Template.Error))
            workbook.AddFullWidthText(
                T("部分模板数据读取失败：", "Some template data could not be read: ") + analysis.Template.Error);

        AppendNpcWorkbookSpawnInformation(workbook, analysis);
        AppendNpcWorkbookLoot(workbook, analysis.Loot, imageCache);
    }

    private void AppendNpcWorkbookBodyParts(
        NpcCompendiumWorkbookWriter workbook,
        IReadOnlyList<NpcBodySlotEntry> bodySlots,
        IDictionary<string, byte[]> imageCache)
    {
        workbook.AddSection(T("肢体", "Body parts"));
        var cells = new List<NpcWorkbookGridCell>();
        for (var i = 0; i < bodySlots.Count; i++)
        {
            cells.Add(new NpcWorkbookGridCell
            {
                Icon = GetNpcWorkbookSpritePng(ResolveNpcWorkbookBodyIcon(bodySlots[i]), imageCache, null),
                Text = bodySlots[i].Name
            });
        }
        workbook.AddGrid(cells, 4);
        for (var i = 0; i < bodySlots.Count; i++)
        {
            var entry = bodySlots[i];
            workbook.AddSubDetail(
                GetNpcWorkbookSpritePng(ResolveNpcWorkbookBodyIcon(entry), imageCache, null),
                entry.Name,
                "Element ID : " + entry.ElementId.ToString(CultureInfo.InvariantCulture) +
                "  |  Slot : " + (entry.Index + 1).ToString(CultureInfo.InvariantCulture),
                i % 2 != 0);
        }
    }

    private void AppendNpcWorkbookEquipment(
        NpcCompendiumWorkbookWriter workbook,
        IReadOnlyList<NpcEquipmentEntry> equipment,
        IDictionary<string, byte[]> imageCache)
    {
        workbook.AddSection(T("装备", "Equipment"));
        var cells = new List<NpcWorkbookGridCell>();
        for (var i = 0; i < equipment.Count; i++)
        {
            var entry = equipment[i];
            var displayText = entry.Name;
            if (entry.Quantity > 1)
                displayText += " ×" + entry.Quantity.ToString(CultureInfo.InvariantCulture);
            var slotName = GetLGuiNpcEquipmentSlotName(entry);
            if (!string.IsNullOrWhiteSpace(slotName))
                displayText += " [" + slotName + "]";
            cells.Add(new NpcWorkbookGridCell
            {
                Icon = GetNpcWorkbookSpritePng(ResolveNpcWorkbookEquipmentIcon(entry), imageCache, null),
                Text = displayText
            });
        }
        workbook.AddGrid(cells, 4);
        for (var i = 0; i < equipment.Count; i++)
        {
            var entry = equipment[i];
            workbook.AddDetail(
                GetNpcWorkbookSpritePng(ResolveNpcWorkbookEquipmentIcon(entry), imageCache, null),
                entry.Name,
                "ID : " + entry.Id,
                BuildLGuiNpcEquipmentTooltip(entry),
                i % 2 != 0);
        }
    }

    private void AppendNpcWorkbookTemplateSection(
        NpcCompendiumWorkbookWriter workbook,
        string title,
        IReadOnlyList<NpcTemplateValue> values,
        int columns,
        bool displayRandomRangeOnly,
        bool useAbilityIcon,
        IDictionary<string, byte[]> imageCache)
    {
        workbook.AddSection(title);
        var cells = new List<NpcWorkbookGridCell>();
        for (var i = 0; i < values.Count; i++)
        {
            var entry = values[i];
            cells.Add(new NpcWorkbookGridCell
            {
                Icon = GetNpcWorkbookSpritePng(
                    ResolveNpcWorkbookTemplateIcon(entry, useAbilityIcon),
                    imageCache,
                    null),
                Text = GetNpcWorkbookTemplateLabel(entry) + " : " +
                       FormatLGuiNpcTemplateDisplayValue(entry, displayRandomRangeOnly)
            });
        }
        workbook.AddGrid(cells, columns);

        for (var i = 0; i < values.Count; i++)
        {
            var entry = values[i];
            var icon = GetNpcWorkbookSpritePng(
                ResolveNpcWorkbookTemplateIcon(entry, useAbilityIcon),
                imageCache,
                null);
            var details = useAbilityIcon
                ? BuildNpcWorkbookAbilityDetails(entry)
                : "ID : " + entry.Id.ToString(CultureInfo.InvariantCulture) + "\n" +
                  BuildLGuiNpcTemplateTooltipBody(entry);
            workbook.AddDetail(
                icon,
                GetNpcWorkbookTemplateLabel(entry),
                FormatLGuiNpcTemplateDisplayValue(entry, displayRandomRangeOnly),
                details,
                i % 2 != 0);
            if (useAbilityIcon)
                AppendNpcWorkbookAbilitySupplement(workbook, entry, imageCache, i % 2 != 0);
        }
    }

    private string GetNpcWorkbookTemplateLabel(NpcTemplateValue entry)
    {
        var label = entry.Name;
        if (!entry.IsResistance)
            return label;
        if (_language == "zh")
        {
            if (!label.EndsWith("抗性", StringComparison.Ordinal))
                label += "抗性";
        }
        else if (label.IndexOf("resistance", StringComparison.OrdinalIgnoreCase) < 0)
        {
            label += " resistance";
        }
        return label;
    }

    private string BuildNpcWorkbookAbilityDetails(NpcTemplateValue entry)
    {
        var lines = new List<string>
        {
            "ID : " + entry.Id.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(entry.TriggerQuestId))
        {
            lines.Add(
                T("获取条件:", "Acquisition condition:") + " " +
                T("完成剧情任务 ", "Complete story quest ") + entry.TriggerQuestId);
        }
        lines.Add(BuildLGuiNpcTemplateTooltipBody(entry));
        var ability = entry.AbilityTooltip;
        if (ability == null)
            return string.Join("\n", lines);
        lines.Add(
            T("模式:", "Mode:") +
            (ability.IsPartyTarget ? T("群体", "Group") : T("单体", "Single")) +
            "  |  " + T("使用概率:", "Usage chance:") +
            ability.UsageChance.ToString(CultureInfo.InvariantCulture) + "%");
        var chance = ability.HasSuccessRate
            ? ability.SuccessRate.ToString(CultureInfo.InvariantCulture) + "%"
            : "-";
        lines.Add(
            T("等级", "Level") + " " + ability.DisplayLevel.ToString(CultureInfo.InvariantCulture) + "  |  " +
            T("目标", "Target") + " " + ability.Target + "  |  " +
            T("成功率", "Success rate") + " " + chance);
        if (!string.IsNullOrWhiteSpace(ability.RelatedAbility) || ability.HasPower)
        {
            var related = T("关联能力", "Related ability") + " : " + ability.RelatedAbility;
            if (ability.HasPower)
                related += "  |  " + T("威力", "Power") + " " + ability.Power.ToString(CultureInfo.InvariantCulture);
            lines.Add(related);
        }
        for (var i = 0; i < ability.Notes.Count; i++)
            lines.Add("• " + ability.Notes[i]);
        var costs = GetNpcWorkbookAbilityCosts(entry.Id, ability);
        for (var i = 0; i < costs.Count; i++)
            lines.Add(costs[i].Label + " : " + costs[i].Value);
        return string.Join("\n", lines);
    }

    private void AppendNpcWorkbookAbilitySupplement(
        NpcCompendiumWorkbookWriter workbook,
        NpcTemplateValue entry,
        IDictionary<string, byte[]> imageCache,
        bool alternate)
    {
        var ability = entry.AbilityTooltip;
        if (ability == null)
            return;
        if (!string.IsNullOrWhiteSpace(ability.RelatedAbility) || ability.HasPower)
        {
            var related = ability.RelatedAbility;
            if (ability.HasPower)
                related += (related.Length > 0 ? "  |  " : "") +
                           T("威力", "Power") + " " + ability.Power.ToString(CultureInfo.InvariantCulture);
            workbook.AddSubDetail(
                GetNpcWorkbookSpritePng(ResolveNpcWorkbookRelatedAbilityIcon(ability), imageCache, null),
                T("关联能力", "Related ability"),
                related,
                alternate);
        }
        var costs = GetNpcWorkbookAbilityCosts(entry.Id, ability);
        for (var i = 0; i < costs.Count; i++)
        {
            workbook.AddSubDetail(
                GetNpcWorkbookSpritePng(costs[i].Icon, imageCache, null),
                costs[i].Label,
                costs[i].Value,
                alternate);
        }
    }

    private List<(string Label, string Value, Sprite? Icon)> GetNpcWorkbookAbilityCosts(
        int abilityId,
        NpcAbilityTooltipInfo ability)
    {
        var result = new List<(string Label, string Value, Sprite? Icon)>();
        var mp = -1;
        var sp = -1;
        if (_abilityCostOverrides.TryGetValue(abilityId, out var custom))
        {
            mp = custom.Mp;
            sp = custom.Sp;
        }
        if (mp > 0)
            result.Add((T("玛那消耗", "Mana cost"), mp.ToString(CultureInfo.InvariantCulture), ResolveLGuiNpcAbilityCostIcon(Act.CostType.MP)));
        else if (mp < 0 && ability.CostType == Act.CostType.MP && ability.Cost > 0)
            result.Add((T("玛那消耗", "Mana cost"), FormatLGuiNpcAbilityCost(ability), ResolveLGuiNpcAbilityCostIcon(Act.CostType.MP)));
        if (sp > 0)
            result.Add((T("活力消耗", "Vigor cost"), sp.ToString(CultureInfo.InvariantCulture), ResolveLGuiNpcAbilityCostIcon(Act.CostType.SP)));
        else if (sp < 0 && ability.CostType == Act.CostType.SP && ability.Cost > 0)
            result.Add((T("活力消耗", "Vigor cost"), FormatLGuiNpcAbilityCost(ability), ResolveLGuiNpcAbilityCostIcon(Act.CostType.SP)));
        return result;
    }

    private void AppendNpcWorkbookSpawnInformation(NpcCompendiumWorkbookWriter workbook, NpcAnalysis analysis)
    {
        workbook.AddSection(T("刷新群落与地牢等级", "Spawn biomes and dungeon levels"));
        if (analysis.Locations.Count == 0)
        {
            workbook.AddFullWidthText(T("没有找到常规随机生成群落", "No normal random-spawn biome was found"));
        }
        else
        {
            workbook.AddFullWidthText(
                T("最高概率地点", "Highest-probability location") + " : " + analysis.HighestLocation +
                " · " + _modules.NpcInfo.FormatProbability(analysis.PeakProbability) + "  |  " +
                T("最高概率危险度", "Peak danger level") + " : " +
                analysis.PeakDangerLevel.ToString(CultureInfo.InvariantCulture) + "  |  " +
                T("常规生成危险度区间", "Normal spawn danger range") + " : " +
                (analysis.MinimumDangerLevel > 0
                    ? analysis.MinimumDangerLevel.ToString(CultureInfo.InvariantCulture) + "+"
                    : "-"));
            for (var i = 0; i < analysis.Locations.Count; i++)
            {
                var location = analysis.Locations[i];
                workbook.AddDetail(
                    null,
                    (i == 0 ? "★ " : "") + location.Name,
                    _modules.NpcInfo.FormatProbability(location.PeakProbability),
                    T("峰值危险度", "Peak danger") + " : " +
                    location.PeakDangerLevel.ToString(CultureInfo.InvariantCulture) + "  |  " +
                    T("最低危险度", "Minimum danger") + " : " +
                    location.MinimumDangerLevel.ToString(CultureInfo.InvariantCulture) + "+\n" +
                    T("生成列表", "Spawn lists") + " : " + location.Route,
                    i % 2 != 0);
            }
        }
        workbook.AddSection(T("所属生成列表", "Containing spawn lists"));
        workbook.AddFullWidthText(
            analysis.SpawnLists.Count == 0
                ? T("无（可能为剧情、地图预放置或脚本直接生成）", "None (possibly quest, map-preplaced, or script-spawned)")
                : string.Join(" / ", analysis.SpawnLists));
    }

    private void AppendNpcWorkbookLoot(
        NpcCompendiumWorkbookWriter workbook,
        IReadOnlyList<NpcLootEntry> loot,
        IDictionary<string, byte[]> imageCache)
    {
        workbook.AddSection(T("掉落物", "Drops"));
        if (loot.Count == 0)
        {
            workbook.AddFullWidthText(T("未找到可展示的掉落规则", "No displayable drop rule was found"));
            return;
        }
        for (var i = 0; i < loot.Count; i++)
        {
            var entry = loot[i];
            workbook.AddDetail(
                GetNpcWorkbookSpritePng(ResolveNpcWorkbookLootIcon(entry.Item), imageCache, null),
                entry.Item,
                entry.Probability + "  |  " + entry.Quantity,
                T("来源", "Source") + " : " + entry.Source + "\n" +
                T("条件", "Conditions") + " : " + entry.Conditions,
                i % 2 != 0);
        }
    }

    private static Sprite? ResolveNpcWorkbookBodyIcon(NpcBodySlotEntry entry)
    {
        try
        {
            return SpriteSheet.Get("Media/Graphics/Icon/Element/", "eq_" + entry.Element.alias) ??
                   entry.Element.GetSprite();
        }
        catch
        {
            return null;
        }
    }

    private static Sprite? ResolveNpcWorkbookEquipmentIcon(NpcEquipmentEntry entry)
    {
        try
        {
            return entry.Item?.GetSprite(0);
        }
        catch
        {
            return null;
        }
    }

    private static Sprite? ResolveNpcWorkbookTemplateIcon(NpcTemplateValue entry, bool useAbilityIcon)
    {
        if (!useAbilityIcon)
        {
            try
            {
                var element = Element.Create(entry.Id, entry.Value);
                var sprite = element?.GetIcon("");
                if (sprite == null && GameAccess.Sources.Elements?.map != null &&
                    GameAccess.Sources.Elements.map.TryGetValue(entry.Id, out var source))
                    sprite = source.GetSprite();
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        GameObject? imageObject = null;
        try
        {
            imageObject = new GameObject("NpcWorkbookAbilityIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var image = imageObject.GetComponent<Image>();
            ACT.Create(entry.Id)?.SetImage(image);
            return image.sprite;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (imageObject != null)
                Destroy(imageObject);
        }
    }

    private static Sprite? ResolveNpcWorkbookRelatedAbilityIcon(NpcAbilityTooltipInfo ability)
    {
        try
        {
            if (ability.RelatedAbilitySource == null)
                return null;
            return Element.Create(ability.RelatedAbilitySource.id, 1)?.GetIcon("");
        }
        catch
        {
            return null;
        }
    }

    private static Sprite? ResolveNpcWorkbookLootIcon(string itemText)
    {
        try
        {
            var match = Regex.Match(itemText ?? "", @"\[([^\[\]]+)\]\s*$");
            if (!match.Success)
                return null;
            var thing = GameAccess.Spawn.CreateThing(match.Groups[1].Value, -1, 1);
            return thing?.GetSprite(0);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? GetNpcWorkbookSpritePng(
        Sprite? sprite,
        IDictionary<string, byte[]> cache,
        Color? tint)
    {
        if (sprite == null || sprite.texture == null)
            return null;
        Rect rect;
        try
        {
            rect = sprite.textureRect;
        }
        catch
        {
            rect = sprite.rect;
        }
        var key = sprite.texture.GetInstanceID().ToString(CultureInfo.InvariantCulture) + ":" +
                  rect.x.ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                  rect.y.ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                  rect.width.ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                  rect.height.ToString("0.###", CultureInfo.InvariantCulture);
        if (tint.HasValue)
        {
            var color = tint.Value;
            key += ":" + color.r.ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                   color.g.ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                   color.b.ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                   color.a.ToString("0.###", CultureInfo.InvariantCulture);
        }
        if (cache.TryGetValue(key, out var existing))
            return existing;
        var png = EncodeNpcWorkbookSprite(sprite, rect, tint);
        if (png != null && png.Length > 0)
            cache[key] = png;
        return png;
    }

    private static byte[]? EncodeNpcWorkbookSprite(Sprite sprite, Rect rect, Color? tint)
    {
        var width = Math.Max(1, Mathf.RoundToInt(rect.width));
        var height = Math.Max(1, Mathf.RoundToInt(rect.height));
        var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D? output = null;
        try
        {
            var source = sprite.texture;
            var scale = new Vector2(rect.width / source.width, rect.height / source.height);
            var offset = new Vector2(rect.x / source.width, rect.y / source.height);
            Graphics.Blit(source, temporary, scale, offset);
            RenderTexture.active = temporary;
            output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            output.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            output.Apply(false, false);
            if (tint.HasValue)
            {
                var color = tint.Value;
                var pixels = output.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i].r = (byte)Mathf.Clamp(Mathf.RoundToInt(pixels[i].r * color.r), 0, 255);
                    pixels[i].g = (byte)Mathf.Clamp(Mathf.RoundToInt(pixels[i].g * color.g), 0, 255);
                    pixels[i].b = (byte)Mathf.Clamp(Mathf.RoundToInt(pixels[i].b * color.b), 0, 255);
                    pixels[i].a = (byte)Mathf.Clamp(Mathf.RoundToInt(pixels[i].a * color.a), 0, 255);
                }
                output.SetPixels32(pixels);
                output.Apply(false, false);
            }
            return output.EncodeToPNG();
        }
        catch
        {
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            if (output != null)
                Destroy(output);
        }
    }
}
