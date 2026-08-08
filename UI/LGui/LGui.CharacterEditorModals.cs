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
    private void OpenLGuiNpcRelationshipEditor()
    {
        var target = GetCurrentDataTarget();
        if (target == null || _targetTab == 0) return;
        SyncNpcRelationshipInputs(target);
        var modal = CreateLGuiCompleteModal("RuntimeNpcRelationship", T("NPC关系与信仰", "NPC relation & faith"), out var content, 1480f, 940f);
        if (modal == null) return;
        var y = 4f;
        y = AddLGuiReadOnlyRow(content, T("NPC", "NPC"), SafeName(target), y);
        y = AddLGuiReadOnlyRow(content, T("当前好感度", "Current affinity"), GetNpcAffinity(target).ToString(CultureInfo.InvariantCulture), y);
        AddLGuiInlineInput(content, T("目标好感度", "Target affinity"), () => _npcAffinityInput, value => _npcAffinityInput = value, 0f, y, 150f, 150f);
        CreateLGuiButton(content, "ApplyAffinity", T("应用", "Apply"), 320f, y, 100f, 42f, () =>
        {
            if (int.TryParse(_npcAffinityInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                SetNpcAffinity(target, value);
            OpenLGuiNpcRelationshipEditor();
        });
        y += 54f;
        y = AddLGuiReadOnlyRow(content, T("当前关系", "Current relationship"), GetNpcHostilityLabel(target), y);
        var currentRelationshipIndex = GetRelationshipIndex(target);
        for (var i = 0; i < RelationshipOptions.Length; i++)
        {
            var option = RelationshipOptions[i];
            var local = option;
            CreateLGuiButton(content, "Relation" + i, (i == currentRelationshipIndex ? "-> " : "") + GetRelationshipLabel(option), i * 150f, y, 138f, 44f, () => { SetNpcHostility(target, local.Value); OpenLGuiNpcRelationshipEditor(); });
        }
        y += 56f;
        y = AddLGuiReadOnlyRow(content, T("队友状态", "Party member"), GetNpcPartyState(target), y);
        CreateLGuiButton(content, "JoinParty", T("加入队伍", "Join party"), 0f, y, 140f, 44f, () => { MakeNpcPartyMember(target); OpenLGuiNpcRelationshipEditor(); });
        CreateLGuiButton(content, "LeaveParty", T("离开队伍", "Leave party"), 152f, y, 140f, 44f, () => { RemoveNpcPartyMember(target); OpenLGuiNpcRelationshipEditor(); });
        CreateLGuiButton(content, "JoinFaction", T("仅加入阵营", "Join faction"), 304f, y, 150f, 44f, () => { AddNpcToPlayerFactionOnly(target); OpenLGuiNpcRelationshipEditor(); });
        CreateLGuiButton(content, "LeaveFaction", T("仅退出阵营", "Leave faction"), 466f, y, 150f, 44f, () => { RemoveNpcFromPlayerFactionOnly(target); OpenLGuiNpcRelationshipEditor(); });
        y += 62f;
        y = AddLGuiSectionTitle(content, T("信仰", "Faith"), y);
        y = AddLGuiReadOnlyRow(content, T("当前信仰", "Current faith"), GetNpcFaithNameOnly(target), y);
        EnsureFaithRows();
        var faiths = _faithRows ?? new List<FaithDef>();
        const int perPage = 12;
        var pages = Math.Max(1, (faiths.Count + perPage - 1) / perPage);
        _lGuiFaithPage = Clamp(_lGuiFaithPage, 0, pages - 1);
        CreateLGuiButton(content, "FaithPrev", "◀", 0f, y, 48f, 42f, () => { _lGuiFaithPage = Math.Max(0, _lGuiFaithPage - 1); OpenLGuiNpcRelationshipEditor(); });
        var faithPage = CreateLGuiText(content, "FaithPage", (_lGuiFaithPage + 1) + " / " + pages, 16, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(faithPage.rectTransform, 58f, y, 120f, 42f);
        CreateLGuiButton(content, "FaithNext", "▶", 188f, y, 48f, 42f, () => { _lGuiFaithPage = Math.Min(pages - 1, _lGuiFaithPage + 1); OpenLGuiNpcRelationshipEditor(); });
        y += 50f;
        var start = _lGuiFaithPage * perPage;
        var end = Math.Min(faiths.Count, start + perPage);
        for (var i = start; i < end; i++)
        {
            var faith = faiths[i];
            var local = faith;
            var column = (i - start) % 2;
            var row = (i - start) / 2;
            CreateLGuiButton(content, "Faith" + i, GetLGuiFaithButtonLabel(faith), column * 660f, y + row * 50f, 640f, 44f, () => { SetNpcFaith(target, local); OpenLGuiNpcRelationshipEditor(); });
        }
        y += Math.Max(1, (end - start + 1) / 2) * 50f;
        content.sizeDelta = new Vector2(0f, Math.Max(780f, y + 20f));
    }
    private void OpenLGuiNpcGeneEditor()
    {
        var target = GetCurrentDataTarget();
        if (target == null || _targetTab == 0) return;
        SyncNpcGeneEditorState(target);
        var genes = GetNpcGeneList(target);
        var modal = CreateLGuiCompleteModal("RuntimeNpcGeneEditor", T("基因编辑", "Gene editor"), out var content, 1540f, 1010f);
        if (modal == null) return;
        var y = 4f;
        y = AddLGuiReadOnlyRow(content, T("NPC", "NPC"), SafeName(target), y);
        y = AddLGuiReadOnlyRow(content, T("槽位 / FP", "Slots / FP"), GetCurrentGeneSlotCount(target) + " / " + GetGeneSlotCount(target) + " | FP " + target.feat, y);
        CreateLGuiButton(content, "Apply", T("应用修改", "Apply changes"), 0f, y, 130f, 44f, () => { ApplyNpcGeneChange(target); OpenLGuiNpcGeneEditor(); });
        CreateLGuiButton(content, "Add", T("新增基因", "Add gene"), 142f, y, 120f, 44f, () => { AddNpcGene(target); OpenLGuiNpcGeneEditor(); });
        CreateLGuiButton(content, "Delete", T("删除选中", "Delete selected"), 274f, y, 130f, 44f, () => { DeleteNpcGene(target); OpenLGuiNpcGeneEditor(); });
        y += 56f;
        y = AddLGuiSectionTitle(content, T("已有基因", "Existing genes"), y);
        for (var i = 0; i < genes.Count; i++)
        {
            var index = i;
            var prefix = i == _npcGeneSelectedIndex ? "→ " : "";
            var summary = prefix + (i + 1) + ". " + GetNpcGeneSummary(genes[i]);
            CreateLGuiButton(content, "Gene" + i, summary, 0f, y, 1120f, 44f, () => { LoadNpcGeneEditorFields(target, genes[index], index); OpenLGuiNpcGeneEditor(); });
            CreateLGuiButton(content, "DeleteGene" + i, T("删除", "Delete"), 1134f, y, 90f, 44f, () => { DeleteNpcGeneAt(target, index); OpenLGuiNpcGeneEditor(); });
            y += 48f;
        }
        y += 8f;
        y = AddLGuiSectionTitle(content, T("编辑内容", "Edit values"), y);
        AddLGuiInlineInput(content, T("源ID", "Source ID"), () => _npcGeneSourceId, value => _npcGeneSourceId = value, 0f, y, 100f, 180f);
        CreateLGuiButton(content, "SourceTable", T("源ID对应表", "Source ID table"), 300f, y, 170f, 42f, () => OpenLGuiNpcGeneSourceReference(target));
        CreateLGuiButton(content, "GeneType", T("类别", "Category") + ": " + GetNpcGeneTypeLabel(_npcGeneTypeIndex), 490f, y, 300f, 42f, () => OpenLGuiNpcGeneTypeSelector(target, true));
        y += 52f;
        AddLGuiInlineInput(content, T("等级", "Level"), () => _npcGeneLv, value => _npcGeneLv = value, 0f, y, 90f, 100f);
        AddLGuiInlineInput(content, T("种子", "Seed"), () => _npcGeneSeed, value => _npcGeneSeed = value, 220f, y, 90f, 110f);
        AddLGuiInlineInput(content, T("费用", "Cost"), () => _npcGeneCost, value => _npcGeneCost = value, 450f, y, 90f, 110f);
        AddLGuiInlineInput(content, T("槽位", "Slots"), () => _npcGeneSlot, value => _npcGeneSlot = value, 680f, y, 90f, 110f);
        y += 54f;
        CreateLGuiButton(content, "EffectTable", T("基因效果对应表", "Gene effect table"), 0f, y, 190f, 42f, () => OpenLGuiNpcGeneEffectReference(target));
        CreateLGuiButton(content, "AddEffect", T("添加效果", "Add effect"), 204f, y, 130f, 42f, () => { _npcGeneEditorValues.Add(new GeneValueInput("", "0")); OpenLGuiNpcGeneEditor(); });
        y += 52f;
        y = AddLGuiSectionTitle(content, T("基因效果", "Gene effects"), y);
        for (var i = 0; i < _npcGeneEditorValues.Count; i++)
        {
            var index = i;
            var row = _npcGeneEditorValues[i];
            var name = CreateLGuiText(content, "EffectName", GetGeneEffectName(row.ElementId), 15, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(name.rectTransform, 0f, y, 340f, 42f);
            AddLGuiInlineInput(content, T("效果ID", "Effect ID"), () => row.ElementId, value => row.ElementId = value, 350f, y, 90f, 120f);
            AddLGuiInlineInput(content, T("数值", "Value"), () => row.Value, value => row.Value = value, 590f, y, 80f, 120f);
            CreateLGuiButton(content, "DeleteEffect" + i, T("删除", "Delete"), 820f, y, 90f, 42f, () => { _npcGeneEditorValues.RemoveAt(index); OpenLGuiNpcGeneEditor(); });
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 30f));
    }
    private void OpenLGuiNpcGeneTypeSelector(Chara target, bool returnToGeneEditor)
    {
        var modal = CreateLGuiCompleteModal("RuntimeNpcGeneType", T("基因类别", "Gene category"), out var content, 760f, 520f);
        if (modal == null) return;
        var y = 8f;
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            var selected = index == _npcGeneTypeIndex ? "-> " : "   ";
            CreateLGuiButton(content, "Type" + i.ToString(CultureInfo.InvariantCulture), selected + GetNpcGeneTypeLabel(i), 24f, y, 620f, 46f, () =>
            {
                _npcGeneTypeIndex = index;
                CloseLGuiEditorModal(true);
                if (returnToGeneEditor)
                    OpenLGuiNpcGeneEditor();
                else
                {
                    _lGuiCharacterSectionExpanded["npcGene"] = true;
                    RebuildLGuiCharacterRows();
                }
            });
            y += 54f;
        }
        content.sizeDelta = new Vector2(0f, y + 20f);
    }
    private void OpenLGuiNpcGeneSourceReference(Chara target)
    {
        var modal = CreateLGuiCompleteModal("RuntimeNpcGeneSources", T("源ID对应表", "Source ID table"), out var content, 1480f, 930f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "Filter", T("过滤", "Filter"), 0f, y, 420f, 44f);
        filter.text = _npcGeneSourceFilter;
        filter.onValueChanged.AddListener(value => _npcGeneSourceFilter = value ?? "");
        CreateLGuiButton(content, "Refresh", T("刷新", "Refresh"), 434f, y, 100f, 44f, () => { _npcGeneSourcePage = 0; OpenLGuiNpcGeneSourceReference(target); });
        y += 54f;
        var rows = GetFilteredNpcGeneSourceIds();
        _npcGeneSourcePage = Clamp(_npcGeneSourcePage, 0, Math.Max(0, (rows.Count + GameRowsPerPage - 1) / GameRowsPerPage - 1));
        y = BuildLGuiReferencePager(content, rows.Count, _npcGeneSourcePage, y, next => { _npcGeneSourcePage = next; OpenLGuiNpcGeneSourceReference(target); });
        var start = _npcGeneSourcePage * GameRowsPerPage;
        var end = Math.Min(rows.Count, start + GameRowsPerPage);
        for (var i = start; i < end; i++)
        {
            var row = rows[i];
            var local = row;
            CreateLGuiButton(content, "Source" + i, row.DisplayName + " | " + row.Id, 0f, y, 1260f, 44f, () => { _npcGeneSourceId = local.Id; OpenLGuiNpcGeneEditor(); });
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(760f, y + 20f));
    }
    private void OpenLGuiNpcGeneEffectReference(Chara target)
    {
        var modal = CreateLGuiCompleteModal("RuntimeNpcGeneEffects", T("基因效果对应表", "Gene effect table"), out var content, 1480f, 930f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "Filter", T("过滤", "Filter"), 0f, y, 420f, 44f);
        filter.text = _npcGeneEffectFilter;
        filter.onValueChanged.AddListener(value => _npcGeneEffectFilter = value ?? "");
        CreateLGuiButton(content, "Refresh", T("刷新", "Refresh"), 434f, y, 100f, 44f, () => { _npcGeneEffectPage = 0; OpenLGuiNpcGeneEffectReference(target); });
        y += 54f;
        var rows = GetFilteredNpcGeneEffectIds();
        _npcGeneEffectPage = Clamp(_npcGeneEffectPage, 0, Math.Max(0, (rows.Count + GameRowsPerPage - 1) / GameRowsPerPage - 1));
        y = BuildLGuiReferencePager(content, rows.Count, _npcGeneEffectPage, y, next => { _npcGeneEffectPage = next; OpenLGuiNpcGeneEffectReference(target); });
        var start = _npcGeneEffectPage * GameRowsPerPage;
        var end = Math.Min(rows.Count, start + GameRowsPerPage);
        for (var i = start; i < end; i++)
        {
            var row = rows[i];
            var local = row;
            CreateLGuiButton(content, "Effect" + i, row.Name + " | " + row.Id + " | " + row.Category, 0f, y, 1260f, 44f, () =>
            {
                _npcGeneEditorValues.Add(new GeneValueInput(local.Id.ToString(CultureInfo.InvariantCulture), "0"));
                _lGuiCharacterSectionExpanded["npcGene"] = true;
                CloseLGuiEditorModal(true);
                RebuildLGuiCharacterRows();
            });
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(760f, y + 20f));
    }
    private void OpenLGuiEtherDiseaseEditor()
    {
        var target = GetCurrentDataTarget();
        if (target == null) return;
        var isPc = _targetTab == 0;
        EnsureEtherDiseaseRows();
        SyncEtherDiseaseEditorState(target);
        var diseases = GetCurrentEtherDiseases(target);
        var modal = CreateLGuiCompleteModal("RuntimeEtherDisease", T("以太病", "Ether disease"), out var content, 1480f, 950f);
        if (modal == null) return;
        var y = 4f;
        y = AddLGuiReadOnlyRow(content, T("目标", "Target"), SafeName(target), y);
        y = AddLGuiReadOnlyRow(content, T("以太病数量 / 进度", "Disease count / corruption"), diseases.Count + " / " + SafeText(() => target.corruption.ToString(CultureInfo.InvariantCulture), "0"), y);
        CreateLGuiButton(content, "Apply", T("应用修改", "Apply changes"), 0f, y, 130f, 44f, () => { ApplyEtherDiseaseChange(target, isPc); OpenLGuiEtherDiseaseEditor(); });
        CreateLGuiButton(content, "Add", T("新增以太病", "Add disease"), 142f, y, 130f, 44f, () => { AddEtherDisease(target, isPc); OpenLGuiEtherDiseaseEditor(); });
        CreateLGuiButton(content, "Delete", T("删除选中", "Delete selected"), 284f, y, 140f, 44f, () => { DeleteSelectedEtherDisease(target, isPc); OpenLGuiEtherDiseaseEditor(); });
        y += 56f;
        for (var i = 0; i < diseases.Count; i++)
        {
            var index = i;
            var row = diseases[i];
            var prefix = i == _etherDiseaseSelectedIndex ? "→ " : "";
            CreateLGuiButton(content, "Disease" + i, prefix + GetEtherDiseaseSummary(target, row), 0f, y, 1120f, 44f, () => { LoadEtherDiseaseEditorFields(target, row, index); OpenLGuiEtherDiseaseEditor(); });
            CreateLGuiButton(content, "DeleteDisease" + i, T("删除", "Delete"), 1134f, y, 90f, 44f, () => { DeleteEtherDisease(target, row, isPc); OpenLGuiEtherDiseaseEditor(); });
            y += 48f;
        }
        y += 8f;
        AddLGuiInlineInput(content, T("以太病ID", "Disease ID"), () => _etherDiseaseId, value => _etherDiseaseId = value, 0f, y, 120f, 130f);
        AddLGuiInlineInput(content, T("等级", "Level"), () => _etherDiseaseValue, value => _etherDiseaseValue = value, 280f, y, 90f, 110f);
        CreateLGuiButton(content, "DiseaseTable", T("以太病对应表", "Disease table"), 510f, y, 180f, 42f, () => OpenLGuiEtherDiseaseReference(target, isPc));
        content.sizeDelta = new Vector2(0f, Math.Max(780f, y + 70f));
    }
    private void OpenLGuiEtherDiseaseReference(Chara target, bool isPc)
    {
        var modal = CreateLGuiCompleteModal("RuntimeEtherDiseaseReference", T("以太病对应表", "Ether disease table"), out var content, 1480f, 930f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "Filter", T("过滤", "Filter"), 0f, y, 420f, 44f);
        filter.text = _etherDiseaseFilter;
        filter.onValueChanged.AddListener(value => _etherDiseaseFilter = value ?? "");
        CreateLGuiButton(content, "Refresh", T("刷新", "Refresh"), 434f, y, 100f, 44f, () => { _etherDiseasePage = 0; OpenLGuiEtherDiseaseReference(target, isPc); });
        y += 54f;
        var rows = GetFilteredEtherDiseaseRows();
        _etherDiseasePage = Clamp(_etherDiseasePage, 0, Math.Max(0, (rows.Count + GameRowsPerPage - 1) / GameRowsPerPage - 1));
        y = BuildLGuiReferencePager(content, rows.Count, _etherDiseasePage, y, next => { _etherDiseasePage = next; OpenLGuiEtherDiseaseReference(target, isPc); });
        var start = _etherDiseasePage * GameRowsPerPage;
        var end = Math.Min(rows.Count, start + GameRowsPerPage);
        for (var i = start; i < end; i++)
        {
            var row = rows[i];
            var local = row;
            CreateLGuiButton(content, "Disease" + i, GetRowLabel(row) + " | " + row.Key, 0f, y, 1260f, 44f, () =>
            {
                _etherDiseaseId = local.Key;
                _etherDiseaseValue = "1";
                _lGuiCharacterSectionExpanded["etherDisease"] = true;
                CloseLGuiEditorModal(true);
                RebuildLGuiCharacterRows();
            });
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(760f, y + 20f));
    }
}
