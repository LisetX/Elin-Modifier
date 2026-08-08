using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiNpcsPage()
    {
        EnsureNpcRows();
        var toolbar = CreateLGuiRect(_lGuiPageHost!, "NpcToolbar");
        AnchorLGuiTop(toolbar, 0f, 204f, 0f, 0f);
        CreateLGuiFieldLabel(toolbar, "NPC ID", 0f, 0f, 260f);
        CreateLGuiFieldLabel(toolbar, T("生成等级", "Spawn level"), 272f, 0f, 120f);
        CreateLGuiFieldLabel(toolbar, T("初始好感度", "Initial affinity"), 404f, 0f, 140f);
        var id = CreateLGuiInput(toolbar, "NpcId", "NPC ID", 0f, 28f, 260f, 46f);
        _lGuiNpcIdInput = id;
        id.text = _npcSpawnId;
        id.onValueChanged.AddListener(value => _npcSpawnId = value ?? "");
        var level = CreateLGuiInput(toolbar, "NpcLevel", T("等级", "Level"), 272f, 28f, 120f, 46f);
        level.text = _npcSpawnLv;
        level.onValueChanged.AddListener(value => _npcSpawnLv = value ?? "-1");
        var affinity = CreateLGuiInput(toolbar, "NpcAffinity", T("好感度", "Affinity"), 404f, 28f, 140f, 46f);
        affinity.text = _npcSpawnAffinity;
        affinity.onValueChanged.AddListener(value => _npcSpawnAffinity = value ?? "0");
        CreateLGuiButton(toolbar, "SpawnNpc", T("生成", "Spawn"), 558f, 28f, 110f, 46f, SpawnNpc);
        var relationLabel = CreateLGuiText(toolbar, "RelationshipLabel", T("关系状态", "Relationship"), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(relationLabel.rectTransform, 0f, 82f, 110f, 46f);
        _lGuiNpcRelationshipLabels.Clear();
        for (var i = 0; i < RelationshipOptions.Length; i++)
        {
            var relationIndex = i;
            var button = CreateLGuiButton(toolbar, "Relationship" + i.ToString(CultureInfo.InvariantCulture), GetLGuiNpcRelationshipButtonText(i), 118f + i * 112f, 82f, 104f, 46f, () =>
            {
                _npcSpawnHostilityIndex = relationIndex;
                RefreshLGuiNpcRelationshipButtons();
            });
            var buttonText = button.GetComponentInChildren<Text>(true);
            if (buttonText != null)
                _lGuiNpcRelationshipLabels.Add(buttonText);
        }
        RefreshLGuiNpcRelationshipButtons();
        CreateLGuiFieldLabel(toolbar, T("NPC模板过滤", "NPC template filter"), 0f, 136f, 420f);
        var filter = CreateLGuiInput(toolbar, "NpcFilter", T("过滤", "Filter"), 0f, 160f, 420f, 40f);
        filter.text = _npcFilter;
        filter.onValueChanged.AddListener(value => { _npcFilter = value ?? ""; RebuildLGuiNpcRows(); });

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "NpcList", 208f);
        _lGuiNpcList = new VirtualList<NpcDef>(scroll, 58f, 18, CreateLGuiVirtualRow, BindLGuiNpcRow);
        RebuildLGuiNpcRows();
    }
    private void RebuildLGuiNpcRows()
    {
        if (_lGuiNpcList == null)
            return;
        EnsureNpcRows();
        _lGuiFilteredNpcs.Clear();
        for (var i = 0; i < _npcRows.Count; i++)
        {
            var npc = _npcRows[i];
            if (LGuiFilterMatches(npc.DisplayName, npc.Id, npc.Race + " " + npc.Job, _npcFilter))
                _lGuiFilteredNpcs.Add(npc);
        }
        _lGuiNpcList.SetItems(_lGuiFilteredNpcs);
    }
    private string GetLGuiNpcRelationshipButtonText(int index)
    {
        if (index < 0 || index >= RelationshipOptions.Length)
            return "";

        var label = GetRelationshipLabel(RelationshipOptions[index]);
        return _npcSpawnHostilityIndex == index ? "-> " + label : label;
    }
    private void RefreshLGuiNpcRelationshipButtons()
    {
        var count = Math.Min(_lGuiNpcRelationshipLabels.Count, RelationshipOptions.Length);
        for (var i = 0; i < count; i++)
        {
            var label = _lGuiNpcRelationshipLabels[i];
            if (label != null)
                label.text = GetLGuiNpcRelationshipButtonText(i);
        }
    }
}
