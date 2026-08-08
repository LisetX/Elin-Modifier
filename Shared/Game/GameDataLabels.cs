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
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private static void MarkDuplicateNpcNames(List<NpcDef> rows)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!counts.ContainsKey(row.Name)) counts[row.Name] = 0;
            counts[row.Name]++;
        }
        foreach (var row in rows)
        {
            if (counts[row.Name] <= 1) continue;
            row.DisplayName = row.Name + " (" + row.Id + ")";
        }
    }
    private static void MarkDuplicateItemNames(List<ItemDef> rows)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!counts.ContainsKey(row.Name)) counts[row.Name] = 0;
            counts[row.Name]++;
        }

        foreach (var row in rows)
        {
            row.DisplayName = row.SeedRefVal >= 0
                ? row.Name
                : row.VariantIndex >= 0
                ? GetNativeThingDisplayName(row.Id, row.SkinId, true, row.Name)
                : row.Name;
            if (counts[row.Name] > 1 && row.VariantIndex < 0)
                row.DisplayName = GetNativeThingDisplayName(row.Id, row.SkinId, false, row.Name);
        }

        counts.Clear();
        foreach (var row in rows)
        {
            if (!counts.ContainsKey(row.DisplayName)) counts[row.DisplayName] = 0;
            counts[row.DisplayName]++;
        }
        foreach (var row in rows)
        {
            if (counts[row.DisplayName] <= 1) continue;
            var suffix = row.Id;
            if (row.VariantIndex >= 0) suffix += " skin " + row.SkinId.ToString(CultureInfo.InvariantCulture);
            row.DisplayName = row.DisplayName + " (" + suffix + ")";
        }
    }
    private static string GetNativeThingDisplayName(string id, int skinId, bool hasVariant, string fallback)
    {
        try
        {
            var thing = GameAccess.Spawn.CreateThing(id, -1, 1);
            if (thing == null) return fallback;
            if (hasVariant)
                SetCardIntProperty(thing, "idSkin", skinId);
            var name = thing.GetName(NameStyle.FullNoArticle, 1);
            name = CleanDisplayName(name);
            return string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ? fallback : name;
        }
        catch { return fallback; }
    }
    private static string CleanDisplayName(string name)
    {
        return string.IsNullOrEmpty(name) ? "" : name.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
