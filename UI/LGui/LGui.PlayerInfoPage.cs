using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiPlayerInfoPage()
    {
        var pc = GetSafePc();
        if (pc != null && (!_playerInfoLoaded || _playerInfoLoadedUid != pc.uid))
            LoadPlayerInfoInputs();

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "PlayerInfoScroll", 0f);
        var content = scroll.content!;
        content.sizeDelta = new Vector2(0f, 2240f);
        var y = 8f;
        CreateLGuiButton(content, "ReloadPlayerInfo", T("刷新当前信息", "Reload current"), 0f, y, 160f, 46f, () =>
        {
            LoadPlayerInfoInputs();
            SwitchLGuiPage(LGuiPage.PlayerInfo);
        });
        CreateLGuiButton(content, "ApplyPlayerInfo", T("应用修改", "Apply changes"), 174f, y, 150f, 46f, () =>
        {
            ApplyPlayerInfoInputs();
            if (_lGuiPlayerInfoStatusText != null)
                _lGuiPlayerInfoStatusText.text = _playerInfoLog;
        });
        _lGuiPlayerInfoStatusText = CreateLGuiText(content, "PlayerInfoStatus", _playerInfoLog, 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiPlayerInfoStatusText.rectTransform, 342f, y, 900f, 46f);
        y += 62f;

        if (pc != null)
            y = AddLGuiPlayerInfoPreview(content, pc, y);

        y = AddLGuiSectionTitle(content, T("基础信息", "Basic Info"), y);
        y = AddLGuiBoundInput(content, T("名字", "Name"), () => _playerInfoName, value => _playerInfoName = value, y);
        y = AddLGuiBoundInput(content, T("别名/称号", "Alias"), () => _playerInfoAlias, value => _playerInfoAlias = value, y);
        y = AddLGuiBoundInput(content, T("敬称", "Honorific"), () => _playerInfoHonorific, value => _playerInfoHonorific = value, y);
        y = AddLGuiBoundInput(content, T("种族ID", "Race ID"), () => _playerInfoRaceId, value => _playerInfoRaceId = value, y);
        y = AddLGuiBoundInput(content, T("职业ID", "Job ID"), () => _playerInfoJobId, value => _playerInfoJobId = value, y);
        y = AddLGuiBoundInput(content, T("性别(0-2)", "Gender (0-2)"), () => _playerInfoGender, value => _playerInfoGender = value, y, 180f);
        y = AddLGuiBoundInput(content, T("年龄", "Age"), () => _playerInfoAge, value => _playerInfoAge = value, y, 180f);
        y = AddLGuiBoundInput(content, T("身高cm", "Height cm"), () => _playerInfoHeight, value => _playerInfoHeight = value, y, 180f);
        y = AddLGuiBoundInput(content, T("体重kg", "Weight kg"), () => _playerInfoWeight, value => _playerInfoWeight = value, y, 180f);
        y = AddLGuiBoundInput(content, T("所属势力ID", "Faction ID"), () => _playerInfoFactionId, value => _playerInfoFactionId = value, y);
        y = AddLGuiBoundInput(content, T("信仰ID", "Faith ID"), () => _playerInfoFaithId, value => _playerInfoFaithId = value, y);

        y = AddLGuiSectionTitle(content, T("出生 / 父母 / 地点", "Birth / Parents / Places"), y + 8f);
        y = AddLGuiBoundInput(content, T("出生年", "Birth year"), () => _playerInfoBirthYear, value => _playerInfoBirthYear = value, y, 180f);
        y = AddLGuiBoundInput(content, T("出生月", "Birth month"), () => _playerInfoBirthMonth, value => _playerInfoBirthMonth = value, y, 180f);
        y = AddLGuiBoundInput(content, T("出生日", "Birth day"), () => _playerInfoBirthDay, value => _playerInfoBirthDay = value, y, 180f);
        y = AddLGuiBoundInput(content, T("家园词条ID", "Home word ID"), () => _playerInfoHomeId, value => _playerInfoHomeId = value, y, 180f);
        y = AddLGuiBoundInput(content, T("所在地词条ID", "Location word ID"), () => _playerInfoLocId, value => _playerInfoLocId = value, y, 180f);
        y = AddLGuiBoundInput(content, T("父亲类型ID", "Father type ID"), () => _playerInfoDadId, value => _playerInfoDadId = value, y, 180f);
        y = AddLGuiBoundInput(content, T("父亲修饰ID", "Father prefix ID"), () => _playerInfoDadAdvId, value => _playerInfoDadAdvId = value, y, 180f);
        y = AddLGuiBoundInput(content, T("母亲类型ID", "Mother type ID"), () => _playerInfoMomId, value => _playerInfoMomId = value, y, 180f);
        y = AddLGuiBoundInput(content, T("母亲修饰ID", "Mother prefix ID"), () => _playerInfoMomAdvId, value => _playerInfoMomAdvId = value, y, 180f);
        y = AddLGuiBoundInput(content, T("喜欢物品ID", "Liked item ID"), () => _playerInfoLikeId, value => _playerInfoLikeId = value, y);

        y = AddLGuiSectionTitle(content, T("列表ID", "ID Lists"), y + 8f);
        y = AddLGuiBoundInput(content, T("专业领域ID", "Domain IDs"), () => _playerInfoDomains, value => _playerInfoDomains = value, y, 720f);
        y = AddLGuiBoundInput(content, T("爱好ID列表", "Hobby IDs"), () => _playerInfoHobbies, value => _playerInfoHobbies = value, y, 720f);
        y = AddLGuiBoundInput(content, T("工作ID列表", "Work IDs"), () => _playerInfoWorks, value => _playerInfoWorks = value, y, 720f);
        y = AddLGuiBoundInput(content, T("总专长点数", "Total feat points"), () => _playerInfoTotalFeat, value => _playerInfoTotalFeat = value, y, 180f);

        y = AddLGuiSectionTitle(content, T("笔记 / 文本", "Notes / Text"), y + 8f);
        y = AddLGuiBoundMultilineInput(content, T("成长经历", "Background"), () => _playerInfoBackground, value => _playerInfoBackground = value, y, 150f);
        y = AddLGuiBoundMultilineInput(content, T("备忘录", "Memo"), () => _playerInfoMemo, value => _playerInfoMemo = value, y, 110f);
        y = AddLGuiBoundMultilineInput(content, T("备忘录2", "Memo 2"), () => _playerInfoMemo2, value => _playerInfoMemo2 = value, y, 110f);
        y = AddLGuiBoundMultilineInput(content, T("角色备注", "Card note"), () => _playerInfoNote, value => _playerInfoNote = value, y, 110f);
        content.sizeDelta = new Vector2(0f, Math.Max(900f, y + 30f));
    }
    private float AddLGuiSectionTitle(RectTransform parent, string label, float y)
    {
        var text = CreateLGuiText(parent, "Section", label, 20, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(text.rectTransform, 0f, y, 1100f, 42f);
        return y + 46f;
    }
    private float AddLGuiBoundInput(RectTransform parent, string label, Func<string> read, Action<string> write, float y, float width = 620f)
    {
        var caption = CreateLGuiText(parent, "Label", label, 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(caption.rectTransform, 0f, y, 210f, 44f);
        var input = CreateLGuiInput(parent, "Input", label, 220f, y, width, 44f);
        input.text = read() ?? "";
        input.onValueChanged.AddListener(value => write(value ?? ""));
        return y + 50f;
    }
    private float AddLGuiBoundMultilineInput(RectTransform parent, string label, Func<string> read, Action<string> write, float y, float height)
    {
        var caption = CreateLGuiText(parent, "Label", label, 17, TextAnchor.UpperLeft, FontStyle.Normal);
        PlaceLGuiRect(caption.rectTransform, 0f, y, 210f, 44f);
        var input = CreateLGuiMultilineInput(parent, "Multiline", 220f, y, 1080f, height);
        input.SetTextWithoutNotify(read() ?? "");
        input.onValueChanged.AddListener(value => write(value ?? ""));
        return y + height + 10f;
    }
}
