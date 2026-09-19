using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static ElinModifierPlugin;

internal sealed partial class MoreInfoModule
{
    private static string BuildItemMoreInfoHoverDetailsUncached(Thing thing)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (thing == null || instance == null)
            return "";

        var lines = new List<string>();
        if (instance._showItemMoreInfoBasicInfo)
        {
            var basicInfo = new List<string>
            {
                BuildItemMoreInfoRarityField(thing),
                BuildItemMoreInfoField(Tr("物品价值", "Item value"), GetItemDataValueText(thing), ItemMoreInfoBasicInfoColor),
                BuildItemMoreInfoField(Tr("重量", "Weight"), SafeText(() => Lang._weight(thing.ChildrenAndSelfWeight), "?"), ItemMoreInfoBasicInfoColor)
            };
            lines.Add(string.Join(" ", basicInfo.ToArray()));
        }

        if (instance._showItemMoreInfoGatheringThreshold)
        {
            var gatheringThreshold = BuildThingGatheringThresholdLine(thing);
            if (!string.IsNullOrEmpty(gatheringThreshold))
                lines.Add(gatheringThreshold);
        }

        if (instance._showItemMoreInfoWeaponStats && CanEditWeaponData(thing))
        {
            var weaponStats = new List<string>
            {
                BuildItemMoreInfoField(Tr("等级", "Level"), SafeInt(() => thing.LV, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("强化", "Enhance"), SafeInt(() => thing.encLV, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("伤害骰面", "Damage dice sides"), SafeInt(() => thing.c_diceDim, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("命中", "Hit"), GetThingElementBase(thing, 66).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("伤害修正", "Damage bonus"), GetThingElementBase(thing, 67).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField("DV", GetThingElementBase(thing, 64).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField("PV", GetThingElementBase(thing, 65).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("充能", "Charges"), SafeInt(() => thing.c_charges, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor),
                BuildItemMoreInfoField(Tr("弹药", "Ammo"), SafeInt(() => thing.c_ammo, 0).ToString(CultureInfo.InvariantCulture), ItemMoreInfoWeaponStatsColor)
            };
            AddNpcMoreInfoLine(lines, BuildNpcMoreInfoEntryLines("", weaponStats, 5));
        }

        var enchantments = new List<string>();
        if (instance._showItemMoreInfoEnchantments)
        {
            try
            {
                var rows = new List<Element>();
                foreach (var element in thing.elements.dict.Values)
                {
                    if (element != null && element.id > 0)
                        rows.Add(element);
                }
                rows.Sort((a, b) => a.id.CompareTo(b.id));

                foreach (var element in rows)
                {
                    var value = GetThingElementEditorValue(thing, element);
                    if (value == 0)
                        continue;
                    var name = GetGeneEffectNameStatic(element.id);
                    enchantments.Add(ColorNpcMoreInfoText(name, ItemMoreInfoEnchantColor) +
                                     "(" + FormatCompactCount(value) + ")");
                }
            }
            catch { }
        }

        AddNpcMoreInfoLine(lines, BuildNpcMoreInfoEntryLines("", enchantments, 5));
        if (lines.Count == 0)
            return "";

        return WrapItemMoreInfoLines(lines);
    }
    private static string WrapItemMoreInfoLines(List<string> lines)
    {
        if (lines == null || lines.Count == 0)
            return "";
        var sb = new StringBuilder();
        sb.Append(Environment.NewLine);
        sb.Append("<size=").Append(GetItemMoreInfoFontSize().ToString(CultureInfo.InvariantCulture)).Append('>');
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                sb.Append(Environment.NewLine);
            sb.Append(StripItemMoreInfoSizeTags(lines[i]));
        }
        sb.Append("</size>");
        return sb.ToString();
    }
    private static string StripItemMoreInfoSizeTags(string line)
    {
        if (string.IsNullOrEmpty(line) || line.IndexOf("<size", StringComparison.OrdinalIgnoreCase) < 0)
            return line ?? "";
        return Regex.Replace(line, "</?size[^>]*>", "", RegexOptions.IgnoreCase);
    }
}
