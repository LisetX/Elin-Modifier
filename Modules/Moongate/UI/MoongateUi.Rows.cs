using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private float CreateLGuiMoongateHeader(RectTransform content, float y, bool isLocal = false)
    {
        var row = CreateLGuiRect(content, "MoongateHeader");
        PlaceLGuiRect(row, 0f, y, 1390f, 42f);
        var image = row.gameObject.AddComponent<Image>();
        image.color = GetLGuiRowColor(0, true);
        RegisterLGuiRoundedImage(image);
        CreateLGuiMoongateCell(row, T("地图标题", "Title"), 16f, 0f, 220f, 42f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, T("作者", "Author"), 244f, 0f, 175f, 42f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, T("地图ID", "Map ID"), 427f, 0f, 205f, 42f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(
            row,
            isLocal ? T("缓存时间", "Cached") : T("上传时间", "Uploaded"),
            640f,
            0f,
            180f,
            42f,
            TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, T("游戏版本", "Game version"), 828f, 0f, 82f, 42f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, T("语言区", "Language"), 918f, 0f, 64f, 42f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, T("来源", "Source"), 990f, 0f, 116f, 42f, TextAnchor.MiddleLeft);
        return y + 48f;
    }
    private float CreateLGuiMoongateFavoriteRow(
        RectTransform content,
        MoongateModule.FavoriteEntry entry,
        int index,
        float y)
    {
        var row = CreateLGuiMoongateRow(content, "FavoriteMoongate_" + index, index, y);
        CreateLGuiMoongateCell(row, string.IsNullOrWhiteSpace(entry.Title) ? T("未加载地图信息", "Metadata not loaded") : entry.Title, 16f, 0f, 220f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Author, 244f, 0f, 175f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Id, 427f, 0f, 205f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.SourceDate, 640f, 0f, 180f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, FormatMoongateVersion(entry.Version), 828f, 0f, 82f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Language, 918f, 0f, 64f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, FormatMoongateSource(entry.SourceKind), 990f, 0f, 116f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiButton(row, "Enter", T("进入", "Enter"), 1116f, 5f, 102f, 44f, () =>
            _modules.Moongate.BeginEnterMap(entry.Id, entry.Language)).interactable = _modules.Moongate.CanEnterMoongate;
        CreateLGuiButton(row, "Remove", T("取消收藏", "Remove"), 1226f, 5f, 148f, 44f, () =>
            _modules.Moongate.RemoveFavorite(entry.Id));
        return y + 60f;
    }
    private float CreateLGuiMoongateSearchRow(
        RectTransform content,
        MoongateModule.MapEntry entry,
        int index,
        float y)
    {
        var row = CreateLGuiMoongateRow(content, "SearchMoongate_" + index, index, y);
        CreateLGuiMoongateCell(row, entry.Title, 16f, 0f, 220f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Author, 244f, 0f, 175f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Id, 427f, 0f, 205f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.SourceDate, 640f, 0f, 180f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, FormatMoongateVersion(entry.Version), 828f, 0f, 82f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Language, 918f, 0f, 64f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, FormatMoongateSource(entry.SourceKind), 990f, 0f, 116f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiButton(row, "Enter", T("进入", "Enter"), 1116f, 5f, 102f, 44f, () =>
            _modules.Moongate.BeginEnterMap(entry.Id, entry.Language)).interactable = _modules.Moongate.CanEnterMoongate;
        var isFavorite = _modules.Moongate.IsFavorite(entry.Id);
        CreateLGuiButton(
            row,
            "Favorite",
            isFavorite ? T("已收藏", "Favorited") : T("收藏", "Favorite"),
            1226f,
            5f,
            148f,
            44f,
            () =>
            {
                if (isFavorite)
                    _modules.Moongate.RemoveFavorite(entry.Id);
                else
                    _modules.Moongate.AddFavorite(entry.Id);
            });
        return y + 60f;
    }
    private float CreateLGuiMoongateLocalRow(
        RectTransform content,
        MoongateModule.LocalEntry entry,
        int index,
        float y)
    {
        var row = CreateLGuiMoongateRow(content, "LocalMoongate_" + index, index, y, 106f);
        CreateLGuiMoongateCell(row, entry.Title, 16f, 0f, 220f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Author, 244f, 0f, 175f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Id, 427f, 0f, 205f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.CachedAt, 640f, 0f, 180f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, FormatMoongateVersion(entry.Version), 828f, 0f, 82f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, entry.Language, 918f, 0f, 64f, 54f, TextAnchor.MiddleLeft);
        CreateLGuiMoongateCell(row, FormatMoongateSource(entry.SourceKind), 990f, 0f, 116f, 54f, TextAnchor.MiddleLeft);
        var updating = _modules.Moongate.IsUpdatingLocalMap(entry.Id);
        var updateBusy = _modules.Moongate.IsUpdatingAnyLocalMap;
        var persistent = CreateLGuiToggle(row, "Persistent", 618f, 58f, 150f, 42f, out var persistentLabel);
        persistentLabel.text = T("持久化", "Persistent");
        persistent.isOn = entry.IsPersistent;
        persistent.interactable = !updateBusy;
        persistent.onValueChanged.AddListener(value =>
            _modules.Moongate.SetLocalMapPersistence(entry, value));

        var actionX = 776f;
        if (entry.IsPersistent)
        {
            CreateLGuiButton(row, "Restore", T("重建持久化地图", "Rebuild persistent map"), actionX, 58f, 158f, 42f, () =>
                _modules.Moongate.RestorePersistentLocalMap(entry)).interactable = !updateBusy;
            actionX += 166f;
        }
        var updateWidth = entry.IsPersistent ? 90f : 100f;
        CreateLGuiButton(row, "Update", updating ? T("更新中", "Updating") : T("更新", "Update"), actionX, 58f, updateWidth, 42f, () =>
            _modules.Moongate.UpdateLocalMap(entry)).interactable = !updateBusy;
        actionX += updateWidth + 8f;
        var enterWidth = entry.IsPersistent ? 86f : 90f;
        CreateLGuiButton(row, "Enter", T("进入", "Enter"), actionX, 58f, enterWidth, 42f, () =>
            _modules.Moongate.BeginEnterMap(entry.Id, entry.Language)).interactable =
            !updateBusy && _modules.Moongate.CanEnterMoongate;
        actionX += enterWidth + 8f;
        var isFavorite = _modules.Moongate.IsFavorite(entry.Id);
        var favoriteWidth = entry.IsPersistent ? 112f : 124f;
        CreateLGuiButton(
            row,
            "Favorite",
            isFavorite ? T("已收藏", "Favorited") : T("收藏", "Favorite"),
            actionX,
            58f,
            favoriteWidth,
            42f,
            () =>
            {
                if (isFavorite)
                    _modules.Moongate.RemoveFavorite(entry.Id);
                else
                    _modules.Moongate.AddFavorite(entry.Id);
            }).interactable = !updateBusy;
        actionX += favoriteWidth + 8f;
        var deleteWidth = entry.IsPersistent ? 108f : 122f;
        CreateLGuiButton(row, "Delete", T("删除", "Delete"), actionX, 58f, deleteWidth, 42f, () =>
            _modules.Moongate.DeleteLocalMap(entry)).interactable = !updateBusy;
        return y + 112f;
    }
    private RectTransform CreateLGuiMoongateRow(
        RectTransform content,
        string name,
        int index,
        float y,
        float height = 54f)
    {
        var row = CreateLGuiRect(content, name);
        PlaceLGuiRect(row, 0f, y, 1390f, height);
        var image = row.gameObject.AddComponent<Image>();
        image.color = GetLGuiRowColor(index, false);
        RegisterLGuiRoundedImage(image);
        return row;
    }
    private void CreateLGuiMoongateCell(
        RectTransform row,
        string value,
        float x,
        float y,
        float width,
        float height,
        TextAnchor alignment)
    {
        var text = CreateLGuiText(
            row,
            "Cell",
            value ?? "",
            15,
            alignment,
            FontStyle.Normal);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        PlaceLGuiRect(text.rectTransform, x, y, width, height);
    }
}
