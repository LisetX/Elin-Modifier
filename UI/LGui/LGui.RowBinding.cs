using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BindLGuiFeatureRow(RectTransform rect, LGuiFeatureRow model, int index)
    {
        var view = rect.GetComponent<LGuiRowView>();
        view.BeginBind();
        view.BoundData = model;
        view.BoundIndex = index;
        ApplyLGuiRowVisual(view, index);
        view.Icon.gameObject.SetActive(false);
        view.Label.gameObject.SetActive(true);
        view.Label.text = model.Label;
        view.Label.fontStyle = FontStyle.Normal;
        if (model.Id == LGuiFeatureId.SimulateAdvance || model.Id == LGuiFeatureId.GenerateDungeon)
        {
            var simulateAdvance = model.Id == LGuiFeatureId.SimulateAdvance;
            view.Secondary.gameObject.SetActive(true);
            view.Secondary.text = simulateAdvance
                ? T("推进时间（分钟）", "Advance time (minutes)")
                : T("危险度", "Danger level");
            view.Input.gameObject.SetActive(true);
            view.Input.contentType = InputField.ContentType.IntegerNumber;
            if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != view.Input.gameObject)
                view.SetInputWithoutNotify(simulateAdvance ? _simulateAdvanceMinutesText : _generateDungeonDangerText);
            view.Toggle.gameObject.SetActive(false);
            view.Primary.gameObject.SetActive(true);
            view.PrimaryText.text = T("执行", "Run");
            view.Auxiliary.gameObject.SetActive(false);
            view.EndBind();
            return;
        }
        view.Secondary.gameObject.SetActive(true);
        view.Secondary.text = GetLGuiFeatureValue(model.Id) ? T("已开启", "On") : T("已关闭", "Off");
        view.Input.gameObject.SetActive(false);
        view.Toggle.gameObject.SetActive(true);
        view.ToggleLabel.text = T("开启", "Enable");
        view.SetToggleWithoutNotify(GetLGuiFeatureValue(model.Id));
        view.Primary.gameObject.SetActive(CanConfigureLGuiFeature(model.Id));
        view.PrimaryText.text = T("设置", "Configure");
        view.Auxiliary.gameObject.SetActive(false);
        view.EndBind();
    }
    private void BindLGuiCharacterRow(RectTransform rect, LGuiCharacterRow model, int index)
    {
        var view = rect.GetComponent<LGuiRowView>();
        view.BeginBind();
        view.BoundData = model;
        view.BoundIndex = index;
        view.Icon.gameObject.SetActive(false);
        view.Primary.gameObject.SetActive(false);
        view.Auxiliary.gameObject.SetActive(false);
        view.Toggle.gameObject.SetActive(false);
        view.Input.gameObject.SetActive(false);
        view.Secondary.gameObject.SetActive(false);
        view.Label.gameObject.SetActive(true);
        PlaceLGuiRect(view.Secondary.rectTransform, 480f, 4f, 250f, 50f);
        PlaceLGuiRect((RectTransform)view.Primary.transform, 1080f, 8f, 130f, 42f);
        HideLGuiRowChoices(view);
        if (view.Dropdown != null && model.Action != LGuiCharacterAction.FaithSelect)
            view.Dropdown.gameObject.SetActive(false);
        if (view.Input.placeholder is Text placeholder)
            placeholder.text = "";

        if (model.IsHeader || model.Target == null)
        {
            ApplyLGuiRowVisual(view, index, true);
            view.Label.text = IndentLGuiText(model.Header, model.Depth);
            view.Label.fontStyle = FontStyle.Normal;
            view.Secondary.gameObject.SetActive(false);
            view.Input.gameObject.SetActive(model.SupportsFilter);
            if (model.SupportsFilter)
            {
                if (view.Input.placeholder is Text filterPlaceholder)
                {
                    filterPlaceholder.text = T("过滤", "Filter");
                    filterPlaceholder.fontStyle = FontStyle.Italic;
                }
                _lGuiCharacterSectionFilters.TryGetValue(model.SectionKey, out var filter);
                if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != view.Input.gameObject)
                    view.SetInputWithoutNotify(filter ?? "");
            }
            view.Primary.gameObject.SetActive(!string.IsNullOrEmpty(model.SectionKey));
            view.PrimaryText.text = model.Expanded ? T("折叠", "Collapse") : T("展开", "Expand");
            view.Auxiliary.gameObject.SetActive(false);
            view.EndBind();
            return;
        }

        if (model.Action != LGuiCharacterAction.None)
        {
            if (model.Action == LGuiCharacterAction.NpcRelationshipChoices ||
                model.Action == LGuiCharacterAction.NpcPartyChoices ||
                model.Action == LGuiCharacterAction.NpcFaithChoices)
            {
                BindLGuiCharacterChoiceRow(view, model, index);
                view.EndBind();
                return;
            }
            if (model.Action == LGuiCharacterAction.FaithSelect)
            {
                BindLGuiFaithDropdownRow(view, model, index);
                view.EndBind();
                return;
            }
            ApplyLGuiRowVisual(view, index);
            view.Label.fontStyle = FontStyle.Normal;
            view.Label.text = IndentLGuiText(model.Header, model.Depth);
            view.Secondary.gameObject.SetActive(!string.IsNullOrEmpty(model.ActionSummary));
            view.Secondary.text = model.ActionSummary;
            var hasInput = !string.IsNullOrEmpty(model.InputKey);
            view.Input.gameObject.SetActive(hasInput);
            if (hasInput)
            {
                if (view.Input.placeholder is Text actionPlaceholder)
                    actionPlaceholder.text = T("输入", "Input");
                if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != view.Input.gameObject)
                    view.SetInputWithoutNotify(_inputs.TryGetValue(model.InputKey, out var actionInput) ? actionInput : "");
            }
            view.Primary.gameObject.SetActive(model.Action != LGuiCharacterAction.ReadOnly);
            view.PrimaryText.text = GetLGuiCharacterActionPrimaryLabel(model.Action);
            var canDeleteAction = model.Action == LGuiCharacterAction.NpcGeneSelect ||
                                  model.Action == LGuiCharacterAction.NpcGeneEffectId ||
                                  model.Action == LGuiCharacterAction.EtherSelect;
            view.Auxiliary.gameObject.SetActive(canDeleteAction);
            view.AuxiliaryText.text = T("删除", "Delete");
            view.EndBind();
            return;
        }

        if (model.Ability != null)
        {
            ApplyLGuiRowVisual(view, index);
            var abilityIcon = GetAbilityIcon(model.Ability);
            view.Icon.gameObject.SetActive(abilityIcon != null);
            view.Icon.sprite = abilityIcon;
            view.Icon.preserveAspect = true;
            view.Label.fontStyle = FontStyle.Normal;
            view.Label.text = IndentLGuiText(GetAbilityLabel(model.Ability), model.Depth);
            view.Secondary.gameObject.SetActive(true);
            view.Secondary.text = GetAbilitySummary(model.Target, model.Ability);
            view.Input.gameObject.SetActive(false);
            view.Toggle.gameObject.SetActive(false);
            view.Primary.gameObject.SetActive(true);
            view.PrimaryText.text = T("编辑", "Edit");
            view.Auxiliary.gameObject.SetActive(false);
            view.EndBind();
            return;
        }

        var rowDef = model.Row!;
        ApplyLGuiRowVisual(view, index);
        view.Label.fontStyle = FontStyle.Normal;
        view.Label.text = IndentLGuiText(model.IsPotential ? GetRowLabel(rowDef) + " - " + T("潜力", "Potential") : GetRowLabel(rowDef), model.Depth);
        view.Secondary.gameObject.SetActive(true);
        view.Secondary.text = T("当前: ", "Current: ") + (model.IsPotential ? CurrentPotentialValue(model.Target, rowDef) : CurrentValue(model.Target, rowDef, model.IsPc));
        var inputKey = GetLGuiCharacterInputKey(model);
        if (!_inputs.TryGetValue(inputKey, out var input))
        {
            input = model.IsPotential ? CurrentPotentialValue(model.Target, rowDef) : CurrentValue(model.Target, rowDef, model.IsPc);
            _inputs[inputKey] = input;
        }
        view.Input.gameObject.SetActive(true);
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != view.Input.gameObject)
            view.SetInputWithoutNotify(input);
        if (!model.IsPotential && CanLock(rowDef))
        {
            view.Toggle.gameObject.SetActive(true);
            view.ToggleLabel.text = T("锁定", "Lock");
            view.SetToggleWithoutNotify(_locks.TryGetValue(inputKey, out var locked) && locked);
        }
        view.Primary.gameObject.SetActive(true);
        view.PrimaryText.text = model.IsPotential ? T("应用潜力", "Apply potential") : T("应用", "Apply");
        var canDelete = !model.IsPotential && (rowDef.Kind == RowKind.Element || rowDef.Kind == RowKind.Feat);
        view.Auxiliary.gameObject.SetActive(canDelete);
        view.AuxiliaryText.text = T("删除", "Delete");
        view.EndBind();
    }
    private void BindLGuiFaithDropdownRow(LGuiRowView view, LGuiCharacterRow model, int index)
    {
        ApplyLGuiRowVisual(view, index);
        view.Label.fontStyle = FontStyle.Normal;
        view.Label.text = IndentLGuiText(model.Header, model.Depth);
        view.Secondary.gameObject.SetActive(true);
        view.Secondary.text = T("当前信仰:", "Current faith: ") + model.ActionSummary;
        PlaceLGuiRect(view.Secondary.rectTransform, 480f, 4f, 330f, 50f);
        view.Input.gameObject.SetActive(false);
        view.Toggle.gameObject.SetActive(false);
        view.Primary.gameObject.SetActive(true);
        view.PrimaryText.text = T("应用", "Apply");
        PlaceLGuiRect((RectTransform)view.Primary.transform, 1170f, 8f, 140f, 42f);
        view.Auxiliary.gameObject.SetActive(false);
        HideLGuiRowChoices(view);

        var target = model.Target;
        var dropdown = view.Dropdown;
        if (target == null || dropdown == null)
            return;

        var faiths = GetLGuiSelectableFaithRows();
        var labels = new List<string>(faiths.Count);
        for (var i = 0; i < faiths.Count; i++)
            labels.Add(GetLGuiFaithButtonLabel(faiths[i]));
        var selectedIndex = Clamp(_lGuiFaithSelectionIndex, 0, Math.Max(0, faiths.Count - 1));
        if (!(dropdown is AutomationDropdown runtimeDropdown) || !runtimeDropdown.IsListOpen)
            view.SetDropdownWithoutNotify(labels, selectedIndex);
        PlaceLGuiRect((RectTransform)dropdown.transform, 830f, 8f, 320f, 42f);
        dropdown.gameObject.SetActive(labels.Count > 0);
    }
    private string GetLGuiCharacterActionPrimaryLabel(LGuiCharacterAction action)
    {
        switch (action)
        {
            case LGuiCharacterAction.NpcRelationshipOption:
            case LGuiCharacterAction.NpcPartyAction:
            case LGuiCharacterAction.NpcFaith:
            case LGuiCharacterAction.FaithSelect:
            case LGuiCharacterAction.NpcGeneTypePopup:
            case LGuiCharacterAction.NpcGeneSelect:
            case LGuiCharacterAction.EtherSelect:
                return T("选择", "Select");
            case LGuiCharacterAction.NpcGeneEffectTablePopup:
            case LGuiCharacterAction.EtherTablePopup:
                return T("打开", "Open");
            case LGuiCharacterAction.NpcGeneAdd:
            case LGuiCharacterAction.NpcGeneEffectAdd:
            case LGuiCharacterAction.EtherAdd:
                return T("新增", "Add");
            default:
                return T("应用", "Apply");
        }
    }
    private void BindLGuiItemRow(RectTransform rect, ItemDef item, int index)
    {
        var view = rect.GetComponent<LGuiRowView>();
        view.BeginBind();
        view.BoundData = item;
        view.BoundIndex = index;
        ApplyLGuiRowVisual(view, index);
        view.Icon.gameObject.SetActive(true);
        view.Icon.sprite = GetItemIcon(item);
        view.Icon.preserveAspect = true;
        view.Label.gameObject.SetActive(true);
        view.Label.fontStyle = FontStyle.Normal;
        view.Label.text = item.DisplayName;
        view.Secondary.gameObject.SetActive(true);
        view.Secondary.text = item.Id;
        view.Input.gameObject.SetActive(false);
        view.Toggle.gameObject.SetActive(false);
        view.Primary.gameObject.SetActive(true);
        view.PrimaryText.text = T("生成", "Spawn");
        view.Auxiliary.gameObject.SetActive(false);
        view.EndBind();
    }
    private void BindLGuiNpcRow(RectTransform rect, NpcDef npc, int index)
    {
        var view = rect.GetComponent<LGuiRowView>();
        view.BeginBind();
        view.BoundData = npc;
        view.BoundIndex = index;
        ApplyLGuiRowVisual(view, index);
        view.Icon.gameObject.SetActive(true);
        view.Icon.sprite = GetNpcIcon(npc);
        view.Icon.preserveAspect = true;
        view.Label.gameObject.SetActive(true);
        view.Label.fontStyle = FontStyle.Normal;
        view.Label.text = npc.DisplayName;
        view.Secondary.gameObject.SetActive(true);
        view.Secondary.text = npc.Id + " | " + npc.Race + " | " + npc.Job;
        view.Input.gameObject.SetActive(false);
        view.Toggle.gameObject.SetActive(false);
        view.Primary.gameObject.SetActive(true);
        view.PrimaryText.text = T("填入", "Use");
        view.Auxiliary.gameObject.SetActive(false);
        view.EndBind();
    }
    private void BindLGuiHomeRow(RectTransform rect, LGuiHomeRow model, int index)
    {
        var view = rect.GetComponent<LGuiRowView>();
        view.BeginBind();
        view.BoundData = model;
        view.BoundIndex = index;
        view.Icon.gameObject.SetActive(false);
        view.Label.gameObject.SetActive(true);
        view.Label.text = IndentLGuiText(model.Label, model.Depth);
        if (model.IsHeader || model.Current == null)
        {
            ApplyLGuiRowVisual(view, index, true);
            view.Label.fontStyle = FontStyle.Normal;
            view.Secondary.gameObject.SetActive(false);
            view.Input.gameObject.SetActive(false);
            view.Toggle.gameObject.SetActive(false);
            view.Primary.gameObject.SetActive(!string.IsNullOrEmpty(model.SectionKey));
            view.PrimaryText.text = model.Expanded ? T("折叠", "Collapse") : T("展开", "Expand");
            view.Auxiliary.gameObject.SetActive(false);
            view.EndBind();
            return;
        }

        ApplyLGuiRowVisual(view, index);
        view.Label.fontStyle = FontStyle.Normal;
        view.Secondary.gameObject.SetActive(true);
        view.Secondary.text = T("当前: ", "Current: ") + model.Current();
        view.Input.gameObject.SetActive(true);
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != view.Input.gameObject)
            view.SetInputWithoutNotify(_inputs.TryGetValue(model.InputKey, out var value) ? value : model.Current());
        var hasToggle = model.IsActive != null && model.SetActive != null;
        view.Toggle.gameObject.SetActive(hasToggle);
        if (hasToggle)
        {
            view.ToggleLabel.text = T("政策启用", "Active");
            view.SetToggleWithoutNotify(model.IsActive!());
        }
        view.Primary.gameObject.SetActive(true);
        view.PrimaryText.text = T("应用", "Apply");
        view.Auxiliary.gameObject.SetActive(false);
        view.EndBind();
    }
    private void BindLGuiEmpRow(RectTransform rect, LGuiEmpRow model, int index)
    {
        var view = rect.GetComponent<LGuiRowView>();
        view.BeginBind();
        view.BoundData = model;
        view.BoundIndex = index;
        ApplyLGuiRowVisual(view, index);
        view.Icon.gameObject.SetActive(false);
        view.Label.gameObject.SetActive(true);
        view.Label.fontStyle = FontStyle.Normal;
        view.Label.text = SafeEmpText(model.Plugin.Name, model.Plugin.Id) + " / " + SafeEmpText(model.Function.Name, model.Function.Id);
        view.Secondary.gameObject.SetActive(true);
        view.Secondary.text = model.Function.Kind == EmpFunctionKind.Patch
            ? GetEmpFunctionKindDisplayName(model.Function.Kind) +
              (model.State.Initialized && model.State.Enabled && model.State.LastApplySucceeded ? " : on" : " : off")
            : GetEmpFunctionKindDisplayName(model.Function.Kind) + (model.State.Initialized ? " | ready" : " | pending");
        var isBooleanValue = model.Function.Kind == EmpFunctionKind.Value && model.Function.ValueKind == EmpValueKind.Bool;
        var hasToggle = model.Function.Kind == EmpFunctionKind.Toggle ||
                        model.Function.Kind == EmpFunctionKind.Patch ||
                        isBooleanValue;
        view.Toggle.gameObject.SetActive(hasToggle);
        if (hasToggle)
        {
            view.ToggleLabel.text = T("开启", "Enable");
            view.SetToggleWithoutNotify(isBooleanValue
                ? ParseEmpBool(model.State.Value, model.Function.DefaultEnabled)
                : model.State.Enabled);
        }
        var hasInput = model.Function.Kind == EmpFunctionKind.Value && model.Function.ValueKind != EmpValueKind.Bool;
        view.Input.gameObject.SetActive(hasInput);
        if (hasInput && (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != view.Input.gameObject))
            view.SetInputWithoutNotify(model.State.Value);
        var showPrimary = model.Function.Kind == EmpFunctionKind.Button ||
                          (model.Function.Kind == EmpFunctionKind.Value && model.Function.ValueKind != EmpValueKind.Bool);
        view.Primary.gameObject.SetActive(showPrimary);
        view.PrimaryText.text = model.Function.Kind == EmpFunctionKind.Button ? T("执行", "Run") : T("应用", "Apply");
        view.Auxiliary.gameObject.SetActive(false);
        view.EndBind();
    }
}
