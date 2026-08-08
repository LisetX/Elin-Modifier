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
using static ElinModifierPlugin;

internal sealed partial class MoreInfoModule
{
    private readonly ElinModifierPlugin _host;
    internal MoreInfoModule(ElinModifierPlugin host)
    {
        _host = host;
    }
    internal static string BuildNpcMoreInfoHoverDetails(Chara chara)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (instance == null)
            return BuildNpcMoreInfoHoverDetailsUncached(chara);

        var now = instance.SchedulerNow;
        var map = SafeObject(() => GameAccess.World.CurrentMap) as Map;
        var uid = GetCharaUid(chara);
        var mask = GetNpcMoreInfoDisplayMask(instance);
        var language = instance._language ?? "";
        var frame = Time.frameCount;
        var interval = instance._lowPerformanceMode ? 0.25f : 0.1f;
        var sameTarget = ReferenceEquals(instance._npcMoreInfoHoverCacheMap, map) &&
                         ReferenceEquals(instance._npcMoreInfoHoverCacheTarget, chara) &&
                         instance._npcMoreInfoHoverCacheUid == uid &&
                         instance._npcMoreInfoHoverCacheMask == mask &&
                         string.Equals(instance._npcMoreInfoHoverCacheLanguage, language, StringComparison.Ordinal);
        if (sameTarget &&
            (instance._npcMoreInfoHoverCacheFrame == frame ||
             (now >= instance._npcMoreInfoHoverCacheTime && now - instance._npcMoreInfoHoverCacheTime < interval)))
        {
            return instance._npcMoreInfoHoverCacheValue;
        }

        var value = BuildNpcMoreInfoHoverDetailsUncached(chara);
        instance._npcMoreInfoHoverCacheMap = map;
        instance._npcMoreInfoHoverCacheTarget = chara;
        instance._npcMoreInfoHoverCacheUid = uid;
        instance._npcMoreInfoHoverCacheMask = mask;
        instance._npcMoreInfoHoverCacheLanguage = language;
        instance._npcMoreInfoHoverCacheFrame = frame;
        instance._npcMoreInfoHoverCacheTime = now;
        instance._npcMoreInfoHoverCacheValue = value;
        return value;
    }
    internal static string BuildItemMoreInfoHoverDetails(Thing thing)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (thing == null || instance == null)
            return "";

        var now = instance.SchedulerNow;
        var map = SafeObject(() => GameAccess.World.CurrentMap) as Map;
        var uid = SafeInt(() => thing.uid, -1);
        var mask = GetItemMoreInfoDisplayMask(instance);
        var language = instance._language ?? "";
        var frame = Time.frameCount;
        var interval = instance._lowPerformanceMode ? 0.25f : 0.1f;
        var sameTarget = ReferenceEquals(instance._itemMoreInfoHoverCacheMap, map) &&
                         ReferenceEquals(instance._itemMoreInfoHoverCacheTarget, thing) &&
                         instance._itemMoreInfoHoverCacheUid == uid &&
                         instance._itemMoreInfoHoverCacheMask == mask &&
                         string.Equals(instance._itemMoreInfoHoverCacheLanguage, language, StringComparison.Ordinal);
        if (sameTarget &&
            (instance._itemMoreInfoHoverCacheFrame == frame ||
             (now >= instance._itemMoreInfoHoverCacheTime && now - instance._itemMoreInfoHoverCacheTime < interval)))
        {
            return instance._itemMoreInfoHoverCacheValue;
        }

        var value = BuildItemMoreInfoHoverDetailsUncached(thing) + BuildPlantMoreInfoHoverDetails(thing);
        instance._itemMoreInfoHoverCacheMap = map;
        instance._itemMoreInfoHoverCacheTarget = thing;
        instance._itemMoreInfoHoverCacheUid = uid;
        instance._itemMoreInfoHoverCacheMask = mask;
        instance._itemMoreInfoHoverCacheLanguage = language;
        instance._itemMoreInfoHoverCacheFrame = frame;
        instance._itemMoreInfoHoverCacheTime = now;
        instance._itemMoreInfoHoverCacheValue = value;
        return value;
    }
    private static int GetItemMoreInfoDisplayMask(ElinModifierPlugin instance)
    {
        var mask = 0;
        if (instance._showItemMoreInfoBasicInfo) mask |= 1 << 0;
        if (instance._showItemMoreInfoWeaponStats) mask |= 1 << 1;
        if (instance._showItemMoreInfoEnchantments) mask |= 1 << 2;
        if (instance._showItemMoreInfoPlantStats) mask |= 1 << 3;
        if (instance._showItemMoreInfoPlantStatsExtended) mask |= 1 << 4;
        if (instance._showItemMoreInfoGatheringThreshold) mask |= 1 << 5;
        mask |= (Clamp(instance._showItemMoreInfoFontSizeOffset, -8, 8) + 8) << 8;
        return mask;
    }
    internal void InvalidateItemMoreInfoCache()
    {
        _host._itemMoreInfoHoverCacheMap = null;
        _host._itemMoreInfoHoverCacheTarget = null;
        _host._itemMoreInfoHoverCacheUid = -1;
        _host._itemMoreInfoHoverCacheMask = -1;
        _host._itemMoreInfoHoverCacheLanguage = "";
        _host._itemMoreInfoHoverCacheFrame = -1;
        _host._itemMoreInfoHoverCacheTime = -9999f;
        _host._itemMoreInfoHoverCacheValue = "";
        _host._plantMoreInfoHoverCacheMap = null;
        _host._plantMoreInfoHoverCacheX = int.MinValue;
        _host._plantMoreInfoHoverCacheZ = int.MinValue;
        _host._plantMoreInfoHoverCacheMask = -1;
        _host._plantMoreInfoHoverCacheLanguage = "";
        _host._plantMoreInfoHoverCacheFrame = -1;
        _host._plantMoreInfoHoverCacheTime = -9999f;
        _host._plantMoreInfoHoverCacheValue = "";
    }
}
