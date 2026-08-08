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
    private void EnsureNpcRows()
    {
        if (_npcRows != null) return;
        _npcRows = new List<NpcDef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in EnumerateSourceCharaRows())
        {
            var id = GetString(row, "id");
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
            var name = GetCharaDisplayName(row);
            if (string.IsNullOrEmpty(name)) name = id;
            if (name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) continue;
            var race = GetString(row, "race");
            var job = GetString(row, "job");
            _npcRows.Add(new NpcDef(id, name, race, job));
        }
        MarkDuplicateNpcNames(_npcRows);
        _npcRows.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        _npcLog = T("已读取NPC数据：", "Loaded NPC data: ") + _npcRows.Count;
    }
}
