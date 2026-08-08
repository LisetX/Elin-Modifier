using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiCharacterPage()
    {
        var toolbar = CreateLGuiRect(_lGuiPageHost!, "CharacterToolbar");
        AnchorLGuiTop(toolbar, 0f, 174f, 0f, 0f);
        CreateLGuiButton(toolbar, "PC", T("玩家", "Player"), 0f, 5f, 110f, 46f, () => SelectLGuiCharacterTarget(0));
        CreateLGuiButton(toolbar, "Talk", T("对话NPC", "Dialogue NPC"), 118f, 5f, 140f, 46f, () => SelectLGuiCharacterTarget(1));
        CreateLGuiButton(toolbar, "Near", T("附近NPC", "Nearby NPC"), 266f, 5f, 140f, 46f, () => SelectLGuiCharacterTarget(2));
        CreateLGuiButton(toolbar, "PrevNpc", "◀", 414f, 5f, 44f, 46f, () => CycleLGuiNearbyNpc(-1));
        CreateLGuiButton(toolbar, "NextNpc", "▶", 464f, 5f, 44f, 46f, () => CycleLGuiNearbyNpc(1));
        _lGuiCharacterTargetText = CreateLGuiText(toolbar, "CharacterTarget", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiCharacterTargetText.rectTransform, 530f, 5f, 360f, 46f);
        if (_targetTab == 2)
            CreateLGuiButton(toolbar, "NearbySelector", T("选择附近NPC", "Select nearby NPC"), 902f, 5f, 168f, 46f, OpenLGuiNearbyNpcSelector);
        var actionX = 0f;
        if (_targetTab == 0 || _targetTab == 2)
        {
            CreateLGuiButton(toolbar, "Teleport", _targetTab == 0 ? T("传送", "Teleport") : T("传送至NPC", "Teleport to NPC"), actionX, 63f, 140f, 46f, OpenLGuiCharacterTeleport);
        }

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "CharacterList", 120f);
        _lGuiCharacterList = new VirtualList<LGuiCharacterRow>(scroll, 58f, 18, CreateLGuiCharacterVirtualRow, BindLGuiCharacterRow);
        RebuildLGuiCharacterRows();
    }
    private void SelectLGuiCharacterTarget(int tab)
    {
        _targetTab = Clamp(tab, 0, 2);
        MarkCharacterDataDirty();
        SwitchLGuiPage(LGuiPage.Character);
    }
    private void CycleLGuiNearbyNpc(int direction)
    {
        _targetTab = 2;
        var rows = GetSortedNearbyNpcs();
        if (rows.Count == 0)
            return;
        var index = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Uid == _nearbyNpcSelectedUid)
            {
                index = i;
                break;
            }
        }
        index = (index + direction) % rows.Count;
        if (index < 0)
            index += rows.Count;
        _nearbyNpcSelectedUid = rows[index].Uid;
        MarkCharacterDataDirty();
        SwitchLGuiPage(LGuiPage.Character);
    }
    private void RebuildLGuiCharacterRows()
    {
        if (_lGuiCharacterList == null)
            return;
        _lGuiCharacterRows.Clear();
        Chara? target = null;
        try { target = GetCurrentDataTarget(); }
        catch { }
        if (target == null)
        {
            if (_lGuiCharacterTargetText != null)
                _lGuiCharacterTargetText.text = T("未获取到人物数据", "No character data");
            _lGuiCharacterRows.Add(new LGuiCharacterRow(T("未获取到人物数据", "No character data")));
            _lGuiCharacterList.SetItems(_lGuiCharacterRows);
            return;
        }

        var isPc = _targetTab == 0;
        if (_lGuiCharacterTargetText != null)
            _lGuiCharacterTargetText.text = SafeName(target);
        try { _lGuiCharacterTargetUid = target.uid; }
        catch { _lGuiCharacterTargetUid = -1; }
        AddLGuiCharacterSection("status", T("状态", "Status"), target, isPc ? _statusRows : _npcStatusRows, isPc);
        EnsureGameRows();
        AddLGuiCharacterSection("attributes", T("主能力", "Main Attributes"), target, _attributeRows, isPc);
        if (isPc)
            AddLGuiCharacterSection("evaluation", T("评价和影响力", "Evaluation and Influence"), target, _playerRows, true);
        EnsureResistRows();
        AddLGuiCharacterSection("resistances", T("抗性", "Resistances"), target, _resistRows, isPc);
        var existingValueIds = GetExistingCharacterValueIds(target);
        AddLGuiCharacterSection("skills", T("技能", "Skills"), target, _skillRows, isPc, existingValueIds);
        AddLGuiCharacterSection("feats", T("专长", "Feats"), target, _featRows, isPc, existingValueIds);
        AddLGuiAbilitySection(target, isPc, existingValueIds);
        if (isPc)
            AddLGuiCharacterActionSection("faithLevels", T("信仰", "Faith"), target, true);
        if (!isPc)
        {
            AddLGuiCharacterActionSection("npcRelationship", T("NPC关系与信仰", "NPC relation & faith"), target, false);
            AddLGuiCharacterActionSection("npcGene", T("基因编辑", "Gene editor"), target, false);
        }
        AddLGuiCharacterActionSection("etherDisease", T("以太病编辑", "Ether disease editor"), target, isPc);
        _lGuiCharacterList.SetItems(_lGuiCharacterRows);
    }
    private void AddLGuiCharacterActionSection(string sectionKey, string title, Chara target, bool isPc)
    {
        if (!_lGuiCharacterSectionExpanded.TryGetValue(sectionKey, out var expanded))
            expanded = false;
        _lGuiCharacterRows.Add(new LGuiCharacterRow(title, sectionKey, expanded, false));
        if (!expanded)
            return;

        switch (sectionKey)
        {
            case "npcRelationship":
                AddLGuiNpcRelationshipRows(target);
                break;
            case "npcGene":
                AddLGuiNpcGeneRows(target);
                break;
            case "etherDisease":
                AddLGuiEtherDiseaseRows(target, isPc);
                break;
            case "faithLevels":
                AddLGuiFaithLevelRows(target);
                break;
        }
    }
    private void AddLGuiFaithLevelRows(Chara target)
    {
        var piety = 0;
        try { piety = target.elements.GetOrCreateElement(85).vBase; }
        catch { }
        AddLGuiCharacterActionInput(
            target,
            true,
            T("虔诚度", "Piety"),
            T("当前: ", "Current: ") + piety.ToString(CultureInfo.InvariantCulture),
            LGuiCharacterAction.FaithPiety,
            "faithPiety",
            piety.ToString(CultureInfo.InvariantCulture));

        var faiths = GetLGuiSelectableFaithRows();
        var currentFaithId = SafeText(() => target.idFaith, "");
        var currentIndex = 0;
        var currentName = GetNpcFaithNameOnly(target);
        for (var i = 0; i < faiths.Count; i++)
        {
            var faith = faiths[i];
            if (!string.Equals(currentFaithId, faith.Id, StringComparison.OrdinalIgnoreCase))
                continue;
            currentIndex = i;
            currentName = GetLGuiFaithButtonLabel(faith);
            break;
        }

        var targetUid = -1;
        try { targetUid = target.uid; }
        catch { }
        if (_lGuiFaithSelectionTargetUid != targetUid ||
            _lGuiFaithSelectionIndex < 0 || _lGuiFaithSelectionIndex >= faiths.Count)
        {
            _lGuiFaithSelectionTargetUid = targetUid;
            _lGuiFaithSelectionIndex = currentIndex;
        }

        if (faiths.Count == 0)
            AddLGuiCharacterAction(target, true, T("信仰选择", "Faith selection"), LGuiCharacterAction.ReadOnly, -1, "", T("当前: 无", "Current: None"));
        else
            AddLGuiCharacterAction(target, true, T("信仰选择", "Faith selection"), LGuiCharacterAction.FaithSelect, _lGuiFaithSelectionIndex, "", currentName);
    }
    private List<FaithDef> GetLGuiSelectableFaithRows()
    {
        EnsureFaithRows();
        var result = new List<FaithDef>();
        if (_faithRows == null)
            return result;
        for (var i = 0; i < _faithRows.Count; i++)
        {
            var faith = _faithRows[i];
            if (faith != null && TryFindReligion(faith.Id) != null)
                result.Add(faith);
        }
        return result;
    }
    private string GetLGuiCharacterActionInputKey(Chara target, string field, int index = -1)
    {
        var suffix = index < 0 ? field : field + ":" + index.ToString(CultureInfo.InvariantCulture);
        return GetTargetInputPrefix(target, _targetTab == 0) + ":inline:" + suffix;
    }
    private void AddLGuiCharacterActionInput(Chara target, bool isPc, string label, string summary, LGuiCharacterAction action, string field, string value, int index = -1, int depth = 1)
    {
        var key = GetLGuiCharacterActionInputKey(target, field, index);
        _inputs[key] = value ?? "";
        _lGuiCharacterRows.Add(new LGuiCharacterRow(label, target, isPc, action, index, key, field, summary, depth));
    }
    private void AddLGuiNpcRelationshipRows(Chara target)
    {
        SyncNpcRelationshipInputs(target);
        AddLGuiCharacterActionInput(target, false, T("好感度", "Affinity"), T("当前: ", "Current: ") + GetNpcAffinity(target), LGuiCharacterAction.NpcAffinity, "affinity", _npcAffinityInput);

        _lGuiCharacterRows.Add(new LGuiCharacterRow(
            T("关系状态", "Relationship"), target, false, LGuiCharacterAction.NpcRelationshipChoices));
        _lGuiCharacterRows.Add(new LGuiCharacterRow(
            T("队伍状态", "Party state") + ": " + GetNpcPartyState(target), target, false, LGuiCharacterAction.NpcPartyChoices));

        EnsureFaithRows();
        var faiths = _faithRows ?? new List<FaithDef>();
        const int faithsPerRow = 3;
        for (var i = 0; i < faiths.Count; i += faithsPerRow)
        {
            _lGuiCharacterRows.Add(new LGuiCharacterRow(
                i == 0 ? T("信仰", "Faith") : "",
                target, false, LGuiCharacterAction.NpcFaithChoices, i));
        }
        if (faiths.Count == 0)
            AddLGuiCharacterAction(target, false, T("信仰: 无", "Faith: None"), LGuiCharacterAction.ReadOnly);
    }
    private static string GetLGuiFaithButtonLabel(FaithDef faith)
    {
        if (faith == null)
            return "";
        return string.IsNullOrWhiteSpace(faith.Name) ? faith.Id : faith.Name;
    }
    private string GetNpcFaithNameOnly(Chara target)
    {
        if (target == null)
            return "-";
        try
        {
            var id = target.idFaith ?? "";
            var faith = TryFindReligion(id);
            var name = faith == null ? "" : SafeText(() => faith.Name, "");
            return string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(id) ? "-" : id) : name;
        }
        catch
        {
            return "?";
        }
    }
    private void AddLGuiCharacterAction(Chara target, bool isPc, string label, LGuiCharacterAction action, int index = -1, string payload = "", string summary = "", int depth = 1)
    {
        _lGuiCharacterRows.Add(new LGuiCharacterRow(label, target, isPc, action, index, "", payload, summary, depth));
    }
    private void AddLGuiNpcGeneRows(Chara target)
    {
        SyncNpcGeneEditorState(target);
        var genes = GetNpcGeneList(target);
        AddLGuiCharacterAction(target, false, T("应用当前基因修改", "Apply gene changes"), LGuiCharacterAction.NpcGeneApply);
        AddLGuiCharacterAction(target, false, T("新增基因", "Add gene"), LGuiCharacterAction.NpcGeneAdd);

        for (var i = 0; i < genes.Count; i++)
        {
            var prefix = i == _npcGeneSelectedIndex ? "-> " : "";
            _lGuiCharacterRows.Add(new LGuiCharacterRow(
                prefix + (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + GetNpcGeneSummary(genes[i]),
                target, false, LGuiCharacterAction.NpcGeneSelect, i, "", "", i == _npcGeneSelectedIndex ? T("当前选中", "Selected") : "", 2));
        }

        if (genes.Count == 0)
            AddLGuiCharacterAction(target, false, T("-> 无", "-> None"), LGuiCharacterAction.ReadOnly, -1, "", "", 2);

        AddLGuiCharacterActionInput(target, false, T("源ID", "Source ID"), T("当前: ", "Current: ") + _npcGeneSourceId, LGuiCharacterAction.NpcGeneField, "source", _npcGeneSourceId);
        AddLGuiCharacterAction(target, false, T("类别", "Category"), LGuiCharacterAction.NpcGeneTypePopup, -1, "", T("当前: ", "Current: ") + GetNpcGeneTypeLabel(_npcGeneTypeIndex));
        AddLGuiCharacterActionInput(target, false, T("等级", "Level"), T("当前: ", "Current: ") + _npcGeneLv, LGuiCharacterAction.NpcGeneField, "level", _npcGeneLv);
        AddLGuiCharacterActionInput(target, false, T("种子", "Seed"), T("当前: ", "Current: ") + _npcGeneSeed, LGuiCharacterAction.NpcGeneField, "seed", _npcGeneSeed);
        AddLGuiCharacterActionInput(target, false, T("费用", "Cost"), T("当前: ", "Current: ") + _npcGeneCost, LGuiCharacterAction.NpcGeneField, "cost", _npcGeneCost);
        AddLGuiCharacterActionInput(target, false, T("槽位", "Slots"), T("当前: ", "Current: ") + _npcGeneSlot, LGuiCharacterAction.NpcGeneField, "slot", _npcGeneSlot);

        for (var i = 0; i < _npcGeneEditorValues.Count; i++)
        {
            var value = _npcGeneEditorValues[i];
            AddLGuiCharacterActionInput(target, false, T("基因效果ID", "Gene effect ID") + ": " + GetGeneEffectName(value.ElementId), T("当前: ", "Current: ") + value.ElementId, LGuiCharacterAction.NpcGeneEffectId, "effectId", value.ElementId, i, 2);
            AddLGuiCharacterActionInput(target, false, T("基因效果数值", "Gene effect value"), T("当前: ", "Current: ") + value.Value, LGuiCharacterAction.NpcGeneEffectValue, "effectValue", value.Value, i, 2);
        }
        AddLGuiCharacterAction(target, false, T("添加基因效果", "Add gene effect"), LGuiCharacterAction.NpcGeneEffectAdd, -1, "", "", 2);
        AddLGuiCharacterAction(target, false, T("基因对应表", "Gene table"), LGuiCharacterAction.NpcGeneEffectTablePopup, -1, "", "", 2);
    }
    private void AddLGuiEtherDiseaseRows(Chara target, bool isPc)
    {
        EnsureEtherDiseaseRows();
        SyncEtherDiseaseEditorState(target);
        var diseases = GetCurrentEtherDiseases(target);
        AddLGuiCharacterAction(target, isPc, T("应用当前以太病修改", "Apply ether disease changes"), LGuiCharacterAction.EtherApply);
        AddLGuiCharacterAction(target, isPc, T("新增以太病", "Add ether disease"), LGuiCharacterAction.EtherAdd);
        for (var i = 0; i < diseases.Count; i++)
        {
            var prefix = i == _etherDiseaseSelectedIndex ? "-> " : "";
            _lGuiCharacterRows.Add(new LGuiCharacterRow(
                prefix + (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + GetEtherDiseaseSummary(target, diseases[i]),
                target, isPc, LGuiCharacterAction.EtherSelect, i, "", "", i == _etherDiseaseSelectedIndex ? T("当前选中", "Selected") : "", 2));
        }
        if (diseases.Count == 0)
            AddLGuiCharacterAction(target, isPc, T("-> 无", "-> None"), LGuiCharacterAction.ReadOnly, -1, "", "", 2);
        AddLGuiCharacterActionInput(target, isPc, T("以太病ID", "Ether disease ID"), T("当前: ", "Current: ") + _etherDiseaseId, LGuiCharacterAction.EtherField, "id", _etherDiseaseId);
        AddLGuiCharacterActionInput(target, isPc, T("等级", "Level"), T("当前: ", "Current: ") + _etherDiseaseValue, LGuiCharacterAction.EtherField, "value", _etherDiseaseValue);
        AddLGuiCharacterAction(target, isPc, T("以太病对应表", "Ether disease table"), LGuiCharacterAction.EtherTablePopup, -1, "", "", 2);
    }
    private void AddLGuiAbilitySection(Chara target, bool isPc, HashSet<int>? existingValueIds = null)
    {
        EnsureAbilityRows();
        const string sectionKey = "abilities";
        if (!_lGuiCharacterSectionExpanded.TryGetValue(sectionKey, out var expanded))
            expanded = false;
        _lGuiCharacterSectionFilters.TryGetValue(sectionKey, out var filter);
        filter ??= "";
        _lGuiCharacterRows.Add(new LGuiCharacterRow(T("能力&咒语", "Abilities & Spells"), sectionKey, expanded));
        if (!expanded)
            return;
        var visible = new List<AbilityDef>();
        for (var i = 0; i < _abilityRows.Count; i++)
        {
            var ability = _abilityRows[i];
            if (PassAbilityFilter(ability, filter))
                visible.Add(ability);
        }
        if (existingValueIds != null)
            visible.Sort((a, b) => CompareAbilitiesExistingFirst(a, b, existingValueIds));
        for (var i = 0; i < visible.Count; i++)
            _lGuiCharacterRows.Add(new LGuiCharacterRow(visible[i], target, isPc));
    }
    private void AddLGuiCharacterSection(string sectionKey, string title, Chara target, IEnumerable<RowDef> rows, bool isPc, HashSet<int>? existingValueIds = null)
    {
        if (!_lGuiCharacterSectionExpanded.TryGetValue(sectionKey, out var expanded))
            expanded = false;
        _lGuiCharacterSectionFilters.TryGetValue(sectionKey, out var sectionFilter);
        sectionFilter ??= "";
        var visible = new List<RowDef>();
        foreach (var row in rows)
        {
            if (LGuiFilterMatches(GetRowLabel(row), row.Key, row.Alias, sectionFilter))
                visible.Add(row);
        }
        if (existingValueIds != null)
            visible.Sort((a, b) => CompareRowsExistingFirst(a, b, existingValueIds));
        _lGuiCharacterRows.Add(new LGuiCharacterRow(title, sectionKey, expanded));
        if (!expanded)
            return;

        foreach (var row in visible)
        {
            _lGuiCharacterRows.Add(new LGuiCharacterRow(row, target, isPc, false));
            if (CanEditPotential(row))
                _lGuiCharacterRows.Add(new LGuiCharacterRow(row, target, isPc, true));
        }
    }
}
