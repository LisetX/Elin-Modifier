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
    private static void AppendAiInventoryThingLine(StringBuilder sb, Thing thing, int index)
    {
        sb.Append(index.ToString(CultureInfo.InvariantCulture)).Append(". ").Append(FormatAiInventoryThing(thing));
    }
    private IEnumerable<AiNameEntry> BuildAiItemNameEntries()
    {
        EnsureItemRows();
        foreach (var item in _itemRows)
        {
            if (item == null) continue;
            var extra = item.VariantIndex >= 0
                ? "variant=" + item.VariantIndex.ToString(CultureInfo.InvariantCulture) + ", skin=" + item.SkinId.ToString(CultureInfo.InvariantCulture)
                : "";
            yield return new AiNameEntry("item", item.Id, item.DisplayName, item.Name, "", extra);
        }
    }
    private IEnumerable<AiNameEntry> BuildAiNpcNameEntries()
    {
        EnsureNpcRows();
        foreach (var npc in _npcRows)
        {
            if (npc == null) continue;
            var extra = "race=" + npc.Race + ", job=" + npc.Job;
            yield return new AiNameEntry("npc", npc.Id, npc.DisplayName, npc.Name, "", extra);
        }
    }
    private IEnumerable<AiNameEntry> BuildAiFaithNameEntries()
    {
        EnsureFaithRows();
        var rows = _faithRows ?? new List<FaithDef>();
        foreach (var faith in rows)
        {
            if (faith == null) continue;
            yield return new AiNameEntry("religion", faith.Id, faith.DisplayName, faith.Name, "faith", "");
        }
    }
    private IEnumerable<AiNameEntry> BuildAiRowNameEntries(string category)
    {
        EnsureGameRows();
        List<RowDef> rows;
        if (category == "trait") rows = _traitRows;
        else if (category == "feat") rows = _featRows;
        else rows = _skillRows;
        foreach (var row in rows)
        {
            if (row == null) continue;
            yield return new AiNameEntry(category, row.Key, GetRowLabel(row), row.Alias, row.Category, "");
        }
    }
    private IEnumerable<AiNameEntry> BuildAiAbilityNameEntries()
    {
        EnsureAbilityRows();
        foreach (var ability in _abilityRows)
        {
            if (ability == null) continue;
            yield return new AiNameEntry("spell", ability.Id.ToString(CultureInfo.InvariantCulture), ability.DisplayName, ability.Alias, ability.Category, ability.Name);
        }
    }
    private IEnumerable<AiNameEntry> BuildAiEnchantNameEntries()
    {
        var seen = new HashSet<int>();
        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            if (id <= 0 || !seen.Add(id))
                continue;
            var name = GetElementDisplayName(row);
            if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                name = GetString(row, "alias");
            if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                continue;
            var alias = GetString(row, "alias");
            var category = GetString(row, "category");
            if (string.IsNullOrEmpty(category)) category = GetString(row, "group");
            if (IsLikelyEnchantElement(row))
                yield return new AiNameEntry("enchantment", id.ToString(CultureInfo.InvariantCulture), name, alias, category, BuildAiElementExtra(row));
        }
    }
    private static bool IsLikelyEnchantElement(object row)
    {
        if (row == null)
            return false;
        try
        {
            if (GetBool(row, "IsWeaponEnc") || GetBool(row, "IsShieldEnc"))
                return true;
        }
        catch { }

        var text = (GetString(row, "alias") + "," +
                    GetString(row, "name") + "," +
                    GetString(row, "name_JP") + "," +
                    GetString(row, "category") + "," +
                    GetString(row, "categorySub") + "," +
                    GetString(row, "group") + "," +
                    GetString(row, "type") + "," +
                    string.Join(",", GetStringArray(row, "tag")) + "," +
                    string.Join(",", GetStringArray(row, "textExtra")) + "," +
                    string.Join(",", GetStringArray(row, "textExtra_JP")) + "," +
                    string.Join(",", GetStringArray(row, "adjective")) + "," +
                    string.Join(",", GetStringArray(row, "adjective_JP")) + "," +
                    string.Join(",", GetStringArray(row, "textAlt"))).ToLowerInvariant();
        return TextHas(text, "enc") || TextHas(text, "enchant") || TextHas(text, "weapon") ||
               TextHas(text, "shield") || TextHas(text, "food") || TextHas(text, "meal") ||
               TextHas(text, "trait") || TextHas(text, "bonus") || TextHas(text, "attribute") ||
               TextHas(text, "element") || TextHas(text, "damage") || TextHas(text, "resist") ||
               TextHas(text, "nutrition") || Array.IndexOf(FixedAttributeEffectIds, GetInt(row, "id")) >= 0 ||
               GetInt(row, "id") == FoodNutritionElementId;
    }
    private static string BuildAiElementExtra(object row)
    {
        var parts = new List<string>();
        var type = GetString(row, "type");
        var group = GetString(row, "group");
        var categorySub = GetString(row, "categorySub");
        if (!string.IsNullOrEmpty(type)) parts.Add("type=" + type);
        if (!string.IsNullOrEmpty(group)) parts.Add("group=" + group);
        if (!string.IsNullOrEmpty(categorySub)) parts.Add("sub=" + categorySub);
        return string.Join(", ", parts.ToArray());
    }
    private static bool AiNameEntryMatchesFilter(AiNameEntry entry, string filter)
    {
        if (entry == null)
            return false;
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        var needle = NormalizeAiKey(filter);
        foreach (var value in new[] { entry.Kind, entry.Id, entry.Name, entry.Alias, entry.Category, entry.Extra })
        {
            var key = NormalizeAiKey(value);
            if (!string.IsNullOrEmpty(key) && (key.Contains(needle) || needle.Contains(key)))
                return true;
        }
        return false;
    }
    private static void AppendAiNameEntryLine(StringBuilder sb, AiNameEntry entry)
    {
        sb.Append("id=").Append(entry.Id)
            .Append(" | name=").Append(entry.Name);
        if (!string.IsNullOrEmpty(entry.Alias))
            sb.Append(" | alias=").Append(entry.Alias);
        if (!string.IsNullOrEmpty(entry.Category))
            sb.Append(" | category=").Append(entry.Category);
        if (!string.IsNullOrEmpty(entry.Extra))
            sb.Append(" | extra=").Append(entry.Extra);
    }
    private string AiToolSetCharacterValue(string args)
    {
        var target = ResolveAiCharacterTarget(AiArgString(args, "target"), out var isPc, out var targetLabel);
        if (target == null)
            return "failed: target not found";
        var field = AiArgString(args, "field");
        var category = AiArgString(args, "category");
        var row = FindAiRow(field, category, isPc);
        if (row == null)
            return "failed: field not found: " + field;
        var value = AiArgInt(args, "value", 0);
        ApplyValue(target, row, value, isPc);
        InvalidateCachedUiValues(GetTargetCachePrefix(target, isPc));
        return "ok: " + targetLabel + " " + GetRowLabel(row) + " = " + value.ToString(CultureInfo.InvariantCulture) + " | " + _log;
    }
    private string AiToolSetCharacterPotential(string args)
    {
        var target = ResolveAiCharacterTarget(AiArgString(args, "target"), out var isPc, out var targetLabel);
        if (target == null)
            return "failed: target not found";
        var field = AiArgString(args, "field");
        var row = FindAiRow(field, "attribute", isPc) ?? FindAiRow(field, "status", isPc);
        if (row == null || !CanEditPotential(row))
            return "failed: potential field not found or not editable: " + field;
        var value = AiArgInt(args, "value", 100);
        SetElementPotential(target, row.Key, value);
        InvalidateCachedUiValues(GetTargetCachePrefix(target, isPc));
        return "ok: " + targetLabel + " " + GetRowLabel(row) + " potential = " + value.ToString(CultureInfo.InvariantCulture) + " | " + _log;
    }
    private string AiToolSetAbilityValues(string args)
    {
        var target = ResolveAiCharacterTarget(AiArgString(args, "target"), out var isPc, out var targetLabel);
        if (target == null)
            return "failed: target not found";
        EnsureAbilityRows();
        var abilityText = AiArgString(args, "ability");
        var ability = FindAiAbility(abilityText);
        if (ability == null)
            return "failed: ability not found: " + abilityText;

        var prefix = GetTargetInputPrefix(target, isPc) + "ability:" + ability.Id + ":ai:";
        var levelKey = prefix + "level";
        var chanceKey = prefix + "chance";
        var powerKey = prefix + "power";
        var hpCostKey = prefix + "hpCost";
        var mpCostKey = prefix + "mpCost";
        var spCostKey = prefix + "spCost";
        var stockKey = prefix + "stock";
        _inputs[levelKey] = AiArgInt(args, "level", GetAbilityLevel(target, ability)).ToString(CultureInfo.InvariantCulture);
        _inputs[chanceKey] = AiArgInt(args, "chance", GetAbilityDisplayChance(target, ability)).ToString(CultureInfo.InvariantCulture);
        _inputs[powerKey] = AiArgInt(args, "power", GetAbilityDisplayPower(target, ability)).ToString(CultureInfo.InvariantCulture);
        _inputs[hpCostKey] = AiArgInt(args, "hp_cost", GetAbilityCost(target, ability, 0)).ToString(CultureInfo.InvariantCulture);
        _inputs[mpCostKey] = AiArgInt(args, "mp_cost", GetAbilityCost(target, ability, 1)).ToString(CultureInfo.InvariantCulture);
        _inputs[spCostKey] = AiArgInt(args, "sp_cost", GetAbilityCost(target, ability, 2)).ToString(CultureInfo.InvariantCulture);
        _inputs[stockKey] = AiArgInt(args, "stock", GetAbilityStock(target, ability)).ToString(CultureInfo.InvariantCulture);
        ApplyAbilityValues(target, ability, levelKey, chanceKey, powerKey, hpCostKey, mpCostKey, spCostKey, stockKey);
        InvalidateCachedUiValue(GetTargetCachePrefix(target, isPc) + ":ability:" + ability.Id);
        return "ok: " + targetLabel + " " + ability.DisplayName + " | " + _log;
    }
    private string AiToolSetNpcRelationship(string args)
    {
        var target = ResolveAiCharacterTarget(AiArgString(args, "target"), out var isPc, out var targetLabel);
        if (target == null || isPc)
            return "failed: NPC target not found";
        if (AiHasArg(args, "affinity"))
            SetNpcAffinity(target, AiArgInt(args, "affinity", GetNpcAffinityValue(target)));
        if (AiHasArg(args, "relationship"))
            SetNpcHostility(target, AiArgRelationship(args, "relationship", target.hostility));
        var action = NormalizeAiKey(AiArgString(args, "party_action", "none"));
        switch (action)
        {
            case "":
            case "none": break;
            case "join_party": MakeNpcPartyMember(target); break;
            case "leave_party": RemoveNpcPartyMember(target); break;
            case "join_faction": AddNpcToPlayerFactionOnly(target); break;
            case "leave_faction": RemoveNpcFromPlayerFactionOnly(target); break;
            default: return "failed: unknown party_action " + action;
        }
        return "ok: " + targetLabel + " relationship updated | " + _log;
    }
    private string AiToolTeleport(string args)
    {
        var mode = NormalizeAiKey(AiArgString(args, "mode"));
        switch (mode)
        {
            case "to_nearby_npc":
                {
                    var target = GetSelectedNearbyNpc();
                    if (target == null) return "failed: no nearby NPC selected";
                    TeleportPlayerBesideNpc(target);
                    return (_log.IndexOf(T("传送失败", "Teleport failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _log;
                }
            case "to_dialogue_npc":
                {
                    var target = GetTalkingNpc();
                    if (target == null) return "failed: no dialogue NPC";
                    TeleportPlayerBesideNpc(target);
                    return (_log.IndexOf(T("传送失败", "Teleport failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _log;
                }
            case "to_landmark":
                {
                    var landmark = AiArgString(args, "landmark");
                    var zone = FindAiTeleportZone(landmark);
                    if (zone == null) return "failed: landmark not found: " + landmark;
                    QueueTeleportToZone(zone);
                    ExecutePendingTeleportRequest();
                    return (_log.IndexOf(T("传送失败", "Teleport failed"), StringComparison.Ordinal) >= 0 || _log.IndexOf(T("传送请求失败", "Teleport request failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _log;
                }
            case "to_world_position":
                {
                    _teleportXInput = AiArgInt(args, "x", 0).ToString(CultureInfo.InvariantCulture);
                    _teleportYInput = AiArgInt(args, "y", 0).ToString(CultureInfo.InvariantCulture);
                    QueueTeleportToWorldPosition();
                    ExecutePendingTeleportRequest();
                    return (_log.IndexOf(T("传送失败", "Teleport failed"), StringComparison.Ordinal) >= 0 || _log.IndexOf(T("传送请求失败", "Teleport request failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _log;
                }
            default:
                return "failed: unknown teleport mode " + mode;
        }
    }
    private string AiToolSetHomeValue(string args)
    {
        var homes = GetPlayerHomeBranches();
        var branch = GetSelectedHomeBranch(homes);
        if (branch == null)
            return "failed: no home data";
        var zone = GetHomeZone(branch);
        var field = AiArgString(args, "field");
        var category = NormalizeAiKey(AiArgString(args, "category", "basic"));
        var value = AiArgInt(args, "value", 0);
        var key = NormalizeAiKey(field);
        if (category == "" || category == "basic")
        {
            switch (key)
            {
                case "hearthlv":
                case "covenantstonelevel":
                case "盟约之石等级": SetHomeBranchLevel(branch, value); break;
                case "civility":
                case "residentcivility":
                case "居民素质": SetHomeCivility(branch, value); break;
                case "soil":
                case "fertility":
                case "肥沃度": SetHomeFertility(branch, zone, value); break;
                case "development":
                case "发展度": SetHomeDevelopment(zone, value); break;
                case "danger":
                case "dangerlevel":
                case "危险度": SetHomeDanger(branch, value); break;
                case "maxap":
                case "maxadminpower":
                case "运营力上限": SetHomeMaxAp(branch, value); break;
                default:
                    category = "auto";
                    break;
            }
            if (category != "auto")
                return "ok: " + _homeLog;
        }

        EnsureHomeRows();
        HomeElementKind kind;
        List<HomeElementDef> rows;
        if (category == "skill" || category == "tech" || category == "家园技能")
        {
            kind = HomeElementKind.Skill;
            rows = _homeSkillRows;
        }
        else if (category == "feat" || category == "landfeat" || category == "家园专长")
        {
            kind = HomeElementKind.Feat;
            rows = _homeFeatRows;
        }
        else if (category == "policy" || category == "家园政策")
        {
            kind = HomeElementKind.Policy;
            rows = _homePolicyRows;
        }
        else
        {
            var found = FindAiHomeElement(field, _homeSkillRows);
            if (found != null)
            {
                kind = HomeElementKind.Skill;
                rows = _homeSkillRows;
            }
            else if ((found = FindAiHomeElement(field, _homeFeatRows)) != null)
            {
                kind = HomeElementKind.Feat;
                rows = _homeFeatRows;
            }
            else
            {
                kind = HomeElementKind.Policy;
                rows = _homePolicyRows;
            }
        }
        var row = FindAiHomeElement(field, rows);
        if (row == null)
            return "failed: home field not found: " + field;
        SetHomeElementLevel(branch, row, value, kind);
        if (kind == HomeElementKind.Policy && AiHasArg(args, "active"))
            SetHomePolicyActive(branch, row, AiArgBool(args, "active", IsHomePolicyActive(branch, row.Id)));
        return "ok: " + _homeLog;
    }
    private string AiToolSetPlayerInfo(string args)
    {
        var pc = GameAccess.Characters.PlayerCharacter;
        var player = GameAccess.Runtime.Player;
        if (pc == null || player == null)
            return "failed: no player data";
        LoadPlayerInfoInputs();
        ApplyAiOptionalString(args, "name", value => _playerInfoName = value);
        ApplyAiOptionalString(args, "alias", value => _playerInfoAlias = value);
        ApplyAiOptionalString(args, "honorific", value => _playerInfoHonorific = value);
        ApplyAiOptionalString(args, "race_id", value => _playerInfoRaceId = value);
        ApplyAiOptionalString(args, "job_id", value => _playerInfoJobId = value);
        ApplyAiOptionalString(args, "faith_id", value => _playerInfoFaithId = value);
        ApplyAiOptionalString(args, "faction_id", value => _playerInfoFactionId = value);
        ApplyAiOptionalInt(args, "gender", value => _playerInfoGender = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "age", value => _playerInfoAge = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "height_cm", value => _playerInfoHeight = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "weight_kg", value => _playerInfoWeight = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "birth_year", value => _playerInfoBirthYear = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "birth_month", value => _playerInfoBirthMonth = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "birth_day", value => _playerInfoBirthDay = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "home_word_id", value => _playerInfoHomeId = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "location_word_id", value => _playerInfoLocId = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "father_type_id", value => _playerInfoDadId = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "father_prefix_id", value => _playerInfoDadAdvId = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "mother_type_id", value => _playerInfoMomId = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalInt(args, "mother_prefix_id", value => _playerInfoMomAdvId = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalString(args, "liked_item_id", value => _playerInfoLikeId = value);
        ApplyAiOptionalString(args, "domain_ids", value => _playerInfoDomains = value);
        ApplyAiOptionalString(args, "hobby_ids", value => _playerInfoHobbies = value);
        ApplyAiOptionalString(args, "work_ids", value => _playerInfoWorks = value);
        ApplyAiOptionalInt(args, "total_feat_points", value => _playerInfoTotalFeat = value.ToString(CultureInfo.InvariantCulture));
        ApplyAiOptionalString(args, "background", value => _playerInfoBackground = value);
        ApplyAiOptionalString(args, "memo", value => _playerInfoMemo = value);
        ApplyAiOptionalString(args, "memo2", value => _playerInfoMemo2 = value);
        ApplyAiOptionalString(args, "card_note", value => _playerInfoNote = value);
        ApplyPlayerInfoInputs();
        return (_playerInfoLog.IndexOf(T("失败", "failed"), StringComparison.Ordinal) >= 0 ? "failed: " : "ok: ") + _playerInfoLog;
    }
    private string AiToolSetUiOption(string args)
    {
        var option = NormalizeAiKey(AiArgString(args, "option"));
        var value = AiArgString(args, "value");
        switch (option)
        {
            case "language":
                _language = NormalizeLanguage(value);
                return "ok: language = " + _language;
            case "ui_style":
            case "uistyle":
                return SetAiUiStyle(value);
            case "opacity":
                _uiAlpha = Clamp(AiParseFloat(value, _uiAlpha), 0.2f, 1f);
                _uiAlphaText = _uiAlpha.ToString("0.00", CultureInfo.InvariantCulture);
                return "ok: opacity = " + _uiAlphaText;
            case "font_size":
            case "fontsize":
            case "ui_font_size":
            case "uifontsize":
                SetUiFontSize(ParseInt(value, _uiFontSize));
                return "ok: font_size = " + GetUiFontSizeLabel();
            case "font_color_hex":
            case "fontcolorhex":
                Color color;
                if (!TryParseHexColor(value, out color))
                    return "failed: invalid color " + value;
                SetCustomUiTextColor(color);
                return "ok: font color = " + _uiTextColorHexText;
            case "font_color_follow_style":
            case "fontcolorfollowstyle":
                if (AiParseBool(value, false)) UseStyleUiTextColor();
                else _uiTextColorFollowsStyle = false;
                return "ok: font_color_follow_style = " + _uiTextColorFollowsStyle.ToString(CultureInfo.InvariantCulture);
            case "main_menu_info":
            case "mainmenuinfo":
            case "show_main_menu_info":
            case "showmainmenuinfo":
                SetShowMainMenuInfo(AiParseBool(value, ShowMainMenuInfo));
                return "ok: main_menu_info = " + ShowMainMenuInfo.ToString(CultureInfo.InvariantCulture);
            case "elin_modifier_watermark":
            case "elinmodifierwatermark":
            case "watermark":
                SetWatermarkEnabled(AiParseBool(value, _modules.Watermark.Enabled));
                return "ok: elin_modifier_watermark = " + _modules.Watermark.Enabled.ToString(CultureInfo.InvariantCulture);
            case "watermark_position_locked":
            case "watermarkpositionlocked":
                _modules.Watermark.SetPositionLocked(AiParseBool(value, _modules.Watermark.PositionLocked));
                return "ok: watermark_position_locked = " + _modules.Watermark.PositionLocked.ToString(CultureInfo.InvariantCulture);
            case "watermark_game_error_notification":
            case "watermarkgameerrornotification":
                SetWatermarkGameErrorNotification(AiParseBool(value, _modules.Watermark.GameErrorNotificationEnabled));
                return "ok: watermark_game_error_notification = " + _modules.Watermark.GameErrorNotificationEnabled.ToString(CultureInfo.InvariantCulture);
            case "watermark_reset_position":
            case "watermarkresetposition":
                ResetWatermarkPosition();
                return "ok: watermark position reset";
            case "adaptive_ui_scale":
            case "adaptiveuiscale":
                _adaptiveUiScale = AiParseBool(value, _adaptiveUiScale);
                ApplyLGuiVisualSettings();
                return "ok: adaptive_ui_scale = " + _adaptiveUiScale.ToString(CultureInfo.InvariantCulture);
            case "custom_ui_scale":
            case "customuiscale":
                SetCustomUiScale(AiParseFloat(value, _customUiScale));
                ApplyLGuiVisualSettings();
                return "ok: custom_ui_scale = " + _customUiScale.ToString("0.00", CultureInfo.InvariantCulture);
            case "force_game_unfocus":
            case "forcegameunfocus":
                _forceGameUnfocus = AiParseBool(value, _forceGameUnfocus);
                return "ok: force_game_unfocus = " + _forceGameUnfocus.ToString(CultureInfo.InvariantCulture);
            case "ui_rounded_corners":
            case "uiroundedcorners":
                _uiRoundedCorners = AiParseBool(value, _uiRoundedCorners);
                ApplyLGuiVisualSettings();
                return "ok: ui_rounded_corners = " + _uiRoundedCorners.ToString(CultureInfo.InvariantCulture);
            case "hotkey":
                KeyCode key;
                if (!TryParseKeyCode(value, out key))
                    return "failed: invalid hotkey " + value;
                SetOpenKey(key);
                return "ok: hotkey = " + GetKeyLabel(_openKey);
            default:
                return "failed: unsupported UI option " + option;
        }
    }
    private string AiToolEmpListPlugins(string args)
    {
        RefreshEmpPluginDefinitionsIfNeeded();
        var filter = AiArgString(args, "filter");
        var limit = Clamp(AiArgInt(args, "limit", 80), 0, 5000);
        var includeFunctions = AiArgBool(args, "include_functions", true);

        var plugins = _pluginDefinitions.Values
            .Where(plugin => EmpPluginMatchesFilter(plugin, filter, includeFunctions))
            .OrderBy(plugin => SafeEmpText(plugin == null ? "" : plugin.Name, SafeEmpText(plugin == null ? "" : plugin.Id, "")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => SafeEmpText(plugin == null ? "" : plugin.Id, ""), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = plugins.Count;
        if (limit > 0 && plugins.Count > limit)
            plugins = plugins.Take(limit).ToList();

        var sb = new StringBuilder();
        sb.Append("ok: emp plugins ")
            .Append(plugins.Count.ToString(CultureInfo.InvariantCulture))
            .Append("/")
            .Append(total.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(filter))
            sb.Append(" filter=").Append(filter);
        if (plugins.Count == 0)
            return sb.Append(" | none").ToString();

        foreach (var plugin in plugins)
        {
            if (plugin == null)
                continue;

            sb.AppendLine();
            sb.Append("- plugin id=").Append(SafeEmpText(plugin.Id, "<empty>"));
            sb.Append(" | name=").Append(SafeEmpText(plugin.Name, "<empty>"));
            sb.Append(" | path=").Append(SafeEmpText(plugin.RelativePath, SafeEmpText(plugin.SourcePath, "<empty>")));
            sb.Append(" | functions=").Append(plugin.Functions.Count.ToString(CultureInfo.InvariantCulture));
            if (!plugin.IsValid)
                sb.Append(" | error=").Append(SafeEmpText(plugin.Error, "<empty>"));

            if (!includeFunctions)
                continue;

            foreach (var function in plugin.Functions)
            {
                if (function == null)
                    continue;
                var state = GetEmpFunctionState(plugin, function);
                sb.AppendLine();
                sb.Append("  - function id=").Append(SafeEmpText(function.Id, "<empty>"));
                sb.Append(" | name=").Append(SafeEmpText(function.Name, "<empty>"));
                sb.Append(" | kind=").Append(GetEmpFunctionKindToken(function.Kind));
                sb.Append(" | value_kind=").Append(GetEmpValueKindToken(function.ValueKind));
                sb.Append(" | enabled=").Append(state.Enabled ? "true" : "false");
                sb.Append(" | value=").Append(SafeEmpText(state.Value, ""));
                sb.Append(" | pending=").Append(state.PendingApply ? "true" : "false");
                sb.Append(" | initialized=").Append(state.Initialized ? "true" : "false");
                sb.Append(" | ops=").Append(GetEmpOperationCount(function).ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(function.Error))
                    sb.Append(" | error=").Append(function.Error);
            }
        }

        return sb.ToString();
    }
    private string AiToolEmpSetFunctionState(string args)
    {
        RefreshEmpPluginDefinitionsIfNeeded();

        var pluginText = AiArgString(args, "plugin");
        var functionText = AiArgString(args, "function");
        var apply = AiArgBool(args, "apply", true);

        EmpPluginDefinition plugin;
        string error;
        if (!TryResolveEmpPlugin(pluginText, out plugin, out error))
            return "failed: " + error;

        EmpFunctionDefinition function;
        if (!TryResolveEmpFunction(plugin, functionText, out function, out error))
            return "failed: " + error;

        var state = GetEmpFunctionState(plugin, function);
        if (function.Kind == EmpFunctionKind.Button)
        {
            if (!apply)
            {
                state.PendingApply = true;
                state.Initialized = false;
                return "ok: " + SafeEmpText(plugin.Name, plugin.Id) + "." + SafeEmpText(function.Name, function.Id) + " pending";
            }
            return ApplyEmpFunctionStateNow(plugin, function, state, false);
        }

        var hasEnabled = AiHasArg(args, "enabled");
        var hasValue = AiHasArg(args, "value");
        if (hasEnabled)
            state.Enabled = AiArgBool(args, "enabled", state.Enabled);
        if (hasValue)
            state.Value = AiArgString(args, "value");
        if (function.ValueKind == EmpValueKind.Bool)
        {
            if (hasEnabled && !hasValue)
                state.Value = state.Enabled ? "true" : "false";
            else if (hasValue && !hasEnabled)
                state.Enabled = ParseEmpBool(state.Value, state.Enabled);
        }

        state.PendingApply = true;
        state.Initialized = false;
        if (!apply)
        {
            MarkEmpPending();
            return "ok: " + SafeEmpText(plugin.Name, plugin.Id) + "." + SafeEmpText(function.Name, function.Id) + " pending";
        }
        return ApplyEmpFunctionStateNow(plugin, function, state, false);
    }
    private string AiToolEmpReloadPlugins(string args)
    {
        ReloadEmpPluginDefinitions();
        return "ok: emp plugins reloaded";
    }
    private bool TryResolveEmpPlugin(string text, out EmpPluginDefinition plugin, out string error)
    {
        plugin = null;
        error = "";
        RefreshEmpPluginDefinitionsIfNeeded();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "plugin is empty";
            return false;
        }

        var key = NormalizeAiKey(text);
        var exact = new List<EmpPluginDefinition>();
        var partial = new List<EmpPluginDefinition>();
        foreach (var candidate in _pluginDefinitions.Values)
        {
            if (candidate == null)
                continue;
            var score = GetEmpPluginMatchScore(candidate, key);
            if (score >= 2)
                exact.Add(candidate);
            else if (score == 1)
                partial.Add(candidate);
        }

        if (exact.Count == 1)
        {
            plugin = exact[0];
            return true;
        }
        if (exact.Count > 1)
        {
            error = "ambiguous plugin: " + string.Join(", ", exact.Take(5).Select(DescribeEmpPlugin));
            return false;
        }
        if (partial.Count == 1)
        {
            plugin = partial[0];
            return true;
        }
        if (partial.Count > 1)
        {
            error = "ambiguous plugin: " + string.Join(", ", partial.Take(5).Select(DescribeEmpPlugin));
            return false;
        }

        error = "plugin not found: " + text;
        return false;
    }
}
