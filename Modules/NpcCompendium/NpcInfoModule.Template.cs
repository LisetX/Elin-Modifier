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

    private sealed class TemplateRandomDelta
    {
        internal long Minimum;
        internal long Maximum;
    }

    private sealed class MainElementOption
    {
        internal int ScaledLevel;
        internal readonly Dictionary<int, int> Modifications = new Dictionary<int, int>();
    }

    private static NpcTemplateInfo BuildTemplateInfo(NpcRecord npc, int additionalLevel)
    {
        var result = new NpcTemplateInfo();
        try
        {
            var requestedLevel = Math.Max(1L, (long)npc.BaseLevel + Math.Max(0, additionalLevel));
            var templateLevel = (int)Math.Min(int.MaxValue, requestedLevel);
            var template = GameAccess.Spawn.CreateCharacter(npc.Id, templateLevel);
            if (template == null || template.elements == null)
                return result;

            PopulateNpcFixedEquipment(template, npc, result);

            var randomDeltas = new Dictionary<int, TemplateRandomDelta>();
            var adventurerType = (int)template.trait.AdvType;
            var hasRandomRace = adventurerType == 1;
            var hasRandomAdventurerJob = adventurerType == 1 || adventurerType == 2;
            var hasRandomTemplateJob = !hasRandomAdventurerJob &&
                                       string.Equals(npc.Row.job, "*r", StringComparison.OrdinalIgnoreCase);
            var hasRandomJob = hasRandomAdventurerJob || hasRandomTemplateJob;
            var randomRaceRows = hasRandomRace ? GetRandomAdventurerRaces() : new List<SourceRace.Row>();
            var randomJobRows = hasRandomAdventurerJob
                ? GetRandomAdventurerJobs()
                : hasRandomTemplateJob
                    ? GetRandomTemplateJobs()
                    : new List<SourceJob.Row>();
            if (template.LV != templateLevel)
                template.SetLv(templateLevel);
            var mainElementOptions = BuildMainElementOptions(npc, templateLevel);
            var randomLevels = new List<int> { templateLevel };
            if (mainElementOptions.Count > 1)
            {
                template.SetMainElement(0, 0, false);
                if (template.LV != templateLevel)
                    template.SetLv(templateLevel);
                randomLevels.Clear();
                for (var i = 0; i < mainElementOptions.Count; i++)
                {
                    var randomLevel = (long)mainElementOptions[i].ScaledLevel + templateLevel - npc.BaseLevel;
                    randomLevels.Add(ClampTemplateLevel(randomLevel));
                }
            }

            NormalizeTemplateElementMap(
                template,
                template.job?.elementMap,
                npc.BaseLevel,
                templateLevel,
                randomLevels,
                randomDeltas,
                !hasRandomJob);
            NormalizeTemplateElementMap(
                template,
                template.race?.elementMap,
                npc.BaseLevel,
                templateLevel,
                randomLevels,
                randomDeltas,
                !hasRandomRace);
            if (hasRandomJob)
                AddRandomElementMapChoiceRanges(
                    randomJobRows.Select(row => row.elementMap),
                    npc.BaseLevel,
                    randomLevels,
                    randomDeltas);
            if (hasRandomRace)
                AddRandomElementMapChoiceRanges(
                    randomRaceRows.Select(row => row.elementMap),
                    npc.BaseLevel,
                    randomLevels,
                    randomDeltas);
            if (mainElementOptions.Count > 1)
                AddMainElementRandomRanges(template, mainElementOptions, randomDeltas);
            if (hasRandomRace)
                NormalizeRandomRaceSpecialFeats(template, npc, randomRaceRows, randomDeltas);
            else
                NormalizeSpecialRandomFeats(template, npc, randomDeltas);

            result.Loaded = true;

            result.Life = template.elements.ValueWithoutLink(SKILL.life);
            result.Mana = template.elements.ValueWithoutLink(SKILL.mana);
            result.Vigor = template.elements.ValueWithoutLink(SKILL.vigor);
            result.Speed = template.elements.ValueWithoutLink(SKILL.SPD);
            result.DV = template.elements.ValueWithoutLink(SKILL.DV);
            result.PV = template.elements.ValueWithoutLink(SKILL.PV);
            result.WeightLimit = template.WeightLimit;

            foreach (var pair in randomDeltas)
            {
                var fixedValue = template.elements.ValueWithoutLink(pair.Key);
                var minimum = ClampTemplateValue((long)fixedValue + pair.Value.Minimum);
                var maximum = ClampTemplateValue((long)fixedValue + pair.Value.Maximum);
                if (minimum > maximum)
                {
                    var swap = minimum;
                    minimum = maximum;
                    maximum = swap;
                }
                if (minimum != maximum)
                {
                    result.RandomRanges[pair.Key] = new NpcTemplateRandomRange
                    {
                        Minimum = minimum,
                        Maximum = maximum
                    };
                }
            }
            PopulateWeightLimitRange(result, template);

            foreach (var element in template.elements.dict.Values)
            {
                if (element == null)
                    continue;
                SourceElement.Row source;
                try { source = element.source; }
                catch { continue; }
                if (source == null)
                    continue;

                var value = template.elements.ValueWithoutLink(element.id);
                if (IsNpcTemplateEnchantment(element, source, value, result.RandomRanges))
                    AddTemplateValue(result, result.Enchantments, source, value);
                if (IsNpcTemplateSpell(element, source))
                {
                    if (value != 0 || element.vSource > 0 || result.RandomRanges.ContainsKey(element.id))
                        AddTemplateValue(result, result.Spells, source, value);
                    continue;
                }
                if (source.isPrimaryAttribute)
                {
                    AddTemplateValue(result, result.MainAbilities, source, value);
                    continue;
                }
                if (string.Equals(source.category, "skill", StringComparison.OrdinalIgnoreCase))
                {
                    if (value != 0 || element.vSource > 0 || result.RandomRanges.ContainsKey(element.id))
                        AddTemplateValue(result, result.Skills, source, value);
                    continue;
                }
                if (string.Equals(source.category, "feat", StringComparison.OrdinalIgnoreCase) &&
                    (value > 0 || result.RandomRanges.ContainsKey(element.id)))
                    AddTemplateValue(result, result.Feats, source, value);
            }

            foreach (var pair in result.RandomRanges)
            {
                if (template.elements.dict.ContainsKey(pair.Key) ||
                    GameAccess.Sources.Elements?.map == null ||
                    !GameAccess.Sources.Elements.map.TryGetValue(pair.Key, out var source))
                    continue;
                if (source.isPrimaryAttribute)
                    AddTemplateValue(result, result.MainAbilities, source, 0);
                else if (IsNpcTemplateSpellSource(source))
                    AddTemplateValue(result, result.Spells, source, 0);
                else if (string.Equals(source.category, "skill", StringComparison.OrdinalIgnoreCase))
                    AddTemplateValue(result, result.Skills, source, 0);
                else if (string.Equals(source.category, "feat", StringComparison.OrdinalIgnoreCase))
                    AddTemplateValue(result, result.Feats, source, 0);
                if (IsNpcTemplateEnchantmentSource(source))
                    AddTemplateValue(result, result.Enchantments, source, 0);
            }

            var resistanceRows = GameAccess.Sources.Elements?.rows;
            if (resistanceRows != null)
            {
                for (var i = 0; i < resistanceRows.Count; i++)
                {
                    var row = resistanceRows[i];
                    if (row == null || !string.Equals(row.category, "resist", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var value = template.elements.ValueWithoutLink(row.id);
                    if ((ContainsTag(row.tag, "hidden") || ContainsTag(row.tag, "high")) &&
                        value == 0 && !result.RandomRanges.ContainsKey(row.id))
                        continue;
                    AddTemplateValue(result, result.Resistances, row, value, true);
                }
            }

            PopulateNpcTemplateCombatAbilities(template, result);
            PopulateNpcStoryAbilities(npc, template, result);
            RemoveNpcTemplateSkillAbilityDuplicates(result);
            RemoveNpcTemplateEnchantmentDuplicates(result);
            SortTemplateValues(result.MainAbilities);
            SortTemplateValues(result.Skills);
            SortTemplateValues(result.Feats);
            SortTemplateValues(result.Spells);
            SortTemplateValues(result.Resistances);
            SortTemplateValues(result.Enchantments);
            PopulateTemplateTooltips(template, result.MainAbilities, false);
            PopulateTemplateTooltips(template, result.Skills, false);
            PopulateTemplateTooltips(template, result.Feats, true);
            PopulateTemplateTooltips(template, result.Spells, false);
            PopulateNpcTemplateAbilityTooltips(template, result.Spells);
            PopulateTemplateTooltips(template, result.Enchantments, false);
        }
        catch (Exception ex)
        {
            result.Error = ex.GetType().Name;
        }
        return result;
    }

    private static void PopulateNpcTemplateCombatAbilities(Chara template, NpcTemplateInfo result)
    {
        try
        {
            var items = template.ability?.list?.items;
            if (items == null)
                return;

            for (var i = 0; i < items.Count; i++)
            {
                var act = items[i]?.act;
                if (act == null)
                    continue;
                SourceElement.Row source;
                try { source = act.source; }
                catch { continue; }
                if (source == null)
                    continue;

                var value = template.elements.ValueWithoutLink(source.id);
                if (value <= 0)
                    value = Math.Max(0, source.LV);
                AddTemplateValue(result, result.Spells, source, value);
            }
        }
        catch
        {
        }
    }

    private static void RemoveNpcTemplateSkillAbilityDuplicates(NpcTemplateInfo result)
    {
        if (result.Spells.Count == 0 || result.Skills.Count == 0)
            return;

        var abilityIds = new HashSet<int>();
        for (var i = 0; i < result.Spells.Count; i++)
            abilityIds.Add(result.Spells[i].Id);
        result.Skills.RemoveAll(entry => abilityIds.Contains(entry.Id));
    }

    private static void RemoveNpcTemplateEnchantmentDuplicates(NpcTemplateInfo result)
    {
        if (result.Enchantments.Count == 0)
            return;

        var displayedIds = new HashSet<int>();
        for (var i = 0; i < result.MainAbilities.Count; i++)
            displayedIds.Add(result.MainAbilities[i].Id);
        for (var i = 0; i < result.Skills.Count; i++)
            displayedIds.Add(result.Skills[i].Id);
        for (var i = 0; i < result.Spells.Count; i++)
            displayedIds.Add(result.Spells[i].Id);
        result.Enchantments.RemoveAll(entry => displayedIds.Contains(entry.Id));
    }

    private static bool IsNpcTemplateSpell(Element element, SourceElement.Row source)
    {
        if (element is Spell)
            return true;
        return IsNpcTemplateSpellSource(source);
    }

    private static bool IsNpcTemplateSpellSource(SourceElement.Row source)
    {
        return source.isSpell ||
               string.Equals(source.categorySub, "spell", StringComparison.OrdinalIgnoreCase) ||
               TextContains(source.type, "spell") ||
               TextContains(source.group, "spell") ||
               TextContains(source.category, "spell") ||
               TextContains(source.categorySub, "spell");
    }

    private static bool TextContains(string text, string value)
    {
        return !string.IsNullOrEmpty(text) &&
               text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsNpcTemplateEnchantment(
        Element element,
        SourceElement.Row source,
        int fixedValue,
        IReadOnlyDictionary<int, NpcTemplateRandomRange> randomRanges)
    {
        if (!IsNpcTemplateEnchantmentSource(source))
            return false;

        try
        {
            if (element.IsFlag && element.Value == 0)
                return false;
        }
        catch
        {
        }

        return fixedValue != 0 || randomRanges.ContainsKey(element.id);
    }

    private static bool IsNpcTemplateEnchantmentSource(SourceElement.Row source)
    {
        if (source == null || ContainsTag(source.tag, "godAbility") ||
            string.Equals(source.categorySub, "god", StringComparison.OrdinalIgnoreCase))
            return false;
        if (source.isPrimaryAttribute ||
            IsNpcTemplateSpellSource(source) ||
            string.Equals(source.category, "skill", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source.category, "feat", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source.category, "resist", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            if (source.IsWeaponEnc)
                return true;
        }
        catch
        {
        }
        return source.id == 55 || source.id == 56 || source.id == 57 ||
               source.id == 68 || source.id == 93 ||
               string.Equals(source.category, "enchant", StringComparison.OrdinalIgnoreCase);
    }

    private static List<MainElementOption> BuildMainElementOptions(NpcRecord npc, int templateLevel)
    {
        var result = new List<MainElementOption>();
        var entries = npc.Row.mainElement;
        var sourceElements = GameAccess.Sources.Elements;
        if (entries == null || sourceElements?.alias == null || sourceElements.map == null)
            return result;

        var generationLevel = Math.Min(templateLevel, 100);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            var parts = entry.Split('/');
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                continue;
            var alias = parts[0].StartsWith("ele", StringComparison.OrdinalIgnoreCase)
                ? parts[0]
                : "ele" + parts[0];
            if (!sourceElements.alias.TryGetValue(alias, out var source))
                continue;
            var scaledLevel = ClampTemplateLevel((long)npc.BaseLevel * source.eleP / 100L);
            if (result.Count > 0 && scaledLevel >= generationLevel)
                continue;

            var option = new MainElementOption { ScaledLevel = scaledLevel };
            var value = parts.Length > 1 ? ParseInt(parts[1]) : 0;
            AddMainElementModification(option.Modifications, source.id, value == 0 ? 10 : value);
            if (!string.IsNullOrWhiteSpace(source.aliasRef) &&
                sourceElements.alias.TryGetValue(source.aliasRef, out var reference))
                AddMainElementModification(option.Modifications, reference.id, 20);
            var pairedElement = GetMainElementPairedElement(source.id);
            if (pairedElement != 0)
                AddMainElementModification(option.Modifications, pairedElement, 10);
            result.Add(option);
        }
        return result;
    }

    private static void AddMainElementModification(IDictionary<int, int> values, int id, int value)
    {
        values.TryGetValue(id, out var current);
        values[id] = current + value;
    }

    private static int GetMainElementPairedElement(int id)
    {
        switch (id)
        {
            case 910: return 951;
            case 911: return 950;
            case 912: return 953;
            case 913: return 952;
            case 916: return 960;
            case 919: return 956;
            case 921: return 971;
            case 922: return 965;
            case 925: return 962;
            case 926: return 961;
            default: return 0;
        }
    }

    private static void AddMainElementRandomRanges(
        Chara template,
        IReadOnlyList<MainElementOption> options,
        IDictionary<int, TemplateRandomDelta> randomDeltas)
    {
        var ids = new HashSet<int>();
        for (var i = 0; i < options.Count; i++)
            ids.UnionWith(options[i].Modifications.Keys);
        foreach (var id in ids)
        {
            var minimum = int.MaxValue;
            var maximum = int.MinValue;
            for (var i = 0; i < options.Count; i++)
            {
                options[i].Modifications.TryGetValue(id, out var value);
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            if (minimum == maximum)
                template.elements.ModBase(id, minimum);
            else
                AddTemplateRandomDelta(randomDeltas, id, minimum, maximum);
        }
    }

    private static void NormalizeTemplateElementMap(
        Chara template,
        Dictionary<int, int>? elementMap,
        int baseLevel,
        int templateLevel,
        IReadOnlyList<int> randomLevels,
        IDictionary<int, TemplateRandomDelta> randomDeltas,
        bool includeFixedContribution)
    {
        if (elementMap == null || elementMap.Count == 0)
            return;
        var baseValues = BuildTemplateMapValues(template.uid, elementMap, baseLevel);
        var targetValues = templateLevel == baseLevel
            ? baseValues
            : BuildTemplateMapValues(template.uid, elementMap, templateLevel);

        foreach (var pair in elementMap)
        {
            if (pair.Value == 0 || GameAccess.Sources.Elements?.map == null ||
                !GameAccess.Sources.Elements.map.TryGetValue(pair.Key, out var source))
                continue;
            var scalesWithLevel = string.Equals(source.category, "attribute", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(source.category, "skill", StringComparison.OrdinalIgnoreCase);
            var actualValue = (scalesWithLevel ? targetValues : baseValues).ValueWithoutLink(pair.Key);
            var fixedLevel = scalesWithLevel ? templateLevel : baseLevel;
            var fixedContribution = includeFixedContribution
                ? NpcTemplateValueMath.GetCharaSourceBounds(pair.Value, fixedLevel, source.lvFactor).FixedValue
                : 0L;
            template.elements.ModBase(pair.Key, ClampTemplateValue(fixedContribution - actualValue));

            if (!includeFixedContribution)
                continue;

            var minimum = long.MaxValue;
            var maximum = long.MinValue;
            if (scalesWithLevel)
            {
                for (var i = 0; i < randomLevels.Count; i++)
                    IncludeSourceValueBounds(pair.Value, randomLevels[i], source.lvFactor, ref minimum, ref maximum);
            }
            else
            {
                IncludeSourceValueBounds(pair.Value, baseLevel, source.lvFactor, ref minimum, ref maximum);
            }
            if (minimum == long.MaxValue)
                continue;
            AddTemplateRandomDelta(
                randomDeltas,
                pair.Key,
                minimum - fixedContribution,
                maximum - fixedContribution);
        }
    }

    private static List<SourceRace.Row> GetRandomAdventurerRaces()
    {
        var result = new List<SourceRace.Row>();
        var rows = GameAccess.Sources.Races?.rows;
        if (rows == null)
            return result;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row != null && row.playable <= 1 &&
                !string.Equals(row.id, "fairy", StringComparison.OrdinalIgnoreCase))
                result.Add(row);
        }
        return result;
    }

    private static List<SourceJob.Row> GetRandomAdventurerJobs()
    {
        return GetRandomJobs(4, true);
    }

    private static List<SourceJob.Row> GetRandomTemplateJobs()
    {
        return GetRandomJobs(4, false);
    }

    private static List<SourceJob.Row> GetRandomJobs(int playableLimit, bool inclusive)
    {
        var result = new List<SourceJob.Row>();
        var rows = GameAccess.Sources.Jobs?.rows;
        if (rows == null)
            return result;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row != null && (inclusive ? row.playable <= playableLimit : row.playable < playableLimit))
                result.Add(row);
        }
        return result;
    }

    private static void AddRandomElementMapChoiceRanges(
        IEnumerable<Dictionary<int, int>> maps,
        int baseLevel,
        IReadOnlyList<int> randomLevels,
        IDictionary<int, TemplateRandomDelta> randomDeltas)
    {
        var mapList = maps.Where(map => map != null).ToList();
        if (mapList.Count == 0)
            return;
        var ids = new HashSet<int>();
        for (var i = 0; i < mapList.Count; i++)
            ids.UnionWith(mapList[i].Keys);

        foreach (var id in ids)
        {
            if (GameAccess.Sources.Elements?.map == null ||
                !GameAccess.Sources.Elements.map.TryGetValue(id, out var source))
                continue;
            var scalesWithLevel = string.Equals(source.category, "attribute", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(source.category, "skill", StringComparison.OrdinalIgnoreCase);
            var minimum = long.MaxValue;
            var maximum = long.MinValue;
            for (var mapIndex = 0; mapIndex < mapList.Count; mapIndex++)
            {
                if (!mapList[mapIndex].TryGetValue(id, out var sourceValue) || sourceValue == 0)
                {
                    minimum = Math.Min(minimum, 0L);
                    maximum = Math.Max(maximum, 0L);
                    continue;
                }
                if (scalesWithLevel)
                {
                    for (var levelIndex = 0; levelIndex < randomLevels.Count; levelIndex++)
                        IncludeSourceValueBounds(
                            sourceValue,
                            randomLevels[levelIndex],
                            source.lvFactor,
                            ref minimum,
                            ref maximum);
                }
                else
                {
                    IncludeSourceValueBounds(sourceValue, baseLevel, source.lvFactor, ref minimum, ref maximum);
                }
            }
            if (minimum != long.MaxValue)
                AddTemplateRandomDelta(randomDeltas, id, minimum, maximum);
        }
    }

    private static ElementContainer BuildTemplateMapValues(
        int uid,
        Dictionary<int, int> elementMap,
        int level)
    {
        var values = new ElementContainer();
        values.ApplyElementMap(uid, SourceValueType.Chara, elementMap, level, false, false);
        return values;
    }

    private static void IncludeSourceValueBounds(
        int sourceValue,
        int level,
        int levelFactor,
        ref long minimum,
        ref long maximum)
    {
        var bounds = NpcTemplateValueMath.GetCharaSourceBounds(sourceValue, level, levelFactor);
        minimum = Math.Min(minimum, bounds.Minimum);
        maximum = Math.Max(maximum, bounds.Maximum);
    }

    private static void NormalizeSpecialRandomFeats(
        Chara template,
        NpcRecord npc,
        IDictionary<int, TemplateRandomDelta> randomDeltas)
    {
        var isGeneric = string.Equals(npc.Id, "chara", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(npc.Id, "player", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(template.race?.id, "bike", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(npc.Id, "bike_cub", StringComparison.OrdinalIgnoreCase))
        {
            if (isGeneric)
                SetFixedSpecialFeat(template, 1423, 10, randomDeltas);
            else
                SetSpecialRandomFeat(template, npc, 1423, 1, 10, randomDeltas);
        }
        else if (string.Equals(npc.Id, "moa", StringComparison.OrdinalIgnoreCase) && !isGeneric)
            SetSpecialRandomFeat(template, npc, 1423, 1, 7, randomDeltas);
        else if (string.Equals(template.race?.id, "horse", StringComparison.OrdinalIgnoreCase))
        {
            PreserveNormalRangeWithSpecialOutcome(
                template,
                npc,
                1423,
                isGeneric ? 5 : 1,
                5,
                randomDeltas);
        }

        if (string.Equals(npc.Id, "putty_mech_b", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(npc.Id, "putty_mech", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(npc.Id, "robot", StringComparison.OrdinalIgnoreCase))
            SetSpecialRandomFeat(template, npc, 1248, 1, 5, randomDeltas);
    }

    private static void SetFixedSpecialFeat(
        Chara template,
        int id,
        int value,
        IDictionary<int, TemplateRandomDelta> randomDeltas)
    {
        template.SetFeat(id, value, false);
        randomDeltas.Remove(id);
    }

    private static void PreserveNormalRangeWithSpecialOutcome(
        Chara template,
        NpcRecord npc,
        int id,
        int specialMinimum,
        int specialMaximum,
        IDictionary<int, TemplateRandomDelta> randomDeltas)
    {
        var fixedValue = GetFixedTemplateMapValue(template, npc, id);
        var minimum = (long)fixedValue;
        var maximum = (long)fixedValue;
        if (randomDeltas.TryGetValue(id, out var existing))
        {
            minimum = Math.Min(minimum, (long)fixedValue + existing.Minimum);
            maximum = Math.Max(maximum, (long)fixedValue + existing.Maximum);
        }
        minimum = Math.Min(minimum, specialMinimum);
        maximum = Math.Max(maximum, specialMaximum);

        template.SetFeat(id, fixedValue, false);
        randomDeltas.Remove(id);
        if (minimum != maximum)
            AddTemplateRandomDelta(randomDeltas, id, minimum - fixedValue, maximum - fixedValue);
    }

    private static void NormalizeRandomRaceSpecialFeats(
        Chara template,
        NpcRecord npc,
        IReadOnlyList<SourceRace.Row> randomRaces,
        IDictionary<int, TemplateRandomDelta> randomDeltas)
    {
        const int featId = 1423;
        var fixedValue = GetFixedNpcMapValue(npc, featId);
        var minimum = (long)fixedValue;
        var maximum = (long)fixedValue;
        if (randomDeltas.TryGetValue(featId, out var existing))
        {
            minimum = Math.Min(minimum, (long)fixedValue + existing.Minimum);
            maximum = Math.Max(maximum, (long)fixedValue + existing.Maximum);
        }

        for (var i = 0; i < randomRaces.Count; i++)
        {
            var raceId = randomRaces[i].id;
            if (string.Equals(raceId, "bike", StringComparison.OrdinalIgnoreCase))
            {
                minimum = Math.Min(minimum, 1L);
                maximum = Math.Max(maximum, 10L);
            }
            else if (string.Equals(raceId, "horse", StringComparison.OrdinalIgnoreCase))
            {
                minimum = Math.Min(minimum, 1L);
                maximum = Math.Max(maximum, 5L);
            }
        }

        template.SetFeat(featId, fixedValue, false);
        randomDeltas.Remove(featId);
        if (minimum != maximum)
            AddTemplateRandomDelta(
                randomDeltas,
                featId,
                minimum - fixedValue,
                maximum - fixedValue);
    }

    private static void SetSpecialRandomFeat(
        Chara template,
        NpcRecord npc,
        int id,
        int minimum,
        int maximum,
        IDictionary<int, TemplateRandomDelta> randomDeltas)
    {
        var fixedValue = GetFixedTemplateMapValue(template, npc, id);
        template.SetFeat(id, fixedValue, false);
        randomDeltas.Remove(id);
        AddTemplateRandomDelta(randomDeltas, id, minimum - fixedValue, maximum - fixedValue);
    }

    private static int GetFixedTemplateMapValue(Chara template, NpcRecord npc, int id)
    {
        long value = GetFixedNpcMapValue(npc, id);
        if (template.race?.elementMap != null && template.race.elementMap.TryGetValue(id, out var raceValue))
            value += GetFixedCharaMapContribution(id, raceValue, npc.BaseLevel);
        if (template.job?.elementMap != null && template.job.elementMap.TryGetValue(id, out var jobValue))
            value += GetFixedCharaMapContribution(id, jobValue, npc.BaseLevel);
        return ClampTemplateValue(value);
    }

    private static int GetFixedNpcMapValue(NpcRecord npc, int id)
    {
        if (npc.Row.elementMap != null && npc.Row.elementMap.TryGetValue(id, out var value))
            return value;
        return 0;
    }

    private static long GetFixedCharaMapContribution(int id, int value, int level)
    {
        if (GameAccess.Sources.Elements?.map == null ||
            !GameAccess.Sources.Elements.map.TryGetValue(id, out var source))
            return value;
        return NpcTemplateValueMath.GetCharaSourceBounds(value, level, source.lvFactor).FixedValue;
    }

    private static void AddTemplateRandomDelta(
        IDictionary<int, TemplateRandomDelta> randomDeltas,
        int id,
        long minimum,
        long maximum)
    {
        if (!randomDeltas.TryGetValue(id, out var range))
        {
            range = new TemplateRandomDelta();
            randomDeltas[id] = range;
        }
        range.Minimum += minimum;
        range.Maximum += maximum;
    }

    private static void PopulateWeightLimitRange(NpcTemplateInfo result, Chara template)
    {
        var minimumStrength = GetTemplateDerivedRangeValue(result, template, 70, template.STR, false);
        var maximumStrength = GetTemplateDerivedRangeValue(result, template, 70, template.STR, true);
        var minimumEndurance = GetTemplateDerivedRangeValue(result, template, 71, template.END, false);
        var maximumEndurance = GetTemplateDerivedRangeValue(result, template, 71, template.END, true);
        var fixedLifting = template.Evalue(207);
        var minimumLifting = GetTemplateDerivedRangeValue(result, template, 207, fixedLifting, false);
        var maximumLifting = GetTemplateDerivedRangeValue(result, template, 207, fixedLifting, true);
        var fixedGene = template.HasElement(1411, false);
        var minimumGene = result.RandomRanges.TryGetValue(1411, out var geneRange)
            ? geneRange.Minimum > 0
            : fixedGene;
        var maximumGene = result.RandomRanges.TryGetValue(1411, out geneRange)
            ? geneRange.Maximum > 0
            : fixedGene;
        var minimum = CalculateTemplateWeightLimit(minimumStrength, minimumEndurance, minimumLifting, minimumGene);
        var maximum = CalculateTemplateWeightLimit(maximumStrength, maximumEndurance, maximumLifting, maximumGene);
        if (minimum == maximum)
            return;
        result.WeightLimitHasRandomRange = true;
        result.WeightLimitRandomMinimum = Math.Min(minimum, maximum);
        result.WeightLimitRandomMaximum = Math.Max(minimum, maximum);
    }

    private static int GetTemplateDerivedRangeValue(
        NpcTemplateInfo result,
        Chara template,
        int id,
        int fixedDerivedValue,
        bool maximum)
    {
        if (!result.RandomRanges.TryGetValue(id, out var range))
            return fixedDerivedValue;
        var fixedElementValue = template.elements.ValueWithoutLink(id);
        return ClampTemplateValue((long)fixedDerivedValue +
                                  (maximum ? range.Maximum : range.Minimum) - fixedElementValue);
    }

    private static int CalculateTemplateWeightLimit(int strength, int endurance, int lifting, bool multiplied)
    {
        long value = (long)strength * 500L + (long)endurance * 250L + (long)lifting * 2000L;
        if (multiplied)
            value *= 5L;
        value += 45000L;
        return ClampTemplateValue(Math.Max(1000L, Math.Min(1073741824L, value)));
    }

    private static int ClampTemplateLevel(long value) =>
        (int)Math.Max(1L, Math.Min(int.MaxValue, value));

    private static int ClampTemplateValue(long value) =>
        (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));

    private static void AddTemplateValue(
        NpcTemplateInfo info,
        List<NpcTemplateValue> target,
        SourceElement.Row source,
        int value,
        bool isResistance = false)
    {
        for (var i = 0; i < target.Count; i++)
            if (target[i].Id == source.id)
                return;
        var entry = new NpcTemplateValue
        {
            Id = source.id,
            Sort = source.sort,
            Name = SafeElementName(source, value),
            Value = value,
            IsResistance = isResistance
        };
        if (info.RandomRanges.TryGetValue(source.id, out var range))
        {
            entry.HasRandomRange = true;
            entry.RandomMinimum = range.Minimum;
            entry.RandomMaximum = range.Maximum;
        }
        target.Add(entry);
    }

    private static string SafeElementName(SourceElement.Row row, int value)
    {
        var name = "";
        try
        {
            name = row.GetName();
        }
        catch
        {
        }
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            name = !string.IsNullOrWhiteSpace(row.name_L)
                ? row.name_L
                : !string.IsNullOrWhiteSpace(row.alias)
                    ? row.alias
                    : row.id.ToString(CultureInfo.InvariantCulture);

        if (!string.Equals(row.category, "feat", StringComparison.OrdinalIgnoreCase))
            return name;

        var rankNames = name
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (rankNames.Length <= 1)
            return name.Trim();

        var rankIndex = Math.Max(1, value) - 1;
        rankIndex = Math.Min(rankIndex, rankNames.Length - 1);
        return rankNames[rankIndex].Trim();
    }

    private static void SortTemplateValues(List<NpcTemplateValue> values)
    {
        values.Sort((left, right) =>
        {
            var sortOrder = left.Sort.CompareTo(right.Sort);
            return sortOrder != 0 ? sortOrder : left.Id.CompareTo(right.Id);
        });
    }

    private static void PopulateTemplateTooltips(
        Chara template,
        IReadOnlyList<NpcTemplateValue> values,
        bool preferFeatEffect)
    {
        if (GameAccess.Sources.Elements?.map == null)
            return;
        for (var i = 0; i < values.Count; i++)
        {
            var entry = values[i];
            if (!GameAccess.Sources.Elements.map.TryGetValue(entry.Id, out var source))
                continue;
            try
            {
                var element = template.elements.GetElement(entry.Id);
                if (element == null)
                {
                    element = Element.Create(entry.Id, entry.Value);
                    element.owner = template.elements;
                }
                if (preferFeatEffect && element is Feat feat)
                {
                    var description = "";
                    try
                    {
                        description = source.GetDetail() ?? "";
                    }
                    catch
                    {
                        description = source.detail_L ?? source.detail ?? "";
                    }
                    if (string.IsNullOrWhiteSpace(description))
                        description = element.GetDetail() ?? "";
                    var effect = feat.GetHint(template.elements) ?? "";
                    entry.TooltipText = JoinTemplateTooltipSections(description, effect);
                }
                else
                    entry.TooltipText = element.GetDetail() ?? "";
            }
            catch
            {
            }
            if (!string.IsNullOrWhiteSpace(entry.TooltipText))
                continue;
            try
            {
                entry.TooltipText = source.GetDetail() ?? "";
            }
            catch
            {
                entry.TooltipText = source.detail_L ?? source.detail ?? "";
            }
        }
    }

    private static string JoinTemplateTooltipSections(string description, string effect)
    {
        description = (description ?? "").Trim();
        effect = (effect ?? "").Trim();
        if (description.Length == 0)
            return effect;
        if (effect.Length == 0 || string.Equals(description, effect, StringComparison.Ordinal))
            return description;
        return description + "\n" + effect;
    }

    private static void PopulateNpcTemplateAbilityTooltips(
        Chara template,
        IReadOnlyList<NpcTemplateValue> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var entry = values[i];
            try
            {
                var element = template.elements.GetElement(entry.Id);
                if (element == null)
                {
                    element = Element.Create(entry.Id, entry.Value);
                    element.owner = template.elements;
                }

                var act = ResolveNpcTemplateAct(template, element, entry.Id);
                if (act == null)
                    continue;

                var source = element.source;
                try
                {
                    var description = source?.GetDetail();
                    if (!string.IsNullOrWhiteSpace(description))
                        entry.TooltipText = description;
                }
                catch
                {
                }
                var tooltip = CreateNpcTemplateAbilityTooltipInfo(template, entry, act, element, source);
                if (element is Spell)
                {
                    tooltip.HasSuccessRate = true;
                    tooltip.SuccessRate = Mathf.Clamp(template.CalcCastingChance(element, 1), 0, 100);
                }

                tooltip.RelatedAbility = ResolveNpcTemplateRelatedAbility(source, out tooltip.RelatedAbilitySource);
                PopulateNpcTemplateAbilityNotes(template, act, element, source, tooltip);
                PopulateNpcTemplateAbilityCost(template, element, tooltip);
                entry.AbilityTooltip = tooltip;
            }
            catch
            {
            }
        }
    }

    private static Act? ResolveNpcTemplateAct(Chara template, Element element, int id)
    {
        try
        {
            if (element.act != null)
                return element.act;
        }
        catch
        {
        }

        try
        {
            var items = template.ability?.list?.items;
            if (items != null)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var act = items[i]?.act;
                    if (act != null && act.id == id)
                        return act;
                }
            }
        }
        catch
        {
        }

        try { return ACT.Create(id); }
        catch { return null; }
    }

    private static string ResolveNpcTemplateAbilityTarget(Act act)
    {
        try { return act.TargetType.ToString().lang(); }
        catch
        {
            try { return act.TargetType.ToString(); }
            catch { return "-"; }
        }
    }

    private static string ResolveNpcTemplateRelatedAbility(
        SourceElement.Row? source,
        out SourceElement.Row? relatedSource)
    {
        relatedSource = null;
        if (source == null || string.IsNullOrWhiteSpace(source.aliasParent))
            return "";
        try
        {
            var elements = GameAccess.Sources.Elements;
            if (elements?.alias != null &&
                elements.alias.TryGetValue(source.aliasParent, out var row) &&
                row != null)
            {
                relatedSource = row;
                return row.GetName() ?? source.aliasParent;
            }
        }
        catch
        {
        }
        return source.aliasParent;
    }

    private static void PopulateNpcTemplateAbilityCost(
        Chara template,
        Element element,
        NpcAbilityTooltipInfo tooltip)
    {
        try
        {
            var cost = element.GetCost(template);
            if (cost.type == Act.CostType.None || cost.cost <= 0)
                return;
            var adjusted = cost.cost;
            if (cost.type == Act.CostType.MP)
            {
                var reduction = template.Evalue(483);
                if (reduction > 0)
                    adjusted = cost.cost * 100 / (100 + (int)Mathf.Sqrt(reduction * 10) * 3);
            }
            tooltip.CostType = cost.type;
            tooltip.Cost = adjusted;
            tooltip.BaseCost = cost.cost;
        }
        catch
        {
        }
    }

    private static void PopulateNpcTemplateAbilityNotes(
        Chara template,
        Act act,
        Element element,
        SourceElement.Row? source,
        NpcAbilityTooltipInfo tooltip)
    {
        var extra = "";
        try { extra = source?.GetText("textExtra", false) ?? ""; }
        catch
        {
        }
        if (!string.IsNullOrWhiteSpace(extra))
        {
            var entries = extra.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < entries.Length; i++)
            {
                var note = entries[i].Trim();
                if (note.Length == 0)
                    continue;
                if (note.StartsWith("@", StringComparison.Ordinal))
                {
                    note = ResolveNpcTemplateAbilityConditionName(
                        template,
                        note.Substring(1),
                        tooltip.Power);
                }
                else
                {
                    note = note.Replace("#calc", tooltip.Power.ToString(CultureInfo.InvariantCulture))
                        .Replace("#ele", tooltip.RelatedAbility.ToLowerInvariant())
                        .Replace(';', ',');
                }
                AddUniqueNpcTemplateAbilityNote(tooltip.Notes, note);
            }
        }

        if (ContainsTag(source?.tag, "syncRide"))
            AddUniqueNpcTemplateAbilityNote(tooltip.Notes, SafeNpcTemplateAbilityLanguage("hintSyncRide"));
        try
        {
            if (GameAccess.Characters.PlayerCharacter?.HasElement(1274) == true &&
                ContainsTag(source?.tag, "dontForget"))
                AddUniqueNpcTemplateAbilityNote(tooltip.Notes, SafeNpcTemplateAbilityLanguage("hintDontForget"));
        }
        catch
        {
        }
        try
        {
            if (act.HaveLongPressAction && element.id != 8230 && element.id != 8232)
                AddUniqueNpcTemplateAbilityNote(tooltip.Notes, SafeNpcTemplateAbilityLanguage("hintPartyAbility"));
        }
        catch
        {
        }
        try
        {
            if (!act.LocalAct)
                AddUniqueNpcTemplateAbilityNote(tooltip.Notes, SafeNpcTemplateAbilityLanguage("isGlobalAct"));
        }
        catch
        {
        }
    }

    private static string ResolveNpcTemplateAbilityConditionName(Chara template, string alias, int power)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return "";
        try
        {
            var condition = Condition.Create(alias, power, null);
            if (condition == null)
                return "";
            condition.owner = template;
            return condition.Name ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string SafeNpcTemplateAbilityLanguage(string key)
    {
        try { return key.lang() ?? ""; }
        catch { return ""; }
    }

    private static void AddUniqueNpcTemplateAbilityNote(List<string> notes, string note)
    {
        if (!string.IsNullOrWhiteSpace(note) && !notes.Contains(note))
            notes.Add(note);
    }
}
