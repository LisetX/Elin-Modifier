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
    private void SyncEtherDiseaseEditorState(Chara target)
    {
        var targetUid = GetCharaUid(target);
        var diseases = GetCurrentEtherDiseases(target);
        if (_etherDiseaseLastTargetUid != targetUid)
        {
            _etherDiseaseLastTargetUid = targetUid;
            _etherDiseaseSelectedIndex = diseases.Count > 0 ? 0 : -1;
            if (_etherDiseaseSelectedIndex >= 0 && _etherDiseaseSelectedIndex < diseases.Count)
                LoadEtherDiseaseEditorFields(target, diseases[_etherDiseaseSelectedIndex], _etherDiseaseSelectedIndex);
            else
                ResetEtherDiseaseEditorDraft();
            return;
        }

        if (diseases.Count == 0)
        {
            if (_etherDiseaseSelectedIndex != -1)
            {
                _etherDiseaseSelectedIndex = -1;
                ResetEtherDiseaseEditorDraft();
            }
            return;
        }

        if (_etherDiseaseSelectedIndex < 0 || _etherDiseaseSelectedIndex >= diseases.Count)
            LoadEtherDiseaseEditorFields(target, diseases[0], 0);
    }
    private void ResetEtherDiseaseEditorDraft()
    {
        _etherDiseaseId = "";
        _etherDiseaseValue = "1";
    }
    private void LoadEtherDiseaseEditorFields(Chara target, RowDef row, int index)
    {
        _etherDiseaseLastTargetUid = GetCharaUid(target);
        _etherDiseaseSelectedIndex = index;
        _etherDiseaseId = row == null ? "" : row.Key;
        _etherDiseaseValue = row == null ? "1" : GetElementValue(target, row.Key).ToString(CultureInfo.InvariantCulture);
    }
    private List<RowDef> GetFilteredEtherDiseaseRows()
    {
        EnsureEtherDiseaseRows();
        if (_lastEtherDiseaseFilter != _etherDiseaseFilter)
        {
            _etherDiseasePage = 0;
            _lastEtherDiseaseFilter = _etherDiseaseFilter;
        }

        var result = new List<RowDef>();
        if (_etherDiseaseRows == null)
            return result;

        foreach (var row in _etherDiseaseRows)
        {
            if (PassFilter(row, _etherDiseaseFilter))
                result.Add(row);
        }
        return result;
    }
    private List<RowDef> GetCurrentEtherDiseases(Chara target)
    {
        var result = new List<RowDef>();
        if (target == null)
            return result;

        try
        {
            foreach (var element in GetElements(target).ListElements(e => e != null && e.source != null && e.source.category == "ether" && e.Value != 0))
            {
                if (element == null)
                    continue;
                var source = element.source;
                var label = SafeText(() => element.Name, "");
                if (string.IsNullOrEmpty(label))
                    label = SafeText(() => source.GetName(), "");
                if (string.IsNullOrEmpty(label))
                    label = element.id.ToString(CultureInfo.InvariantCulture);
                result.Add(new RowDef(element.id.ToString(CultureInfo.InvariantCulture), label, RowKind.Feat)
                {
                    Alias = SafeText(() => source.alias, ""),
                    Category = SafeText(() => source.category, ""),
                    Max = SafeInt(() => source.max, 0)
                });
            }
        }
        catch { }

        result.Sort(CompareRows);
        RemoveDuplicateRowsByKey(result);
        return result;
    }
    private string GetEtherDiseaseSummary(Chara target, RowDef row)
    {
        var value = GetElementValue(target, row.Key);
        var text = row.Label + " [" + row.Key + "] " + T("等级", "Lv") + " " + value.ToString(CultureInfo.InvariantCulture);
        var max = row.Max > 0 ? row.Max : GetEtherDiseaseMax(row.Key);
        if (max > 0)
            text += "/" + max.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(row.Alias))
            text += " (" + row.Alias + ")";
        return text;
    }
    private void ApplyEtherDiseaseChange(Chara target, bool isPc)
    {
        ApplyEtherDiseaseChangeInternal(target, isPc, false);
    }
    private void AddEtherDisease(Chara target, bool isPc)
    {
        ApplyEtherDiseaseChangeInternal(target, isPc, true);
    }
    private void ApplyEtherDiseaseChangeInternal(Chara target, bool isPc, bool addNew)
    {
        try
        {
            if (!TryBuildEtherDiseaseEdit(out var id, out var value, out var error))
            {
                _log = error;
                return;
            }

            var current = GetCurrentEtherDiseases(target);
            var oldId = (!addNew && _etherDiseaseSelectedIndex >= 0 && _etherDiseaseSelectedIndex < current.Count) ? current[_etherDiseaseSelectedIndex].Key : "";
            if (!addNew && !string.IsNullOrEmpty(oldId) && oldId != id.ToString(CultureInfo.InvariantCulture))
                SetEtherDiseaseValue(target, ParseInt(oldId, 0), 0, isPc);

            SetEtherDiseaseValue(target, id, value, isPc);

            var diseases = GetCurrentEtherDiseases(target);
            _etherDiseaseSelectedIndex = FindEtherDiseaseIndex(diseases, id);
            if (_etherDiseaseSelectedIndex >= 0)
                LoadEtherDiseaseEditorFields(target, diseases[_etherDiseaseSelectedIndex], _etherDiseaseSelectedIndex);

            _log = (addNew ? T("已新增以太病: ", "Added ether disease: ") : T("已修改以太病: ", "Modified ether disease: ")) + SafeName(target);
        }
        catch (Exception ex)
        {
            _log = T("修改以太病失败: ", "Modify ether disease failed: ") + ex.Message;
        }
    }
    private void DeleteSelectedEtherDisease(Chara target, bool isPc)
    {
        var diseases = GetCurrentEtherDiseases(target);
        if (_etherDiseaseSelectedIndex < 0 || _etherDiseaseSelectedIndex >= diseases.Count)
        {
            _log = T("未选中以太病", "No ether disease selected");
            return;
        }
        DeleteEtherDisease(target, diseases[_etherDiseaseSelectedIndex], isPc);
    }
    private void DeleteEtherDisease(Chara target, RowDef row, bool isPc)
    {
        try
        {
            var id = ParseInt(row.Key, 0);
            if (id <= 0)
            {
                _log = T("以太病不存在: ", "Ether disease does not exist: ") + row.Key;
                return;
            }

            SetEtherDiseaseValue(target, id, 0, isPc);
            var diseases = GetCurrentEtherDiseases(target);
            if (_etherDiseaseSelectedIndex >= diseases.Count)
                _etherDiseaseSelectedIndex = diseases.Count - 1;
            if (_etherDiseaseSelectedIndex >= 0)
                LoadEtherDiseaseEditorFields(target, diseases[_etherDiseaseSelectedIndex], _etherDiseaseSelectedIndex);
            else
                ResetEtherDiseaseEditorDraft();
            _log = T("已删除以太病: ", "Deleted ether disease: ") + row.Label;
        }
        catch (Exception ex)
        {
            _log = T("删除以太病失败: ", "Delete ether disease failed: ") + ex.Message;
        }
    }
    private bool TryBuildEtherDiseaseEdit(out int id, out int value, out string error)
    {
        id = 0;
        value = 0;
        error = "";

        if (!int.TryParse((_etherDiseaseId ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= 0)
        {
            error = T("以太病ID不是有效数字", "Ether disease ID is not a valid number");
            return false;
        }
        if (!IsEtherDiseaseId(id))
        {
            error = T("以太病不存在: ", "Ether disease does not exist: ") + id.ToString(CultureInfo.InvariantCulture);
            return false;
        }
        if (!int.TryParse((_etherDiseaseValue ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = T("等级输入不是数字", "Level input is not a number");
            return false;
        }

        value = Clamp(value, 0, Math.Max(1, GetEtherDiseaseMax(id.ToString(CultureInfo.InvariantCulture))));
        return true;
    }
    private void SetEtherDiseaseValue(Chara target, int id, int value, bool isPc)
    {
        value = Clamp(value, 0, Math.Max(1, GetEtherDiseaseMax(id.ToString(CultureInfo.InvariantCulture))));
        target.SetFeat(id, value, false);
        SyncEtherCorruptionFromDiseases(target);
        try { target.Refresh(false); } catch { }
        InvalidateCachedUiValues(GetTargetCachePrefix(target, isPc));
    }
    private static void SyncEtherCorruptionFromDiseases(Chara target)
    {
        if (target == null)
            return;
        try
        {
            var total = 0;
            foreach (var element in target.elements.ListElements(e => e != null && e.source != null && e.source.category == "ether" && e.Value != 0))
                total += element.Value;
            target.corruption = total * 100 + target.corruption % 100;
        }
        catch { }
    }
    private int FindEtherDiseaseIndex(List<RowDef> rows, int id)
    {
        var key = id.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Key == key)
                return i;
        return -1;
    }
    private bool IsEtherDiseaseId(int id)
    {
        EnsureEtherDiseaseRows();
        var key = id.ToString(CultureInfo.InvariantCulture);
        if (_etherDiseaseRows != null)
        {
            for (var i = 0; i < _etherDiseaseRows.Count; i++)
                if (_etherDiseaseRows[i].Key == key)
                    return true;
        }
        return false;
    }
    private int GetEtherDiseaseMax(string idText)
    {
        EnsureEtherDiseaseRows();
        if (_etherDiseaseRows == null)
            return 1;
        for (var i = 0; i < _etherDiseaseRows.Count; i++)
        {
            var row = _etherDiseaseRows[i];
            if (row.Key == idText)
                return row.Max > 0 ? row.Max : 1;
        }
        return 1;
    }
}
