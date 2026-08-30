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

public sealed partial class ElinModifierPlugin
{
    private string GetCachedUiValue(string key, Func<string> factory)
    {
        if (!_lowPerformanceMode)
            return factory();

        var frame = Time.frameCount;
        CachedUiValue cached;
        if (_lowPerformanceValueCache.TryGetValue(key, out cached))
        {
            if (cached.Frame == frame || frame - cached.Frame < LowPerformanceUiValueCacheFrames)
                return cached.Value;
        }

        var value = factory();
        _lowPerformanceValueCache[key] = new CachedUiValue(frame, value);
        return value;
    }
    private void InvalidateCachedUiValue(string key)
    {
        if (!string.IsNullOrEmpty(key))
            _lowPerformanceValueCache.Remove(key);
    }
    private void InvalidateCachedUiValues(string keyPrefix)
    {
        if (string.IsNullOrEmpty(keyPrefix) || _lowPerformanceValueCache.Count == 0)
            return;

        var keys = new List<string>();
        foreach (var key in _lowPerformanceValueCache.Keys)
            if (key.StartsWith(keyPrefix, StringComparison.Ordinal))
                keys.Add(key);
        for (var i = 0; i < keys.Count; i++)
            _lowPerformanceValueCache.Remove(keys[i]);
    }
    private void EnsureInput(string key, string value)
    {
        if (!_inputs.ContainsKey(key))
            _inputs[key] = value;
    }
    private bool PassAbilityFilter(AbilityDef ability, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        var f = filter.ToLowerInvariant();
        return ability.DisplayName.ToLowerInvariant().Contains(f) ||
               ability.Name.ToLowerInvariant().Contains(f) ||
               ability.Id.ToString().Contains(f) ||
               (ability.Alias ?? "").ToLowerInvariant().Contains(f) ||
               (ability.Category ?? "").ToLowerInvariant().Contains(f);
    }
    private string GetAbilityLabel(AbilityDef ability)
    {
        var label = ability.DisplayName;
        if (!string.IsNullOrEmpty(ability.Alias))
            label += " [" + ability.Alias + "]";
        else
            label += " [" + ability.Id + "]";
        return label;
    }
    private string GetAbilitySummary(Chara target, AbilityDef ability)
    {
        return T("等级", "Lv") + " " + GetAbilityLevel(target, ability) + " | " +
               T("成功率", "Chance") + " " + GetAbilityDisplayChance(target, ability) + "\n" +
               T("威力", "Power") + " " + GetAbilityDisplayPower(target, ability) + " | " +
               T("库存", "Stock") + " " + GetAbilityStock(target, ability);
    }
    private bool PassFilter(RowDef row, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        var f = filter.ToLowerInvariant();
        return row.Label.ToLowerInvariant().Contains(f) ||
               (row.Alias ?? "").ToLowerInvariant().Contains(f) ||
               row.Key.ToLowerInvariant().Contains(f);
    }
    private static bool CanEditPotential(RowDef row)
    {
        if (row.Kind != RowKind.Element)
            return false;
        if (row.Key == "79")
            return true;
        if (!int.TryParse(row.Key, out var id))
            return false;
        return id >= 70 && id <= 77;
    }
    private static string GetTargetCachePrefix(Chara target, bool isPc)
    {
        if (isPc)
            return "pc";
        try { return "npc:" + target.uid; }
        catch { return "npc"; }
    }
    private static string GetTargetInputPrefix(Chara target, bool isPc)
    {
        if (isPc)
            return "pc:";
        try { return "npc:" + target.uid.ToString(CultureInfo.InvariantCulture) + ":"; }
        catch { return "npc:"; }
    }
    internal string GetRowLabel(RowDef row)
    {
        if (_language == "ja")
            return GetJapaneseRowLabel(row);
        if (_language == "ru")
            return GetRussianRowLabel(row);
        if (_language != "en")
            return row.Label;
        if (row.Kind == RowKind.CardInt && row.Key == "HP") return "Life (Current HP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "mana") return "Mana (Current MP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "stamina") return "Vigor (Current SP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "SAN") return "Madness";
        if (row.Kind == RowKind.GeneSlot) return "Gene slots";
        if (row.Kind == RowKind.CharaIntProperty && row.Key == "feat")
            return string.Equals(row.Label, "专长点", StringComparison.Ordinal) ? "Feat points" : "FP";
        if (row.Kind == RowKind.PlayerField && row.Key == "karma") return "Karma";
        if (row.Kind == RowKind.PlayerField && row.Key == "fame") return "Fame";
        if (row.Kind == RowKind.ZoneInfluence) return "Influence";
        if (row.Kind != RowKind.Element)
            return row.Label;
        switch (row.Key)
        {
            case "60": return "Life";
            case "61": return "Mana";
            case "62": return "Vigor";
            case "79": return "Speed";
            case "70": return "Strength";
            case "71": return "Constitution";
            case "72": return "Dexterity";
            case "73": return "Perception";
            case "74": return "Learning";
            case "75": return "Will";
            case "76": return "Magic";
            case "77": return "Charisma";
            case "961": return "Fire Resistance";
            case "962": return "Cold Resistance";
            case "963": return "Lightning Resistance";
            case "964": return "Darkness Resistance";
            case "965": return "Mind Resistance";
            case "966": return "Poison Resistance";
            case "967": return "Nether Resistance";
            case "968": return "Sound Resistance";
            case "969": return "Nerve Resistance";
            case "970": return "Chaos Resistance";
            case "971": return "Magic Resistance";
            case "972": return "Ether Resistance";
            case "973": return "Acid Resistance";
            case "974": return "Cut Resistance";
            case "975": return "Rot Resistance";
            default: return row.Label;
        }
    }
    private static string GetJapaneseRowLabel(RowDef row)
    {
        if (row.Kind == RowKind.CardInt && row.Key == "HP") return "生命(現在HP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "mana") return "マナ(現在MP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "stamina") return "活力(現在SP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "SAN") return "狂気度";
        if (row.Kind == RowKind.GeneSlot) return "遺伝子スロット数";
        if (row.Kind == RowKind.CharaIntProperty && row.Key == "feat")
            return string.Equals(row.Label, "专长点", StringComparison.Ordinal) ? "特技ポイント" : "FP";
        if (row.Kind == RowKind.PlayerField && row.Key == "karma") return "カルマ";
        if (row.Kind == RowKind.PlayerField && row.Key == "fame") return "名声";
        if (row.Kind == RowKind.ZoneInfluence) return "影響力";
        if (row.Kind != RowKind.Element)
            return row.Label;
        switch (row.Key)
        {
            case "60": return "生命力";
            case "61": return "マナ";
            case "62": return "活力";
            case "79": return "速度";
            case "70": return "筋力";
            case "71": return "耐久";
            case "72": return "器用";
            case "73": return "感覚";
            case "74": return "学習";
            case "75": return "意志";
            case "76": return "魔力";
            case "77": return "魅力";
            case "961": return "火炎耐性";
            case "962": return "冷気耐性";
            case "963": return "電撃耐性";
            case "964": return "暗黒耐性";
            case "965": return "幻惑耐性";
            case "966": return "毒耐性";
            case "967": return "地獄耐性";
            case "968": return "音耐性";
            case "969": return "神経耐性";
            case "970": return "混沌耐性";
            case "971": return "魔法耐性";
            case "972": return "エーテル耐性";
            case "973": return "酸耐性";
            case "974": return "切断耐性";
            case "975": return "腐敗耐性";
            default: return row.Label;
        }
    }
    private static string GetRussianRowLabel(RowDef row)
    {
        if (row.Kind == RowKind.CardInt && row.Key == "HP") return "Жизнь (текущ. HP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "mana") return "Мана (текущ. MP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "stamina") return "Выносливость (текущ. SP)";
        if (row.Kind == RowKind.CharaStatProperty && row.Key == "SAN") return "Безумие";
        if (row.Kind == RowKind.GeneSlot) return "Слоты генов";
        if (row.Kind == RowKind.CharaIntProperty && row.Key == "feat")
            return string.Equals(row.Label, "专长点", StringComparison.Ordinal) ? "Очки талантов" : "FP";
        if (row.Kind == RowKind.PlayerField && row.Key == "karma") return "Карма";
        if (row.Kind == RowKind.PlayerField && row.Key == "fame") return "Слава";
        if (row.Kind == RowKind.ZoneInfluence) return "Влияние";
        if (row.Kind != RowKind.Element)
            return row.Label;
        switch (row.Key)
        {
            case "60": return "Жизненность";
            case "61": return "Мана";
            case "62": return "Выносливость";
            case "79": return "Скорость";
            case "70": return "Сила";
            case "71": return "Телосложение";
            case "72": return "Ловкость";
            case "73": return "Восприятие";
            case "74": return "Обучаемость";
            case "75": return "Воля";
            case "76": return "Магия";
            case "77": return "Харизма";
            case "961": return "Сопр. огню";
            case "962": return "Сопр. холоду";
            case "963": return "Сопр. молнии";
            case "964": return "Сопр. тьме";
            case "965": return "Сопр. разуму";
            case "966": return "Сопр. яду";
            case "967": return "Сопр. аду";
            case "968": return "Сопр. звуку";
            case "969": return "Сопр. нервам";
            case "970": return "Сопр. хаосу";
            case "971": return "Сопр. магии";
            case "972": return "Сопр. эфиру";
            case "973": return "Сопр. кислоте";
            case "974": return "Сопр. резанию";
            case "975": return "Сопр. гниению";
            default: return row.Label;
        }
    }
    private static bool CanLock(RowDef row)
    {
        return row.Kind == RowKind.CardInt && row.Key == "HP" ||
               row.Kind == RowKind.CharaStatProperty && (row.Key == "mana" || row.Key == "stamina");
    }
    private void ApplyLocks()
    {
        if (_locks.Count == 0) return;
        var pc = GetSafePc();
        if (pc == null) return;
        foreach (var row in _statusRows)
        {
            if (!CanLock(row)) continue;
            var inputKey = "pc:" + row.Kind + ":" + row.Key;
            if (!_locks.TryGetValue(inputKey, out var locked) || !locked) continue;
            if (!_inputs.TryGetValue(inputKey, out var text) || !int.TryParse(text, out var value)) continue;
            ApplyValueSilently(pc, row, value, true);
        }
    }
    private void ApplyValueSilently(Chara target, RowDef row, int value, bool isPc)
    {
        try
        {
            switch (row.Kind)
            {
                case RowKind.CharaStatProperty: SetCharaStatPropertyRaw(target, row.Key, value); break;
                case RowKind.CardInt: SetCardIntRaw(target, row.Key, value); break;
            }
        }
        catch { }
    }
    private string CurrentValue(Chara target, RowDef row, bool isPc)
    {
        try
        {
            switch (row.Kind)
            {
                case RowKind.Element:
                case RowKind.Feat: return GetElementValue(target, row.Key).ToString();
                case RowKind.StatObject: return GetStatObjectValue(target, row.Key, isPc);
                case RowKind.CharaStatProperty: return GetCharaStatPropertyValue(target, row.Key);
                case RowKind.CharaIntProperty: return GetCharaIntPropertyValue(target, row.Key);
                case RowKind.GeneSlot: return GetGeneSlotCount(target).ToString(CultureInfo.InvariantCulture);
                case RowKind.PlayerField: return GetPlayerField(row.Key);
                case RowKind.ZoneInfluence: return GetCurrentZoneInfluence();
                case RowKind.CardInt: return GetCardInt(target, row.Key).ToString();
                default: return "?";
            }
        }
        catch { return "?"; }
    }
    private string CurrentPotentialValue(Chara target, RowDef row)
    {
        try
        {
            var element = GetElement(target, row.Key);
            return (element == null ? 100 : element.Potential).ToString(CultureInfo.InvariantCulture);
        }
        catch { return "?"; }
    }
    private void ApplyValue(Chara target, RowDef row, int value, bool isPc)
    {
        switch (row.Kind)
        {
            case RowKind.Element: SetElement(target, row.Key, value, GetElementBasePotential(target, row.Key)); break;
            case RowKind.Feat: SetFeatElement(target, row.Key, value); break;
            case RowKind.StatObject: SetStatObject(target, row.Key, value, isPc); break;
            case RowKind.CharaStatProperty: SetCharaStatProperty(target, row.Key, value); break;
            case RowKind.CharaIntProperty: SetCharaIntProperty(target, row.Key, value); break;
            case RowKind.GeneSlot: SetGeneSlotCount(target, value); break;
            case RowKind.PlayerField: SetPlayerField(row.Key, value); break;
            case RowKind.ZoneInfluence: SetCurrentZoneInfluence(value); break;
            case RowKind.CardInt: SetCardInt(target, row.Key, value); break;
        }
    }
}
