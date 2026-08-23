using System;
using System.Collections.Generic;

internal sealed class NpcStoryAbilityDefinition
{
    internal NpcStoryAbilityDefinition(int abilityId, string questId, int usageChance, bool isPartyTarget)
    {
        AbilityId = abilityId;
        QuestId = questId;
        UsageChance = usageChance;
        IsPartyTarget = isPartyTarget;
    }

    internal int AbilityId { get; }
    internal string QuestId { get; }
    internal int UsageChance { get; }
    internal bool IsPartyTarget { get; }
}

internal static class NpcStoryAbilityCatalog
{
    private static readonly NpcStoryAbilityDefinition[] Empty = Array.Empty<NpcStoryAbilityDefinition>();
    private static readonly NpcStoryAbilityDefinition[] Farris =
    {
        new NpcStoryAbilityDefinition(6754, "stone_dream", 50, false)
    };

    internal static IReadOnlyList<NpcStoryAbilityDefinition> GetForNpc(string npcId)
    {
        return string.Equals(npcId, "farris", StringComparison.OrdinalIgnoreCase)
            ? Farris
            : Empty;
    }
}
