internal sealed partial class NpcInfoModule
{
    private static NpcAbilityTooltipInfo CreateNpcTemplateAbilityTooltipInfo(
        Chara template,
        NpcTemplateValue entry,
        Act act,
        Element element,
        SourceElement.Row? source)
    {
        var item = ResolveNpcTemplateAbilityItem(template, entry.Id);
        var isPartyTarget = item?.pt ?? entry.StoryPartyTarget ?? false;
        var usageChance = item != null
            ? item.chance
            : entry.StoryUsageChance ?? 0;
        return new NpcAbilityTooltipInfo
        {
            DisplayLevel = element.DisplayValue,
            Target = ResolveNpcTemplateAbilityTarget(act),
            Power = element.GetPower(template),
            HasPower = source != null && source.lvFactor > 0,
            IsPartyTarget = isPartyTarget || act.TargetType.ForceParty,
            UsageChance = usageChance < 0 ? 0 : usageChance
        };
    }

    private static ActList.Item? ResolveNpcTemplateAbilityItem(Chara template, int id)
    {
        try
        {
            var items = template.ability?.list?.items;
            if (items == null)
                return null;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item?.act != null && item.act.id == id)
                    return item;
            }
        }
        catch
        {
        }
        return null;
    }
}
