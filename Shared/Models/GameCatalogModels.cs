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

internal sealed class ItemDef
{
    public readonly string Id;
    public readonly string Name;
    public readonly object? Source;
    public readonly int VariantIndex;
    public readonly int SkinId;
    public readonly int SeedRefVal;
    public string DisplayName;

    public ItemDef(string id, string name, object? source, int variantIndex, int skinId, int seedRefVal = -1)
    {
        Id = id;
        Name = name;
        Source = source;
        VariantIndex = variantIndex;
        SkinId = skinId;
        SeedRefVal = seedRefVal;
        DisplayName = name;
    }
}

internal sealed class NpcDef
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Race;
    public readonly string Job;
    public string DisplayName;

    public NpcDef(string id, string name, string race, string job)
    {
        Id = id;
        Name = name;
        Race = race ?? "";
        Job = job ?? "";
        DisplayName = name;
    }
}

internal sealed class FaithDef
{
    public readonly string Id;
    public readonly string Name;
    public readonly string DisplayName;

    public FaithDef(string id, string name)
    {
        Id = id ?? "";
        Name = name ?? "";
        DisplayName = string.IsNullOrEmpty(Name) || string.Equals(Name, Id, StringComparison.OrdinalIgnoreCase)
            ? Id
            : Name + " (" + Id + ")";
    }
}

internal sealed class MaterialDef
{
    public readonly int Id;
    public readonly string Name;
    public readonly string Category;

    public MaterialDef(int id, string name, string category)
    {
        Id = id;
        Name = name;
        Category = category;
    }
}

internal sealed class GeneValueInput
{
    public string ElementId;
    public string Value;

    public GeneValueInput(string elementId, string value)
    {
        ElementId = elementId;
        Value = value;
    }
}

internal sealed class GeneEffectDef
{
    public readonly int Id;
    public readonly string Name;
    public readonly string Alias;
    public readonly string Category;

    public GeneEffectDef(int id, string name, string alias, string category)
    {
        Id = id;
        Name = name;
        Alias = alias ?? "";
        Category = category ?? "";
    }
}

