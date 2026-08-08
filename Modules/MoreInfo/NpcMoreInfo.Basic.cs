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
    private static string BuildNpcMoreInfoHoverDetailsUncached(Chara chara)
    {
        var lines = new List<string>();
        GetNpcMoreInfoSlowLines(chara, out var identityCore, out var attributes, out var resists, out var skills, out var abilities, out var feats);
        var instance = ElinModifierPlugin.ActiveInstance;
        var order = instance == null ? NpcMoreInfoOrderKeys : instance.GetNpcMoreInfoOrder();
        var levelIsPrefixed = ShouldPrefixNpcMoreInfoLevel();
        for (var i = 0; i < order.Length; i++)
        {
            switch (order[i])
            {
                case "level":
                    if (ShouldShowNpcMoreInfoLevel() && !levelIsPrefixed)
                    {
                        var level = SafeInt(() => chara.LV, 0).ToString(CultureInfo.InvariantCulture);
                        AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("level", ColorNpcMoreInfoText("Lv." + level, NpcMoreInfoLevelColor)));
                    }
                    break;
                case "identity":
                    if (ShouldShowNpcMoreInfoIdentity())
                        AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("identity", ColorNpcMoreInfoText(BuildNpcMoreInfoIdentityLine(chara, identityCore), NpcMoreInfoIdentityColor)));
                    break;
                case "relation":
                    if (ShouldShowNpcMoreInfoRelationFaith())
                        AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("relation", ColorNpcMoreInfoText(BuildNpcMoreInfoRelationFaithLine(chara), NpcMoreInfoRelationColor)));
                    break;
                case "vitals":
                    if (ShouldShowNpcMoreInfoVitals())
                        AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("vitals", BuildNpcMoreInfoVitalsLine(chara)));
                    break;
                case "attributes": AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("attributes", attributes)); break;
                case "buffs": AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("buffs", BuildNpcMoreInfoBuffLine(chara))); break;
                case "resists": AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("resists", resists)); break;
                case "skills": AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("skills", skills)); break;
                case "abilities": AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("abilities", abilities)); break;
                case "feats": AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("feats", feats)); break;
                case "combat":
                    if (ShouldShowNpcMoreInfoCombatSimulation())
                        AddNpcMoreInfoLine(lines, ApplyNpcMoreInfoExtraFontSize("combat", BuildNpcMoreInfoCombatEstimateLine(chara)));
                    break;
            }
        }

        if (lines.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.Append(Environment.NewLine);
        sb.Append("<size=").Append(GetNpcMoreInfoFontSize().ToString(CultureInfo.InvariantCulture)).Append('>');
        sb.Append(string.Join(Environment.NewLine, lines.ToArray()));
        sb.Append("</size>");
        return sb.ToString();
    }
    private static int GetNpcMoreInfoFontSize()
    {
        return Clamp(14 + (ElinModifierPlugin.ActiveInstance?._showNpcMoreInfoFontSizeOffset ?? 0), 6, 22);
    }
    internal static string ApplyNpcMoreInfoExtraFontSize(string key, string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var extra = ElinModifierPlugin.ActiveInstance?.GetNpcMoreInfoExtraFontSize(key) ?? (string.Equals(key, "vitals", StringComparison.Ordinal) ? 4 : 0);
        var size = Clamp(GetNpcMoreInfoFontSize() + extra, 1, 30);
        return "<size=" + size.ToString(CultureInfo.InvariantCulture) + ">" + text + "</size>";
    }
    private static void GetNpcMoreInfoSlowLines(Chara chara, out string identityCore, out string attributes, out string resists, out string skills, out string abilities, out string feats)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (instance == null)
        {
            identityCore = ShouldShowNpcMoreInfoIdentity() ? BuildNpcMoreInfoIdentityCoreLine(chara) : "";
            attributes = ShouldShowNpcMoreInfoAttributes() ? BuildNpcMoreInfoAttributesLine(chara) : "";
            resists = ShouldShowNpcMoreInfoResists() ? BuildNpcMoreInfoResistLine(chara) : "";
            skills = ShouldShowNpcMoreInfoSkills() ? BuildNpcMoreInfoSkillLine(chara) : "";
            abilities = ShouldShowNpcMoreInfoAbilities() ? BuildNpcMoreInfoAbilityLine(chara) : "";
            feats = ShouldShowNpcMoreInfoFeats() ? BuildNpcMoreInfoFeatLine(chara) : "";
            return;
        }

        var now = instance.SchedulerNow;
        var map = SafeObject(() => GameAccess.World.CurrentMap) as Map;
        var uid = GetCharaUid(chara);
        var mask = GetNpcMoreInfoDisplayMask(instance) & ((1 << 0) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5) | (1 << 6));
        var language = instance._language ?? "";
        var interval = instance._lowPerformanceMode ? 1.5f : 0.75f;
        var cacheValid = ReferenceEquals(instance._npcMoreInfoSlowCacheMap, map) &&
                         ReferenceEquals(instance._npcMoreInfoSlowCacheTarget, chara) &&
                         instance._npcMoreInfoSlowCacheUid == uid &&
                         instance._npcMoreInfoSlowCacheMask == mask &&
                         string.Equals(instance._npcMoreInfoSlowCacheLanguage, language, StringComparison.Ordinal) &&
                         now >= instance._npcMoreInfoSlowCacheTime &&
                         now - instance._npcMoreInfoSlowCacheTime < interval;
        if (!cacheValid)
        {
            instance._npcMoreInfoSlowIdentityCore = ShouldShowNpcMoreInfoIdentity() ? BuildNpcMoreInfoIdentityCoreLine(chara) : "";
            instance._npcMoreInfoSlowAttributes = ShouldShowNpcMoreInfoAttributes() ? BuildNpcMoreInfoAttributesLine(chara) : "";
            instance._npcMoreInfoSlowResists = ShouldShowNpcMoreInfoResists() ? BuildNpcMoreInfoResistLine(chara) : "";
            instance._npcMoreInfoSlowSkills = ShouldShowNpcMoreInfoSkills() ? BuildNpcMoreInfoSkillLine(chara) : "";
            instance._npcMoreInfoSlowAbilities = ShouldShowNpcMoreInfoAbilities() ? BuildNpcMoreInfoAbilityLine(chara) : "";
            instance._npcMoreInfoSlowFeats = ShouldShowNpcMoreInfoFeats() ? BuildNpcMoreInfoFeatLine(chara) : "";
            instance._npcMoreInfoSlowCacheMap = map;
            instance._npcMoreInfoSlowCacheTarget = chara;
            instance._npcMoreInfoSlowCacheUid = uid;
            instance._npcMoreInfoSlowCacheMask = mask;
            instance._npcMoreInfoSlowCacheLanguage = language;
            instance._npcMoreInfoSlowCacheTime = now;
        }

        identityCore = instance._npcMoreInfoSlowIdentityCore;
        attributes = instance._npcMoreInfoSlowAttributes;
        resists = instance._npcMoreInfoSlowResists;
        skills = instance._npcMoreInfoSlowSkills;
        abilities = instance._npcMoreInfoSlowAbilities;
        feats = instance._npcMoreInfoSlowFeats;
    }
    private static int GetNpcMoreInfoDisplayMask(ElinModifierPlugin instance)
    {
        var mask = 0;
        if (instance._showNpcMoreInfoIdentity) mask |= 1 << 0;
        if (instance._showNpcMoreInfoVitals) mask |= 1 << 1;
        if (instance._showNpcMoreInfoAttributes) mask |= 1 << 2;
        if (instance._showNpcMoreInfoResists) mask |= 1 << 3;
        if (instance._showNpcMoreInfoSkills) mask |= 1 << 4;
        if (instance._showNpcMoreInfoAbilities) mask |= 1 << 5;
        if (instance._showNpcMoreInfoFeats) mask |= 1 << 6;
        if (instance._showNpcMoreInfoCombatSimulation) mask |= 1 << 7;
        if (instance._showNpcMoreInfoBuffs) mask |= 1 << 13;
        if (instance._showNpcMoreInfoLevel) mask |= 1 << 14;
        if (instance._showNpcMoreInfoRelationFaith) mask |= 1 << 15;
        mask |= (Clamp(instance._showNpcMoreInfoFontSizeOffset, -8, 8) + 8) << 8;
        return mask;
    }
    internal void InvalidateNpcMoreInfoCaches(bool clearResistDefinitions = false)
    {
        _host._npcMoreInfoHoverCacheMap = null;
        _host._npcMoreInfoHoverCacheTarget = null;
        _host._npcMoreInfoHoverCacheUid = -1;
        _host._npcMoreInfoHoverCacheMask = -1;
        _host._npcMoreInfoHoverCacheLanguage = "";
        _host._npcMoreInfoHoverCacheFrame = -1;
        _host._npcMoreInfoHoverCacheTime = -9999f;
        _host._npcMoreInfoHoverCacheValue = "";

        _host._npcMoreInfoSlowCacheMap = null;
        _host._npcMoreInfoSlowCacheTarget = null;
        _host._npcMoreInfoSlowCacheUid = -1;
        _host._npcMoreInfoSlowCacheMask = -1;
        _host._npcMoreInfoSlowCacheLanguage = "";
        _host._npcMoreInfoSlowCacheTime = -9999f;
        _host._npcMoreInfoSlowIdentityCore = "";
        _host._npcMoreInfoSlowAttributes = "";
        _host._npcMoreInfoSlowResists = "";
        _host._npcMoreInfoSlowSkills = "";
        _host._npcMoreInfoSlowAbilities = "";
        _host._npcMoreInfoSlowFeats = "";

        _host._npcMoreInfoCombatCacheMap = null;
        _host._npcMoreInfoCombatCachePc = null;
        _host._npcMoreInfoCombatCacheNpc = null;
        _host._npcMoreInfoCombatCachePcUid = -1;
        _host._npcMoreInfoCombatCacheNpcUid = -1;
        _host._npcMoreInfoCombatCacheFingerprint = 0UL;
        _host._npcMoreInfoCombatCacheDynamicFingerprint = 0UL;
        _host._npcMoreInfoCombatCacheEstimatesValid = false;
        _host._npcMoreInfoCombatCacheKillEstimate = default;
        _host._npcMoreInfoCombatCacheDeathEstimate = default;
        _host._npcMoreInfoCombatCacheCheckTime = -9999f;
        _host._npcMoreInfoCombatCacheFullTime = -9999f;
        _host._npcMoreInfoCombatCacheValue = "";

        if (clearResistDefinitions)
        {
            _host._npcMoreInfoResistDefinitionSource = null;
            _host._npcMoreInfoResistDefinitionLanguage = "";
            _host._npcMoreInfoResistDefinitions = null;
        }
    }
    private static void AddNpcMoreInfoLine(List<string> lines, string line)
    {
        if (!string.IsNullOrEmpty(line))
            lines.Add(line);
    }
    private static string BuildNpcMoreInfoIdentityCoreLine(Chara chara)
    {
        var parts = new List<string>();
        parts.Add(GetNpcMoreInfoGender(chara));
        parts.Add(GetNpcMoreInfoAge(chara));
        parts.Add(SafeText(() => chara.race.GetName(), "?"));
        parts.Add(SafeText(() => chara.job.GetName(), "?"));
        parts.Add(GetNpcMoreInfoAutoCombatTypeText(chara));
        parts.Add(GetNpcMoreInfoAttackStyle(chara));
        parts.Add(GetNpcMoreInfoArmorStyle(chara));
        return JoinNpcMoreInfoParts(parts);
    }
    private static string BuildNpcMoreInfoIdentityLine(Chara chara, string identityCore)
    {
        var parts = new List<string>();
        parts.Add(identityCore);
        parts.Add(GetNpcMoreInfoCarryWeightText(chara));
        parts.Add(GetNpcMoreInfoHungerText(chara));
        return JoinNpcMoreInfoParts(parts);
    }
    private static string BuildNpcMoreInfoRelationFaithLine(Chara chara)
    {
        var affinity = SafeInt(() => chara._affinity, 0).ToString(CultureInfo.InvariantCulture);
        var relationship = GetNpcMoreInfoRelationshipText(chara);
        var faith = GetNpcMoreInfoFaithText(chara);
        var hobbies = GetNpcMoreInfoHobbyText(chara);
        var works = GetNpcMoreInfoWorkText(chara);
        var favorites = GetNpcMoreInfoFavoriteText(chara);
        var entries = new List<string>
        {
            Tr("好感度", "Affinity") + ":" + affinity,
            Tr("关系", "Relation") + ":" + relationship,
            Tr("信仰", "Faith") + ":" + faith,
            Tr("工作", "Work") + ":" + works,
            Tr("爱好", "Hobbies") + ":" + hobbies,
            Tr("喜欢的东西", "Favorite things") + ":" + favorites
        };
        return BuildNpcMoreInfoEntryLines("", entries,
            GetCurrentNpcMoreInfoPerLine("relation", 3), " ");
    }
    private static string GetNpcMoreInfoHobbyText(Chara chara)
    {
        return GetNpcMoreInfoHobbyNames(chara, false);
    }
    private static string GetNpcMoreInfoWorkText(Chara chara)
    {
        return GetNpcMoreInfoHobbyNames(chara, true);
    }
    private static string GetNpcMoreInfoHobbyNames(Chara chara, bool works)
    {
        try
        {
            var entries = works ? chara.ListWorks() : chara.ListHobbies();
            if (entries == null || entries.Count == 0)
                return "-";

            var names = new List<string>();
            for (var i = 0; i < entries.Count; i++)
            {
                var name = SafeText(() => entries[i]?.Name ?? "", "").Trim();
                if (name.Length > 0 && !names.Contains(name))
                    names.Add(name);
            }
            return names.Count == 0 ? "-" : string.Join(GetNpcMoreInfoListSeparator(), names.ToArray());
        }
        catch
        {
            return "?";
        }
    }
    private static string GetNpcMoreInfoFavoriteText(Chara chara)
    {
        try
        {
            if (!chara.knowFav)
                return "-";
            var category = SafeText(() => chara.GetFavCat()?.GetName() ?? "", "").Trim();
            var food = SafeText(() => chara.GetFavFood()?.GetName() ?? "", "").Trim();
            if (category.Length == 0)
                return food.Length == 0 ? "-" : food;
            if (food.Length == 0 || string.Equals(category, food, StringComparison.Ordinal))
                return category;
            return category + GetNpcMoreInfoListSeparator() + food;
        }
        catch
        {
            return "?";
        }
    }
    private static string GetNpcMoreInfoListSeparator()
    {
        var language = ElinModifierPlugin.ActiveInstance?._language ?? "zh";
        return language == "zh" || language == "ja" ? "、" : ", ";
    }
    private static string GetNpcMoreInfoRelationshipText(Chara chara)
    {
        Hostility hostility;
        try { hostility = chara.hostility; }
        catch { return "?"; }
        switch (hostility)
        {
            case Hostility.Enemy: return Tr("敌对", "Enemy");
            case Hostility.Neutral: return Tr("中立", "Neutral");
            case Hostility.Friend: return Tr("友好", "Friend");
            case Hostility.Ally: return Tr("盟友", "Ally");
            default: return hostility.ToString();
        }
    }
    private static string GetNpcMoreInfoFaithText(Chara chara)
    {
        try
        {
            var name = SafeText(() => chara.faith?.Name, "");
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            var id = SafeText(() => chara.idFaith, "");
            if (string.IsNullOrWhiteSpace(id))
                return "-";
            var religion = TryFindReligion(id);
            name = religion == null ? "" : SafeText(() => religion.Name, "");
            return string.IsNullOrWhiteSpace(name) ? id : name;
        }
        catch
        {
            return "?";
        }
    }
    private static string GetNpcMoreInfoCarryWeightText(Chara chara)
    {
        try
        {
            var current = SafeInt(() => chara.ChildrenWeight, 0) / 1000f;
            var max = SafeInt(() => chara.WeightLimit, 0) / 1000f;
            return Tr("负重", "Carry") + ":" +
                   current.ToString("F1", CultureInfo.InvariantCulture) + "/" +
                   max.ToString("F1", CultureInfo.InvariantCulture);
        }
        catch
        {
            return Tr("负重", "Carry") + ":?";
        }
    }
    private static string GetNpcMoreInfoAutoCombatTypeText(Chara chara)
    {
        try
        {
            var source = chara.tactics?.source;
            var name = source == null ? "" : SafeText(() => source.GetName(), source.id ?? "");
            if (string.IsNullOrWhiteSpace(name))
                name = "?";
            return name;
        }
        catch
        {
            return "?";
        }
    }
    private static string GetNpcMoreInfoHungerText(Chara chara)
    {
        try
        {
            var hunger = chara.hunger;
            var current = SafeInt(() => hunger.value, 0);
            var max = SafeInt(() => hunger.max, 100);
            return Tr("饱食度", "Satiety") + ":" +
                   current.ToString(CultureInfo.InvariantCulture) + "/" +
                   max.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return Tr("饱食度", "Satiety") + ":?";
        }
    }
    private static string BuildNpcMoreInfoVitalsLine(Chara chara)
    {
        var entries = new List<string>
        {
            ColorNpcMoreInfoText("HP", NpcMoreInfoHpColor) + ":" + SafeInt(() => chara.hp, 0).ToString(CultureInfo.InvariantCulture) + "/" +
                SafeInt(() => chara.MaxHP, 0).ToString(CultureInfo.InvariantCulture),
            ColorNpcMoreInfoText("MP", NpcMoreInfoMpColor) + ":" + SafeInt(() => chara.mana.value, 0).ToString(CultureInfo.InvariantCulture) + "/" +
                SafeInt(() => chara.mana.max, 0).ToString(CultureInfo.InvariantCulture),
            ColorNpcMoreInfoText("SP", NpcMoreInfoSpColor) + ":" + SafeInt(() => chara.stamina.value, 0).ToString(CultureInfo.InvariantCulture) + "/" +
                SafeInt(() => chara.stamina.max, 0).ToString(CultureInfo.InvariantCulture),
            ColorNpcMoreInfoText("EXP", NpcMoreInfoExpColor) + ":" + FormatCompactCount(SafeInt(() => chara.exp, 0)) + "/" +
                FormatCompactCount(SafeInt(() => chara.ExpToNext, 0)),
            ColorNpcMoreInfoText(Tr("速度", "Speed"), NpcMoreInfoSpeedColor) + ":" + SafeInt(() => chara.Speed, 0).ToString(CultureInfo.InvariantCulture),
            ColorNpcMoreInfoText("DV", NpcMoreInfoDvColor) + ":" + FormatCompactCount(SafeInt(() => chara.DV, 0)),
            ColorNpcMoreInfoText("PV", NpcMoreInfoPvColor) + ":" + FormatCompactCount(SafeInt(() => chara.PV, 0))
        };
        return BuildNpcMoreInfoEntryLines("", entries, GetCurrentNpcMoreInfoPerLine("vitals", 4), " ");
    }
    private static string BuildNpcMoreInfoAttributesLine(Chara chara)
    {
        var sb = new StringBuilder();
        var maxPerLine = GetCurrentNpcMoreInfoPerLine("attributes", 4);
        for (var i = 0; i < NpcMoreInfoAttributeIds.Length; i++)
        {
            if (sb.Length > 0)
                sb.Append(i % maxPerLine == 0 ? Environment.NewLine : " ");
            var label = Tr(NpcMoreInfoAttributeLabelsZh[i], NpcMoreInfoAttributeLabelsEn[i]);
            sb.Append(ColorNpcMoreInfoText(label, NpcMoreInfoAttributeColor))
              .Append(':')
              .Append(FormatCompactCount(SafeInt(() => chara.Evalue(NpcMoreInfoAttributeIds[i]), 0)));
        }
        return sb.ToString();
    }
    private static string BuildNpcMoreInfoBuffLine(Chara chara)
    {
        if (!ShouldShowNpcMoreInfoBuffs() || chara == null)
            return "";

        var entries = new List<string>();
        try
        {
            IEnumerable<BaseStats> stats = chara.conditions.Concat(
                !chara.IsPCFaction
                    ? Array.Empty<BaseStats>()
                    : new BaseStats[2] { chara.hunger, chara.stamina });
            foreach (var item in stats)
            {
                if (item == null || item is ConBaseTransmuteMimic)
                    continue;

                var name = SafeText(() => item.GetPhaseStr(), "");
                if (string.IsNullOrEmpty(name) || name == "#")
                    continue;

                var details = new List<string>(2);
                try
                {
                    if (item is ConBuffStats buffStats)
                    {
                        var value = buffStats.CalcValue();
                        if (buffStats.IsDebuff)
                            value = -value;
                        details.Add("L:" + FormatCompactCount(value));
                    }
                    else if (item is Condition condition && condition.power != 0)
                    {
                        details.Add("L:" + FormatCompactCount(condition.power));
                    }
                }
                catch { }

                try
                {
                    if (item is Condition condition && condition.HasDuration)
                    {
                        var duration = condition.TextDuration;
                        if (!string.IsNullOrWhiteSpace(duration))
                            details.Add("T:" + FormatCompactNumericText(duration));
                    }
                }
                catch { }

                var entry = ColorNpcMoreInfoText(name, NpcMoreInfoBuffColor);
                if (details.Count > 0)
                    entry += "(" + string.Join(" ", details.ToArray()) + ")";
                entries.Add(entry);
            }
        }
        catch { }
        return BuildNpcMoreInfoEntryLines("", entries, GetCurrentNpcMoreInfoPerLine("buffs", 5));
    }
    private static string BuildOriginalNpcMoreInfoBuffLine(Chara chara)
    {
        if (chara == null)
            return "";

        try
        {
            IEnumerable<BaseStats> stats = chara.conditions.Concat(
                !chara.IsPCFaction
                    ? Array.Empty<BaseStats>()
                    : new BaseStats[2] { chara.hunger, chara.stamina });
            var sb = new StringBuilder();
            var count = 0;
            foreach (var item in stats)
            {
                if (item == null || item is ConBaseTransmuteMimic)
                    continue;

                var text = item.GetPhaseStr();
                if (string.IsNullOrEmpty(text) || text == "#")
                    continue;

                var color = Color.white;
                switch (item.source.group)
                {
                    case "Bad":
                    case "Debuff":
                    case "Disease":
                        color = GameAccess.Ui.Colors.colorDebuff;
                        break;
                    case "Buff":
                        color = GameAccess.Ui.Colors.colorBuff;
                        break;
                }

                if (GameAccess.Runtime.Debug.showExtra)
                {
                    text += "(" + item.GetValue().ToString(CultureInfo.InvariantCulture) + ")";
                    if (chara.resistCon != null && chara.resistCon.ContainsKey(item.id))
                        text += "{" + chara.resistCon[item.id].ToString(CultureInfo.InvariantCulture) + "}";
                }

                count++;
                sb.Append(text.TagColor(color)).Append(", ");
            }

            if (count == 0)
                return "";
            return Environment.NewLine + "<size=14>" + sb.ToString().TrimEnd(", ".ToCharArray()) + "</size>";
        }
        catch
        {
            return "";
        }
    }
    internal static string RemoveOriginalNpcMoreInfoBuffLine(Chara chara, string text)
    {
        var result = text ?? "";
        var original = BuildOriginalNpcMoreInfoBuffLine(chara);
        if (string.IsNullOrEmpty(original))
            return result;

        var index = result.LastIndexOf(original, StringComparison.Ordinal);
        return index < 0 ? result : result.Remove(index, original.Length);
    }
    internal static string RemoveOriginalNpcMoreInfoFavoriteLine(Chara chara, string text)
    {
        var result = text ?? "";
        try
        {
            if (chara == null || !chara.knowFav)
                return result;

            var category = chara.GetFavCat()?.GetName() ?? "";
            var food = chara.GetFavFood()?.GetName() ?? "";
            var original = Environment.NewLine + "<size=14>" +
                           "favgift".lang(category.ToLower(), food) + "</size>";
            var index = result.IndexOf(original, StringComparison.Ordinal);
            if (index >= 0)
                return result.Remove(index, original.Length);

            if (category.Length == 0 && food.Length == 0)
                return result;
            var lines = result.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.IndexOf("<size=14>", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (category.Length > 0 && line.IndexOf(category, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (food.Length > 0 && line.IndexOf(food, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                lines.RemoveAt(i);
                return string.Join(Environment.NewLine, lines.ToArray());
            }
        }
        catch { }
        return result;
    }
    private static string BuildNpcMoreInfoResistLine(Chara chara)
    {
        var entries = new List<string>();
        try
        {
            var definitions = GetNpcMoreInfoResistDefinitions();
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                var value = SafeInt(() => chara.Evalue(definition.Id), 0);
                if (value == 0)
                    continue;

                entries.Add(ColorNpcMoreInfoText(definition.Name, NpcMoreInfoResistColor) + ":" + GetNpcMoreInfoResistRank(value) + "(" +
                            FormatCompactCount(value) + ")");
            }
        }
        catch { }
        return BuildNpcMoreInfoEntryLines("", entries, GetCurrentNpcMoreInfoPerLine("resists", 5));
    }
    private static List<NpcMoreInfoResistDefinition> GetNpcMoreInfoResistDefinitions()
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        object? source = null;
        try { source = GameAccess.Sources.Elements; }
        catch { }
        var language = instance?._language ?? "";
        if (instance != null &&
            instance._npcMoreInfoResistDefinitions != null &&
            ReferenceEquals(instance._npcMoreInfoResistDefinitionSource, source) &&
            string.Equals(instance._npcMoreInfoResistDefinitionLanguage, language, StringComparison.Ordinal))
        {
            return instance._npcMoreInfoResistDefinitions;
        }

        var result = new List<NpcMoreInfoResistDefinition>();
        var seen = new HashSet<int>();
        try
        {
            foreach (var row in EnumerateSourceElementRows())
            {
                if (row == null || !string.Equals(GetString(row, "category"), "resist", StringComparison.OrdinalIgnoreCase))
                    continue;
                var id = GetInt(row, "id");
                if (id <= 0 || !seen.Add(id))
                    continue;
                var name = GetNativeElementDisplayName(row);
                if (string.IsNullOrEmpty(name))
                    name = id.ToString(CultureInfo.InvariantCulture);
                result.Add(new NpcMoreInfoResistDefinition(id, name));
            }
        }
        catch { }

        if (instance != null)
        {
            instance._npcMoreInfoResistDefinitionSource = source;
            instance._npcMoreInfoResistDefinitionLanguage = language;
            instance._npcMoreInfoResistDefinitions = result;
        }
        return result;
    }
    private static string BuildNpcMoreInfoAbilityLine(Chara chara)
    {
        var entries = new List<string>();
        var seen = new HashSet<int>();

        try
        {
            var items = chara.ability?.list?.items;
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null || item.act == null)
                        continue;
                    var act = item.act;
                    var id = SafeInt(() => act.id, 0);
                    if (id > 0 && !seen.Add(id))
                        continue;

                    var name = GetNpcMoreInfoAbilityName(act);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var level = GetNpcMoreInfoAbilityLevel(chara, act);
                    var chance = SafeInt(() => item.chance, 0);
                    if (chance <= 0)
                        chance = SafeInt(() => act.source.chance, 0);

                    entries.Add(ColorNpcMoreInfoText(name, NpcMoreInfoAbilityColor) + "(L:" + FormatCompactCount(level) + " " +
                                "P:" + FormatCompactCount(chance) + "%)");
                }
            }
        }
        catch { }

        if (entries.Count == 0)
            return "";
        return BuildNpcMoreInfoEntryLines("", entries, GetCurrentNpcMoreInfoPerLine("abilities", 5));
    }
    private static string BuildNpcMoreInfoSkillLine(Chara chara)
    {
        return BuildNpcMoreInfoElementListLine(chara, false);
    }
    private static string BuildNpcMoreInfoFeatLine(Chara chara)
    {
        return BuildNpcMoreInfoElementListLine(chara, true);
    }
    private static string BuildNpcMoreInfoElementListLine(Chara chara, bool feats)
    {
        var instance = ElinModifierPlugin.ActiveInstance;
        if (instance == null || chara == null)
            return "";
        var entries = new List<string>();
        try
        {
            instance.EnsureGameRows();
            var rows = feats ? instance._featRows : instance._skillRows;
            if (rows == null)
                return "";
            var elements = GetElements(chara);
            foreach (var row in rows)
            {
                if (!int.TryParse(row.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    continue;
                Element element = null;
                try { element = elements.GetElement(id); }
                catch { }
                if (element == null)
                    continue;
                var value = SafeInt(() => element.ValueWithoutLink, 0);
                if (value == 0)
                    continue;
                var color = feats ? NpcMoreInfoFeatColor : NpcMoreInfoSkillColor;
                entries.Add(ColorNpcMoreInfoText(instance.GetRowLabel(row), color) + "(" + FormatCompactCount(value) + ")");
            }
        }
        catch { }
        return BuildNpcMoreInfoEntryLines("", entries,
            GetCurrentNpcMoreInfoPerLine(feats ? "feats" : "skills", 5));
    }
    private static string BuildNpcMoreInfoEntryLines(string label, List<string> entries, int maxPerLine, string? separatorOverride = null)
    {
        if (entries == null || entries.Count == 0)
            return "";
        maxPerLine = Math.Max(1, maxPerLine);
        var separator = separatorOverride ?? Tr("、", ", ");
        var sb = new StringBuilder();
        for (var start = 0; start < entries.Count; start += maxPerLine)
        {
            if (sb.Length > 0)
                sb.Append(Environment.NewLine);
            if (!string.IsNullOrEmpty(label))
                sb.Append(label).Append(':');
            var end = Math.Min(entries.Count, start + maxPerLine);
            for (var i = start; i < end; i++)
            {
                if (i > start)
                    sb.Append(separator);
                sb.Append(entries[i]);
            }
        }
        return sb.ToString();
    }
}
