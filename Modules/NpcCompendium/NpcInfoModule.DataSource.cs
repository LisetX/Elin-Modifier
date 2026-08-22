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

    private void EnsureData()
    {
        var sourceIdentity = GameAccess.Sources.Manager;
        if (sourceIdentity != null && ReferenceEquals(sourceIdentity, _sourceIdentity) && _npcs.Count > 0)
            return;

        _sourceIdentity = sourceIdentity;
        _npcs.Clear();
        _npcById.Clear();
        _spawnListCache.Clear();
        _distributionCache.Clear();
        _biomeDisplayNameCache.Clear();
        _extendedSearchTextCache.Clear();
        _spawnListIds.Clear();
        _biomes = null;
        if (sourceIdentity == null)
            return;

        foreach (var value in EnumerateRows(GameAccess.Sources.Characters, "SourceChara+Row"))
        {
            if (!(value is SourceChara.Row row) || string.IsNullOrWhiteSpace(row.id) || _npcById.ContainsKey(row.id))
                continue;
            var name = SafeName(row);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                name = row.id;
            var npc = new NpcRecord
            {
                Id = row.id,
                Name = name,
                Race = row.race ?? "",
                Job = row.job ?? "",
                Biome = row.biome ?? "",
                Hostility = row.hostility ?? "",
                Category = row.category ?? "",
                Equipment = row.equip ?? "",
                BaseLevel = row.LV,
                Chance = row.chance,
                Quality = row.quality,
                Row = row
            };
            _npcs.Add(npc);
            _npcById.Add(npc.Id, npc);
        }
        _npcs.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

        var seenLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in EnumerateRows(GameAccess.Sources.SpawnLists, "SourceSpawnList+Row"))
        {
            if (!(value is SourceSpawnList.Row row) || string.IsNullOrWhiteSpace(row.id) || !seenLists.Add(row.id))
                continue;
            var isCharaList = string.Equals(row.type, "chara", StringComparison.OrdinalIgnoreCase) ||
                              row.id.StartsWith("c_", StringComparison.OrdinalIgnoreCase) ||
                              (!string.IsNullOrWhiteSpace(row.parent) && row.parent.StartsWith("c_", StringComparison.OrdinalIgnoreCase));
            if (!isCharaList)
                continue;
            _spawnListIds.Add(row.id);
        }
        _spawnListIds.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<object> EnumerateRows(object? source, string expectedTypeName)
    {
        if (source == null)
            yield break;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var type = source.GetType(); type != null; type = type.BaseType)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                object? value;
                try { value = fields[fieldIndex].GetValue(source); }
                catch { continue; }
                if (value is IDictionary dictionary)
                {
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (entry.Value != null && entry.Value.GetType().FullName == expectedTypeName && seen.Add(entry.Value))
                            yield return entry.Value;
                    }
                }
                else if (value is IEnumerable enumerable && !(value is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item != null && item.GetType().FullName == expectedTypeName && seen.Add(item))
                            yield return item;
                    }
                }
            }
        }
    }
}
