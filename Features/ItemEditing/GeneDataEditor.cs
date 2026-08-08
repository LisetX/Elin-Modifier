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
    private List<NpcDef> GetFilteredGeneSourceIds()
    {
        if (_lastGeneSourceFilter != _geneSourceFilter)
        {
            _geneSourcePage = 0;
            _lastGeneSourceFilter = _geneSourceFilter;
        }

        var result = new List<NpcDef>();
        if (_npcRows == null)
            return result;

        var filter = (_geneSourceFilter ?? "").Trim().ToLowerInvariant();
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
    private List<GeneEffectDef> GetFilteredGeneEffectIds()
    {
        EnsureGeneEffectRows();
        if (_lastGeneEffectFilter != _geneEffectFilter)
        {
            _geneEffectPage = 0;
            _lastGeneEffectFilter = _geneEffectFilter;
        }

        var result = new List<GeneEffectDef>();
        var filter = (_geneEffectFilter ?? "").Trim().ToLowerInvariant();
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
    private void EnsureGeneEffectRows()
    {
        if (_geneEffectRows != null)
            return;

        _geneEffectRows = new List<GeneEffectDef>();
        var seen = new HashSet<int>();
        foreach (var row in EnumerateSourceElementRows())
        {
            var id = GetInt(row, "id");
            if (id <= 0 || !seen.Add(id)) continue;
            var name = GetElementDisplayName(row);
            if (string.IsNullOrEmpty(name)) name = GetString(row, "alias");
            if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) continue;
            var alias = GetString(row, "alias");
            var category = GetString(row, "category");
            if (string.IsNullOrEmpty(category)) category = GetString(row, "group");
            _geneEffectRows.Add(new GeneEffectDef(id, name, alias, category));
        }
        _geneEffectRows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
    private void OpenGeneEditorWindow(Thing thing)
    {
        if (!CanEditGene(thing) || !EnsureEditableGeneDna(thing))
            return;

        _geneEditorTarget = thing;
        _geneEditorName = SafeThingName(thing);
        LoadGeneEditorFields(thing);
        _geneEditorWindowVisible = false;
        if (!IsLGuiInitialized())
            return;
        EnsureLGuiEditorVisible();
        OpenLGuiGeneItemEditor();
    }
    private void LoadGeneEditorFields(Thing thing)
    {
        var dna = thing.c_DNA;
        _geneEditorSourceId = dna.id ?? "";
        _geneEditorLv = dna.lv.ToString(CultureInfo.InvariantCulture);
        _geneEditorSeed = dna.seed.ToString(CultureInfo.InvariantCulture);
        _geneEditorCost = dna.cost.ToString(CultureInfo.InvariantCulture);
        _geneEditorSlot = dna.slot.ToString(CultureInfo.InvariantCulture);
        _geneEditorValues.Clear();

        if (dna.vals == null)
            dna.vals = new List<int>();
        for (var i = 0; i + 1 < dna.vals.Count; i += 2)
        {
            _geneEditorValues.Add(new GeneValueInput(
                dna.vals[i].ToString(CultureInfo.InvariantCulture),
                dna.vals[i + 1].ToString(CultureInfo.InvariantCulture)));
        }
    }
    private void ApplyGeneEditorChange()
    {
        try
        {
            var target = _geneEditorTarget;
            if (!CanEditGene(target))
            {
                _log = T("目标基因不存在", "Target gene does not exist");
                _geneEditorWindowVisible = false;
                return;
            }

            if (!TryParseGeneEditorInt(_geneEditorLv, T("等级", "Level"), out var lv) ||
                !TryParseGeneEditorInt(_geneEditorSeed, T("种子", "Seed"), out var seed) ||
                !TryParseGeneEditorInt(_geneEditorCost, T("费用", "Cost"), out var cost) ||
                !TryParseGeneEditorInt(_geneEditorSlot, T("占用槽位", "Required slots"), out var slot))
                return;

            var values = new List<int>();
            for (var i = 0; i < _geneEditorValues.Count; i++)
            {
                var row = _geneEditorValues[i];
                if (string.IsNullOrWhiteSpace(row.ElementId) && string.IsNullOrWhiteSpace(row.Value))
                    continue;
                if (!TryParseGeneEditorInt(row.ElementId, T("基因效果", "Gene effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var elementId) ||
                    !TryParseGeneEditorInt(row.Value, T("数值", "Value") + " " + (i + 1).ToString(CultureInfo.InvariantCulture), out var value))
                    return;
                if (elementId <= 0)
                {
                    _log = T("基因效果", "Gene effect") + " " + (i + 1).ToString(CultureInfo.InvariantCulture) + T(" 必须大于0", " must be greater than 0");
                    return;
                }
                values.Add(elementId);
                values.Add(value);
            }

            var dna = target!.c_DNA;
            dna.id = (_geneEditorSourceId ?? "").Trim();
            dna.lv = lv;
            dna.seed = seed;
            dna.cost = cost;
            dna.slot = slot;
            if (dna.vals == null)
                dna.vals = new List<int>();
            dna.vals.Clear();
            dna.vals.AddRange(values);

            if (!string.IsNullOrEmpty(dna.id))
            {
                try { target.MakeRefFrom(dna.id); }
                catch { }
            }
            target.c_DNA = dna;
            try { target.ChangeMaterial(dna.GetMaterialId(dna.type), false); }
            catch { }

            RefreshInventoryUi();
            _geneEditorName = SafeThingName(target);
            _log = T("已修改基因: ", "Modified gene: ") + _geneEditorName;
            _geneEditorWindowVisible = false;
        }
        catch (Exception ex)
        {
            _log = T("修改基因失败: ", "Modify gene failed: ") + ex.Message;
        }
    }
    private bool TryParseGeneEditorInt(string text, string label, out int value)
    {
        if (int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;
        _log = label + T(" 输入不是数字", " input is not a number");
        return false;
    }
    private string GetGeneEffectName(string idText)
    {
        if (!int.TryParse((idText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementId))
            return "";
        EnsureGeneEffectRows();
        if (_geneEffectRows != null)
        {
            foreach (var effect in _geneEffectRows)
                if (effect.Id == elementId)
                    return effect.Name;
        }
        var row = FindSourceElementRowById(elementId);
        return row == null ? T("未知元素", "Unknown element") : GetElementDisplayName(row);
    }
    private static string GetGeneTypeName(DNA dna)
    {
        try { return dna.type.ToString(); }
        catch { return ""; }
    }
    private static string GetGeneTypeName(string typeText)
    {
        if (!int.TryParse((typeText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return "";
        try { return ((DNA.Type)value).ToString(); }
        catch { return ""; }
    }
}
