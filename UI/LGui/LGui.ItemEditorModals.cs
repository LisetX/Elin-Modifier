using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void OpenLGuiItemDataEditor()
    {
        var modal = CreateLGuiCompleteModal("RuntimeItemDataEditor", T("修改物品数据", "Modify item data") + " | " + _itemDataEditorName, out var content, 1540f, 1010f);
        if (modal == null) return;
        var y = 4f;
        y = AddLGuiSectionTitle(content, T("基础数据", "Base data"), y);
        AddLGuiInlineInput(content, T("等级", "Level"), () => _itemDataEditorLv, value => _itemDataEditorLv = value, 0f, y);
        AddLGuiInlineInput(content, T("强化", "Enhance"), () => _itemDataEditorEncLv, value => _itemDataEditorEncLv = value, 340f, y);
        AddLGuiInlineInput(content, T("材质ID", "Material ID"), () => _itemDataEditorMaterialId, value => _itemDataEditorMaterialId = value, 680f, y);
        AddLGuiInlineInput(content, T("重量", "Weight"), () => _itemDataEditorWeight, value => _itemDataEditorWeight = value, 1020f, y);
        y += 52f;
        AddLGuiInlineInput(content, T("变体ID", "Variant ID"), () => _itemDataEditorSkin, value => _itemDataEditorSkin = value, 0f, y);
        AddLGuiInlineInput(content, T("固定价格", "Fixed price"), () => _itemDataEditorPriceFix, value => _itemDataEditorPriceFix = value, 340f, y);
        AddLGuiInlineInput(content, T("价值", "Value"), () => _itemDataEditorValue, value => _itemDataEditorValue = value, 680f, y);
        AddLGuiInlineInput(content, T("价值修正", "Value bonus"), () => _itemDataEditorValueBonus, value => _itemDataEditorValueBonus = value, 1020f, y);
        y += 60f;
        y = AddLGuiBlessedState(content, y, "ItemBless", () => _itemDataEditorBlessedStateValue, value => _itemDataEditorBlessedStateValue = value, OpenLGuiItemDataEditor);
        y = AddLGuiItemDataFlags(content, y);
        y = AddLGuiRarityButtons(content, _itemDataEditorRarityValue, value => _itemDataEditorRarityValue = value, OpenLGuiItemDataEditor, y);
        y = AddLGuiEffectEditor(content, T("物品附魔", "Item enchantments"), _itemDataEditorEnchantments, GetItemEnchantName, _itemDataEditorTarget, y, OpenLGuiItemDataEditor, () =>
            OpenLGuiEffectReference(T("附魔效果对应表", "Enchant effect table"), GetFilteredItemEnchantIds, () => _itemEnchantFilter, value => _itemEnchantFilter = value, _itemEnchantPage, value => _itemEnchantPage = value, row => _itemDataEditorEnchantments.Add(new GeneValueInput(row.Id.ToString(CultureInfo.InvariantCulture), "0")), OpenLGuiItemDataEditor));
        y += 10f;
        CreateLGuiButton(content, "Apply", T("确认", "Confirm"), 0f, y, 120f, 44f, ApplyLGuiItemDataChange);
        CreateLGuiButton(content, "Cancel", T("取消", "Cancel"), 134f, y, 120f, 44f, CloseLGuiEditorModal);
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 70f));
    }
    private void OpenLGuiFoodEditor()
    {
        var modal = CreateLGuiCompleteModal("RuntimeFoodEditor", T("修改食品数据", "Modify food data") + " | " + _foodEditorName, out var content, 1540f, 1010f);
        if (modal == null) return;
        var y = 4f;
        y = AddLGuiSectionTitle(content, T("基础数据", "Base data"), y);
        AddLGuiInlineInput(content, T("等级", "Level"), () => _foodEditorLv, value => _foodEditorLv = value, 0f, y);
        AddLGuiInlineInput(content, T("强化", "Enhance"), () => _foodEditorEncLv, value => _foodEditorEncLv = value, 340f, y);
        AddLGuiInlineInput(content, T("材质ID", "Material ID"), () => _foodEditorMaterialId, value => _foodEditorMaterialId = value, 680f, y);
        AddLGuiInlineInput(content, T("重量", "Weight"), () => _foodEditorWeight, value => _foodEditorWeight = value, 1020f, y);
        y += 52f;
        AddLGuiInlineInput(content, T("腐烂度", "Rot"), () => _foodEditorDecay, value => _foodEditorDecay = value, 0f, y);
        y += 60f;
        y = AddLGuiBlessedState(content, y, "FoodBless", () => _foodEditorBlessedStateValue, value => _foodEditorBlessedStateValue = value, OpenLGuiFoodEditor);
        y = AddLGuiFoodFlags(content, y);
        y = AddLGuiRarityButtons(content, _foodEditorRarityValue, value => _foodEditorRarityValue = value, OpenLGuiFoodEditor, y);
        y = AddLGuiEffectEditor(content, T("加成效果", "Bonus effects"), _foodEditorEffects, GetFoodEffectName, _foodEditorTarget, y, OpenLGuiFoodEditor, () =>
            OpenLGuiEffectReference(T("食物效果对应表", "Food effect table"), GetFilteredFoodEffectIds, () => _foodEffectFilter, value => _foodEffectFilter = value, _foodEffectPage, value => _foodEffectPage = value, row => _foodEditorEffects.Add(new GeneValueInput(row.Id.ToString(CultureInfo.InvariantCulture), "0")), OpenLGuiFoodEditor));
        y += 10f;
        CreateLGuiButton(content, "Apply", T("确认", "Confirm"), 0f, y, 120f, 44f, ApplyLGuiFoodDataChange);
        CreateLGuiButton(content, "Cancel", T("取消", "Cancel"), 134f, y, 120f, 44f, CloseLGuiEditorModal);
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 70f));
    }
    private float AddLGuiWeaponFlags(RectTransform content, float y)
    {
        y = AddLGuiBlessedState(content, y, "WeaponBless", () => _weaponEditorBlessedStateValue, value => _weaponEditorBlessedStateValue = value, OpenLGuiWeaponEditor);
        var flags = new List<Tuple<string, Func<bool>, Action<bool>>>
        {
            Tuple.Create(T("偷窃", "Stolen"), (Func<bool>)(() => _weaponEditorFlagStolen), (Action<bool>)(value => _weaponEditorFlagStolen = value)),
            Tuple.Create(T("制作", "Crafted"), (Func<bool>)(() => _weaponEditorFlagCrafted), (Action<bool>)(value => _weaponEditorFlagCrafted = value)),
            Tuple.Create(T("赠礼", "Gifted"), (Func<bool>)(() => _weaponEditorFlagGifted), (Action<bool>)(value => _weaponEditorFlagGifted = value)),
            Tuple.Create(T("复制品", "Replica"), (Func<bool>)(() => _weaponEditorFlagReplica), (Action<bool>)(value => _weaponEditorFlagReplica = value)),
            Tuple.Create(T("复制", "Copy"), (Func<bool>)(() => _weaponEditorFlagCopy), (Action<bool>)(value => _weaponEditorFlagCopy = value)),
            Tuple.Create(T("耐火", "Fireproof"), (Func<bool>)(() => _weaponEditorFlagFireproof), (Action<bool>)(value => _weaponEditorFlagFireproof = value)),
            Tuple.Create(T("耐酸", "Acidproof"), (Func<bool>)(() => _weaponEditorFlagAcidproof), (Action<bool>)(value => _weaponEditorFlagAcidproof = value)),
            Tuple.Create(T("损坏", "Broken"), (Func<bool>)(() => _weaponEditorFlagBroken), (Action<bool>)(value => _weaponEditorFlagBroken = value)),
            Tuple.Create(T("不可出售", "No sell"), (Func<bool>)(() => _weaponEditorFlagNoSell), (Action<bool>)(value => _weaponEditorFlagNoSell = value)),
            Tuple.Create(T("失物", "Lost property"), (Func<bool>)(() => _weaponEditorFlagLostProperty), (Action<bool>)(value => _weaponEditorFlagLostProperty = value))
        };
        for (var i = 0; i < flags.Count; i++)
        {
            var flag = flags[i];
            var column = i % 5;
            var line = i / 5;
            var toggle = CreateLGuiToggle(content, "Flag" + i, column * 270f, y + line * 48f, 250f, 42f, out var label);
            label.text = flag.Item1;
            toggle.isOn = flag.Item2();
            toggle.onValueChanged.AddListener(flag.Item3.Invoke);
        }
        return y + 106f;
    }
    private void OpenLGuiWeaponEditor()
    {
        var modal = CreateLGuiCompleteModal("RuntimeWeaponEditor", T("修改武器数据", "Modify weapon data") + " | " + _weaponEditorName, out var content, 1600f, 1030f);
        if (modal == null) return;
        var y = 4f;
        y = AddLGuiSectionTitle(content, T("基础数据", "Base data"), y);
        var fields = new List<Tuple<string, Func<string>, Action<string>>>
        {
            Tuple.Create(T("等级", "Level"), (Func<string>)(() => _weaponEditorLv), (Action<string>)(value => _weaponEditorLv = value)),
            Tuple.Create(T("强化", "Enhance"), (Func<string>)(() => _weaponEditorEncLv), (Action<string>)(value => _weaponEditorEncLv = value)),
            Tuple.Create(T("材质ID", "Material ID"), (Func<string>)(() => _weaponEditorMaterialId), (Action<string>)(value => _weaponEditorMaterialId = value)),
            Tuple.Create(T("伤害骰面", "Damage dice sides"), (Func<string>)(() => _weaponEditorDiceDim), (Action<string>)(value => _weaponEditorDiceDim = value)),
            Tuple.Create(T("命中", "Hit"), (Func<string>)(() => _weaponEditorHit), (Action<string>)(value => _weaponEditorHit = value)),
            Tuple.Create(T("伤害修正", "Damage bonus"), (Func<string>)(() => _weaponEditorDamage), (Action<string>)(value => _weaponEditorDamage = value)),
            Tuple.Create("DV", (Func<string>)(() => _weaponEditorDv), (Action<string>)(value => _weaponEditorDv = value)),
            Tuple.Create("PV", (Func<string>)(() => _weaponEditorPv), (Action<string>)(value => _weaponEditorPv = value)),
            Tuple.Create(T("重量", "Weight"), (Func<string>)(() => _weaponEditorWeight), (Action<string>)(value => _weaponEditorWeight = value)),
            Tuple.Create(T("充能", "Charges"), (Func<string>)(() => _weaponEditorCharges), (Action<string>)(value => _weaponEditorCharges = value)),
            Tuple.Create(T("弹药", "Ammo"), (Func<string>)(() => _weaponEditorAmmo), (Action<string>)(value => _weaponEditorAmmo = value)),
            Tuple.Create(T("射程", "Range"), (Func<string>)(() => _weaponEditorRangeText), (Action<string>)(value => _weaponEditorRangeText = value)),
            Tuple.Create(T("穿透", "Penetration"), (Func<string>)(() => _weaponEditorPenetrationText), (Action<string>)(value => _weaponEditorPenetrationText = value)),
            Tuple.Create(T("改造槽位", "Modification slots"), (Func<string>)(() => _weaponEditorModificationSlots), (Action<string>)(value => _weaponEditorModificationSlots = value))
        };
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var column = i % 3;
            var line = i / 3;
            AddLGuiInlineInput(content, field.Item1, field.Item2, field.Item3, column * 450f, y + line * 50f, 150f, 180f);
        }
        y += ((fields.Count + 2) / 3) * 50f;
        y = AddLGuiWeaponFlags(content, y);
        y = AddLGuiRarityButtons(content, _weaponEditorRarityValue, value => _weaponEditorRarityValue = value, OpenLGuiWeaponEditor, y);
        y = AddLGuiEffectEditor(content, T("武器附魔", "Weapon enchantments"), _weaponEditorEnchantments, GetWeaponEnchantName, _weaponEditorTarget, y, OpenLGuiWeaponEditor, () =>
            OpenLGuiEffectReference(T("武器附魔对应表", "Weapon enchant table"), GetFilteredWeaponEnchantIds, () => _weaponEnchantFilter, value => _weaponEnchantFilter = value, _weaponEnchantPage, value => _weaponEnchantPage = value, row => _weaponEditorEnchantments.Add(new GeneValueInput(row.Id.ToString(CultureInfo.InvariantCulture), "0")), OpenLGuiWeaponEditor));
        y += 10f;
        CreateLGuiButton(content, "Apply", T("确认", "Confirm"), 0f, y, 120f, 44f, ApplyLGuiWeaponDataChange);
        CreateLGuiButton(content, "Cancel", T("取消", "Cancel"), 134f, y, 120f, 44f, CloseLGuiEditorModal);
        content.sizeDelta = new Vector2(0f, Math.Max(850f, y + 70f));
    }
}
