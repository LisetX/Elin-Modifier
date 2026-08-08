using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{

    private string GetLGuiNpcRaceDisplayName(NpcRecord npc)
    {
        var raw = npc.Race ?? "";
        if (_language != "zh" || string.IsNullOrWhiteSpace(raw))
            return raw;

        try
        {
            var localized = npc.Row.race_row?.name_L;
            if (!string.IsNullOrWhiteSpace(localized) &&
                !localized.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                return localized;
        }
        catch
        {
        }

        return raw;
    }

    private string GetLGuiNpcJobDisplayName(NpcRecord npc)
    {
        var raw = npc.Job ?? "";
        if (_language != "zh" || string.IsNullOrWhiteSpace(raw))
            return raw;

        try
        {
            if (GameAccess.Sources.Jobs.map.TryGetValue(raw, out var row))
            {
                var localized = row.name_L;
                if (!string.IsNullOrWhiteSpace(localized) &&
                    !localized.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    return localized;
            }
        }
        catch
        {
        }

        return raw;
    }
}
