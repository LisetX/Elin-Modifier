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

internal readonly struct ThreatMarkerSnapshot
{
    public readonly Rect Rect;
    public readonly string Name;
    public readonly string LevelText;
    public readonly float HpRatio;

    public ThreatMarkerSnapshot(Rect rect, string name, string levelText, float hpRatio)
    {
        Rect = rect;
        Name = name ?? "";
        LevelText = levelText ?? "";
        HpRatio = hpRatio;
    }
}

