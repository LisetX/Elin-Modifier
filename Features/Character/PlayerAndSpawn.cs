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

    private void LoadPlayerInfoInputs()
    {
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            var player = GameAccess.Runtime.Player;
            if (pc == null || player == null)
            {
                _playerInfoLog = T("未获取到玩家数据", "No player data");
                return;
            }

            var bio = EnsureBiography(pc);
            _playerInfoLoadedUid = pc.uid;
            _playerInfoName = string.IsNullOrEmpty(pc.c_altName) ? pc.NameSimple : pc.c_altName;
            _playerInfoAlias = pc._alias ?? "";
            _playerInfoHonorific = player.title ?? "";
            _playerInfoRaceId = pc.c_idRace ?? "";
            _playerInfoJobId = pc.c_idJob ?? "";
            _playerInfoFaithId = pc.idFaith ?? "";
            _playerInfoFactionId = pc.idFaction ?? "";
            _playerInfoGender = bio.gender.ToString(CultureInfo.InvariantCulture);
            _playerInfoAge = SafeInt(() => bio.GetAge(pc)).ToString(CultureInfo.InvariantCulture);
            _playerInfoHeight = bio.height.ToString(CultureInfo.InvariantCulture);
            _playerInfoWeight = bio.weight.ToString(CultureInfo.InvariantCulture);
            _playerInfoBirthYear = bio.birthYear.ToString(CultureInfo.InvariantCulture);
            _playerInfoBirthMonth = bio.birthMonth.ToString(CultureInfo.InvariantCulture);
            _playerInfoBirthDay = bio.birthDay.ToString(CultureInfo.InvariantCulture);
            _playerInfoHomeId = bio.idHome.ToString(CultureInfo.InvariantCulture);
            _playerInfoLocId = bio.idLoc.ToString(CultureInfo.InvariantCulture);
            _playerInfoDadId = bio.idDad.ToString(CultureInfo.InvariantCulture);
            _playerInfoDadAdvId = bio.idAdvDad.ToString(CultureInfo.InvariantCulture);
            _playerInfoMomId = bio.idMom.ToString(CultureInfo.InvariantCulture);
            _playerInfoMomAdvId = bio.idAdvMom.ToString(CultureInfo.InvariantCulture);
            _playerInfoLikeCategoryId = pc.GetFavCat()?.id ?? "";
            _playerInfoLikeFoodId = pc.GetFavFood()?.id ?? "";
            _playerInfoDomains = JoinIntList(player.domains);
            _playerInfoHobbies = JoinIntList(GetCharaIntList(pc, "_hobbies"));
            _playerInfoWorks = JoinIntList(GetCharaIntList(pc, "_works"));
            _playerInfoTotalFeat = player.totalFeat.ToString(CultureInfo.InvariantCulture);
            _playerInfoNote = pc.c_note ?? "";
            _playerInfoMemo = player.memo ?? "";
            _playerInfoMemo2 = player.memo2 ?? "";
            _playerInfoBackground = LoadPlayerBackgroundRaw();
            _playerInfoLoaded = true;
            _playerInfoLog = T("玩家信息已读取", "Player info loaded");
        }
        catch (Exception ex)
        {
            _playerInfoLog = T("读取玩家信息失败: ", "Failed to load player info: ") + ex.Message;
        }
    }

    private void ApplyPlayerInfoInputs()
    {
        var errors = new List<string>();
        var pc = GameAccess.Characters.PlayerCharacter;
        var player = GameAccess.Runtime.Player;
        if (pc == null || player == null)
        {
            _playerInfoLog = T("未获取到玩家数据", "No player data");
            return;
        }

        var bio = EnsureBiography(pc);
        TryApplyPlayerInfo(T("名字", "Name"), () => pc.c_altName = (_playerInfoName ?? "").Trim(), errors);
        TryApplyPlayerInfo(T("别名/称号", "Alias"), () => pc._alias = (_playerInfoAlias ?? "").Trim(), errors);
        TryApplyPlayerInfo(T("敬称", "Honorific"), () => player.title = (_playerInfoHonorific ?? "").Trim(), errors);

        var raceId = (_playerInfoRaceId ?? "").Trim();
        if (!string.IsNullOrEmpty(raceId) && !string.Equals(raceId, pc.c_idRace, StringComparison.OrdinalIgnoreCase))
            TryApplyPlayerInfo(T("种族ID", "Race ID"), () => pc.ChangeRace(raceId), errors);

        var jobId = (_playerInfoJobId ?? "").Trim();
        if (!string.IsNullOrEmpty(jobId) && !string.Equals(jobId, pc.c_idJob, StringComparison.OrdinalIgnoreCase))
            TryApplyPlayerInfo(T("职业ID", "Job ID"), () => pc.ChangeJob(jobId), errors);

        ApplyPlayerInfoInt(T("性别", "Gender"), _playerInfoGender, errors, value => bio.SetGender(Clamp(value, 0, 2)));
        ApplyPlayerInfoInt(T("年龄", "Age"), _playerInfoAge, errors, value => bio.SetAge(pc, Math.Max(0, value)));
        ApplyPlayerInfoInt(T("身高", "Height"), _playerInfoHeight, errors, value => bio.height = Math.Max(1, value));
        ApplyPlayerInfoInt(T("体重", "Weight"), _playerInfoWeight, errors, value => bio.weight = Math.Max(1, value));
        ApplyPlayerInfoInt(T("出生年", "Birth year"), _playerInfoBirthYear, errors, value => bio.birthYear = value);
        ApplyPlayerInfoInt(T("出生月", "Birth month"), _playerInfoBirthMonth, errors, value => bio.birthMonth = Clamp(value, 1, 12));
        ApplyPlayerInfoInt(T("出生日", "Birth day"), _playerInfoBirthDay, errors, value => bio.birthDay = Clamp(value, 1, 31));
        ApplyPlayerInfoInt(T("家园词条ID", "Home word ID"), _playerInfoHomeId, errors, value => bio.idHome = value);
        ApplyPlayerInfoInt(T("所在地词条ID", "Location word ID"), _playerInfoLocId, errors, value => bio.idLoc = value);
        ApplyPlayerInfoInt(T("父亲类型ID", "Father type ID"), _playerInfoDadId, errors, value => bio.idDad = value);
        ApplyPlayerInfoInt(T("父亲修饰ID", "Father prefix ID"), _playerInfoDadAdvId, errors, value => bio.idAdvDad = value);
        ApplyPlayerInfoInt(T("母亲类型ID", "Mother type ID"), _playerInfoMomId, errors, value => bio.idMom = value);
        ApplyPlayerInfoInt(T("母亲修饰ID", "Mother prefix ID"), _playerInfoMomAdvId, errors, value => bio.idAdvMom = value);

        TryApplyPlayerInfo(T("喜欢类别ID", "Favorite category ID"), () => SetPlayerLikedCategoryId(pc, bio, _playerInfoLikeCategoryId), errors);
        TryApplyPlayerInfo(T("喜欢食物ID", "Favorite food ID"), () => SetPlayerLikedFoodId(pc, bio, _playerInfoLikeFoodId), errors);
        TryApplyPlayerInfo(T("所属势力ID", "Faction ID"), () => pc.idFaction = (_playerInfoFactionId ?? "").Trim(), errors);
        TryApplyPlayerInfo(T("信仰ID", "Faith ID"), () =>
        {
            var faithId = (_playerInfoFaithId ?? "").Trim();
            if (string.IsNullOrEmpty(faithId)) pc.idFaith = "";
            else pc.SetFaith(faithId);
        }, errors);
        TryApplyPlayerInfo(T("专业领域ID", "Domain IDs"), () =>
        {
            player.domains = ParseIntCsv(_playerInfoDomains);
            player.RefreshDomain();
        }, errors);
        TryApplyPlayerInfo(T("爱好ID列表", "Hobby IDs"), () => SetCharaIntList(pc, "_hobbies", ParseIntCsv(_playerInfoHobbies)), errors);
        TryApplyPlayerInfo(T("工作ID列表", "Work IDs"), () => SetCharaIntList(pc, "_works", ParseIntCsv(_playerInfoWorks)), errors);
        ApplyPlayerInfoInt(T("总专长点数", "Total feat points"), _playerInfoTotalFeat, errors, value => player.totalFeat = Math.Max(0, value));
        TryApplyPlayerInfo(T("角色备注", "Card note"), () => pc.c_note = _playerInfoNote ?? "", errors);
        TryApplyPlayerInfo(T("备忘录", "Memo"), () => player.memo = _playerInfoMemo ?? "", errors);
        TryApplyPlayerInfo(T("备忘录2", "Memo 2"), () => player.memo2 = _playerInfoMemo2 ?? "", errors);
        TryApplyPlayerInfo(T("成长经历", "Background"), () => SavePlayerBackgroundRaw(_playerInfoBackground ?? ""), errors);
        TryApplyPlayerInfo(T("刷新角色", "Refresh character"), () =>
        {
            pc.ValidateWorks();
            pc.Refresh(false);
            RefreshPlayerBiographyWindow(pc);
        }, errors);

        _playerInfoLoaded = false;
        LoadPlayerInfoInputs();
        _playerInfoLog = errors.Count == 0
            ? T("玩家信息已应用", "Player info applied")
            : T("部分项目失败: ", "Some items failed: ") + string.Join("; ", errors.ToArray());
    }

    private void ApplyPlayerInfoInt(string label, string text, List<string> errors, Action<int> apply)
    {
        int value;
        if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            errors.Add(label + T(" 不是数字", " is not a number"));
            return;
        }
        TryApplyPlayerInfo(label, () => apply(value), errors);
    }

    private static void TryApplyPlayerInfo(string label, Action action, List<string> errors)
    {
        try { action(); }
        catch (Exception ex) { errors.Add(label + ": " + ex.Message); }
    }

    private static Biography EnsureBiography(Chara pc)
    {
        if (pc.bio == null)
            pc.bio = new Biography();
        return pc.bio;
    }

    private static void SetPlayerLikedFoodId(Chara pc, Biography bio, string? value)
    {
        var id = (value ?? "").Trim();
        if (string.IsNullOrEmpty(id))
        {
            bio.idLike = "";
            if (bio.strs != null && bio.strs.Length > 1)
                bio.strs[1] = "";
            return;
        }

        var things = GameAccess.Sources.Things?.map;
        if (things == null || !things.ContainsKey(id))
            throw new ArgumentException("未找到物品ID: " + id);
        bio.idLike = id;
        if (bio.strs == null || bio.strs.Length < 3)
        {
            var old = bio.strs ?? Array.Empty<string>();
            Array.Resize(ref old, 3);
            bio.strs = old;
        }
        bio.strs[1] = PlayerLikedFoodOverridePrefix + id;
        if (!string.Equals(pc.bio?.idLike, id, StringComparison.Ordinal))
            throw new InvalidOperationException("喜欢食物ID写入后未保留");
    }

    private static void SetPlayerLikedCategoryId(Chara pc, Biography bio, string? value)
    {
        var id = (value ?? "").Trim();
        if (string.IsNullOrEmpty(id))
        {
            if (bio.strs != null && bio.strs.Length > 2)
                bio.strs[2] = "";
            return;
        }

        var categories = GameAccess.Sources.Categories?.map;
        if (categories == null || !categories.ContainsKey(id))
            throw new ArgumentException("未找到类别ID: " + id);
        EnsureBiographyStringCapacity(bio);
        bio.strs[2] = PlayerLikedCategoryOverridePrefix + id;
        if (!TryGetPlayerLikedCategoryOverride(pc, out var row) || row == null || !string.Equals(row.id, id, StringComparison.Ordinal))
            throw new InvalidOperationException("喜欢类别ID写入后未保留");
    }

    private static void EnsureBiographyStringCapacity(Biography bio)
    {
        if (bio.strs != null && bio.strs.Length >= 3)
            return;
        var old = bio.strs ?? Array.Empty<string>();
        Array.Resize(ref old, 3);
        bio.strs = old;
    }

    private const string PlayerLikedFoodOverridePrefix = "ElinModifierLikedItem:";
    private const string PlayerLikedCategoryOverridePrefix = "ElinModifierLikedCategory:";

    private static bool TryGetPlayerLikedFoodOverride(Chara chara, out SourceThing.Row? row)
    {
        row = null;
        try
        {
            if (chara == null || !chara.IsPC)
                return false;
            var strs = chara.bio?.strs;
            if (strs == null || strs.Length < 2 || string.IsNullOrEmpty(strs[1]) ||
                !strs[1].StartsWith(PlayerLikedFoodOverridePrefix, StringComparison.Ordinal))
                return false;
            var id = strs[1].Substring(PlayerLikedFoodOverridePrefix.Length);
            return !string.IsNullOrEmpty(id) && GameAccess.Sources.Things.map.TryGetValue(id, out row);
        }
        catch
        {
            row = null;
            return false;
        }
    }

    private static bool TryGetPlayerLikedCategoryOverride(Chara chara, out SourceCategory.Row? row)
    {
        row = null;
        try
        {
            if (chara == null || !chara.IsPC)
                return false;
            var strs = chara.bio?.strs;
            if (strs == null || strs.Length < 3 || string.IsNullOrEmpty(strs[2]) ||
                !strs[2].StartsWith(PlayerLikedCategoryOverridePrefix, StringComparison.Ordinal))
                return false;
            var id = strs[2].Substring(PlayerLikedCategoryOverridePrefix.Length);
            var categories = GameAccess.Sources.Categories?.map;
            return !string.IsNullOrEmpty(id) && categories != null && categories.TryGetValue(id, out row);
        }
        catch
        {
            row = null;
            return false;
        }
    }

    private static void RefreshPlayerBiographyWindow(Chara pc)
    {
        try
        {
            var window = WindowChara.Instance;
            if (window == null || !ReferenceEquals(window.chara, pc) || window.textLike == null)
                return;
            var id = pc.bio?.idLike ?? "";
            window.textLike.text = GameAccess.Sources.Cards.map.TryGetValue(id, out var row)
                ? row.GetName()
                : id;
        }
        catch
        {
        }
    }

    private static string LoadPlayerBackgroundRaw()
    {
        try
        {
            var path = GetPlayerBackgroundPath();
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        }
        catch { return ""; }
    }

    private static void SavePlayerBackgroundRaw(string text)
    {
        var path = GetPlayerBackgroundPath();
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException("background path is empty");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, text ?? "", Encoding.UTF8);
    }

    private static string GetPlayerBackgroundPath()
    {
        try
        {
            var savePath = GameIO.pathCurrentSave;
            return string.IsNullOrEmpty(savePath) ? "" : Path.Combine(savePath, "background.txt");
        }
        catch { return ""; }
    }

    private static List<int> ParseIntCsv(string text)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(text))
            return result;
        var parts = text.Split(new[] { ',', ';', '，', '；', '、', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            int value;
            if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException(part + " is not a number");
            result.Add(value);
        }
        return result;
    }

    private static string JoinIntList(IEnumerable<int> values)
    {
        if (values == null) return "";
        var sb = new StringBuilder();
        foreach (var value in values)
        {
            if (sb.Length > 0) sb.Append(",");
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static List<int> GetCharaIntList(Chara pc, string fieldName)
    {
        var result = new List<int>();
        var field = typeof(Chara).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(pc) is IEnumerable items)
        {
            foreach (var item in items)
                if (item is int value)
                    result.Add(value);
        }
        return result;
    }

    private static void SetCharaIntList(Chara pc, string fieldName, List<int> values)
    {
        var field = typeof(Chara).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(typeof(Chara).Name, fieldName);
        if (field.GetValue(pc) is IList<int> list)
        {
            list.Clear();
            foreach (var value in values)
                list.Add(value);
        }
        else
        {
            field.SetValue(pc, values);
        }
    }

    internal static int SafeInt(Func<int> getter, int fallback = 0)
    {
        try { return getter(); }
        catch { return fallback; }
    }

    internal static string SafeText(Func<string> getter, string fallback = "???")
    {
        try
        {
            var value = getter();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch { return fallback; }
    }

    private string GetPlayerDomainNames()
    {
        var container = GameAccess.Runtime.Player?.GetDomains();
        if (container == null) return "";
        var sb = new StringBuilder();
        foreach (var element in container.dict.Values)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(element.Name);
        }
        return sb.ToString();
    }

    private string GetPlayerFavoriteGiftText()
    {
        var pc = GameAccess.Characters.PlayerCharacter;
        if (pc == null) return "";
        var cat = pc.GetFavCat()?.GetName() ?? "";
        var food = pc.GetFavFood()?.GetName() ?? "";
        if (string.IsNullOrEmpty(cat)) return food;
        if (string.IsNullOrEmpty(food)) return cat;
        return cat + " / " + food;
    }

    private string GetPlayerWeaponStyleText()
    {
        var pc = GameAccess.Characters.PlayerCharacter;
        if (pc == null) return "";
        var weapon = pc.GetFavWeaponSkill();
        var weaponName = weapon != null ? weapon.Name : SafeText(() => Element.Get(100).GetText("name"));
        return weaponName + " / " + pc.GetFavAttackStyle();
    }

    private string GetPlayerArmorStyleText()
    {
        var pc = GameAccess.Characters.PlayerCharacter;
        if (pc == null) return "";
        var armor = pc.GetFavArmorSkill();
        return armor != null ? armor.Name : SafeText(() => Element.Get(120).GetText("name"));
    }

}
