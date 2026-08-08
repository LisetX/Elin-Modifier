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

internal sealed class RelationshipOption
{
    public readonly string Label;
    public readonly Hostility Value;

    public RelationshipOption(string label, Hostility value)
    {
        Label = label;
        Value = value;
    }
}

internal sealed class PendingTeleportRequest
{
    public readonly Zone? TargetZone;
    public readonly int X;
    public readonly int Y;

    private PendingTeleportRequest(Zone? targetZone, int x, int y)
    {
        TargetZone = targetZone;
        X = x;
        Y = y;
    }

    public static PendingTeleportRequest ForZone(Zone zone)
    {
        return new PendingTeleportRequest(zone, 0, 0);
    }

    public static PendingTeleportRequest ForWorldPosition(int x, int y)
    {
        return new PendingTeleportRequest(null, x, y);
    }
}

