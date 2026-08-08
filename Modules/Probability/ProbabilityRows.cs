using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class ProbabilityModule
{
    private void BindProbabilityRow(RectTransform rect, ProbabilityRow model, int index)
    {
        var view = rect.GetComponent<LGuiRowView>();
        view.BeginBind();
        view.BoundData = model;
        view.BoundIndex = index;
        view.Icon.gameObject.SetActive(false);
        view.Toggle.gameObject.SetActive(false);
        view.Label.gameObject.SetActive(true);
        view.Label.fontStyle = FontStyle.Normal;

        if (model.IsMessage)
        {
            ApplyLGuiRowVisual(view, index, true);
            view.Label.text = model.Message;
            view.Secondary.gameObject.SetActive(false);
            view.Input.gameObject.SetActive(false);
            view.Primary.gameObject.SetActive(false);
            view.Auxiliary.gameObject.SetActive(false);
            view.EndBind();
            return;
        }

        if (model.IsHeader)
        {
            ApplyLGuiRowVisual(view, index, true);
            view.Label.text = GetProbabilityCategoryLabel(model.CategoryKey);
            view.Secondary.gameObject.SetActive(true);
            view.Secondary.text = model.Count.ToString(CultureInfo.InvariantCulture) + T(" 项", " values") +
                                  (model.ModifiedCount > 0 ? "  |  " + T("已修改 ", "Modified ") + model.ModifiedCount.ToString(CultureInfo.InvariantCulture) : "");
            view.Input.gameObject.SetActive(false);
            view.Primary.gameObject.SetActive(true);
            view.PrimaryText.text = model.Expanded ? T("折叠", "Collapse") : T("展开", "Expand");
            view.Auxiliary.gameObject.SetActive(false);
            view.EndBind();
            return;
        }

        var entry = model.Entry!;
        ApplyLGuiRowVisual(view, index);
        var idText = string.IsNullOrWhiteSpace(entry.RowId) ? "" : "  [" + entry.RowId + "]";
        view.Label.text = IndentLGuiText(entry.DisplayName + idText, 1);
        view.Secondary.gameObject.SetActive(true);
        object current;
        try { current = entry.ReadCurrent(); }
        catch { current = entry.InitialValue; }
        var original = entry.HasOriginal ? entry.OriginalValue ?? entry.InitialValue : entry.InitialValue;
        view.Secondary.text = GetProbabilityMemberLabel(entry) + ": " + FormatProbabilityValue(current) +
                              "  |  " + T("原始 ", "Original ") + FormatProbabilityValue(original);
        view.Input.gameObject.SetActive(true);
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != view.Input.gameObject)
        {
            if (!entry.InputDirty)
                entry.InputText = FormatProbabilityValue(current);
            view.SetInputWithoutNotify(entry.InputText);
        }
        view.Primary.gameObject.SetActive(true);
        view.PrimaryText.text = T("应用", "Apply");
        view.Auxiliary.gameObject.SetActive(entry.HasOriginal);
        view.AuxiliaryText.text = T("恢复", "Restore");
        view.EndBind();
    }
    private void ApplyProbabilityEntry(ProbabilityEntry entry, string text)
    {
        if (!TryParseProbabilityValue(text, entry.Field.FieldType, out var parsed, out var error))
        {
            entry.InputDirty = true;
            _probabilityLog = entry.DisplayName + " / " + GetProbabilityMemberLabel(entry) + ": " + error;
            return;
        }

        try
        {
            var current = entry.ReadCurrent();
            if (!entry.HasOriginal && Equals(current, parsed))
            {
                entry.InputText = FormatProbabilityValue(current);
                entry.InputDirty = false;
                _probabilityLog = T("数值未发生变化", "Value is unchanged");
                return;
            }

            if (!entry.HasOriginal)
            {
                entry.OriginalValue = current;
                entry.HasOriginal = true;
            }
            entry.Field.SetValue(entry.Owner, parsed);
            RefreshProbabilityCaches();
            var applied = entry.ReadCurrent();
            entry.InputText = FormatProbabilityValue(applied);
            entry.InputDirty = false;
            if (entry.OriginalValue != null && Equals(applied, entry.OriginalValue))
            {
                entry.HasOriginal = false;
                entry.OriginalValue = null;
            }
            RecountProbabilityModifications();
            _probabilityLog = T("已修改: ", "Modified: ") + entry.DisplayName + " / " + GetProbabilityMemberLabel(entry) + " = " + entry.InputText;
        }
        catch (Exception ex)
        {
            _probabilityLog = T("修改失败: ", "Modification failed: ") + ex.Message;
        }
    }
    private void RestoreProbabilityEntry(ProbabilityEntry entry, bool updateLog)
    {
        if (!entry.HasOriginal)
            return;
        try
        {
            entry.Field.SetValue(entry.Owner, entry.OriginalValue);
            RefreshProbabilityCaches();
            entry.InputText = FormatProbabilityValue(entry.ReadCurrent());
            entry.InputDirty = false;
            entry.HasOriginal = false;
            entry.OriginalValue = null;
            RecountProbabilityModifications();
            if (updateLog)
                _probabilityLog = T("已恢复: ", "Restored: ") + entry.DisplayName + " / " + GetProbabilityMemberLabel(entry);
        }
        catch (Exception ex)
        {
            if (updateLog)
                _probabilityLog = T("恢复失败: ", "Restore failed: ") + ex.Message;
        }
    }
    internal void RestoreAll(bool updateLog)
    {
        var restored = 0;
        var failed = 0;
        for (var i = 0; i < _probabilityEntries.Count; i++)
        {
            var entry = _probabilityEntries[i];
            if (!entry.HasOriginal)
            {
                if (entry.InputDirty)
                {
                    try { entry.InputText = FormatProbabilityValue(entry.ReadCurrent()); }
                    catch { entry.InputText = FormatProbabilityValue(entry.InitialValue); }
                    entry.InputDirty = false;
                }
                continue;
            }
            try
            {
                entry.Field.SetValue(entry.Owner, entry.OriginalValue);
                entry.InputText = FormatProbabilityValue(entry.ReadCurrent());
                entry.InputDirty = false;
                entry.HasOriginal = false;
                entry.OriginalValue = null;
                restored++;
            }
            catch
            {
                failed++;
            }
        }
        if (restored > 0)
            RefreshProbabilityCaches();
        RecountProbabilityModifications();
        if (updateLog)
            _probabilityLog = T("已恢复 ", "Restored ") + restored.ToString(CultureInfo.InvariantCulture) + T(" 项概率修改", " probability values") +
                                     (failed > 0 ? T("，失败 ", "; failed: ") + failed.ToString(CultureInfo.InvariantCulture) : "");
    }
    private void RecountProbabilityModifications()
    {
        _probabilityModifiedCount = 0;
        for (var i = 0; i < _probabilityEntries.Count; i++)
            if (_probabilityEntries[i].HasOriginal)
                _probabilityModifiedCount++;
        UpdateProbabilitySummary();
    }
    private void RefreshProbabilityCaches()
    {
        ApplyMiniGameProbabilityValues();
        ApplyDropMultiplierValues();

        try
        {
            var lists = SpawnList.allList == null
                ? new List<SpawnList>()
                : SpawnList.allList.Values.Where(list => list != null).Distinct().ToList();

            var sourceLists = lists
                .Where(list => !string.IsNullOrWhiteSpace(list.id) &&
                               GameAccess.Sources.SpawnLists?.map != null &&
                               GameAccess.Sources.SpawnLists.map.ContainsKey(list.id))
                .OrderBy(list => GetSpawnListDepth(list.id))
                .ToList();
            var sourceListSet = new HashSet<SpawnList>(sourceLists);
            for (var i = 0; i < sourceLists.Count; i++)
            {
                var list = sourceLists[i];
                if (list.filter != null && GameAccess.Sources.SpawnLists.map.TryGetValue(list.id, out var sourceRow))
                    list.CreateMaster(list.filter, sourceRow.parent);
                else
                    RecalculateSpawnListTotal(list);
            }

            for (var i = 0; i < lists.Count; i++)
            {
                var list = lists[i];
                if (sourceListSet.Contains(list))
                    continue;

                if (list.filter is CharaFilter)
                {
                    var parent = GetDynamicCharaSpawnListParent(list);
                    if (!string.IsNullOrWhiteSpace(parent) && !string.Equals(parent, list.id, StringComparison.Ordinal))
                    {
                        list.CreateMaster(list.filter, parent);
                        continue;
                    }
                }
                else if (list.filter is ThingFilter &&
                         !string.Equals(list.id, "thing", StringComparison.Ordinal))
                {
                    list.CreateMaster(list.filter, "thing");
                    continue;
                }

                RecalculateSpawnListTotal(list);
            }
        }
        catch { }

        try
        {
            if (SourceMaterial.tierMap == null)
                return;
            foreach (var tierList in SourceMaterial.tierMap.Values)
            {
                if (tierList?.tiers == null)
                    continue;
                for (var tierIndex = 0; tierIndex < tierList.tiers.Length; tierIndex++)
                {
                    var tier = tierList.tiers[tierIndex];
                    if (tier?.list == null)
                        continue;
                    var total = 0L;
                    for (var rowIndex = 0; rowIndex < tier.list.Count; rowIndex++)
                        total += tier.list[rowIndex]?.chance ?? 0;
                    tier.sum = total > int.MaxValue ? int.MaxValue : total < int.MinValue ? int.MinValue : (int)total;
                }
            }
        }
        catch { }
    }
}
