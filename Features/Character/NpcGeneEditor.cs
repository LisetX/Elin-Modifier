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
    private void SyncNpcGeneEditorState(Chara target)
    {
        var targetUid = GetCharaUid(target);
        var genes = GetNpcGeneList(target);
        if (_npcGeneLastTargetUid != targetUid)
        {
            _npcGeneLastTargetUid = targetUid;
            _npcGeneSelectedIndex = genes.Count > 0 ? 0 : -1;
            if (_npcGeneSelectedIndex >= 0 && _npcGeneSelectedIndex < genes.Count)
                LoadNpcGeneEditorFields(target, genes[_npcGeneSelectedIndex], _npcGeneSelectedIndex);
            else
                ResetNpcGeneEditorDraft(target);
            return;
        }

        if (genes.Count == 0)
        {
            if (_npcGeneSelectedIndex != -1)
            {
                _npcGeneSelectedIndex = -1;
                ResetNpcGeneEditorDraft(target);
            }
            return;
        }

        if (_npcGeneSelectedIndex < 0 || _npcGeneSelectedIndex >= genes.Count)
            LoadNpcGeneEditorFields(target, genes[0], 0);
    }
    private void ResetNpcGeneEditorDraft(Chara target)
    {
        _npcGeneSourceId = "";
        _npcGeneLv = Math.Max(1, target.LV).ToString(CultureInfo.InvariantCulture);
        _npcGeneSeed = "0";
        _npcGeneCost = "0";
        _npcGeneSlot = "0";
        _npcGeneTypeIndex = 1;
        _npcGeneIsManiGene = false;
        _npcGeneEditorValues.Clear();
    }
    private void LoadNpcGeneEditorFields(Chara target, DNA dna, int index)
    {
        _npcGeneLastTargetUid = GetCharaUid(target);
        _npcGeneSelectedIndex = index;
        _npcGeneSourceId = dna?.id ?? "";
        _npcGeneLv = dna == null ? "1" : dna.lv.ToString(CultureInfo.InvariantCulture);
        _npcGeneSeed = dna == null ? "0" : dna.seed.ToString(CultureInfo.InvariantCulture);
        _npcGeneCost = dna == null ? "0" : dna.cost.ToString(CultureInfo.InvariantCulture);
        _npcGeneSlot = dna == null ? "0" : dna.slot.ToString(CultureInfo.InvariantCulture);
        _npcGeneTypeIndex = dna == null ? 1 : GetNpcGeneTypeIndex(dna.type);
        _npcGeneIsManiGene = dna != null && dna.isManiGene;
        _npcGeneEditorValues.Clear();

        if (dna?.vals == null)
            return;

        for (var i = 0; i + 1 < dna.vals.Count; i += 2)
        {
            _npcGeneEditorValues.Add(new GeneValueInput(
                dna.vals[i].ToString(CultureInfo.InvariantCulture),
                dna.vals[i + 1].ToString(CultureInfo.InvariantCulture)));
        }
    }
    private List<NpcDef> GetFilteredNpcGeneSourceIds()
    {
        if (_lastNpcGeneSourceFilter != _npcGeneSourceFilter)
        {
            _npcGeneSourcePage = 0;
            _lastNpcGeneSourceFilter = _npcGeneSourceFilter;
        }

        var result = new List<NpcDef>();
        if (_npcRows == null)
            return result;

        var filter = (_npcGeneSourceFilter ?? "").Trim().ToLowerInvariant();
        foreach (var npc in _npcRows)
        {
            if (string.IsNullOrEmpty(filter) ||
                npc.DisplayName.ToLowerInvariant().Contains(filter) ||
                npc.Name.ToLowerInvariant().Contains(filter) ||
                npc.Id.ToLowerInvariant().Contains(filter) ||
                npc.Race.ToLowerInvariant().Contains(filter) ||
                npc.Job.ToLowerInvariant().Contains(filter))
                result.Add(npc);
        }
        return result;
    }
    private List<GeneEffectDef> GetFilteredNpcGeneEffectIds()
    {
        EnsureGeneEffectRows();
        if (_lastNpcGeneEffectFilter != _npcGeneEffectFilter)
        {
            _npcGeneEffectPage = 0;
            _lastNpcGeneEffectFilter = _npcGeneEffectFilter;
        }

        var result = new List<GeneEffectDef>();
        var filter = (_npcGeneEffectFilter ?? "").Trim().ToLowerInvariant();
        if (_geneEffectRows == null)
            return result;

        foreach (var effect in _geneEffectRows)
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
    private void ApplyNpcGeneChange(Chara target, bool isPc = false)
    {
        ApplyNpcGeneChangeInternal(target, false, isPc);
    }
    private void AddNpcGene(Chara target, bool isPc = false)
    {
        ApplyNpcGeneChangeInternal(target, true, isPc);
    }
    private void DeleteNpcGene(Chara target, bool isPc = false)
    {
        if (!CanEditNpcGene(target))
            return;
        var genes = GetNpcGeneList(target);
        if (_npcGeneSelectedIndex < 0 || _npcGeneSelectedIndex >= genes.Count)
        {
            _log = T("未选中基因", "No gene selected");
            return;
        }
        DeleteNpcGeneAt(target, _npcGeneSelectedIndex, isPc);
    }
    private void DeleteNpcGeneAt(
        Chara target,
        int index,
        bool isPc = false)
    {
        try
        {
            if (!CanEditNpcGene(target))
            {
                _log = T("未获取到人物数据", "No character data");
                return;
            }

            var genes = GetNpcGeneList(target);
            if (index < 0 || index >= genes.Count)
            {
                _log = T("目标基因不存在", "Target gene does not exist");
                return;
            }

            var dna = genes[index];
            RemoveNpcGeneSilently(target, dna);
            try { target.RemoveAllStances(); } catch { }
            try { target.Refresh(false); } catch { }
            InvalidateCachedUiValues(GetTargetCachePrefix(target, isPc));

            _npcGeneSelectedIndex = genes.Count == 0 ? -1 : Math.Min(index, genes.Count - 1);
            if (_npcGeneSelectedIndex >= 0)
                LoadNpcGeneEditorFields(target, genes[_npcGeneSelectedIndex], _npcGeneSelectedIndex);
            else
                ResetNpcGeneEditorDraft(target);

            _log = isPc
                ? T("已删除玩家基因: ", "Deleted player gene: ") + SafeName(target)
                : T("已删除NPC基因: ", "Deleted NPC gene: ") + SafeName(target);
        }
        catch (Exception ex)
        {
            _log = T("删除NPC基因失败: ", "Delete NPC gene failed: ") + ex.Message;
        }
    }
    private void ApplyNpcGeneChangeInternal(
        Chara target,
        bool addNew,
        bool isPc)
    {
        try
        {
            if (!CanEditNpcGene(target))
            {
                _log = T("未获取到人物数据", "No character data");
                return;
            }

            if (!TryBuildNpcGeneFromInputs(target, out var newDna, out var error))
            {
                _log = error;
                return;
            }

            var genes = GetNpcGeneList(target);
            var editingExisting = !addNew && _npcGeneSelectedIndex >= 0 && _npcGeneSelectedIndex < genes.Count;
            var originalIndex = editingExisting ? _npcGeneSelectedIndex : genes.Count;
            DNA? oldDna = editingExisting ? genes[_npcGeneSelectedIndex] : null;

            if (editingExisting && oldDna != null)
            {
                RemoveNpcGeneSilently(target, oldDna);
                try { target.RemoveAllStances(); } catch { }
            }

            newDna.Apply(target);

            var currentGenes = GetNpcGeneList(target);
            if (editingExisting)
            {
                var newIndex = currentGenes.IndexOf(newDna);
                if (newIndex >= 0 && newIndex != originalIndex)
                {
                    currentGenes.RemoveAt(newIndex);
                    if (originalIndex < 0) originalIndex = 0;
                    if (originalIndex > currentGenes.Count) originalIndex = currentGenes.Count;
                    currentGenes.Insert(originalIndex, newDna);
                    newIndex = originalIndex;
                }
                _npcGeneSelectedIndex = newIndex;
            }
            else
            {
                _npcGeneSelectedIndex = currentGenes.IndexOf(newDna);
            }

            try { target.Refresh(false); } catch { }
            InvalidateCachedUiValues(GetTargetCachePrefix(target, isPc));

            if (_npcGeneSelectedIndex >= 0 && _npcGeneSelectedIndex < currentGenes.Count)
                LoadNpcGeneEditorFields(target, currentGenes[_npcGeneSelectedIndex], _npcGeneSelectedIndex);

            _log = isPc
                ? (editingExisting
                    ? T("已修改玩家基因: ", "Modified player gene: ")
                    : T("已新增玩家基因: ", "Added player gene: ")) + SafeName(target)
                : (editingExisting
                    ? T("已修改NPC基因: ", "Modified NPC gene: ")
                    : T("已新增NPC基因: ", "Added NPC gene: ")) + SafeName(target);
        }
        catch (Exception ex)
        {
            _log = T("修改NPC基因失败: ", "Modify NPC gene failed: ") + ex.Message;
        }
    }
    private bool TryBuildNpcGeneFromInputs(Chara target, out DNA dna, out string error)
    {
        dna = new DNA();
        error = "";

        if (!TryParseGeneEditorInt(_npcGeneLv, T("等级", "Level"), out var lv) ||
            !TryParseGeneEditorInt(_npcGeneSeed, T("种子", "Seed"), out var seed) ||
            !TryParseGeneEditorInt(_npcGeneCost, T("费用", "Cost"), out var cost) ||
            !TryParseGeneEditorInt(_npcGeneSlot, T("槽位", "Slots"), out var slot))
        {
            error = _log;
            return false;
        }

        var values = new List<int>();
        for (var i = 0; i < _npcGeneEditorValues.Count; i++)
        {
            var row = _npcGeneEditorValues[i];
            if (string.IsNullOrWhiteSpace(row.ElementId) && string.IsNullOrWhiteSpace(row.Value))
                continue;
            if (!TryParseGeneEditorInt(row.ElementId, T("基因效果", "Gene effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var elementId) ||
                !TryParseGeneEditorInt(row.Value, T("数值", "Value") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var value))
            {
                error = _log;
                return false;
            }
            if (elementId <= 0)
            {
                error = T("基因效果", "Gene effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture) + T(" 必须大于0", " must be greater than 0");
                return false;
            }
            if (FindSourceElementRowById(elementId) == null)
            {
                error = T("基因效果不存在: ", "Gene effect does not exist: ") + elementId.ToString(CultureInfo.InvariantCulture);
                return false;
            }
            values.Add(elementId);
            values.Add(value);
        }

        dna.id = (_npcGeneSourceId ?? "").Trim();
        dna.type = GetNpcGeneTypeFromIndex(_npcGeneTypeIndex);
        dna.lv = lv;
        dna.seed = seed;
        dna.cost = cost;
        dna.slot = slot;
        dna.isManiGene = _npcGeneIsManiGene;
        if (dna.vals == null)
            dna.vals = new List<int>();
        dna.vals.Clear();
        dna.vals.AddRange(values);

        return true;
    }
    private static void RemoveNpcGeneSilently(Chara target, DNA dna)
    {
        if (target == null || dna == null)
            return;

        try
        {
            var genes = target.c_genes;
            if (genes?.items != null)
                genes.items.Remove(dna);
        }
        catch { }

        try
        {
            target.feat += dna.cost * target.GeneCostMTP / 100;
        }
        catch { }

        try
        {
            dna.Apply(target, true);
        }
        catch { }
    }
    private static CharaGenes GetOrCreateNpcGenes(Chara target)
    {
        var genes = target.c_genes;
        if (genes != null)
            return genes;
        genes = new CharaGenes();
        target.c_genes = genes;
        return genes;
    }
    private static List<DNA> GetNpcGeneList(Chara target)
    {
        try
        {
            var genes = target.c_genes?.items;
            if (genes != null)
                return genes;
        }
        catch { }
        return new List<DNA>();
    }
    private static bool CanEditNpcGene(Chara? target)
    {
        return target != null;
    }
    private static int GetCurrentGeneSlotCount(Chara target)
    {
        try { return target.CurrentGeneSlot; }
        catch { return 0; }
    }
    internal static int GetCharaUid(Chara target)
    {
        try { return target.uid; }
        catch { return -1; }
    }
    private static int GetNpcGeneTypeIndex(DNA.Type type)
    {
        return type switch
        {
            DNA.Type.Inferior => 0,
            DNA.Type.Default => 1,
            DNA.Type.Superior => 2,
            DNA.Type.Brain => 3,
            _ => 1
        };
    }
    private static DNA.Type GetNpcGeneTypeFromIndex(int index)
    {
        return index switch
        {
            0 => DNA.Type.Inferior,
            1 => DNA.Type.Default,
            2 => DNA.Type.Superior,
            3 => DNA.Type.Brain,
            _ => DNA.Type.Default
        };
    }
    private string GetNpcGeneTypeLabel(int index)
    {
        return index switch
        {
            0 => T("低级", "Inferior"),
            1 => T("默认", "Default"),
            2 => T("高级", "Superior"),
            3 => T("大脑", "Brain"),
            _ => T("默认", "Default")
        };
    }
    private string GetNpcGeneSummary(DNA dna)
    {
        if (dna == null)
            return T("空基因", "Empty gene");

        string text;
        try { text = dna.GetText(); }
        catch { text = ""; }

        if (string.IsNullOrWhiteSpace(text))
            text = !string.IsNullOrWhiteSpace(dna.id) ? dna.id : T("未命名基因", "Unnamed gene");

        return text + " | " +
               T("类型", "Type") + ": " + GetNpcGeneTypeLabel(GetNpcGeneTypeIndex(dna.type)) + " | " +
               T("等级", "Level") + ": " + dna.lv.ToString(CultureInfo.InvariantCulture) + " | " +
               T("费用", "Cost") + ": " + dna.cost.ToString(CultureInfo.InvariantCulture) + " | " +
               T("槽位", "Slots") + ": " + dna.slot.ToString(CultureInfo.InvariantCulture);
    }
}
