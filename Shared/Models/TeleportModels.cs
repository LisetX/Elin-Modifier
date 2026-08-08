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

internal sealed class TeleportZoneEntry
{
    public readonly Zone Zone;
    public readonly string Label;
    public readonly string SearchText;
    public readonly int X;
    public readonly int Y;
    public readonly string Name;

    public TeleportZoneEntry(Zone zone, string label, string searchText, int x, int y, string name)
    {
        Zone = zone;
        Label = label ?? "";
        SearchText = searchText ?? "";
        X = x;
        Y = y;
        Name = name ?? "";
    }
}

