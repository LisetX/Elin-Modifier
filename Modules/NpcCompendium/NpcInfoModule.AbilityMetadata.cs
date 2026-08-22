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
        return new NpcAbilityTooltipInfo
        {
            DisplayLevel = element.DisplayValue,
            Target = ResolveNpcTemplateAbilityTarget(act),
            Power = element.GetPower(template),
            HasPower = source != null && source.lvFactor > 0,
            IsPartyTarget = item?.pt == true || act.TargetType.ForceParty,
            UsageChance = item == null || item.chance < 0 ? 0 : item.chance
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
