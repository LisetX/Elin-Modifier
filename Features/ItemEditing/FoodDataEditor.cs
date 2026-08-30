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
    private List<GeneEffectDef> GetFilteredFoodEffectIds()
    {
        EnsureFoodEffectRows();
        if (_lastFoodEffectFilter != _foodEffectFilter)
        {
            _foodEffectPage = 0;
            _lastFoodEffectFilter = _foodEffectFilter;
        }

        var result = new List<GeneEffectDef>();
        var filter = (_foodEffectFilter ?? "").Trim().ToLowerInvariant();
        if (_foodEffectRows == null)
            return result;

        foreach (var effect in _foodEffectRows)
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
    private void EnsureFoodEffectRows()
    {
        if (_foodEffectRows != null)
        {
            AddCurrentFoodEffectRows(_foodEffectRows);
            AddFixedFoodEffectRows(_foodEffectRows);
            return;
        }

        var rows = new Dictionary<int, GeneEffectDef>();
        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            if (!IsFoodEffectCandidate(row))
                continue;
            AddFoodEffectRow(rows, id, GetString(row, "category"));
        }

        foreach (var input in _foodEditorEffects)
        {
            if (int.TryParse((input.ElementId ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                IsFoodEffectCandidateId(id))
                AddFoodEffectRow(rows, id, "Current food");
        }

        AddFixedAttributeEffectRows(rows, "Attribute");
        AddFoodEffectRow(rows, FoodNutritionElementId, "Food");

        _foodEffectRows = new List<GeneEffectDef>(rows.Values);
        _foodEffectRows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void AddCurrentFoodEffectRows(List<GeneEffectDef> rows)
    {
        if (rows == null)
            return;

        var existing = new HashSet<int>();
        foreach (var row in rows)
            existing.Add(row.Id);

        var added = false;
        var map = new Dictionary<int, GeneEffectDef>();
        foreach (var input in _foodEditorEffects)
        {
            if (!int.TryParse((input.ElementId ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                existing.Contains(id) ||
                !IsFoodEffectCandidateId(id))
                continue;
            AddFoodEffectRow(map, id, "Current food");
        }
        foreach (var row in map.Values)
        {
            rows.Add(row);
            added = true;
        }
        if (added)
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void AddFixedFoodEffectRows(List<GeneEffectDef> rows)
    {
        AddFixedEffectRows(rows, map =>
        {
            AddFixedAttributeEffectRows(map, "Attribute");
            AddFoodEffectRow(map, FoodNutritionElementId, "Food");
        });
    }
    private void AddFoodEffectRow(Dictionary<int, GeneEffectDef> rows, int id, string categoryHint)
    {
        if (id <= 0 || rows.ContainsKey(id))
            return;

        var row = FindSourceElementRowById(id);
        if (row == null)
            return;
        var name = GetElementDisplayName(row);
        if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            name = GetString(row, "alias");
        if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            return;
        var alias = GetString(row, "alias");
        var category = GetString(row, "category");
        if (string.IsNullOrEmpty(category)) category = GetString(row, "group");
        if (string.IsNullOrEmpty(category)) category = categoryHint ?? "";
        rows[id] = new GeneEffectDef(id, name, alias, category);
    }
    private static bool IsFoodEffectCandidate(object row)
    {
        try
        {
            if (GetBool(row, "IsWeaponEnc") || GetBool(row, "IsShieldEnc"))
                return false;
        }
        catch { }

        try
        {
            if (GetBool(row, "isSpell") || GetBool(row, "isTrait") || GetBool(row, "isSkill") || GetBool(row, "isAttribute"))
                return false;
        }
        catch { }

        try
        {
            if (GetStringArray(row, "foodEffect").Length > 0)
                return true;
        }
        catch { }

        var alias = GetString(row, "alias");
        if (string.Equals(alias, "quality", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(alias, "itemQuality", StringComparison.OrdinalIgnoreCase))
            return true;

        var categoryText = (GetString(row, "category") + "," +
                            GetString(row, "categorySub") + "," +
                            GetString(row, "group") + "," +
                            GetString(row, "type") + "," +
                            string.Join(",", GetStringArray(row, "tag"))).ToLowerInvariant();
        return TextHas(categoryText, "food") || TextHas(categoryText, "meal") || TextHas(categoryText, "cook");
    }
    private static bool IsFoodEffectCandidateId(int id)
    {
        var row = FindSourceElementRowById(id);
        return row != null && IsFoodEffectCandidate(row);
    }
    private void OpenFoodEditorWindow(Thing thing)
    {
        if (!CanEditFoodData(thing))
            return;

        _foodEditorTarget = thing;
        _foodEditorName = SafeThingName(thing);
        LoadFoodEditorFields(thing);
        _foodEditorWindowVisible = false;
        if (!IsLGuiInitialized())
            return;
        EnsureLGuiEditorVisible();
        OpenLGuiFoodEditor();
    }
    private void LoadFoodEditorFields(Thing thing)
    {
        _foodEditorLv = thing.LV.ToString(CultureInfo.InvariantCulture);
        _foodEditorEncLv = thing.encLV.ToString(CultureInfo.InvariantCulture);
        _foodEditorMaterialId = thing.idMaterial.ToString(CultureInfo.InvariantCulture);
        _foodEditorRarityValue = thing.rarityLv;
        _foodEditorBlessedStateValue = (int)NormalizeBlessedState((int)thing.blessedState);
        _foodEditorFlagStolen = thing.isStolen;
        _foodEditorFlagCrafted = thing.isCrafted;
        _foodEditorFlagGifted = thing.isGifted;
        _foodEditorFlagReplica = thing.isReplica;
        _foodEditorFlagCopy = thing.isCopy;
        _foodEditorFlagFireproof = thing.isFireproof;
        _foodEditorFlagAcidproof = thing.isAcidproof;
        _foodEditorFlagBroken = thing.isBroken;
        _foodEditorFlagNoSell = thing.noSell;
        _foodEditorFlagLostProperty = thing.isLostProperty;
        _foodEditorWeight = thing.SelfWeight.ToString(CultureInfo.InvariantCulture);
        _foodEditorDecay = Clamp(GetRawFoodDecay(thing), 0, Math.Max(1, thing.MaxDecay)).ToString(CultureInfo.InvariantCulture);
        LoadFoodEffects(thing);
    }
    private void LoadFoodEffects(Thing thing)
    {
        _foodEditorEffects.Clear();
        _foodEditorLoadedEffectIds.Clear();

        try
        {
            var rows = new List<Element>();
            foreach (var element in thing.elements.dict.Values)
                if (element != null && element.id > 0)
                    rows.Add(element);
            rows.Sort((a, b) => a.id.CompareTo(b.id));

            foreach (var element in rows)
            {
                _foodEditorLoadedEffectIds.Add(element.id);
                _foodEditorEffects.Add(new GeneValueInput(
                    element.id.ToString(CultureInfo.InvariantCulture),
                    GetThingElementEditorValue(thing, element).ToString(CultureInfo.InvariantCulture)));
            }
        }
        catch { }
    }
    private string GetFoodEffectName(string idText)
    {
        return GetGeneEffectName(idText);
    }
    private Dictionary<int, int>? BuildFoodEffectValues()
    {
        var values = new Dictionary<int, int>();
        for (var i = 0; i < _foodEditorEffects.Count; i++)
        {
            var row = _foodEditorEffects[i];
            if (string.IsNullOrWhiteSpace(row.ElementId) && string.IsNullOrWhiteSpace(row.Value))
                continue;
            if (!TryParseWeaponEditorInt(row.ElementId, T("食物效果", "Food effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var elementId) ||
                !TryParseWeaponEditorInt(row.Value, T("数值", "Value") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var value))
                return null;
            if (elementId <= 0)
            {
                _log = T("食物效果", "Food effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture) + T(" 必须大于0", " must be greater than 0");
                return null;
            }
            if (values.ContainsKey(elementId))
            {
                _log = T("食物效果重复: ", "Duplicate food effect: ") + elementId.ToString(CultureInfo.InvariantCulture);
                return null;
            }
            values[elementId] = value;
        }
        return values;
    }
    private void ApplyFoodEffectValues(Thing thing, Dictionary<int, int> values)
    {
        foreach (var id in _foodEditorLoadedEffectIds)
        {
            if (!values.ContainsKey(id))
                SetThingElementEditorValue(thing, id, 0);
        }

        foreach (var pair in values)
            SetThingElementEditorValue(thing, pair.Key, pair.Value);
    }
    private void ApplyFoodEditorChange()
    {
        try
        {
            var target = _foodEditorTarget;
            if (!CanEditFoodData(target))
            {
                _log = T("目标食品不存在", "Target food does not exist");
                _foodEditorWindowVisible = false;
                return;
            }

            int lv, encLv, materialId, weight, decay;
            if (!TryParseWeaponEditorInt(_foodEditorLv, T("等级", "Level"), out lv) ||
                !TryParseWeaponEditorInt(_foodEditorEncLv, T("强化", "Enhance"), out encLv) ||
                !TryParseWeaponEditorInt(_foodEditorMaterialId, T("材质ID", "Material ID"), out materialId) ||
                !TryParseWeaponEditorInt(_foodEditorWeight, T("重量", "Weight"), out weight) ||
                !TryParseWeaponEditorInt(_foodEditorDecay, T("腐烂度", "Rot"), out decay))
                return;

            lv = Math.Max(1, lv);
            weight = Math.Max(0, weight);
            var effectValues = BuildFoodEffectValues();
            if (effectValues == null)
                return;

            var thing = target!;
            var maxDecay = Math.Max(1, thing.MaxDecay);
            thing.LV = lv;
            thing.encLV = encLv;
            thing.rarityLv = _foodEditorRarityValue;
            thing.SetBlessedState(NormalizeBlessedState(_foodEditorBlessedStateValue));
            ApplyFoodEditorFlags(thing);
            if (materialId != thing.idMaterial)
            {
                try { thing.ChangeMaterial(materialId, true); }
                catch { thing.idMaterial = materialId; }
            }
            thing.ChangeWeight(weight);
            thing.decay = Clamp(decay, 0, maxDecay);
            ApplyFoodEffectValues(thing, effectValues);

            RefreshInventoryUi();
            RefreshFoodRotOverlayForCard(thing);
            _foodEditorName = SafeThingName(thing);
            LoadFoodEditorFields(thing);
            _log = T("已修改食品数据: ", "Modified food data: ") + _foodEditorName;
            _foodEditorWindowVisible = false;
        }
        catch (Exception ex)
        {
            _log = T("修改食品数据失败: ", "Modify food data failed: ") + ex.Message;
        }
    }
    private void ApplyFoodEditorFlags(Thing thing)
    {
        thing.isStolen = _foodEditorFlagStolen;
        thing.isCrafted = _foodEditorFlagCrafted;
        thing.isGifted = _foodEditorFlagGifted;
        thing.isReplica = _foodEditorFlagReplica;
        thing.isCopy = _foodEditorFlagCopy;
        thing.isFireproof = _foodEditorFlagFireproof;
        thing.isAcidproof = _foodEditorFlagAcidproof;
        thing.isBroken = _foodEditorFlagBroken;
        thing.noSell = _foodEditorFlagNoSell;
        thing.isLostProperty = _foodEditorFlagLostProperty;
    }
}
