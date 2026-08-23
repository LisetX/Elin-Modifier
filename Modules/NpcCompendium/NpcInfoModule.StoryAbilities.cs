using System;

internal sealed partial class NpcInfoModule
{
    private static void PopulateNpcStoryAbilities(
        NpcRecord npc,
        Chara template,
        NpcTemplateInfo result)
    {
        var definitions = NpcStoryAbilityCatalog.GetForNpc(npc.Id);
        var elements = GameAccess.Sources.Elements;
        if (definitions.Count == 0 || elements?.map == null)
            return;

        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (!elements.map.TryGetValue(definition.AbilityId, out var source) || source == null)
                continue;

            var value = template.elements.ValueWithoutLink(definition.AbilityId);
            if (value <= 0)
                value = Math.Max(1, source.LV);
            AddTemplateValue(result, result.Spells, source, value);

            for (var index = 0; index < result.Spells.Count; index++)
            {
                var entry = result.Spells[index];
                if (entry.Id != definition.AbilityId)
                    continue;
                entry.TriggerQuestId = definition.QuestId;
                entry.StoryUsageChance = definition.UsageChance;
                entry.StoryPartyTarget = definition.IsPartyTarget;
                break;
            }
        }
    }
}
