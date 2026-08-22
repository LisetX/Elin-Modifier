using System;
using System.Collections.Generic;
using System.Globalization;

internal sealed partial class NpcInfoModule
{
    private bool MatchesExtendedNpcFilter(NpcRecord npc, string filter)
    {
        if (!_extendedSearchTextCache.TryGetValue(npc.Id, out var searchText))
        {
            searchText = BuildExtendedNpcSearchText(npc);
            _extendedSearchTextCache[npc.Id] = searchText;
        }
        return searchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string BuildExtendedNpcSearchText(NpcRecord npc)
    {
        var values = new List<string>();
        try
        {
            var template = BuildTemplateInfo(npc, 0);
            for (var i = 0; i < template.Equipment.Count; i++)
            {
                AddNpcSearchValue(values, template.Equipment[i].Name);
                AddNpcSearchValue(values, template.Equipment[i].Id);
            }
            AddNpcTemplateSearchValues(values, template.Feats);
            AddNpcTemplateSearchValues(values, template.Spells);
            AddNpcTemplateSearchValues(values, template.Enchantments);
        }
        catch
        {
        }
        try
        {
            var loot = BuildLootEntries(npc);
            for (var i = 0; i < loot.Count; i++)
                AddNpcSearchValue(values, loot[i].Item);
        }
        catch
        {
        }
        return string.Join("\n", values);
    }

    private static void AddNpcTemplateSearchValues(
        ICollection<string> values,
        IReadOnlyList<NpcTemplateValue> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            AddNpcSearchValue(values, entries[i].Name);
            AddNpcSearchValue(values, entries[i].Id.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddNpcSearchValue(ICollection<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(value.Trim());
    }
}
