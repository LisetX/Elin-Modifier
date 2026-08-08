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
    private string GetWeaponRarityLabel(int value)
    {
        switch (value)
        {
            case -100:
                return T("低级", "Poor");
            case 0:
                return T("普通", "Standard");
            case 100:
                return T("高级", "Superior");
            case 200:
                return T("奇迹", "Miracle");
            case 300:
                return T("神器", "Godly");
            case 400:
                return T("古遗物", "Artifact");
            default:
                return T("当前值", "Current value");
        }
    }
    private void OpenWeaponEditorWindow(Thing thing)
    {
        if (!CanEditWeaponData(thing))
            return;

        _weaponEditorTarget = thing;
        _weaponEditorName = SafeThingName(thing);
        LoadWeaponEditorFields(thing);
        _weaponEditorWindowVisible = false;
        if (!IsLGuiInitialized())
            return;
        EnsureLGuiEditorVisible();
        OpenLGuiWeaponEditor();
    }
    private void LoadWeaponEditorFields(Thing thing)
    {
        _weaponEditorLv = thing.LV.ToString(CultureInfo.InvariantCulture);
        _weaponEditorEncLv = thing.encLV.ToString(CultureInfo.InvariantCulture);
        _weaponEditorMaterialId = thing.idMaterial.ToString(CultureInfo.InvariantCulture);
        _weaponEditorRarityValue = thing.rarityLv;
        _weaponEditorBlessedStateValue = (int)NormalizeBlessedState((int)thing.blessedState);
        _weaponEditorFlagStolen = thing.isStolen;
        _weaponEditorFlagCrafted = thing.isCrafted;
        _weaponEditorFlagGifted = thing.isGifted;
        _weaponEditorFlagReplica = thing.isReplica;
        _weaponEditorFlagCopy = thing.isCopy;
        _weaponEditorFlagFireproof = thing.isFireproof;
        _weaponEditorFlagAcidproof = thing.isAcidproof;
        _weaponEditorFlagBroken = thing.isBroken;
        _weaponEditorFlagNoSell = thing.noSell;
        _weaponEditorFlagLostProperty = thing.isLostProperty;
        _weaponEditorDiceDim = thing.c_diceDim.ToString(CultureInfo.InvariantCulture);
        _weaponEditorHit = GetThingElementBase(thing, 66).ToString(CultureInfo.InvariantCulture);
        _weaponEditorDamage = GetThingElementBase(thing, 67).ToString(CultureInfo.InvariantCulture);
        _weaponEditorDv = GetThingElementBase(thing, 64).ToString(CultureInfo.InvariantCulture);
        _weaponEditorPv = GetThingElementBase(thing, 65).ToString(CultureInfo.InvariantCulture);
        _weaponEditorWeight = thing.SelfWeight.ToString(CultureInfo.InvariantCulture);
        _weaponEditorCharges = thing.c_charges.ToString(CultureInfo.InvariantCulture);
        _weaponEditorAmmo = thing.c_ammo.ToString(CultureInfo.InvariantCulture);
        _weaponEditorRangeText = SafeIntText(() => thing.range);
        _weaponEditorPenetrationText = SafeIntText(() => thing.Penetration);
        _weaponEditorModificationSlots = GetWeaponModificationSlotCount(thing).ToString(CultureInfo.InvariantCulture);
        LoadWeaponEnchantments(thing);
    }
    private void ApplyWeaponEditorChange()
    {
        try
        {
            var target = _weaponEditorTarget;
            if (!CanEditWeaponData(target))
            {
                _log = T("目标武器不存在", "Target weapon does not exist");
                _weaponEditorWindowVisible = false;
                return;
            }

            int lv, encLv, materialId, diceDim, hit, damage, dv, pv, weight, charges, ammo, range, penetration, modificationSlots;
            if (!TryParseWeaponEditorInt(_weaponEditorLv, T("等级", "Level"), out lv) ||
                !TryParseWeaponEditorInt(_weaponEditorEncLv, T("强化", "Enhance"), out encLv) ||
                !TryParseWeaponEditorInt(_weaponEditorMaterialId, T("材质ID", "Material ID"), out materialId) ||
                !TryParseWeaponEditorInt(_weaponEditorDiceDim, T("伤害骰面", "Damage dice sides"), out diceDim) ||
                !TryParseWeaponEditorInt(_weaponEditorHit, T("命中", "Hit"), out hit) ||
                !TryParseWeaponEditorInt(_weaponEditorDamage, T("伤害修正", "Damage bonus"), out damage) ||
                !TryParseWeaponEditorInt(_weaponEditorDv, "DV", out dv) ||
                !TryParseWeaponEditorInt(_weaponEditorPv, "PV", out pv) ||
                !TryParseWeaponEditorInt(_weaponEditorWeight, T("重量", "Weight"), out weight) ||
                !TryParseWeaponEditorInt(_weaponEditorCharges, T("充能", "Charges"), out charges) ||
                !TryParseWeaponEditorInt(_weaponEditorAmmo, T("弹药", "Ammo"), out ammo) ||
                !TryParseWeaponEditorInt(_weaponEditorRangeText, T("射程", "Range"), out range) ||
                !TryParseWeaponEditorInt(_weaponEditorPenetrationText, T("穿透", "Penetration"), out penetration) ||
                !TryParseWeaponEditorInt(_weaponEditorModificationSlots, T("改造槽位", "Modification slots"), out modificationSlots))
                return;

            lv = Math.Max(1, lv);
            diceDim = Math.Max(1, diceDim);
            weight = Math.Max(0, weight);
            charges = Math.Max(0, charges);
            ammo = Math.Max(0, ammo);
            range = Math.Max(0, range);
            modificationSlots = Clamp(modificationSlots, 0, 1000);

            var thing = target!;
            thing.LV = lv;
            thing.encLV = encLv;
            thing.rarityLv = _weaponEditorRarityValue;
            thing.SetBlessedState(NormalizeBlessedState(_weaponEditorBlessedStateValue));
            ApplyWeaponEditorFlags(thing);
            if (materialId != thing.idMaterial)
                thing.ChangeMaterial(materialId, true);
            thing.c_diceDim = diceDim;
            SetThingElementBase(thing, 66, hit);
            SetThingElementBase(thing, 67, damage);
            SetThingElementBase(thing, 64, dv);
            SetThingElementBase(thing, 65, pv);
            thing.ChangeWeight(weight);
            thing.c_charges = charges;
            thing.c_ammo = ammo;
            SetWeaponRangeOverride(thing, range);
            SetWeaponPenetrationOverride(thing, penetration);
            ResizeWeaponModificationSlots(thing, modificationSlots);
            ApplyWeaponEnchantValues(thing, BuildWeaponEnchantValues() ?? new Dictionary<int, int>());

            RefreshInventoryUi();
            _weaponEditorName = SafeThingName(thing);
            LoadWeaponEditorFields(thing);
            _log = T("已修改武器数据: ", "Modified weapon data: ") + _weaponEditorName;
            _weaponEditorWindowVisible = false;
        }
        catch (Exception ex)
        {
            _log = T("修改武器数据失败: ", "Modify weapon data failed: ") + ex.Message;
        }
    }
    private void ApplyWeaponEditorFlags(Thing thing)
    {
        thing.isStolen = _weaponEditorFlagStolen;
        thing.isCrafted = _weaponEditorFlagCrafted;
        thing.isGifted = _weaponEditorFlagGifted;
        thing.isReplica = _weaponEditorFlagReplica;
        thing.isCopy = _weaponEditorFlagCopy;
        thing.isFireproof = _weaponEditorFlagFireproof;
        thing.isAcidproof = _weaponEditorFlagAcidproof;
        thing.isBroken = _weaponEditorFlagBroken;
        thing.noSell = _weaponEditorFlagNoSell;
        thing.isLostProperty = _weaponEditorFlagLostProperty;
    }
    private static int GetWeaponModificationSlotCount(Thing thing)
    {
        return thing?.sockets?.Count ?? 0;
    }
    private static int GetRawWeaponRange(Thing thing)
    {
        return thing?.source == null ? 0 : thing.source.range;
    }
    private static int GetRawWeaponPenetration(Thing thing)
    {
        var substats = thing?.source?.substats;
        return substats == null || substats.Length == 0 ? 0 : substats[0];
    }
    private static void SetWeaponRangeOverride(Thing thing, int value)
    {
        if (thing == null)
            return;
        if (thing.mapInt == null)
            thing.mapInt = new Dictionary<int, int>();
        if (value == GetRawWeaponRange(thing))
            thing.mapInt.Remove(WeaponRangeOverrideMapKey);
        else
            thing.mapInt[WeaponRangeOverrideMapKey] = value;
        thing.isModified = true;
    }
    private static void SetWeaponPenetrationOverride(Thing thing, int value)
    {
        if (thing == null)
            return;
        if (thing.mapInt == null)
            thing.mapInt = new Dictionary<int, int>();
        if (value == GetRawWeaponPenetration(thing))
            thing.mapInt.Remove(WeaponPenetrationOverrideMapKey);
        else
            thing.mapInt[WeaponPenetrationOverrideMapKey] = value;
        thing.isModified = true;
    }
    private static void ResizeWeaponModificationSlots(Thing thing, int requestedCount)
    {
        if (thing == null)
            return;

        requestedCount = Clamp(requestedCount, 0, 1000);
        if (thing.sockets == null)
            thing.sockets = new List<int>();

        while (thing.sockets.Count < requestedCount)
            thing.sockets.Add(0);

        for (var i = thing.sockets.Count - 1; i >= 0 && thing.sockets.Count > requestedCount; i--)
        {
            if (thing.sockets[i] == 0)
                thing.sockets.RemoveAt(i);
        }

        if (thing.sockets.Count == 0)
            thing.sockets = null;
        thing.isModified = true;
    }
    private List<GeneEffectDef> GetFilteredWeaponEnchantIds()
    {
        EnsureWeaponEnchantEffectRows();
        if (_lastWeaponEnchantFilter != _weaponEnchantFilter)
        {
            _weaponEnchantPage = 0;
            _lastWeaponEnchantFilter = _weaponEnchantFilter;
        }

        var filter = (_weaponEnchantFilter ?? "").Trim().ToLowerInvariant();
        var result = new List<GeneEffectDef>();
        if (_weaponEnchantEffectRows == null)
            return result;
        foreach (var effect in _weaponEnchantEffectRows)
        {
            if (!string.IsNullOrEmpty(filter) &&
                !effect.Name.ToLowerInvariant().Contains(filter) &&
                !effect.Alias.ToLowerInvariant().Contains(filter) &&
                !effect.Id.ToString(CultureInfo.InvariantCulture).Contains(filter) &&
                !effect.Category.ToLowerInvariant().Contains(filter))
                continue;
            result.Add(effect);
        }
        return result;
    }
    private void EnsureWeaponEnchantEffectRows()
    {
        if (_weaponEnchantEffectRows != null)
        {
            AddCurrentWeaponEnchantEffectRows(_weaponEnchantEffectRows);
            AddFixedWeaponEnchantEffectRows(_weaponEnchantEffectRows);
            return;
        }
        var rows = new Dictionary<int, GeneEffectDef>();
        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            if (!IsWeaponEnchantCandidate(row))
                continue;
            AddWeaponEnchantEffectRow(rows, id, GetString(row, "category"));
        }
        foreach (var input in _weaponEditorEnchantments)
        {
            if (int.TryParse((input.ElementId ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                IsWeaponEnchantCandidateId(id))
                AddWeaponEnchantEffectRow(rows, id, "Current weapon");
        }
        AddFixedAttributeEffectRows(rows, "Attribute");
        _weaponEnchantEffectRows = new List<GeneEffectDef>(rows.Values);
        _weaponEnchantEffectRows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void AddCurrentWeaponEnchantEffectRows(List<GeneEffectDef> rows)
    {
        if (rows == null)
            return;

        var existing = new HashSet<int>();
        foreach (var row in rows)
            existing.Add(row.Id);

        var added = false;
        var map = new Dictionary<int, GeneEffectDef>();
        foreach (var input in _weaponEditorEnchantments)
        {
            if (!int.TryParse((input.ElementId ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                existing.Contains(id) ||
                !IsWeaponEnchantCandidateId(id))
                continue;
            AddWeaponEnchantEffectRow(map, id, "Current weapon");
        }
        foreach (var row in map.Values)
        {
            rows.Add(row);
            added = true;
        }
        if (added)
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void AddFixedWeaponEnchantEffectRows(List<GeneEffectDef> rows)
    {
        AddFixedEffectRows(rows, map => AddFixedAttributeEffectRows(map, "Attribute"));
    }
    private static bool IsWeaponEnchantCandidate(object row)
    {
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
                    string.Join(",", GetStringArray(row, "tag")) + "," +
                    string.Join(",", GetStringArray(row, "textExtra")) + "," +
                    string.Join(",", GetStringArray(row, "textExtra_JP")) + "," +
                    string.Join(",", GetStringArray(row, "adjective")) + "," +
                    string.Join(",", GetStringArray(row, "adjective_JP")) + "," +
                    string.Join(",", GetStringArray(row, "textAlt"))).ToLowerInvariant();
        return TextHas(text, "weapon") || TextHas(text, "sword") || TextHas(text, "blade") || TextHas(text, "shield") ||
               TextHas(text, "armor") || TextHas(text, "armour") || TextHas(text, "equip") || TextHas(text, "attack") ||
               TextHas(text, "defense") || TextHas(text, "defence") || TextHas(text, "damage") || TextHas(text, "hit") ||
               TextHas(text, "dv") || TextHas(text, "pv") || TextHas(text, "enc") || TextHas(text, "melee") ||
               TextHas(text, "ranged") || TextHas(text, "ammo") || TextHas(text, "crystal") || TextHas(text, "element");
    }
    private static bool IsWeaponEnchantCandidateId(int id)
    {
        var row = FindSourceElementRowById(id);
        return row != null && IsWeaponEnchantCandidate(row);
    }
    private string GetWeaponEnchantName(string idText)
    {
        return GetGeneEffectName(idText);
    }
    private void LoadWeaponEnchantments(Thing thing)
    {
        _weaponEditorEnchantments.Clear();
        _weaponEditorLoadedEnchantIds.Clear();
        try
        {
            foreach (var element in thing.elements.dict.Values)
            {
                if (element == null || element.id <= 0)
                    continue;
                _weaponEditorLoadedEnchantIds.Add(element.id);
                _weaponEditorEnchantments.Add(new GeneValueInput(
                    element.id.ToString(CultureInfo.InvariantCulture),
                    GetThingElementEditorValue(thing, element).ToString(CultureInfo.InvariantCulture)));
            }
        }
        catch { }
    }
    private Dictionary<int, int>? BuildWeaponEnchantValues()
    {
        var values = new Dictionary<int, int>();
        for (var i = 0; i < _weaponEditorEnchantments.Count; i++)
        {
            var row = _weaponEditorEnchantments[i];
            if (string.IsNullOrWhiteSpace(row.ElementId) && string.IsNullOrWhiteSpace(row.Value))
                continue;
            if (!TryParseWeaponEditorInt(row.ElementId, T("附魔效果", "Enchant effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var elementId) ||
                !TryParseWeaponEditorInt(row.Value, T("数值", "Value") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var value))
                return null;
            if (elementId <= 0)
                return null;
            if (values.ContainsKey(elementId))
                return null;
            values[elementId] = value;
        }
        return values;
    }
    private void ApplyWeaponEnchantValues(Thing thing, Dictionary<int, int> values)
    {
        if (thing == null || values == null)
            return;
        foreach (var id in _weaponEditorLoadedEnchantIds)
        {
            if (!values.ContainsKey(id))
                SetThingElementEditorValue(thing, id, 0);
        }
        foreach (var pair in values)
            SetThingElementEditorValue(thing, pair.Key, pair.Value);
    }
    private bool TryParseWeaponEditorInt(string text, string label, out int value)
    {
        if (int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;
        _log = label + T(" 输入不是数字", " input is not a number");
        return false;
    }
}
