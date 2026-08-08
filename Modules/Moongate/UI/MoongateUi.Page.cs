using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiMoongatePage()
    {
        var module = _modules.Moongate;
        module.EnsureOnlineMapsLoaded();

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "MoongateScroll", 0f);
        _lGuiMoongateScroll = scroll;
        var content = scroll.content!;
        var y = 8f;

        CreateLGuiButton(
            content,
            "UploadCurrentMapToElinModifier",
            module.IsUploading
                ? T("正在上传地图...", "Uploading map...")
                : T("上传地图至 Elin Modifier 云存储服务器", "Upload map to Elin Modifier cloud storage server"),
            0f,
            y,
            430f,
            46f,
            module.UploadCurrentMap).interactable = !module.IsUploading;
        y += 60f;

        var landholderPrivileges = CreateLGuiToggle(
            content,
            "MoongateLandholderPrivileges",
            0f,
            y,
            560f,
            44f,
            out var landholderPrivilegesLabel);
        landholderPrivilegesLabel.text = T(
            "提升月门地图权限",
            "Elevate moongate map permissions");
        landholderPrivileges.isOn = module.LandholderPrivilegesEnabled;
        landholderPrivileges.onValueChanged.AddListener(module.SetLandholderPrivilegesEnabled);
        y += 58f;

        var mapIdLabel = CreateLGuiText(
            content,
            "SpecifiedMapIdLabel",
            T("指定地图ID:", "Map ID:"),
            17,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(mapIdLabel.rectTransform, 0f, y, 150f, 44f);
        var mapIdInput = CreateLGuiInput(
            content,
            "SpecifiedMapId",
            T("输入地图ID", "Enter map ID"),
            154f,
            y,
            710f,
            44f);
        mapIdInput.text = module.SpecifiedMapId;
        mapIdInput.onValueChanged.AddListener(value => module.SpecifiedMapId = value ?? "");
        CreateLGuiButton(
            content,
            "EnterSpecifiedMoongate",
            module.IsEntering ? T("进入中", "Entering") : T("进入", "Enter"),
            880f,
            y,
            110f,
            44f,
            module.EnterSpecifiedMap).interactable = module.CanEnterMoongate;
        CreateLGuiButton(
            content,
            "FavoriteSpecifiedMoongate",
            T("收藏", "Favorite"),
            1004f,
            y,
            110f,
            44f,
            module.AddSpecifiedFavorite);
        CreateLGuiButton(
            content,
            "RefreshMoongateIndexes",
            module.IsLoading ? T("加载中", "Loading") : T("刷新页面", "Refresh page"),
            1128f,
            y,
            154f,
            44f,
            module.RefreshOnlineMaps).interactable = !module.IsLoading;
        y += 60f;

        y = AddLGuiSectionTitle(content, T("收藏月门", "Favorited moongates"), y);
        var favorites = module.GetFavorites();
        if (favorites.Count == 0)
        {
            var empty = CreateLGuiText(
                content,
                "NoFavoriteMoongates",
                T("暂无收藏", "No favorites"),
                16,
                TextAnchor.MiddleLeft,
                FontStyle.Normal);
            PlaceLGuiRect(empty.rectTransform, 18f, y, 1220f, 46f);
            y += 54f;
        }
        else
        {
            y = CreateLGuiMoongateHeader(content, y);
            for (var i = 0; i < favorites.Count; i++)
                y = CreateLGuiMoongateFavoriteRow(content, favorites[i], i, y);
        }

        y += 10f;
        y = AddLGuiSectionTitle(content, T("本地月门", "Local moongates"), y);
        var localMaps = module.GetLocalMaps();
        if (localMaps.Count == 0)
        {
            var empty = CreateLGuiText(
                content,
                "NoLocalMoongates",
                T("暂无本地缓存", "No local cache"),
                16,
                TextAnchor.MiddleLeft,
                FontStyle.Normal);
            PlaceLGuiRect(empty.rectTransform, 18f, y, 1220f, 46f);
            y += 54f;
        }
        else
        {
            y = CreateLGuiMoongateHeader(content, y, true);
            var localPageCount = Math.Max(
                1,
                (localMaps.Count + MoongateLocalRowsPerPage - 1) / MoongateLocalRowsPerPage);
            module.LocalPage = Mathf.Clamp(module.LocalPage, 0, localPageCount - 1);
            var localStart = module.LocalPage * MoongateLocalRowsPerPage;
            var localEnd = Math.Min(localMaps.Count, localStart + MoongateLocalRowsPerPage);
            for (var i = localStart; i < localEnd; i++)
                y = CreateLGuiMoongateLocalRow(content, localMaps[i], i - localStart, y);

            CreateLGuiButton(content, "PreviousLocalMoongatePage", T("上一页", "Previous"), 0f, y, 130f, 44f, () =>
            {
                module.LocalPage = Math.Max(0, module.LocalPage - 1);
                RebuildLGuiMoongatePagePreservingScroll();
            }).interactable = module.LocalPage > 0;
            CreateLGuiButton(content, "NextLocalMoongatePage", T("下一页", "Next"), 144f, y, 130f, 44f, () =>
            {
                module.LocalPage = Math.Min(localPageCount - 1, module.LocalPage + 1);
                RebuildLGuiMoongatePagePreservingScroll();
            }).interactable = module.LocalPage + 1 < localPageCount;
            var localPageLabel = CreateLGuiText(
                content,
                "LocalMoongatePageLabel",
                (module.LocalPage + 1).ToString(CultureInfo.InvariantCulture) +
                " / " +
                localPageCount.ToString(CultureInfo.InvariantCulture),
                16,
                TextAnchor.MiddleLeft,
                FontStyle.Normal);
            PlaceLGuiRect(localPageLabel.rectTransform, 292f, y, 220f, 44f);
            y += 56f;
        }

        y += 10f;
        y = AddLGuiSectionTitle(content, T("搜索月门", "Search moongates"), y);
        var searchLabel = CreateLGuiText(
            content,
            "MoongateSearchLabel",
            T("搜索:", "Search:"),
            17,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(searchLabel.rectTransform, 0f, y, 100f, 44f);
        var searchInput = CreateLGuiInput(
            content,
            "MoongateSearch",
            T("地图标题 / 作者 / 地图ID", "Map title / author / map ID"),
            104f,
            y,
            650f,
            44f);
        searchInput.text = module.SearchText;
        searchInput.onValueChanged.AddListener(value => module.SearchText = value ?? "");
        searchInput.onEndEdit.AddListener(value =>
        {
            module.SearchText = value ?? "";
            module.SearchPage = 0;
            SwitchLGuiPage(LGuiPage.Moongate);
        });
        CreateLGuiButton(content, "ApplyMoongateSearch", T("搜索", "Search"), 768f, y, 100f, 44f, () =>
        {
            module.SearchText = searchInput.text ?? "";
            module.SearchPage = 0;
            SwitchLGuiPage(LGuiPage.Moongate);
        });
        CreateLGuiButton(content, "ClearMoongateSearch", T("清空", "Clear"), 882f, y, 100f, 44f, () =>
        {
            module.SearchText = "";
            module.SearchPage = 0;
            SwitchLGuiPage(LGuiPage.Moongate);
        });
        y += 58f;

        var languageLabel = CreateLGuiText(
            content,
            "MoongateLanguageLabel",
            T("语言区:", "Language:"),
            16,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(languageLabel.rectTransform, 0f, y, 100f, 44f);
        var languageValues = new[] { "ALL", "CN", "JP", "EN" };
        var languageOptions = new List<string> { T("全部", "All"), "CN", "JP", "EN" };
        var selectedLanguageIndex = Array.FindIndex(
            languageValues,
            value => string.Equals(value, module.SearchLanguage, StringComparison.OrdinalIgnoreCase));
        if (selectedLanguageIndex < 0)
            selectedLanguageIndex = 0;
        CreateAutomationDropdown(
            content,
            "MoongateLanguage",
            languageOptions,
            selectedLanguageIndex,
            104f,
            y,
            190f,
            44f,
            selectedIndex =>
            {
                if (selectedIndex < 0 || selectedIndex >= languageValues.Length)
                    return;
                module.SearchLanguage = languageValues[selectedIndex];
                module.SearchPage = 0;
                SwitchLGuiPage(LGuiPage.Moongate);
            });

        var sourceLabel = CreateLGuiText(
            content,
            "MoongateSourceLabel",
            T("来源:", "Source:"),
            16,
            TextAnchor.MiddleLeft,
            FontStyle.Normal);
        PlaceLGuiRect(sourceLabel.rectTransform, 320f, y, 90f, 44f);
        var sourceValues = new[] { "ALL", "OFFICIAL", "EM" };
        var sourceOptions = new List<string>
        {
            T("全部", "All"),
            T("官方", "Official"),
            T("EM云存储", "EM cloud storage")
        };
        var selectedSourceIndex = Array.FindIndex(
            sourceValues,
            value => string.Equals(value, module.SearchSource, StringComparison.OrdinalIgnoreCase));
        if (selectedSourceIndex < 0)
            selectedSourceIndex = 0;
        CreateAutomationDropdown(
            content,
            "MoongateSource",
            sourceOptions,
            selectedSourceIndex,
            414f,
            y,
            240f,
            44f,
            selectedIndex =>
            {
                if (selectedIndex < 0 || selectedIndex >= sourceValues.Length)
                    return;
                module.SearchSource = sourceValues[selectedIndex];
                module.SearchPage = 0;
                SwitchLGuiPage(LGuiPage.Moongate);
            });
        y += 58f;

        if (module.IsLoading && module.OnlineMapCount == 0)
        {
            var loading = CreateLGuiText(
                content,
                "MoongateLoading",
                T("正在加载 CN / JP / EN 月门地图索引...", "Loading CN / JP / EN moongate indexes..."),
                16,
                TextAnchor.MiddleLeft,
                FontStyle.Normal);
            PlaceLGuiRect(loading.rectTransform, 18f, y, 1220f, 46f);
            y += 54f;
        }
        else
        {
            var results = module.GetSearchResults();
            var resultSummary = CreateLGuiText(
                content,
                "MoongateSearchSummary",
                T("搜索结果: ", "Results: ") + results.Count.ToString(CultureInfo.InvariantCulture),
                16,
                TextAnchor.MiddleLeft,
                FontStyle.Normal);
            PlaceLGuiRect(resultSummary.rectTransform, 0f, y, 1220f, 42f);
            y += 46f;

            if (results.Count == 0)
            {
                var empty = CreateLGuiText(
                    content,
                    "NoMoongateResults",
                    T("没有符合条件的月门地图", "No matching moongate maps"),
                    16,
                    TextAnchor.MiddleLeft,
                    FontStyle.Normal);
                PlaceLGuiRect(empty.rectTransform, 18f, y, 1220f, 46f);
                y += 54f;
            }
            else
            {
                y = CreateLGuiMoongateHeader(content, y);
                var pageCount = Math.Max(1, (results.Count + MoongateSearchRowsPerPage - 1) / MoongateSearchRowsPerPage);
                module.SearchPage = Clamp(module.SearchPage, 0, pageCount - 1);
                var start = module.SearchPage * MoongateSearchRowsPerPage;
                var end = Math.Min(results.Count, start + MoongateSearchRowsPerPage);
                for (var i = start; i < end; i++)
                    y = CreateLGuiMoongateSearchRow(content, results[i], i - start, y);

                CreateLGuiButton(content, "PreviousMoongatePage", T("上一页", "Previous"), 0f, y, 130f, 44f, () =>
                {
                    module.SearchPage = Math.Max(0, module.SearchPage - 1);
                    RebuildLGuiMoongatePagePreservingScroll();
                }).interactable = module.SearchPage > 0;
                CreateLGuiButton(content, "NextMoongatePage", T("下一页", "Next"), 144f, y, 130f, 44f, () =>
                {
                    module.SearchPage = Math.Min(pageCount - 1, module.SearchPage + 1);
                    RebuildLGuiMoongatePagePreservingScroll();
                }).interactable = module.SearchPage + 1 < pageCount;
                var pageLabel = CreateLGuiText(
                    content,
                    "MoongatePageLabel",
                    (module.SearchPage + 1).ToString(CultureInfo.InvariantCulture) +
                    " / " +
                    pageCount.ToString(CultureInfo.InvariantCulture),
                    16,
                    TextAnchor.MiddleLeft,
                    FontStyle.Normal);
                PlaceLGuiRect(pageLabel.rectTransform, 292f, y, 220f, 44f);
                y += 56f;
            }
        }

        content.sizeDelta = new Vector2(0f, Math.Max(900f, y + 30f));
    }
    private void RebuildLGuiMoongatePagePreservingScroll()
    {
        var normalizedPosition = _lGuiMoongateScroll == null
            ? 1f
            : _lGuiMoongateScroll.verticalNormalizedPosition;

        SwitchLGuiPage(LGuiPage.Moongate);

        if (_lGuiMoongateScroll == null)
            return;

        Canvas.ForceUpdateCanvases();
        _lGuiMoongateScroll.StopMovement();
        _lGuiMoongateScroll.verticalNormalizedPosition = Mathf.Clamp01(normalizedPosition);
    }
}
