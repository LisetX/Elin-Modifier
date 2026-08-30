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

internal sealed class HomeElementDef
{
    public readonly int Id;
    public readonly string Name;
    public readonly string Category;
    public readonly object Source;
    public string Alias = "";
    public string DisplayName;
    public int Max;

    public HomeElementDef(int id, string name, string category, object source)
    {
        Id = id;
        Name = name;
        Category = category;
        Source = source;
        DisplayName = name;
    }
}

