using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    void ILGuiRowHandler.OnLGuiRowFavorite(LGuiRowView row)
    {
        if (row.BoundData is LGuiFeatureRow feature)
            ToggleIndependentFeatureFavorite(feature.Id);
    }

    void ILGuiRowHandler.OnLGuiRowPrimary(LGuiRowView row)
    {
        switch (row.BoundData)
        {
            case LGuiFeatureRow feature when feature.Id == LGuiFeatureId.SimulateAdvance:
                _simulateAdvanceMinutesText = row.Input.text;
                ExecuteSimulatedAdvance();
                break;
            case LGuiFeatureRow feature when feature.Id == LGuiFeatureId.GenerateDungeon:
                _generateDungeonDangerText = row.Input.text;
                ExecuteGenerateDungeon();
                break;
            case LGuiFeatureRow feature when CanConfigureLGuiFeature(feature.Id):
                OpenLGuiFeatureConfiguration(feature.Id);
                break;
            case LGuiCharacterRow header when header.IsHeader && !string.IsNullOrEmpty(header.SectionKey):
                _lGuiCharacterSectionExpanded[header.SectionKey] = !header.Expanded;
                RebuildLGuiCharacterRows();
                break;
            case LGuiCharacterRow ability when ability.Ability != null && ability.Target != null:
                OpenLGuiAbilityEditor(ability.Target, ability.Ability, ability.IsPc);
                break;
            case LGuiCharacterRow action when action.Action != LGuiCharacterAction.None:
                ApplyLGuiCharacterAction(action, row);
                break;
            case LGuiCharacterRow character:
                ApplyLGuiCharacterRow(character, row);
                break;
            case ItemDef item:
                SpawnItem(item);
                break;
            case NpcDef npc:
                _npcSpawnId = npc.Id;
                if (_lGuiNpcIdInput != null && !string.Equals(_lGuiNpcIdInput.text, _npcSpawnId, StringComparison.Ordinal))
                    _lGuiNpcIdInput.text = _npcSpawnId;
                _npcLog = T("已填入: ", "Selected: ") + npc.DisplayName;
                break;
            case LGuiHomeRow home when home.IsHeader && !string.IsNullOrEmpty(home.SectionKey):
                _lGuiHomeSectionExpanded[home.SectionKey] = !home.Expanded;
                RebuildLGuiHomeRows();
                break;
            case LGuiHomeRow home when !home.IsHeader && home.Apply != null:
                var homeText = row.Input.text;
                _inputs[home.InputKey] = homeText;
                if (int.TryParse(homeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var homeValue))
                    home.Apply(homeValue);
                else
                    _homeLog = home.Label + T(" 输入不是数字", " input is not a number");
                break;
            case ProbabilityModule.ProbabilityRow probability when probability.IsHeader:
                _modules.Probability.ToggleCategory(probability);
                break;
            case ProbabilityModule.ProbabilityRow probability when probability.Entry != null:
                _modules.Probability.ApplyRow(probability, row.Input.text);
                break;
            case LGuiDebugRow debug:
                if (debug.Member.CanWrite && IsDebugEditableType(debug.ValueType) && debug.ValueType != typeof(bool))
                {
                    _debugInputs[debug.Key] = row.Input.text;
                    ApplyDebugValue(debug.Key, new DebugBinding(debug.Instance, debug.Member), debug.ValueType);
                }
                else if (debug.Value != null && !IsDebugLeafType(debug.ValueType))
                {
                    _lGuiDebugObjectStack.Add(_lGuiDebugTarget!);
                    _lGuiDebugPathStack.Add(_lGuiDebugTargetLabel + "." + debug.Member.Name);
                    _lGuiDebugTarget = debug.Value;
                    _lGuiDebugTargetLabel = _lGuiDebugPathStack[_lGuiDebugPathStack.Count - 1];
                    _lGuiDebugTargetPath = debug.Key;
                    RebuildLGuiDebugRows();
                    if (_lGuiDebugTargetText != null)
                        _lGuiDebugTargetText.text = _lGuiDebugTargetLabel;
                }
                break;
            case LGuiEmpRow emp:
                if (emp.Function.ValueParameters.Count > 0)
                    OpenLGuiEmpValueEditor(emp);
                else
                {
                    emp.State.PendingApply = true;
                    emp.State.Initialized = false;
                    ApplyEmpFunctionStateNow(emp.Plugin, emp.Function, emp.State, true);
                }
                break;
        }
        _lGuiDataDirty = true;
    }
    private bool TryParseLGuiCharacterActionInt(string text, string label, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            _log = label + T(" 输入不是数字", " input is not a number");
            return false;
        }
        return true;
    }
    private void ApplyLGuiCharacterAction(LGuiCharacterRow model, LGuiRowView view)
    {
        var target = model.Target;
        if (target == null)
            return;

        var text = view.Input.text;
        if (!string.IsNullOrEmpty(model.InputKey))
            _inputs[model.InputKey] = text;

        switch (model.Action)
        {
            case LGuiCharacterAction.NpcAffinity:
                if (TryParseLGuiCharacterActionInt(text, T("好感度", "Affinity"), out var affinity))
                    SetNpcAffinity(target, affinity);
                break;
            case LGuiCharacterAction.NpcRelationshipOption:
                if (model.ActionIndex >= 0 && model.ActionIndex < RelationshipOptions.Length)
                    SetNpcHostility(target, RelationshipOptions[model.ActionIndex].Value);
                break;
            case LGuiCharacterAction.NpcPartyAction:
                switch (model.ActionPayload)
                {
                    case "joinParty": MakeNpcPartyMember(target); break;
                    case "leaveParty": RemoveNpcPartyMember(target); break;
                    case "joinFaction": AddNpcToPlayerFactionOnly(target); break;
                    case "leaveFaction": RemoveNpcFromPlayerFactionOnly(target); break;
                }
                break;
            case LGuiCharacterAction.NpcFaith:
                EnsureFaithRows();
                if (_faithRows != null && model.ActionIndex >= 0 && model.ActionIndex < _faithRows.Count)
                    SetNpcFaith(target, _faithRows[model.ActionIndex]);
                break;
            case LGuiCharacterAction.FaithSelect:
                var selectableFaiths = GetLGuiSelectableFaithRows();
                var faithTargetUid = -1;
                try { faithTargetUid = target.uid; }
                catch { }
                var faithSelectionIndex = _lGuiFaithSelectionTargetUid == faithTargetUid
                    ? _lGuiFaithSelectionIndex
                    : model.ActionIndex;
                if (faithSelectionIndex >= 0 && faithSelectionIndex < selectableFaiths.Count)
                {
                    SetPlayerFaith(target, selectableFaiths[faithSelectionIndex]);
                    _lGuiFaithSelectionTargetUid = faithTargetUid;
                    _lGuiFaithSelectionIndex = faithSelectionIndex;
                }
                break;
            case LGuiCharacterAction.FaithPiety:
                if (!TryParseLGuiCharacterActionInt(text, T("虔诚度", "Piety"), out var pietyValue))
                    break;
                pietyValue = Math.Max(0, pietyValue);
                target.elements.SetBase(85, pietyValue);
                target.RefreshFaithElement();
                _log = T("已修改虔诚度: ", "Piety updated: ") + pietyValue.ToString(CultureInfo.InvariantCulture);
                break;
            case LGuiCharacterAction.NpcGeneSelect:
                var genes = GetNpcGeneList(target);
                if (model.ActionIndex >= 0 && model.ActionIndex < genes.Count)
                    LoadNpcGeneEditorFields(target, genes[model.ActionIndex], model.ActionIndex);
                break;
            case LGuiCharacterAction.NpcGeneApply:
                ApplyNpcGeneChange(target, model.IsPc);
                break;
            case LGuiCharacterAction.NpcGeneAdd:
                AddNpcGene(target, model.IsPc);
                break;
            case LGuiCharacterAction.NpcGeneTypePopup:
                OpenLGuiNpcGeneTypeSelector(target, false);
                return;
            case LGuiCharacterAction.NpcGeneField:
                ApplyLGuiNpcGeneField(target, model.ActionPayload, text, model.IsPc);
                break;
            case LGuiCharacterAction.NpcGeneEffectId:
            case LGuiCharacterAction.NpcGeneEffectValue:
                if (model.ActionIndex >= 0 && model.ActionIndex < _npcGeneEditorValues.Count)
                {
                    if (model.Action == LGuiCharacterAction.NpcGeneEffectId)
                        _npcGeneEditorValues[model.ActionIndex].ElementId = text;
                    else
                        _npcGeneEditorValues[model.ActionIndex].Value = text;
                    ApplyNpcGeneChange(target, model.IsPc);
                }
                break;
            case LGuiCharacterAction.NpcGeneEffectAdd:
                _npcGeneEditorValues.Add(new GeneValueInput("", "0"));
                break;
            case LGuiCharacterAction.NpcGeneEffectTablePopup:
                OpenLGuiNpcGeneEffectReference(target);
                return;
            case LGuiCharacterAction.EtherSelect:
                var diseases = GetCurrentEtherDiseases(target);
                if (model.ActionIndex >= 0 && model.ActionIndex < diseases.Count)
                    LoadEtherDiseaseEditorFields(target, diseases[model.ActionIndex], model.ActionIndex);
                break;
            case LGuiCharacterAction.EtherApply:
                ApplyEtherDiseaseChange(target, model.IsPc);
                break;
            case LGuiCharacterAction.EtherAdd:
                AddEtherDisease(target, model.IsPc);
                break;
            case LGuiCharacterAction.EtherField:
                if (model.ActionPayload == "id")
                    _etherDiseaseId = text;
                else if (model.ActionPayload == "value")
                    _etherDiseaseValue = text;
                ApplyEtherDiseaseChange(target, model.IsPc);
                break;
            case LGuiCharacterAction.EtherTablePopup:
                OpenLGuiEtherDiseaseReference(target, model.IsPc);
                return;
        }

        MarkCharacterDataDirty();
        RebuildLGuiCharacterRows();
    }
    private void ApplyLGuiNpcGeneField(
        Chara target,
        string field,
        string value,
        bool isPc)
    {
        switch (field)
        {
            case "source": _npcGeneSourceId = value; break;
            case "level": _npcGeneLv = value; break;
            case "seed": _npcGeneSeed = value; break;
            case "cost": _npcGeneCost = value; break;
            case "slot": _npcGeneSlot = value; break;
        }
        ApplyNpcGeneChange(target, isPc);
    }
    void ILGuiRowHandler.OnLGuiRowAuxiliary(LGuiRowView row)
    {
        if (row.BoundData is LGuiCharacterRow action && action.Target != null)
        {
            if (action.Action == LGuiCharacterAction.NpcGeneSelect)
                DeleteNpcGeneAt(action.Target, action.ActionIndex, action.IsPc);
            else if (action.Action == LGuiCharacterAction.NpcGeneEffectId && action.ActionIndex >= 0 && action.ActionIndex < _npcGeneEditorValues.Count)
            {
                _npcGeneEditorValues.RemoveAt(action.ActionIndex);
                ApplyNpcGeneChange(action.Target, action.IsPc);
            }
            else if (action.Action == LGuiCharacterAction.EtherSelect)
            {
                var diseases = GetCurrentEtherDiseases(action.Target);
                if (action.ActionIndex >= 0 && action.ActionIndex < diseases.Count)
                    DeleteEtherDisease(action.Target, diseases[action.ActionIndex], action.IsPc);
            }
            else
                goto ContinueAuxiliary;
            MarkCharacterDataDirty();
            RebuildLGuiCharacterRows();
            return;
        }
    ContinueAuxiliary:
        if (row.BoundData is ProbabilityModule.ProbabilityRow probability && probability.Entry != null)
        {
            _modules.Probability.RestoreRow(probability);
            return;
        }
        if (row.BoundData is LGuiCharacterRow character && character.Row != null && character.Target != null && !character.IsPotential)
        {
            RemoveRowValue(character.Target, character.Row);
            MarkCharacterDataDirty();
            RebuildLGuiCharacterRows();
            return;
        }
        if (!(row.BoundData is LGuiDebugRow debug) || debug.Error.Length > 0)
            return;
        var locked = _debugLocks.TryGetValue(debug.Key, out var current) && current;
        locked = !locked;
        _debugLocks[debug.Key] = locked;
        if (locked)
            _debugBindings[debug.Key] = new DebugBinding(debug.Instance, debug.Member);
        else
            _debugBindings.Remove(debug.Key);
        _lGuiDataDirty = true;
        _lGuiDebugList?.RefreshBoundRows();
    }
    void ILGuiRowHandler.OnLGuiRowChoice(LGuiRowView row, int choiceIndex)
    {
        if (!(row.BoundData is LGuiCharacterRow model) || model.Target == null)
            return;

        var target = model.Target;
        switch (model.Action)
        {
            case LGuiCharacterAction.NpcRelationshipChoices:
                if (choiceIndex >= 0 && choiceIndex < RelationshipOptions.Length)
                    SetNpcHostility(target, RelationshipOptions[choiceIndex].Value);
                break;
            case LGuiCharacterAction.NpcPartyChoices:
                switch (choiceIndex)
                {
                    case 0: MakeNpcPartyMember(target); break;
                    case 1: RemoveNpcPartyMember(target); break;
                    case 2: AddNpcToPlayerFactionOnly(target); break;
                    case 3: RemoveNpcFromPlayerFactionOnly(target); break;
                }
                break;
            case LGuiCharacterAction.NpcFaithChoices:
                EnsureFaithRows();
                var faithIndex = model.ActionIndex + choiceIndex;
                if (_faithRows != null && choiceIndex >= 0 && choiceIndex < 3 && faithIndex >= 0 && faithIndex < _faithRows.Count)
                    SetNpcFaith(target, _faithRows[faithIndex]);
                break;
            default:
                return;
        }

        MarkCharacterDataDirty();
        RebuildLGuiCharacterRows();
        _lGuiDataDirty = true;
    }
    void ILGuiRowHandler.OnLGuiRowDropdown(LGuiRowView row, int optionIndex)
    {
        if (!(row.BoundData is LGuiCharacterRow model) || model.Target == null ||
            model.Action != LGuiCharacterAction.FaithSelect)
            return;

        var faiths = GetLGuiSelectableFaithRows();
        if (optionIndex < 0 || optionIndex >= faiths.Count)
            return;

        try { _lGuiFaithSelectionTargetUid = model.Target.uid; }
        catch { _lGuiFaithSelectionTargetUid = -1; }
        _lGuiFaithSelectionIndex = optionIndex;
    }
    void ILGuiRowHandler.OnLGuiRowToggle(LGuiRowView row, bool value)
    {
        switch (row.BoundData)
        {
            case LGuiFeatureRow feature:
                SetLGuiFeatureValue(feature.Id, value);
                break;
            case LGuiCharacterRow character when character.Row != null && character.Target != null:
                _locks[GetLGuiCharacterInputKey(character)] = value;
                break;
            case LGuiEmpRow emp:
                if (emp.Function.Kind == EmpFunctionKind.Value && emp.Function.ValueKind == EmpValueKind.Bool)
                    emp.State.Value = value ? "true" : "false";
                else
                    emp.State.Enabled = value;
                emp.State.PendingApply = true;
                emp.State.Initialized = false;
                ApplyEmpFunctionStateNow(emp.Plugin, emp.Function, emp.State, true);
                break;
            case LGuiDebugRow debug when debug.ValueType == typeof(bool):
                _debugInputs[debug.Key] = value ? "true" : "false";
                ApplyDebugValue(debug.Key, new DebugBinding(debug.Instance, debug.Member), debug.ValueType);
                break;
            case LGuiHomeRow home when home.SetActive != null:
                home.SetActive(value);
                break;
        }
        _lGuiDataDirty = true;
    }
    void ILGuiRowHandler.OnLGuiRowInput(LGuiRowView row, string value)
    {
        switch (row.BoundData)
        {
            case LGuiFeatureRow feature when feature.Id == LGuiFeatureId.SimulateAdvance:
                _simulateAdvanceMinutesText = value ?? "60";
                break;
            case LGuiFeatureRow feature when feature.Id == LGuiFeatureId.GenerateDungeon:
                _generateDungeonDangerText = value ?? DungeonGenerationPolicy.DefaultRequestedDanger.ToString(CultureInfo.InvariantCulture);
                break;
            case LGuiCharacterRow header when header.IsHeader && !string.IsNullOrEmpty(header.SectionKey):
                _lGuiCharacterSectionFilters[header.SectionKey] = value ?? "";
                break;
            case LGuiCharacterRow action when action.Action != LGuiCharacterAction.None && !string.IsNullOrEmpty(action.InputKey):
                _inputs[action.InputKey] = value ?? "";
                break;
            case LGuiCharacterRow character:
                _inputs[GetLGuiCharacterInputKey(character)] = value ?? "";
                break;
            case LGuiEmpRow emp:
                emp.State.Value = value ?? "";
                break;
            case LGuiDebugRow debug:
                _debugInputs[debug.Key] = value ?? "";
                break;
            case LGuiHomeRow home:
                _inputs[home.InputKey] = value ?? "";
                break;
            case ProbabilityModule.ProbabilityRow probability when probability.Entry != null:
                _modules.Probability.SetRowInput(probability, value);
                break;
        }
    }
    void ILGuiRowHandler.OnLGuiRowInputCommit(LGuiRowView row, string value)
    {
        if (row.BoundData is LGuiCharacterRow header && header.IsHeader && !string.IsNullOrEmpty(header.SectionKey))
        {
            _lGuiCharacterSectionFilters[header.SectionKey] = value ?? "";
            RebuildLGuiCharacterRows();
        }
    }
    private void ApplyLGuiCharacterRow(LGuiCharacterRow model, LGuiRowView view)
    {
        if (model.Row == null || model.Target == null)
            return;
        var key = GetLGuiCharacterInputKey(model);
        var text = view.Input.text;
        _inputs[key] = text;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            _log = GetRowLabel(model.Row) + T(" 输入不是数字", " input is not a number");
            return;
        }
        if (model.IsPotential)
            SetElementPotential(model.Target, model.Row.Key, value);
        else
            ApplyValue(model.Target, model.Row, value, model.IsPc);
        MarkCharacterDataDirty();
    }
    private string GetLGuiCharacterInputKey(LGuiCharacterRow model)
    {
        if (model.Row == null || model.Target == null)
            return "runtime:none";
        return GetTargetInputPrefix(model.Target, model.IsPc) + model.Row.Kind + ":" + model.Row.Key + (model.IsPotential ? ":potential" : "");
    }
}
