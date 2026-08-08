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
    private void OpenLGuiItemAmountEditor()
    {
        var target = _itemAmountTarget;
        var modal = CreateLGuiCompleteModal("RuntimeItemAmount", T("修改持有数量", "Modify held amount"), out var content, 980f, 620f);
        if (modal == null) return;
        var y = 10f;
        y = AddLGuiReadOnlyRow(content, T("物品", "Item"), _itemAmountName, y);
        y = AddLGuiReadOnlyRow(content, T("当前数量", "Current amount"), target == null ? "-" : target.Num.ToString(CultureInfo.InvariantCulture), y);
        AddLGuiInlineInput(content, T("目标数量:", "Target amount:"), () => _itemAmountInput, value => _itemAmountInput = value, 0f, y, 150f, 180f);
        y += 60f;
        CreateLGuiButton(content, "Apply", T("确认", "Confirm"), 0f, y, 120f, 44f, ApplyLGuiItemAmountChange);
        CreateLGuiButton(content, "Cancel", T("取消", "Cancel"), 134f, y, 120f, 44f, CloseLGuiEditorModal);
        content.sizeDelta = new Vector2(0f, 430f);
    }
    private void OpenLGuiRodStackingEditor()
    {
        var target = _rodStackingTarget;
        var candidates = GetRodStackingCandidates(target);
        if (_rodStackingSource == null || !candidates.Any(candidate => ReferenceEquals(candidate, _rodStackingSource)))
            _rodStackingSource = candidates.FirstOrDefault();

        const int pageSize = 3;
        var totalPages = Math.Max(1, (candidates.Count + pageSize - 1) / pageSize);
        _rodStackingCandidatePage = Math.Max(0, Math.Min(_rodStackingCandidatePage, totalPages - 1));
        var pageStart = _rodStackingCandidatePage * pageSize;
        var pageEnd = Math.Min(pageStart + pageSize, candidates.Count);
        var visibleRowCount = Math.Max(1, pageEnd - pageStart);
        const float candidateRowStartY = 326f;
        const float candidateRowStep = 46f;
        var controlsY = candidateRowStartY + visibleRowCount * candidateRowStep + 12f;
        var contentHeight = controlsY + 56f;
        var modalHeight = Math.Max(640f, Math.Min(790f, contentHeight + 150f));

        var modal = CreateLGuiCompleteModal("RuntimeRodStacking", T("充能堆叠", "Stack charges"), out var content, 1120f, modalHeight);
        if (modal == null)
            return;

        void AddRodSlot(string name, string title, Thing? thing, float x)
        {
            var titleText = CreateLGuiText(content, name + "Title", title, 17, TextAnchor.MiddleCenter, FontStyle.Normal);
            PlaceLGuiRect(titleText.rectTransform, x, 6f, 250f, 38f);

            var background = CreateLGuiImage(content, name + "Background", x, 48f, 250f, 188f);
            background.color = GetLGuiRowColor(name == "Target" ? 0 : 1, true);
            RegisterLGuiRoundedImage(background);

            if (thing != null)
            {
                try
                {
                    var icon = CreateLGuiImage(background.transform, name + "Icon", 65f, 12f, 120f, 112f);
                    icon.sprite = thing.GetSprite(0);
                    icon.preserveAspect = true;
                    icon.color = Color.white;
                }
                catch { }

                var itemName = CreateLGuiText(background.transform, name + "Name", SafeThingName(thing), 15, TextAnchor.MiddleCenter, FontStyle.Normal);
                PlaceLGuiRect(itemName.rectTransform, 8f, 126f, 234f, 28f);
                var charges = CreateLGuiText(background.transform, name + "Charges", T("充能", "Charges") + ": " + Math.Max(0, thing.c_charges).ToString(CultureInfo.InvariantCulture), 15, TextAnchor.MiddleCenter, FontStyle.Normal);
                PlaceLGuiRect(charges.rectTransform, 8f, 154f, 234f, 26f);
            }
            else
            {
                var empty = CreateLGuiText(background.transform, name + "Empty", T("请选择消耗物品", "Select a donor item"), 15, TextAnchor.MiddleCenter, FontStyle.Normal);
                StretchLGuiRect(empty.rectTransform, 12f, 12f, 12f, 12f);
            }
        }

        AddRodSlot("Target", T("被充能物品", "Receiving item"), target, 100f);
        var arrow = CreateLGuiText(content, "Arrow", "←", 34, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(arrow.rectTransform, 458f, 100f, 120f, 64f);
        AddRodSlot("Source", T("消耗物品", "Consumed item"), _rodStackingSource, 670f);

        var expected = target != null && _rodStackingSource != null
            ? Math.Min(int.MaxValue, (long)Math.Max(0, target.c_charges) + Math.Max(0, _rodStackingSource.c_charges)).ToString(CultureInfo.InvariantCulture)
            : "-";
        var expectedText = CreateLGuiText(content, "ExpectedCharges", T("堆叠后充能", "Charges after stacking") + ": " + expected, 16, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(expectedText.rectTransform, 0f, 242f, 1036f, 36f);

        var listTitle = CreateLGuiText(content, "CandidateTitle", T("可消耗物品", "Consumable items"), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(listTitle.rectTransform, 18f, 282f, 700f, 38f);
        var pageText = CreateLGuiText(content, "CandidatePage", (_rodStackingCandidatePage + 1).ToString(CultureInfo.InvariantCulture) + " / " + totalPages.ToString(CultureInfo.InvariantCulture), 15, TextAnchor.MiddleRight, FontStyle.Normal);
        PlaceLGuiRect(pageText.rectTransform, 850f, 282f, 180f, 38f);

        if (candidates.Count == 0)
        {
            var empty = CreateLGuiText(content, "NoCandidates", T("没有可消耗物品", "No consumable items"), 16, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(empty.rectTransform, 18f, candidateRowStartY, 920f, 40f);
        }
        else
        {
            for (var i = pageStart; i < pageEnd; i++)
            {
                var candidate = candidates[i];
                var rowY = candidateRowStartY + (i - pageStart) * candidateRowStep;
                var selected = ReferenceEquals(candidate, _rodStackingSource);
                var row = CreateLGuiImage(content, "CandidateRow" + i.ToString(CultureInfo.InvariantCulture), 18f, rowY, 1012f, 40f);
                row.color = GetLGuiRowColor(i, selected);
                RegisterLGuiRoundedImage(row);
                var label = CreateLGuiText(row.transform, "Label", SafeThingName(candidate) + "    " + T("充能", "Charges") + ": " + Math.Max(0, candidate.c_charges).ToString(CultureInfo.InvariantCulture), 15, TextAnchor.MiddleLeft, FontStyle.Normal);
                PlaceLGuiRect(label.rectTransform, 14f, 1f, 790f, 38f);
                CreateLGuiButton(row.transform, "Select", selected ? "→ " + T("已选择", "Selected") : T("选择", "Select"), 824f, 2f, 172f, 36f, () =>
                {
                    _rodStackingSource = candidate;
                    OpenLGuiRodStackingEditor();
                });
            }
        }

        CreateLGuiButton(content, "Previous", T("上一页", "Previous"), 18f, controlsY, 126f, 44f, () =>
        {
            _rodStackingCandidatePage = Math.Max(0, _rodStackingCandidatePage - 1);
            OpenLGuiRodStackingEditor();
        });
        CreateLGuiButton(content, "Next", T("下一页", "Next"), 156f, controlsY, 126f, 44f, () =>
        {
            _rodStackingCandidatePage = Math.Min(totalPages - 1, _rodStackingCandidatePage + 1);
            OpenLGuiRodStackingEditor();
        });
        CreateLGuiButton(content, "Apply", T("充能堆叠", "Stack charges"), 748f, controlsY, 150f, 44f, () =>
        {
            if (ApplyRodStacking())
                CloseLGuiEditorModal();
            else
                OpenLGuiRodStackingEditor();
        });
        CreateLGuiButton(content, "Cancel", T("取消", "Cancel"), 912f, controlsY, 118f, 44f, CloseLGuiEditorModal);
        content.sizeDelta = new Vector2(0f, contentHeight);
    }
}
