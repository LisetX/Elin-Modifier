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
    private void EnsureMaterialRows()
    {
        if (_materialRows != null) return;
        _materialRows = new List<MaterialDef>();
        var seen = new HashSet<int>();
        foreach (var row in EnumerateSourceMaterialRows())
        {
            var id = GetInt(row, "id");
            if (id < 0 || !seen.Add(id)) continue;
            var name = GetMaterialDisplayName(row);
            if (string.IsNullOrEmpty(name)) name = GetString(row, "alias");
            var category = GetString(row, "category");
            _materialRows.Add(new MaterialDef(id, name, category));
        }
        _materialRows.Sort((a, b) => a.Id.CompareTo(b.Id));
    }
    private static IEnumerable<object> EnumerateSourceThingRows()
    {
        var source = GameAccess.Sources.Things;
        if (source == null) yield break;
        for (var t = source.GetType(); t != null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = field.GetValue(source);
                if (value is IDictionary dict)
                {
                    foreach (DictionaryEntry entry in dict)
                        if (entry.Value != null && entry.Value.GetType().FullName == "SourceThing+Row")
                            yield return entry.Value;
                }
                else if (value is IEnumerable items)
                {
                    foreach (var item in items)
                        if (item != null && item.GetType().FullName == "SourceThing+Row")
                            yield return item;
                }
            }
        }
    }
    private static IEnumerable<object> EnumerateSourceCharaRows()
    {
        var source = GameAccess.Sources.Characters;
        if (source == null) yield break;
        for (var t = source.GetType(); t != null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = field.GetValue(source);
                if (value is IDictionary dict)
                {
                    foreach (DictionaryEntry entry in dict)
                        if (entry.Value != null && entry.Value.GetType().FullName == "SourceChara+Row")
                            yield return entry.Value;
                }
                else if (value is IEnumerable items)
                {
                    foreach (var item in items)
                        if (item != null && item.GetType().FullName == "SourceChara+Row")
                            yield return item;
                }
            }
        }
    }
    private static IEnumerable<object> EnumerateSourceMaterialRows()
    {
        var source = GameAccess.Sources.Materials;
        if (source == null) yield break;
        for (var t = source.GetType(); t != null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = field.GetValue(source);
                if (value is IDictionary dict)
                {
                    foreach (DictionaryEntry entry in dict)
                        if (entry.Value != null && entry.Value.GetType().FullName == "SourceMaterial+Row")
                            yield return entry.Value;
                }
                else if (value is IEnumerable items)
                {
                    foreach (var item in items)
                        if (item != null && item.GetType().FullName == "SourceMaterial+Row")
                            yield return item;
                }
            }
        }
    }
    private static IEnumerable<object> EnumerateSourceReligionRows()
    {
        var source = GameAccess.Sources.Religions;
        if (source == null) yield break;
        for (var t = source.GetType(); t != null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = field.GetValue(source);
                if (value is IDictionary dict)
                {
                    foreach (DictionaryEntry entry in dict)
                        if (entry.Value != null && entry.Value.GetType().FullName == "SourceReligion+Row")
                            yield return entry.Value;
                }
                else if (value is IEnumerable items)
                {
                    foreach (var item in items)
                        if (item != null && item.GetType().FullName == "SourceReligion+Row")
                            yield return item;
                }
            }
        }
    }
    private static string GetThingDisplayName(object row)
    {
        try
        {
            var method = row.GetType().GetMethod("GetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var value = method == null ? "" : CleanDisplayName(method.Invoke(row, null) as string ?? "");
            if (!string.IsNullOrEmpty(value) && !value.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                return value;
        }
        catch { }

        foreach (var name in new[] { "name_L", "name", "altname_L", "altname", "name_JP" })
        {
            var value = GetString(row, name);
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return GetString(row, "id");
    }
    private static string GetCharaDisplayName(object row)
    {
        var method = row.GetType().GetMethod("GetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (method != null)
        {
            var value = method.Invoke(row, null) as string;
            if (!string.IsNullOrEmpty(value)) return value;
        }
        foreach (var name in new[] { "name_L", "name", "aka_L", "aka", "altname_L", "altname", "name_JP" })
        {
            var value = GetString(row, name);
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return GetString(row, "id");
    }
    private static string GetMaterialDisplayName(object row)
    {
        foreach (var name in new[] { "name_L", "name", "name_JP", "alias" })
        {
            var value = GetString(row, name);
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return "";
    }
    private static string GetReligionDisplayName(object row)
    {
        foreach (var name in new[] { "name_L", "name", "name_JP", "id" })
        {
            var value = GetString(row, name);
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return "";
    }
    private static string[] GetVariantNames(object row)
    {
        foreach (var name in new[] { "name2_L", "name2", "name2_JP" })
        {
            var values = GetStringArray(row, name);
            if (values.Length > 0) return values;
        }
        return Array.Empty<string>();
    }
    private static int[] GetVariantSkinIds(object row)
    {
        return GetIntArray(row, "skins");
    }
}
