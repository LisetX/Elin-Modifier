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
    private float AddLGuiPlayerInfoPreview(RectTransform content, Chara pc, float y)
    {
        var bio = EnsureBiography(pc);
        y = AddLGuiSectionTitle(content, T("当前显示", "Current display"), y);
        y = AddLGuiReadOnlyRow(content, T("名字", "Name"), SafeText(() => pc.NameSimple), y);
        y = AddLGuiReadOnlyRow(content, T("别名/称号", "Alias"), SafeText(() => pc.Aka), y);
        y = AddLGuiReadOnlyRow(content, T("敬称", "Honorific"), SafeText(() => GameAccess.Runtime.Player.title), y);
        y = AddLGuiReadOnlyRow(content, T("简介", "Profile"), SafeText(() => bio.TextBio(pc) + " " + bio.TextBio2(pc)), y);
        y = AddLGuiReadOnlyRow(content, T("生日", "Birthday"), SafeText(() => bio.TextBirthDate(pc, false)), y);
        y = AddLGuiReadOnlyRow(content, T("父亲", "Father"), SafeText(() => bio.nameDad), y);
        y = AddLGuiReadOnlyRow(content, T("母亲", "Mother"), SafeText(() => bio.nameMom), y);
        y = AddLGuiReadOnlyRow(content, T("出生地", "Birthplace"), SafeText(() => bio.nameBirthplace), y);
        y = AddLGuiReadOnlyRow(content, T("家园", "Home"), SafeText(() => pc.homeZone == null ? "???" : pc.homeZone.Name, "???"), y);
        y = AddLGuiReadOnlyRow(content, T("所在地", "Current zone"), SafeText(() => pc.currentZone == null ? "???" : pc.currentZone.Name, "???"), y);
        y = AddLGuiReadOnlyRow(content, T("所属势力", "Faction"), SafeText(() => pc.faction == null ? "???" : pc.faction.name, "???"), y);
        y = AddLGuiReadOnlyRow(content, T("信仰", "Faith"), SafeText(() => pc.faith == null ? "???" : pc.faith.Name, "???"), y);
        y = AddLGuiReadOnlyRow(content, T("专业领域", "Domains"), SafeText(GetPlayerDomainNames), y);
        y = AddLGuiReadOnlyRow(content, T("喜欢的东西", "Favorite gift"), SafeText(GetPlayerFavoriteGiftText), y);
        y = AddLGuiReadOnlyRow(content, T("爱好", "Hobby"), SafeText(() => pc.GetTextHobby(false)), y);
        y = AddLGuiReadOnlyRow(content, T("工作", "Work"), SafeText(() => pc.GetTextWork(false)), y);
        y = AddLGuiReadOnlyRow(content, T("武器风格", "Weapon style"), SafeText(GetPlayerWeaponStyleText), y);
        y = AddLGuiReadOnlyRow(content, T("护甲风格", "Armor style"), SafeText(GetPlayerArmorStyleText), y);
        return y + 8f;
    }
}
