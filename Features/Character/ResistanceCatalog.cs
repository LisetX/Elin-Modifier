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
    private void EnsureResistRows()
    {
        if (_resistRows != null) return;
        _resistRows = new List<RowDef>();
        var seen = new HashSet<int>();
        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            if (id <= 0 || !seen.Add(id)) continue;
            var alias = GetString(row, "alias");
            var name = GetDisplayName(row);
            if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) continue;
            var type = GetString(row, "type");
            var group = GetString(row, "group");
            var category = GetString(row, "category");
            var categorySub = GetString(row, "categorySub");
            var tags = string.Join(",", GetStringArray(row, "tag"));
            var text = alias + "," + name + "," + type + "," + group + "," + category + "," + categorySub + "," + tags;
            if (!IsResistanceElement(id, text, name)) continue;
            _resistRows.Add(new RowDef(id.ToString(), name, RowKind.Element)
            {
                Alias = alias,
                Category = string.IsNullOrEmpty(category) ? group : category
            });
        }
        _resistRows.Sort((a, b) => ParseInt(a.Key, 0).CompareTo(ParseInt(b.Key, 0)));
        RemoveDuplicateLabels(_resistRows);
        if (_resistRows.Count == 0)
            _resistRows.AddRange(_fallbackResistRows);
        _log = T("已读取抗性数据：", "Loaded resistance data: ") + _resistRows.Count;
    }
    private static bool IsResistanceElement(int id, string text, string name)
    {
        if (TextHas(name, "精灵") || TextHas(name, "鳞粉"))
            return false;
        if (name != null && name.IndexOf("抗性", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (TextHas(text, "resist") || TextHas(text, "resistance"))
            return true;
        if (id >= 900 && id <= 999 && TextHas(text, "res"))
            return true;
        return false;
    }
    private static void RemoveDuplicateLabels(List<RowDef> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            var key = rows[i].Label.Trim();
            if (!seen.Add(key)) rows.RemoveAt(i);
        }
    }
    private static void RemoveDuplicateRowsByKey(List<RowDef> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!seen.Add(rows[i].Key)) rows.RemoveAt(i);
        }
    }
    private static int CompareRows(RowDef a, RowDef b)
    {
        var ca = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
        return ca != 0 ? ca : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
    }
}
