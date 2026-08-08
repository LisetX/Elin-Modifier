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
    private void EnsureFaithRows()
    {
        if (_faithRows != null && _faithRows.Count > 0) return;
        _faithRows = new List<FaithDef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var manager = GameAccess.Runtime.Game?.religions;
            if (manager?.list != null)
            {
                for (var i = 0; i < manager.list.Count; i++)
                    AddFaithRow(manager.list[i], seen);
            }

            if (manager?.dictAll != null)
            {
                foreach (var pair in manager.dictAll)
                    AddFaithRow(pair.Value, seen);
            }
        }
        catch
        {
        }

        if (_faithRows.Count == 0)
        {
            foreach (var row in EnumerateSourceReligionRows())
            {
                var id = GetString(row, "id");
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                var name = GetReligionDisplayName(row);
                if (string.IsNullOrEmpty(name)) name = id;
                _faithRows.Add(new FaithDef(id, name));
            }
        }

        _faithRows.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
    }
    private void AddFaithRow(Religion? religion, HashSet<string> seen)
    {
        if (religion == null) return;
        var id = SafeText(() => religion.id, "");
        if (string.IsNullOrEmpty(id) || !seen.Add(id)) return;
        var name = SafeText(() => religion.Name, "");
        if (string.IsNullOrEmpty(name)) name = id;
        _faithRows?.Add(new FaithDef(id, name));
    }
}
