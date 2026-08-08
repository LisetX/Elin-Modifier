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
    private void SetShowNpcMoreInfo(bool enabled)
    {
        _showNpcMoreInfo = enabled;
        try
        {
            if (WidgetMouseover.Instance != null)
                ConfigureNpcMoreInfoHoverDirection(WidgetMouseover.Instance, enabled: false);
        }
        catch { }
        InvalidateNpcMoreInfoCaches();
        _log = enabled
            ? T("显示NPC更多信息已开启", "Show more NPC info enabled")
            : T("显示NPC更多信息已关闭", "Show more NPC info disabled");
    }
    private static string NormalizeNpcMoreInfoOrder(string? value)
    {
        var result = new List<string>(NpcMoreInfoOrderKeys.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = (value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var key = parts[i].Trim().ToLowerInvariant();
            var valid = false;
            for (var j = 0; j < NpcMoreInfoOrderKeys.Length; j++)
            {
                if (!string.Equals(NpcMoreInfoOrderKeys[j], key, StringComparison.OrdinalIgnoreCase))
                    continue;
                key = NpcMoreInfoOrderKeys[j];
                valid = true;
                break;
            }
            if (valid && seen.Add(key))
                result.Add(key);
        }

        for (var i = 0; i < NpcMoreInfoOrderKeys.Length; i++)
        {
            var missingKey = NpcMoreInfoOrderKeys[i];
            if (!seen.Add(missingKey))
                continue;
            if (string.Equals(missingKey, "relation", StringComparison.Ordinal))
            {
                var identityIndex = result.FindIndex(item => string.Equals(item, "identity", StringComparison.OrdinalIgnoreCase));
                if (identityIndex >= 0)
                {
                    result.Insert(identityIndex + 1, missingKey);
                    continue;
                }
            }
            result.Add(missingKey);
        }
        return string.Join(",", result.ToArray());
    }
    internal string[] GetNpcMoreInfoOrder()
    {
        _showNpcMoreInfoOrder = NormalizeNpcMoreInfoOrder(_showNpcMoreInfoOrder);
        return _showNpcMoreInfoOrder.Split(',');
    }
    private void MoveNpcMoreInfoOrderItem(string key, int targetIndex)
    {
        var order = new List<string>(GetNpcMoreInfoOrder());
        var currentIndex = order.FindIndex(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            return;
        targetIndex = Clamp(targetIndex, 0, order.Count - 1);
        if (currentIndex == targetIndex)
            return;
        var item = order[currentIndex];
        order.RemoveAt(currentIndex);
        if (targetIndex > order.Count)
            targetIndex = order.Count;
        order.Insert(targetIndex, item);
        _showNpcMoreInfoOrder = string.Join(",", order.ToArray());
        InvalidateNpcMoreInfoCaches();
    }
    private bool IsNpcMoreInfoOrderItemEnabled(string key)
    {
        switch (key)
        {
            case "level": return _showNpcMoreInfoLevel;
            case "identity": return _showNpcMoreInfoIdentity;
            case "relation": return _showNpcMoreInfoRelationFaith;
            case "vitals": return _showNpcMoreInfoVitals;
            case "attributes": return _showNpcMoreInfoAttributes;
            case "buffs": return _showNpcMoreInfoBuffs;
            case "resists": return _showNpcMoreInfoResists;
            case "skills": return _showNpcMoreInfoSkills;
            case "abilities": return _showNpcMoreInfoAbilities;
            case "feats": return _showNpcMoreInfoFeats;
            case "combat": return _showNpcMoreInfoCombatSimulation;
            default: return false;
        }
    }
    private static bool IsNpcMoreInfoMultiEntryKey(string key)
    {
        return string.Equals(key, "relation", StringComparison.Ordinal) ||
               string.Equals(key, "vitals", StringComparison.Ordinal) ||
               string.Equals(key, "attributes", StringComparison.Ordinal) ||
               string.Equals(key, "buffs", StringComparison.Ordinal) ||
               string.Equals(key, "resists", StringComparison.Ordinal) ||
               string.Equals(key, "skills", StringComparison.Ordinal) ||
               string.Equals(key, "abilities", StringComparison.Ordinal) ||
               string.Equals(key, "feats", StringComparison.Ordinal);
    }
    private int GetNpcMoreInfoPerLine(string key)
    {
        switch (key)
        {
            case "relation": return _showNpcMoreInfoRelationPerLine;
            case "vitals": return _showNpcMoreInfoVitalsPerLine;
            case "attributes": return _showNpcMoreInfoAttributesPerLine;
            case "buffs": return _showNpcMoreInfoBuffsPerLine;
            case "resists": return _showNpcMoreInfoResistsPerLine;
            case "skills": return _showNpcMoreInfoSkillsPerLine;
            case "abilities": return _showNpcMoreInfoAbilitiesPerLine;
            case "feats": return _showNpcMoreInfoFeatsPerLine;
            default: return 1;
        }
    }
    internal static int GetCurrentNpcMoreInfoPerLine(string key, int fallback)
    {
        var instance = Instance;
        return instance == null ? fallback : instance.GetNpcMoreInfoPerLine(key);
    }
    private void SetNpcMoreInfoPerLine(string key, int value)
    {
        value = Clamp(value, 1, 99);
        switch (key)
        {
            case "relation": _showNpcMoreInfoRelationPerLine = value; break;
            case "vitals": _showNpcMoreInfoVitalsPerLine = value; break;
            case "attributes": _showNpcMoreInfoAttributesPerLine = value; break;
            case "buffs": _showNpcMoreInfoBuffsPerLine = value; break;
            case "resists": _showNpcMoreInfoResistsPerLine = value; break;
            case "skills": _showNpcMoreInfoSkillsPerLine = value; break;
            case "abilities": _showNpcMoreInfoAbilitiesPerLine = value; break;
            case "feats": _showNpcMoreInfoFeatsPerLine = value; break;
            default: return;
        }
        InvalidateNpcMoreInfoCaches();
    }
    internal int GetNpcMoreInfoExtraFontSize(string key)
    {
        switch (key)
        {
            case "level": return _showNpcMoreInfoLevelExtraFontSize;
            case "identity": return _showNpcMoreInfoIdentityExtraFontSize;
            case "relation": return _showNpcMoreInfoRelationExtraFontSize;
            case "vitals": return _showNpcMoreInfoVitalsExtraFontSize;
            case "attributes": return _showNpcMoreInfoAttributesExtraFontSize;
            case "buffs": return _showNpcMoreInfoBuffsExtraFontSize;
            case "resists": return _showNpcMoreInfoResistsExtraFontSize;
            case "skills": return _showNpcMoreInfoSkillsExtraFontSize;
            case "abilities": return _showNpcMoreInfoAbilitiesExtraFontSize;
            case "feats": return _showNpcMoreInfoFeatsExtraFontSize;
            case "combat": return _showNpcMoreInfoCombatExtraFontSize;
            default: return 0;
        }
    }
    private void SetNpcMoreInfoExtraFontSize(string key, int value)
    {
        value = Clamp(value, -8, 8);
        switch (key)
        {
            case "level": _showNpcMoreInfoLevelExtraFontSize = value; break;
            case "identity": _showNpcMoreInfoIdentityExtraFontSize = value; break;
            case "relation": _showNpcMoreInfoRelationExtraFontSize = value; break;
            case "vitals": _showNpcMoreInfoVitalsExtraFontSize = value; break;
            case "attributes": _showNpcMoreInfoAttributesExtraFontSize = value; break;
            case "buffs": _showNpcMoreInfoBuffsExtraFontSize = value; break;
            case "resists": _showNpcMoreInfoResistsExtraFontSize = value; break;
            case "skills": _showNpcMoreInfoSkillsExtraFontSize = value; break;
            case "abilities": _showNpcMoreInfoAbilitiesExtraFontSize = value; break;
            case "feats": _showNpcMoreInfoFeatsExtraFontSize = value; break;
            case "combat": _showNpcMoreInfoCombatExtraFontSize = value; break;
            default: return;
        }
        InvalidateNpcMoreInfoCaches();
    }
    internal static bool ShouldPrefixNpcMoreInfoLevel()
    {
        var instance = Instance;
        if (instance == null || !instance._showNpcMoreInfoLevel)
            return false;
        var order = instance.GetNpcMoreInfoOrder();
        for (var i = 0; i < order.Length; i++)
        {
            if (!instance.IsNpcMoreInfoOrderItemEnabled(order[i]))
                continue;
            return string.Equals(order[i], "level", StringComparison.Ordinal);
        }
        return false;
    }
}
