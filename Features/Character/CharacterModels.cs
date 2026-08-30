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

internal sealed class RowDef
{
    public readonly string Key;
    public readonly string Label;
    public readonly RowKind Kind;
    public string Alias;
    public string Category;
    public int Max;

    public RowDef(string key, string label, RowKind kind)
    {
        Key = key;
        Label = label;
        Kind = kind;
    }
}

internal sealed class AbilityDef
{
    public readonly int Id;
    public readonly string Name;
    public readonly object Source;
    public string Alias = "";
    public string Category = "";
    public string DisplayName;

    public AbilityDef(int id, string name, object source)
    {
        Id = id;
        Name = name;
        Source = source;
        DisplayName = name;
    }
}

internal sealed class AbilityCostOverride
{
    public readonly int Hp;
    public readonly int Mp;
    public readonly int Sp;

    public AbilityCostOverride(int hp, int mp, int sp)
    {
        Hp = hp < 0 ? -1 : hp;
        Mp = mp < 0 ? -1 : mp;
        Sp = sp < 0 ? -1 : sp;
    }
}

