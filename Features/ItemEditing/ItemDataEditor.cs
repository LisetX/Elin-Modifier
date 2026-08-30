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
    private List<GeneEffectDef> GetFilteredItemEnchantIds()
    {
        EnsureItemEnchantEffectRows();
        if (_lastItemEnchantFilter != _itemEnchantFilter)
        {
            _itemEnchantPage = 0;
            _lastItemEnchantFilter = _itemEnchantFilter;
        }

        var result = new List<GeneEffectDef>();
        var filter = (_itemEnchantFilter ?? "").Trim().ToLowerInvariant();
        if (_itemEnchantEffectRows == null)
            return result;

        foreach (var effect in _itemEnchantEffectRows)
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
    private void EnsureItemEnchantEffectRows()
    {
        if (_itemEnchantEffectRows != null)
        {
            AddCurrentItemEnchantEffectRows(_itemEnchantEffectRows);
            return;
        }

        var rows = new Dictionary<int, GeneEffectDef>();
        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            AddItemEnchantEffectRow(rows, id, GetString(row, "category"));
        }

        foreach (var input in _itemDataEditorEnchantments)
        {
            if (int.TryParse((input.ElementId ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                AddItemEnchantEffectRow(rows, id, "Current item");
        }

        _itemEnchantEffectRows = new List<GeneEffectDef>(rows.Values);
        _itemEnchantEffectRows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void AddCurrentItemEnchantEffectRows(List<GeneEffectDef> rows)
    {
        if (rows == null)
            return;

        var existing = new HashSet<int>();
        foreach (var row in rows)
            existing.Add(row.Id);

        var added = false;
        var map = new Dictionary<int, GeneEffectDef>();
        foreach (var input in _itemDataEditorEnchantments)
        {
            if (!int.TryParse((input.ElementId ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || existing.Contains(id))
                continue;
            AddItemEnchantEffectRow(map, id, "Current item");
        }
        foreach (var row in map.Values)
        {
            rows.Add(row);
            added = true;
        }
        if (added)
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void AddItemEnchantEffectRow(Dictionary<int, GeneEffectDef> rows, int id, string categoryHint)
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
    private void AddWeaponEnchantEffectRow(Dictionary<int, GeneEffectDef> rows, int id, string categoryHint)
    {
        AddItemEnchantEffectRow(rows, id, categoryHint);
    }
    private void AddFixedAttributeEffectRows(Dictionary<int, GeneEffectDef> rows, string categoryHint)
    {
        foreach (var id in FixedAttributeEffectIds)
            AddItemEnchantEffectRow(rows, id, categoryHint);
    }
    private static void AddFixedEffectRows(List<GeneEffectDef> rows, Action<Dictionary<int, GeneEffectDef>> addRows)
    {
        if (rows == null || addRows == null)
            return;

        var existing = new HashSet<int>();
        foreach (var row in rows)
            existing.Add(row.Id);

        var map = new Dictionary<int, GeneEffectDef>();
        addRows(map);
        var added = false;
        foreach (var row in map.Values)
        {
            if (existing.Contains(row.Id))
                continue;
            rows.Add(row);
            added = true;
        }
        if (added)
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void OpenItemDataEditorWindow(Thing thing)
    {
        if (!CanEditItemData(thing))
            return;

        _itemDataEditorTarget = thing;
        _itemDataEditorName = SafeThingName(thing);
        LoadItemDataEditorFields(thing);
        _itemDataEditorWindowVisible = false;
        if (!IsLGuiInitialized())
            return;
        EnsureLGuiEditorVisible();
        OpenLGuiItemDataEditor();
    }
    private void LoadItemDataEditorFields(Thing thing)
    {
        _itemDataEditorLv = thing.LV.ToString(CultureInfo.InvariantCulture);
        _itemDataEditorEncLv = thing.encLV.ToString(CultureInfo.InvariantCulture);
        _itemDataEditorMaterialId = thing.idMaterial.ToString(CultureInfo.InvariantCulture);
        _itemDataEditorRarityValue = thing.rarityLv;
        _itemDataEditorBlessedStateValue = (int)NormalizeBlessedState((int)thing.blessedState);
        _itemDataEditorFlagStolen = thing.isStolen;
        _itemDataEditorFlagCrafted = thing.isCrafted;
        _itemDataEditorFlagGifted = thing.isGifted;
        _itemDataEditorFlagReplica = thing.isReplica;
        _itemDataEditorFlagCopy = thing.isCopy;
        _itemDataEditorFlagFireproof = thing.isFireproof;
        _itemDataEditorFlagAcidproof = thing.isAcidproof;
        _itemDataEditorFlagBroken = thing.isBroken;
        _itemDataEditorFlagNoSell = thing.noSell;
        _itemDataEditorFlagLostProperty = thing.isLostProperty;
        _itemDataEditorWeight = thing.SelfWeight.ToString(CultureInfo.InvariantCulture);
        _itemDataEditorSkin = thing.idSkin.ToString(CultureInfo.InvariantCulture);
        _itemDataEditorLoadedPriceFixRaw = thing.c_priceFix;
        _itemDataEditorLoadedValueRaw = thing.c_fixedValue;
        _itemDataEditorLoadedPriceFixText = GetItemDataPriceFixText(thing);
        _itemDataEditorLoadedValueText = GetItemDataValueText(thing);
        _itemDataEditorPriceFix = _itemDataEditorLoadedPriceFixText;
        _itemDataEditorValue = _itemDataEditorLoadedValueText;
        _itemDataEditorValueBonus = thing.c_priceAdd.ToString(CultureInfo.InvariantCulture);
        LoadItemDataEnchantments(thing);
    }
    private static string GetItemDataPriceFixText(Thing thing)
    {
        try
        {
            if (thing.c_priceFix != 0)
                return thing.c_priceFix.ToString(CultureInfo.InvariantCulture);
        }
        catch { }

        return SafeIntText(() => thing.GetPrice(CurrencyType.Money, false, PriceType.Default, GameAccess.Characters.PlayerCharacter), "0");
    }
    internal static string GetItemDataValueText(Thing thing)
    {
        try
        {
            if (thing.c_fixedValue != 0)
                return thing.c_fixedValue.ToString(CultureInfo.InvariantCulture);
        }
        catch { }

        return SafeIntText(() => thing.GetValue(PriceType.Default, false), "0");
    }
    private static bool ShouldKeepLoadedFallback(int rawValue, string loadedText, string currentText)
    {
        return rawValue == 0 &&
               string.Equals((loadedText ?? "").Trim(), (currentText ?? "").Trim(), StringComparison.Ordinal);
    }
    private void LoadItemDataEnchantments(Thing thing)
    {
        _itemDataEditorEnchantments.Clear();
        _itemDataEditorLoadedEnchantIds.Clear();

        try
        {
            var rows = new List<Element>();
            foreach (var element in thing.elements.dict.Values)
                if (element != null && element.id > 0)
                    rows.Add(element);
            rows.Sort((a, b) => a.id.CompareTo(b.id));

            foreach (var element in rows)
            {
                _itemDataEditorLoadedEnchantIds.Add(element.id);
                _itemDataEditorEnchantments.Add(new GeneValueInput(
                    element.id.ToString(CultureInfo.InvariantCulture),
                    GetThingElementEditorValue(thing, element).ToString(CultureInfo.InvariantCulture)));
            }
        }
        catch { }
    }
    private string GetItemEnchantName(string idText)
    {
        return GetGeneEffectName(idText);
    }
    private Dictionary<int, int>? BuildItemDataEnchantValues()
    {
        var values = new Dictionary<int, int>();
        for (var i = 0; i < _itemDataEditorEnchantments.Count; i++)
        {
            var row = _itemDataEditorEnchantments[i];
            if (string.IsNullOrWhiteSpace(row.ElementId) && string.IsNullOrWhiteSpace(row.Value))
                continue;
            if (!TryParseWeaponEditorInt(row.ElementId, T("附魔效果", "Enchant effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var elementId) ||
                !TryParseWeaponEditorInt(row.Value, T("数值", "Value") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var value))
                return null;
            if (elementId <= 0)
            {
                _log = T("附魔效果", "Enchant effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture) + T(" 必须大于0", " must be greater than 0");
                return null;
            }
            if (values.ContainsKey(elementId))
            {
                _log = T("附魔效果重复: ", "Duplicate enchant effect: ") + elementId.ToString(CultureInfo.InvariantCulture);
                return null;
            }
            values[elementId] = value;
        }
        return values;
    }
    private void ApplyItemDataEnchantValues(Thing thing, Dictionary<int, int> values)
    {
        foreach (var id in _itemDataEditorLoadedEnchantIds)
        {
            if (!values.ContainsKey(id))
                SetThingElementEditorValue(thing, id, 0);
        }

        foreach (var pair in values)
            SetThingElementEditorValue(thing, pair.Key, pair.Value);
    }
    private void ApplyItemDataEditorChange()
    {
        try
        {
            var target = _itemDataEditorTarget;
            if (!CanEditItemData(target))
            {
                _log = T("目标物品不存在", "Target item does not exist");
                _itemDataEditorWindowVisible = false;
                return;
            }

            int lv, encLv, materialId, weight, skin, priceFix, value, valueBonus;
            if (!TryParseWeaponEditorInt(_itemDataEditorLv, T("等级", "Level"), out lv) ||
                !TryParseWeaponEditorInt(_itemDataEditorEncLv, T("强化", "Enhance"), out encLv) ||
                !TryParseWeaponEditorInt(_itemDataEditorMaterialId, T("材质ID", "Material ID"), out materialId) ||
                !TryParseWeaponEditorInt(_itemDataEditorWeight, T("重量", "Weight"), out weight) ||
                !TryParseWeaponEditorInt(_itemDataEditorSkin, T("变体ID", "Variant ID"), out skin) ||
                !TryParseWeaponEditorInt(_itemDataEditorPriceFix, T("固定价格", "Fixed price"), out priceFix) ||
                !TryParseWeaponEditorInt(_itemDataEditorValue, T("价值", "Value"), out value) ||
                !TryParseWeaponEditorInt(_itemDataEditorValueBonus, T("价值修正", "Value bonus"), out valueBonus))
                return;

            lv = Math.Max(1, lv);
            weight = Math.Max(0, weight);
            var enchantValues = BuildItemDataEnchantValues();
            if (enchantValues == null)
                return;

            var thing = target!;
            thing.LV = lv;
            thing.encLV = encLv;
            thing.rarityLv = _itemDataEditorRarityValue;
            thing.SetBlessedState(NormalizeBlessedState(_itemDataEditorBlessedStateValue));
            ApplyItemDataEditorFlags(thing);
            if (materialId != thing.idMaterial)
            {
                try { thing.ChangeMaterial(materialId, true); }
                catch { thing.idMaterial = materialId; }
            }
            thing.ChangeWeight(weight);
            thing.idSkin = skin;
            thing.c_priceFix = ShouldKeepLoadedFallback(_itemDataEditorLoadedPriceFixRaw, _itemDataEditorLoadedPriceFixText, _itemDataEditorPriceFix) ? 0 : priceFix;
            thing.c_fixedValue = ShouldKeepLoadedFallback(_itemDataEditorLoadedValueRaw, _itemDataEditorLoadedValueText, _itemDataEditorValue) ? 0 : value;
            thing.c_priceAdd = valueBonus;
            ApplyItemDataEnchantValues(thing, enchantValues);

            RefreshInventoryUi();
            _itemDataEditorName = SafeThingName(thing);
            LoadItemDataEditorFields(thing);
            _log = T("已修改物品数据: ", "Modified item data: ") + _itemDataEditorName;
            _itemDataEditorWindowVisible = false;
        }
        catch (Exception ex)
        {
            _log = T("修改物品数据失败: ", "Modify item data failed: ") + ex.Message;
        }
    }
    private void ApplyItemDataEditorFlags(Thing thing)
    {
        thing.isStolen = _itemDataEditorFlagStolen;
        thing.isCrafted = _itemDataEditorFlagCrafted;
        thing.isGifted = _itemDataEditorFlagGifted;
        thing.isReplica = _itemDataEditorFlagReplica;
        thing.isCopy = _itemDataEditorFlagCopy;
        thing.isFireproof = _itemDataEditorFlagFireproof;
        thing.isAcidproof = _itemDataEditorFlagAcidproof;
        thing.isBroken = _itemDataEditorFlagBroken;
        thing.noSell = _itemDataEditorFlagNoSell;
        thing.isLostProperty = _itemDataEditorFlagLostProperty;
    }
}
