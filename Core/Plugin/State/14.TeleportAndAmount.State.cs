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
    private string _itemAmountInput = "";
    private string _teleportXInput = "";
    private string _teleportYInput = "";
    private string _teleportFilter = "";
    private string _lastTeleportFilter = "";
    private readonly List<TeleportZoneEntry> _teleportAllZoneCache = new List<TeleportZoneEntry>();
    private readonly List<TeleportZoneEntry> _teleportZoneCache = new List<TeleportZoneEntry>();
    private readonly List<TeleportZoneEntry> _emptyTeleportZoneCache = new List<TeleportZoneEntry>(0);
    private Region? _teleportAllZoneCacheRegion;
    private string _teleportZoneCacheFilter = "";
    private int _teleportAllZoneCacheSourceCount = -1;
    private bool _teleportZoneCacheDirty = true;
    private bool _teleportFilterCacheDirty = true;
    private PendingTeleportRequest? _pendingTeleportRequest;
    private string _itemAmountName = "";
    private Thing? _itemAmountTarget;
}
