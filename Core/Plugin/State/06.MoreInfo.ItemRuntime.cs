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
    internal Map? _itemMoreInfoHoverCacheMap;
    internal Thing? _itemMoreInfoHoverCacheTarget;
    internal int _itemMoreInfoHoverCacheUid = -1;
    internal int _itemMoreInfoHoverCacheMask = -1;
    internal string _itemMoreInfoHoverCacheLanguage = "";
    internal int _itemMoreInfoHoverCacheFrame = -1;
    internal float _itemMoreInfoHoverCacheTime = -9999f;
    internal string _itemMoreInfoHoverCacheValue = "";
    internal Map? _plantMoreInfoHoverCacheMap;
    internal int _plantMoreInfoHoverCacheX = int.MinValue;
    internal int _plantMoreInfoHoverCacheZ = int.MinValue;
    internal int _plantMoreInfoHoverCacheMask = -1;
    internal string _plantMoreInfoHoverCacheLanguage = "";
    internal int _plantMoreInfoHoverCacheFrame = -1;
    internal float _plantMoreInfoHoverCacheTime = -9999f;
    internal string _plantMoreInfoHoverCacheValue = "";
}
