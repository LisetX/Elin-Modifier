using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiHomePage()
    {
        EnsureHomeRows();
        var toolbar = CreateLGuiRect(_lGuiPageHost!, "HomeToolbar");
        AnchorLGuiTop(toolbar, 0f, 58f, 0f, 0f);
        CreateLGuiButton(toolbar, "PrevHome", "◀", 0f, 5f, 48f, 46f, () => CycleLGuiHome(-1));
        CreateLGuiButton(toolbar, "NextHome", "▶", 56f, 5f, 48f, 46f, () => CycleLGuiHome(1));
        _lGuiHomeSelectionText = CreateLGuiText(toolbar, "HomeSelection", "", 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiHomeSelectionText.rectTransform, 118f, 5f, 520f, 46f);
        var filter = CreateLGuiInput(toolbar, "HomeFilter", T("过滤", "Filter"), 650f, 5f, 420f, 46f);
        filter.text = _lGuiHomeFilter;
        filter.onValueChanged.AddListener(value =>
        {
            _lGuiHomeFilter = value ?? "";
            RebuildLGuiHomeRows();
        });

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "HomeList", 62f);
        _lGuiHomeList = new VirtualList<LGuiHomeRow>(scroll, 58f, 18, CreateLGuiVirtualRow, BindLGuiHomeRow);
        RebuildLGuiHomeRows();
    }
    private void CycleLGuiHome(int direction)
    {
        var homes = GetPlayerHomeBranches();
        if (homes.Count == 0)
            return;
        var branch = GetSelectedHomeBranch(homes);
        var index = branch == null ? 0 : homes.IndexOf(branch);
        if (index < 0) index = 0;
        index = (index + direction) % homes.Count;
        if (index < 0) index += homes.Count;
        _selectedHomeUid = GetHomeBranchKey(homes[index]);
        RebuildLGuiHomeRows();
    }
    private void RebuildLGuiHomeRows()
    {
        if (_lGuiHomeList == null)
            return;
        _lGuiHomeRows.Clear();
        var homes = GetPlayerHomeBranches();
        var branch = GetSelectedHomeBranch(homes);
        if (branch == null)
        {
            _lGuiHomeRows.Add(new LGuiHomeRow(T("未获取到家园数据", "No home data")));
            _lGuiHomeList.SetItems(_lGuiHomeRows);
            if (_lGuiHomeSelectionText != null) _lGuiHomeSelectionText.text = "-";
            return;
        }
        if (_lGuiHomeSelectionText != null)
            _lGuiHomeSelectionText.text = GetHomeBranchDisplayName(branch) + "  (" + homes.Count.ToString(CultureInfo.InvariantCulture) + ")";
        var zone = GetHomeZone(branch);
        var branchKey = GetHomeBranchKey(branch).ToString(CultureInfo.InvariantCulture);
        var basicExpanded = AddLGuiHomeHeader("basic", T("基础信息", "Basic Info"));
        if (basicExpanded)
        {
            AddLGuiHomeBasic(branchKey, T("盟约之石等级", "Covenant Stone Level"), "hearthLv", () => branch.lv.ToString(CultureInfo.InvariantCulture), value => SetHomeBranchLevel(branch, value));
            AddLGuiHomeBasic(branchKey, T("居民素质", "Resident Civility"), "civility", () => SafeHomeInt(() => branch.GetCivility()), value => SetHomeCivility(branch, value));
            AddLGuiHomeBasic(branchKey, T("肥沃度", "Fertility"), "soil", () => SafeHomeInt(() => branch.MaxSoil), value => SetHomeFertility(branch, zone, value));
            AddLGuiHomeBasic(branchKey, T("发展度", "Development"), "development", () => SafeHomeInt(() => Math.Max(0, zone == null ? 0 : zone.development / 10)), value => SetHomeDevelopment(zone, value));
            AddLGuiHomeBasic(branchKey, T("危险度", "Danger Level"), "danger", () => SafeHomeInt(() => branch.DangerLV), value => SetHomeDanger(branch, value));
            AddLGuiHomeBasic(branchKey, T("运营力上限", "Max Admin Power"), "maxAp", () => SafeHomeInt(() => branch.MaxAP), value => SetHomeMaxAp(branch, value));
        }
        AddLGuiHomeElements(branch, branchKey, "skills", T("家园技能", "Home Skills"), _homeSkillRows, HomeElementKind.Skill);
        AddLGuiHomeElements(branch, branchKey, "feats", T("家园专长", "Home Feats"), _homeFeatRows, HomeElementKind.Feat);
        AddLGuiHomeElements(branch, branchKey, "policies", T("家园政策", "Home Policies"), _homePolicyRows, HomeElementKind.Policy);
        _lGuiHomeList.SetItems(_lGuiHomeRows);
    }
    private bool AddLGuiHomeHeader(string sectionKey, string label)
    {
        if (!_lGuiHomeSectionExpanded.TryGetValue(sectionKey, out var expanded))
            expanded = false;
        _lGuiHomeRows.Add(new LGuiHomeRow(label, sectionKey, expanded));
        return expanded;
    }
    private void AddLGuiHomeBasic(string branchKey, string label, string key, Func<string> current, Action<int> apply)
    {
        if (!LGuiFilterMatches(label, key, "", _lGuiHomeFilter))
            return;
        var inputKey = "home:basic:" + branchKey + ":" + key;
        EnsureInput(inputKey, current());
        _lGuiHomeRows.Add(new LGuiHomeRow(label, inputKey, current, apply));
    }
    private void AddLGuiHomeElements(FactionBranch branch, string branchKey, string sectionKey, string title, List<HomeElementDef> rows, HomeElementKind kind)
    {
        if (!AddLGuiHomeHeader(sectionKey, title))
            return;
        if (rows == null)
            return;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!PassHomeElementFilter(row, _lGuiHomeFilter))
                continue;
            var inputKey = "home:element:" + branchKey + ":" + kind + ":" + row.Id.ToString(CultureInfo.InvariantCulture);
            EnsureInput(inputKey, GetHomeElementBaseLevel(branch, row.Id).ToString(CultureInfo.InvariantCulture));
            Func<bool>? isActive = kind == HomeElementKind.Policy ? () => IsHomePolicyActive(branch, row.Id) : null;
            Action<bool>? setActive = kind == HomeElementKind.Policy ? value => SetHomePolicyActive(branch, row, value) : null;
            _lGuiHomeRows.Add(new LGuiHomeRow(GetHomeElementLabel(row), inputKey,
                () => GetHomeElementValueText(branch, row.Id),
                value => SetHomeElementLevel(branch, row, value, kind),
                isActive, setActive));
        }
    }
}
