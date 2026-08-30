using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void InitializeLGui()
    {
        if (_lGuiInitialized)
            return;

        try
        {
            _lGuiFont = FindLGuiFont();

            _lGuiRoot = new GameObject("ElinModifier.LGui", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_lGuiRoot);
            _lGuiCanvas = _lGuiRoot.GetComponent<Canvas>();
            _lGuiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _lGuiCanvas.sortingOrder = 32000;

            _lGuiCanvasScaler = _lGuiRoot.GetComponent<CanvasScaler>();
            _lGuiCanvasScaler.referenceResolution = new Vector2(2560f, 1440f);
            _lGuiCanvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _lGuiRootGroup = _lGuiRoot.GetComponent<CanvasGroup>();
            _lGuiRootFade = _lGuiRoot.AddComponent<LGuiFadeDriver>();
            _lGuiRootFade.Initialize(_lGuiRootGroup);
            _lGuiRootFade.SetImmediate(0f, false);

            var blocker = CreateLGuiRect(_lGuiRoot.transform, "Blocker");
            StretchLGuiRect(blocker, 0f, 0f, 0f, 0f);
            _lGuiBlockerImage = blocker.gameObject.AddComponent<Image>();
            _lGuiBlockerImage.color = new Color(0f, 0f, 0f, 0.42f);

            _lGuiWindow = CreateLGuiRect(blocker, "MainWindow");
            _lGuiWindow.anchorMin = new Vector2(0.5f, 0.5f);
            _lGuiWindow.anchorMax = new Vector2(0.5f, 0.5f);
            _lGuiWindow.pivot = new Vector2(0.5f, 0.5f);
            _lGuiWindow.sizeDelta = new Vector2(1840f, 1080f);
            _lGuiWindow.anchoredPosition = Vector2.zero;
            _lGuiWindowImage = _lGuiWindow.gameObject.AddComponent<Image>();
            _lGuiWindowMask = _lGuiWindow.gameObject.AddComponent<Mask>();
            _lGuiWindowMask.showMaskGraphic = true;
            _lGuiWindowMask.enabled = false;
            _lGuiWindowGroup = _lGuiWindow.gameObject.AddComponent<CanvasGroup>();
            _lGuiWindowFade = _lGuiWindow.gameObject.AddComponent<LGuiFadeDriver>();
            _lGuiWindowFade.Initialize(_lGuiWindowGroup);

            var header = CreateLGuiRect(_lGuiWindow, "Header");
            AnchorLGuiTop(header, 0f, 64f, 0f, 0f);
            _lGuiHeaderImage = header.gameObject.AddComponent<Image>();
            RegisterLGuiRoundedImage(_lGuiHeaderImage);
            var drag = header.gameObject.AddComponent<LGuiDragHandle>();
            drag.Initialize(_lGuiWindow, _lGuiCanvas);

            _lGuiTitle = CreateLGuiText(header, "Title", "Elin Modifier", 24, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(_lGuiTitle.rectTransform, 22f, 0f, 900f, 64f);
            _lGuiCredit = CreateLGuiText(header, "Credit", GetLGuiHeaderCreditText(), 12, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(_lGuiCredit.rectTransform, 220f, 0f, 928f, 64f);
            _lGuiStartupModeLabel = CreateLGuiText(header, "StartupModeLabel", T("启动模式:", "Startup mode:"), 14, TextAnchor.MiddleRight, FontStyle.Normal);
            PlaceLGuiRect(_lGuiStartupModeLabel.rectTransform, 880f, 0f, 100f, 64f);
            _lGuiStartupModeDropdown = CreateAutomationDropdown(
                header,
                "StartupMode",
                GetLGuiStartupModeOptions(),
                GetLGuiStartupModeIndex(),
                988f,
                10f,
                170f,
                44f,
                SetLGuiStartupModeIndex,
                true);
            var globalConfigSaveButton = CreateLGuiButton(header, "GlobalConfigSave", T("保存配置", "Save config"), 1172f, 10f, 126f, 44f, SaveLGuiGlobalConfig);
            _lGuiGlobalConfigSaveRect = globalConfigSaveButton.transform as RectTransform;
            _lGuiGlobalConfigSaveLabel = globalConfigSaveButton.GetComponentInChildren<Text>(true);
            var globalConfigLoadButton = CreateLGuiButton(header, "GlobalConfigLoad", T("读取配置", "Load config"), 1308f, 10f, 126f, 44f, LoadLGuiGlobalConfig);
            _lGuiGlobalConfigLoadRect = globalConfigLoadButton.transform as RectTransform;
            _lGuiGlobalConfigLoadLabel = globalConfigLoadButton.GetComponentInChildren<Text>(true);
            _lGuiVersion = CreateLGuiText(header, "Version", GetLGuiHeaderVersionText(), 14, TextAnchor.MiddleRight, FontStyle.Normal);
            PlaceLGuiRect(_lGuiVersion.rectTransform, 1460f, 0f, 286f, 64f);
            CreateLGuiButton(header, "Close", "×", 1768f, 10f, 54f, 44f, HideLGui);

            var sidebar = CreateLGuiRect(_lGuiWindow, "Sidebar");
            sidebar.anchorMin = new Vector2(0f, 0f);
            sidebar.anchorMax = new Vector2(0f, 1f);
            sidebar.pivot = new Vector2(0f, 1f);
            sidebar.offsetMin = new Vector2(0f, 0f);
            sidebar.offsetMax = new Vector2(250f, -64f);
            _lGuiSidebarImage = sidebar.gameObject.AddComponent<Image>();
            RegisterLGuiRoundedImage(_lGuiSidebarImage);

            CreateLGuiNavButton(sidebar, LGuiPage.Features, T("独立功能", "Independent Features"), 18f);
            CreateLGuiNavButton(sidebar, LGuiPage.Character, T("游戏数据修改", "Character Data"), 78f);
            CreateLGuiNavButton(sidebar, LGuiPage.Items, T("物品生成", "Item Spawn"), 138f);
            CreateLGuiNavButton(sidebar, LGuiPage.Npcs, T("NPC生成", "NPC Spawn"), 198f);
            CreateLGuiNavButton(sidebar, LGuiPage.PlayerInfo, T("玩家信息", "Player Info"), 258f);
            CreateLGuiNavButton(sidebar, LGuiPage.Home, T("家园管理", "Home Management"), 318f);
            CreateLGuiNavButton(sidebar, LGuiPage.Probability, T("事件概率", "Event Probabilities"), 378f);
            CreateLGuiNavButton(sidebar, LGuiPage.Automation, AutomationText("自动化", "Automation", "自動化", "Автоматизация"), 438f);
            CreateLGuiNavButton(sidebar, LGuiPage.Moongate, T("月门", "Moongate"), 498f);
            CreateLGuiNavButton(sidebar, LGuiPage.NpcInfo, T("NPC图鉴", "NPC Compendium"), 558f);
            CreateLGuiNavButton(sidebar, LGuiPage.Nightly, "Nightly", 618f);
            CreateLGuiNavButton(sidebar, LGuiPage.Ai, T("AI辅助", "AI Assistant"), 678f);
            CreateLGuiNavButton(sidebar, LGuiPage.Emp, T("插件管理", "Plugin Manager"), 738f);
            CreateLGuiNavButton(sidebar, LGuiPage.Settings, T("UI设置", "UI Settings"), 798f);
            if (_debugAuthorized)
                CreateLGuiNavButton(sidebar, LGuiPage.Debug, T("调试模式", "Debug mode"), 858f);

            _lGuiPageHost = CreateLGuiRect(_lGuiWindow, "PageHost");
            _lGuiPageHost.anchorMin = new Vector2(0f, 0f);
            _lGuiPageHost.anchorMax = new Vector2(1f, 1f);
            _lGuiPageHost.offsetMin = new Vector2(266f, 46f);
            _lGuiPageHost.offsetMax = new Vector2(-18f, -76f);

            _lGuiStatus = CreateLGuiText(_lGuiWindow, "Status", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
            _lGuiStatus.rectTransform.anchorMin = new Vector2(0f, 0f);
            _lGuiStatus.rectTransform.anchorMax = new Vector2(1f, 0f);
            _lGuiStatus.rectTransform.pivot = new Vector2(0.5f, 0f);
            _lGuiStatus.rectTransform.offsetMin = new Vector2(270f, 6f);
            _lGuiStatus.rectTransform.offsetMax = new Vector2(-18f, 38f);

            _lGuiInitialized = true;
            _lGuiVisible = false;
            _lGuiRoot.SetActive(false);
            ApplyLGuiVisualSettings();
            SwitchLGuiPage(LGuiPage.Features);
        }
        catch (Exception ex)
        {
            _lGuiInitialized = false;
            _log = "Runtime uGUI init failed: " + ex.Message;
            if (_lGuiRoot != null)
                UnityEngine.Object.Destroy(_lGuiRoot);
            _lGuiRoot = null;
        }
    }
    private void ShutdownLGui()
    {
        CloseLGuiEditorModal();
        DisposeLGuiVirtualLists();
        if (_lGuiRoundedWindowSprite != null)
            UnityEngine.Object.Destroy(_lGuiRoundedWindowSprite);
        if (_lGuiRoundedWindowTexture != null)
            UnityEngine.Object.Destroy(_lGuiRoundedWindowTexture);
        if (_lGuiRoundedCapsuleSprite != null)
            UnityEngine.Object.Destroy(_lGuiRoundedCapsuleSprite);
        if (_lGuiRoundedCapsuleTexture != null)
            UnityEngine.Object.Destroy(_lGuiRoundedCapsuleTexture);
        if (_lGuiRoot != null)
            UnityEngine.Object.Destroy(_lGuiRoot);
        if (_lGuiOwnedEventSystem != null)
            UnityEngine.Object.Destroy(_lGuiOwnedEventSystem);
        _lGuiRoot = null;
        _lGuiOwnedEventSystem = null;
        _lGuiCanvas = null;
        _lGuiCanvasScaler = null;
        _lGuiRootGroup = null;
        _lGuiRootFade = null;
        _lGuiWindowGroup = null;
        _lGuiWindowFade = null;
        _lGuiBlockerImage = null;
        _lGuiWindowImage = null;
        _lGuiWindowMask = null;
        _lGuiHeaderImage = null;
        _lGuiSidebarImage = null;
        _lGuiWindow = null;
        _lGuiPageHost = null;
        _lGuiTitle = null;
        _lGuiCredit = null;
        _lGuiVersion = null;
        _lGuiStartupModeLabel = null;
        _lGuiStartupModeDropdown = null;
        _lGuiGlobalConfigSaveRect = null;
        _lGuiGlobalConfigLoadRect = null;
        _lGuiGlobalConfigSaveLabel = null;
        _lGuiGlobalConfigLoadLabel = null;
        _lGuiStatus = null;
        _lGuiRoundedWindowSprite = null;
        _lGuiRoundedWindowTexture = null;
        _lGuiRoundedCapsuleSprite = null;
        _lGuiRoundedCapsuleTexture = null;
        _lGuiNavButtons.Clear();
        _lGuiNavLabels.Clear();
        _lGuiInitialized = false;
        _lGuiVisible = false;
    }
    private bool IsLGuiInitialized()
    {
        return _lGuiInitialized && _lGuiRoot != null;
    }
    private bool IsLGuiVisible()
    {
        return IsLGuiInitialized() && _lGuiVisible && _lGuiRoot!.activeSelf;
    }
    private void ToggleLGui()
    {
        if (!IsLGuiInitialized())
            return;

        if (_lGuiVisible)
            BeginLGuiHide();
        else
            ShowLGui();
    }
    private void ShowLGui()
    {
        if (!IsLGuiInitialized())
            return;
        var wasActive = _lGuiRoot!.activeSelf;
        _lGuiVisible = true;
        _lGuiRoot.SetActive(true);
        EnsureLGuiEventSystem();
        RefreshLGuiFontIfNeeded(true);
        if (!wasActive)
            _lGuiRootFade?.SetImmediate(0f, false);
        ApplyLGuiVisualSettings();
        _lGuiRootFade?.FadeTo(1f, LGuiMainFadeInSeconds, true);
        if (_lGuiPage == LGuiPage.Moongate || _lGuiPage == LGuiPage.NpcInfo)
        {
            SwitchLGuiPage(_lGuiPage);
        }
        else
        {
            _lGuiDataDirty = true;
            RefreshLGuiNow(true);
        }
    }
    private void BeginLGuiHide()
    {
        if (!IsLGuiInitialized())
            return;
        _lGuiVisible = false;
        SetLGuiOwnedEventSystemActive(false);
        RestoreLGuiImeMode();
        if (_lGuiRootFade == null)
        {
            _lGuiRoot!.SetActive(false);
            return;
        }
        _lGuiRootFade.FadeTo(0f, LGuiMainFadeOutSeconds, false, () =>
        {
            if (IsLGuiInitialized() && !_lGuiVisible)
                _lGuiRoot!.SetActive(false);
        });
    }
    private void HideLGui()
    {
        if (!IsLGuiInitialized())
            return;
        if (_lGuiEditorModal != null)
            CloseLGuiEditorModal();
        else
            BeginLGuiHide();
    }
    private void TickLGui()
    {
        RefreshLGuiFontIfNeeded(false);
        if (!IsLGuiVisible())
            return;

        EnsureLGuiEventSystem();
        UpdateLGuiImeMode();
        var dynamicDue = ShouldRefreshLGuiDynamicValues();
        var slowDue = ShouldRefreshLGuiSlowValues();
        if (_lGuiDataDirty && (_lGuiPage == LGuiPage.Moongate || _lGuiPage == LGuiPage.NpcInfo))
        {
            SwitchLGuiPage(_lGuiPage);
            return;
        }
        if (_lGuiDataDirty)
            RefreshLGuiNow(true);
        else if (slowDue)
        {
            if (_lGuiPage == LGuiPage.Debug)
                _lGuiDebugList?.RefreshBoundRows();
            RefreshLGuiNow(false);
        }
        else if (dynamicDue)
            RefreshLGuiVisibleRows();
    }
    private void NotifyLGuiDataDirty()
    {
        _lGuiDataDirty = true;
    }
    private void RefreshLGuiNow(bool rebuild)
    {
        if (!IsLGuiInitialized())
            return;

        if (LGuiPageRequiresCharacterData(_lGuiPage) && !HasCharacterData())
        {
            SwitchLGuiPage(LGuiPage.Features);
            return;
        }

        UpdateLGuiNavButtons();

        if (_lGuiTitle != null)
            _lGuiTitle.text = "Elin Modifier";
        if (_lGuiCredit != null)
            _lGuiCredit.text = GetLGuiHeaderCreditText();
        if (_lGuiVersion != null)
            _lGuiVersion.text = GetLGuiHeaderVersionText();
        if (_lGuiGlobalConfigSaveLabel != null)
            _lGuiGlobalConfigSaveLabel.text = T("保存配置", "Save config");
        if (_lGuiGlobalConfigLoadLabel != null)
            _lGuiGlobalConfigLoadLabel.text = T("读取配置", "Load config");
        RefreshLGuiStartupModeControl();
        LayoutLGuiHeaderTexts();
        if (_lGuiStatus != null)
            _lGuiStatus.text = GetLGuiPageStatus();

        if (rebuild)
        {
            switch (_lGuiPage)
            {
                case LGuiPage.Character:
                    RebuildLGuiCharacterRows();
                    break;
                case LGuiPage.Items:
                    RebuildLGuiItemRows();
                    break;
                case LGuiPage.Npcs:
                    RebuildLGuiNpcRows();
                    break;
                case LGuiPage.Home:
                    RebuildLGuiHomeRows();
                    break;
                case LGuiPage.Debug:
                    RebuildLGuiDebugRows();
                    break;
                case LGuiPage.Emp:
                    RebuildLGuiEmpRows();
                    break;
            }
        }

        RefreshLGuiVisibleRows();
        _lGuiDataDirty = false;
        MarkLGuiValuesClean();
    }
    private void RefreshLGuiVisibleRows()
    {
        _lGuiFeatureList?.RefreshBoundRows();
        if (!IsLGuiCharacterInputFocused())
            _lGuiCharacterList?.RefreshBoundRows();
        _lGuiItemList?.RefreshBoundRows();
        _lGuiNpcList?.RefreshBoundRows();
        _lGuiHomeList?.RefreshBoundRows();
        _modules.Probability.RefreshVisibleRows();
        _lGuiEmpList?.RefreshBoundRows();
        RefreshLGuiAiControls();
        if (_lGuiStatus != null)
            _lGuiStatus.text = GetLGuiPageStatus();
        if (_lGuiEmpStatusText != null)
            _lGuiEmpStatusText.text = _pluginManagerLog ?? "";
    }
    private bool IsLGuiCharacterInputFocused()
    {
        if (_lGuiPage != LGuiPage.Character || _lGuiPageHost == null || EventSystem.current == null)
            return false;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null && selected.GetComponent<InputField>() != null && selected.transform.IsChildOf(_lGuiPageHost))
            return true;
        return _modules.LGuiFocus.HasFocusedInputWithin(_lGuiPageHost);
    }
    private static string IndentLGuiText(string text, int depth)
    {
        return depth <= 0 ? text : new string(' ', depth * 4) + text;
    }
    private void SwitchLGuiPage(LGuiPage page)
    {
        if (_lGuiPageHost == null)
            return;
        if (page == LGuiPage.Nightly && _modules.Nightly == null)
            return;
        if (LGuiPageRequiresCharacterData(page) && !HasCharacterData())
        {
            UpdateLGuiNavButtons();
            return;
        }
        EnsureLGuiPageHarmonyPatches(page);
        CloseLGuiEditorModal(true);
        _lGuiPage = page;
        DisposeLGuiVirtualLists();
        DestroyLGuiChildren(_lGuiPageHost);

        switch (page)
        {
            case LGuiPage.Features:
                BuildLGuiFeaturesPage();
                break;
            case LGuiPage.Character:
                BuildLGuiCharacterPage();
                break;
            case LGuiPage.Items:
                BuildLGuiItemsPage();
                break;
            case LGuiPage.Npcs:
                BuildLGuiNpcsPage();
                break;
            case LGuiPage.PlayerInfo:
                BuildLGuiPlayerInfoPage();
                break;
            case LGuiPage.Home:
                BuildLGuiHomePage();
                break;
            case LGuiPage.Probability:
                BuildProbabilityPage();
                break;
            case LGuiPage.Automation:
                BuildAutomationPage();
                break;
            case LGuiPage.Nightly:
                BuildLGuiNightlyPage();
                break;
            case LGuiPage.Moongate:
                BuildLGuiMoongatePage();
                break;
            case LGuiPage.NpcInfo:
                BuildLGuiNpcInfoPage();
                break;
            case LGuiPage.Ai:
                BuildLGuiAiPage();
                break;
            case LGuiPage.Debug:
                BuildLGuiDebugPage();
                break;
            case LGuiPage.Emp:
                BuildLGuiEmpPage();
                break;
            case LGuiPage.Settings:
                BuildLGuiSettingsPage();
                break;
        }
        _lGuiDataDirty = true;
        ApplyLGuiVisualSettings();
        RefreshLGuiNow(true);
    }
    private string GetLGuiHeaderCreditText()
    {
        return _language == "zh"
            ? "        Coder : Liset    官方网站 : https://m9.pw/    QQ群 : 771844665"
            : "        Coder : Liset    Website : https://m9.pw/";
    }
    private string GetLGuiHeaderVersionText()
    {
        var version = GetDebugPluginVersion(Info);
        return T("版本", "Version") + " : " + (string.IsNullOrWhiteSpace(version) ? ModMetadata.Version : version);
    }
    private void SaveLGuiGlobalConfig()
    {
        SaveConfig(true);
        ShowLGuiGlobalConfigStatus();
    }
    private void LoadLGuiGlobalConfig()
    {
        LoadConfig();
        InitializeOptimization();
        RebuildLGuiAll();
        ShowLGuiGlobalConfigStatus();
    }
    private void ShowLGuiGlobalConfigStatus()
    {
        if (_lGuiStatus != null)
            _lGuiStatus.text = string.IsNullOrWhiteSpace(_configLog) ? T("配置操作完成", "Configuration operation completed") : _configLog;
    }
    private static bool LGuiPageRequiresCharacterData(LGuiPage page)
    {
        return page == LGuiPage.Character ||
               page == LGuiPage.Items ||
               page == LGuiPage.Npcs ||
               page == LGuiPage.PlayerInfo ||
               page == LGuiPage.Home ||
               page == LGuiPage.Probability ||
               page == LGuiPage.Automation ||
               page == LGuiPage.Nightly ||
               page == LGuiPage.Moongate ||
               page == LGuiPage.NpcInfo;
    }
    private void UpdateLGuiNavButtons()
    {
        var hasCharacterData = HasCharacterData();
        var activeColor = GetActiveUiTextColor();
        foreach (var pair in _lGuiNavButtons)
        {
            var enabled = (!LGuiPageRequiresCharacterData(pair.Key) || hasCharacterData) &&
                          (pair.Key != LGuiPage.Nightly || _modules.Nightly != null);
            pair.Value.interactable = enabled;
            if (_lGuiNavLabels.TryGetValue(pair.Key, out var label) && label != null)
            {
                label.text = GetLGuiPageTitle(pair.Key);
                label.color = enabled ? activeColor : new Color(activeColor.r, activeColor.g, activeColor.b, 0.36f);
            }
        }
    }
    private void LayoutLGuiHeaderTexts()
    {
        if (_lGuiTitle == null ||
            _lGuiCredit == null ||
            _lGuiVersion == null ||
            _lGuiStartupModeLabel == null ||
            _lGuiStartupModeDropdown == null ||
            _lGuiGlobalConfigSaveRect == null ||
            _lGuiGlobalConfigLoadRect == null)
            return;

        const float versionRight = 1746f;
        const float minimumVersionWidth = 90f;
        const float buttonWidth = 126f;
        const float versionGap = 12f;
        const float buttonGap = 10f;
        const float creditGap = 18f;
        const float startupDropdownWidth = 170f;
        const float startupControlGap = 10f;
        const float startupLabelGap = 6f;

        var versionWidth = Math.Max(minimumVersionWidth, Mathf.Ceil(_lGuiVersion.preferredWidth) + 4f);
        var versionX = versionRight - versionWidth;
        var loadButtonX = versionX - versionGap - buttonWidth;
        var saveButtonX = loadButtonX - buttonGap - buttonWidth;
        var startupDropdownX = saveButtonX - startupControlGap - startupDropdownWidth;
        var startupLabelWidth = Math.Max(76f, Mathf.Ceil(_lGuiStartupModeLabel.preferredWidth) + 4f);
        var startupLabelX = startupDropdownX - startupLabelGap - startupLabelWidth;
        PlaceLGuiRect(_lGuiVersion.rectTransform, versionX, 0f, versionWidth, 64f);
        PlaceLGuiRect(_lGuiGlobalConfigLoadRect, loadButtonX, 10f, buttonWidth, 44f);
        PlaceLGuiRect(_lGuiGlobalConfigSaveRect, saveButtonX, 10f, buttonWidth, 44f);
        PlaceLGuiRect((RectTransform)_lGuiStartupModeDropdown.transform, startupDropdownX, 10f, startupDropdownWidth, 44f);
        PlaceLGuiRect(_lGuiStartupModeLabel.rectTransform, startupLabelX, 0f, startupLabelWidth, 64f);

        var creditX = 22f + Mathf.Ceil(_lGuiTitle.preferredWidth);
        PlaceLGuiRect(_lGuiCredit.rectTransform, creditX, 0f, Math.Max(0f, startupLabelX - creditGap - creditX), 64f);
    }
    private static string NormalizeStartupMode(string value)
    {
        return string.Equals(value, StartupModePreload, StringComparison.OrdinalIgnoreCase)
            ? StartupModePreload
            : StartupModeHighReliability;
    }
    private List<string> GetLGuiStartupModeOptions()
    {
        return new List<string>
        {
            T("预加载", "Preload"),
            T("高可靠", "High reliability")
        };
    }
    private int GetLGuiStartupModeIndex()
    {
        return string.Equals(NormalizeStartupMode(_startupMode), StartupModeHighReliability, StringComparison.Ordinal) ? 1 : 0;
    }
    private void SetLGuiStartupModeIndex(int index)
    {
        _startupMode = index == 1 ? StartupModeHighReliability : StartupModePreload;
        SaveConfig(false);
    }
    private void RefreshLGuiStartupModeControl()
    {
        if (_lGuiStartupModeLabel != null)
            _lGuiStartupModeLabel.text = T("启动模式:", "Startup mode:");
        if (_lGuiStartupModeDropdown == null)
            return;
        var options = GetLGuiStartupModeOptions();
        _lGuiStartupModeDropdown.options.Clear();
        for (var i = 0; i < options.Count; i++)
            _lGuiStartupModeDropdown.options.Add(new Dropdown.OptionData(options[i]));
        _lGuiStartupModeDropdown.SetValueWithoutNotify(GetLGuiStartupModeIndex());
        _lGuiStartupModeDropdown.RefreshShownValue();
    }
}
