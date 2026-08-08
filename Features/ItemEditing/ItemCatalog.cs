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
    private void EnsureItemRows()
    {
        if (_itemRows != null) return;
        _itemRows = new List<ItemDef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in EnumerateSourceThingRows())
        {
            var id = GetString(row, "id");
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
            var name = GetThingDisplayName(row);
            if (string.IsNullOrEmpty(name)) name = id;
            if (name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) continue;
            _itemRows.Add(new ItemDef(id, name, row, -1, -1));
            if (TryAddPlantSeedItemRows(id, row))
                continue;
            var variants = GetVariantNames(row);
            var skins = GetVariantSkinIds(row);
            var variantCount = Math.Max(variants.Length, skins.Length);
            for (var i = 0; i < variantCount; i++)
            {
                var skinId = i < skins.Length ? skins[i] : i;
                var variant = i < variants.Length ? variants[i] : "";
                if (string.IsNullOrEmpty(variant))
                    variant = GetNativeThingDisplayName(id, skinId, true, "");
                if (string.IsNullOrEmpty(variant) || variant == name) continue;
                _itemRows.Add(new ItemDef(id, variant, row, i, skinId));
            }
        }
        MarkDuplicateItemNames(_itemRows);
        _itemRows.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        _itemLog = T("已读取物品数据：", "Loaded item data: ") + _itemRows.Count;
    }
    private bool TryAddPlantSeedItemRows(string itemId, object source)
    {
        if (!string.Equals(itemId, "seed", StringComparison.OrdinalIgnoreCase))
            return false;

        var rows = GameAccess.Sources.Objects?.rows;
        if (rows == null)
            return false;

        var seen = new HashSet<int>();
        var added = 0;
        foreach (var seedRow in rows)
        {
            try
            {
                if (seedRow == null || !seedRow.HasTag(CTAG.seed) || !seen.Add(seedRow.id))
                    continue;

                var seed = TraitSeed.MakeSeed(seedRow);
                if (seed == null)
                    continue;
                var name = CleanDisplayName(seed.GetName(NameStyle.FullNoArticle, 1));
                if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    continue;

                _itemRows.Add(new ItemDef(itemId, name, source, added, seed.idSkin, seedRow.id));
                added++;
            }
            catch
            {
            }
        }
        return added > 0;
    }
}
