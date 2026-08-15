using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed class MainMenuInfoModule
{
    internal bool Enabled { get; private set; } = true;
    private UIButton? _mainMenuInfoButton;
    private Layer? _mainMenuInfoLayer;
    private bool _mainMenuInfoAutoOpened;
    private bool _mainMenuInfoAutoOpenScheduled;

    internal void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }

    internal void RefreshButton(ElinModifierPlugin host)
    {
        LayerTitle? title = null;
        try { title = LayerTitle.Instance; }
        catch { }

        if (!Enabled || title == null)
        {
            DestroyButton();
            return;
        }

        if (_mainMenuInfoButton != null)
        {
            SetMainMenuInfoButtonText(_mainMenuInfoButton);
            return;
        }

        try
        {
            var source = FindMainMenuStartButton(title);
            if (source == null || source.transform.parent == null)
                return;

            var parent = source.transform.parent;
            var button = UnityEngine.Object.Instantiate(source, parent);
            button.name = "ElinModifierMainMenuInfoButton";
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => OpenWindow(host));
            button.onDoubleClick = null;
            button.onRightClick = null;
            SetMainMenuInfoButtonText(button);

            var layout = parent.GetComponent<LayoutGroup>();
            if (layout != null)
            {
                button.transform.SetSiblingIndex(Math.Max(0, source.transform.GetSiblingIndex()));
                if (parent is RectTransform parentRect)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }
            else
            {
                var sourceRect = source.transform as RectTransform;
                var buttonRect = button.transform as RectTransform;
                if (sourceRect != null && buttonRect != null)
                {
                    buttonRect.anchorMin = sourceRect.anchorMin;
                    buttonRect.anchorMax = sourceRect.anchorMax;
                    buttonRect.pivot = sourceRect.pivot;
                    buttonRect.sizeDelta = sourceRect.sizeDelta;
                    buttonRect.anchoredPosition = sourceRect.anchoredPosition + Vector2.up * (Math.Max(4f, sourceRect.rect.height) + 8f);
                }
            }

            _mainMenuInfoButton = button;
        }
        catch { }
    }

    private static UIButton? FindMainMenuStartButton(LayerTitle title)
    {
        UIButton[] buttons;
        try { buttons = title.GetComponentsInChildren<UIButton>(true); }
        catch { return null; }

        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null)
                continue;
            try
            {
                var click = button.onClick;
                for (var eventIndex = 0; eventIndex < click.GetPersistentEventCount(); eventIndex++)
                    if (string.Equals(click.GetPersistentMethodName(eventIndex), "OnClickStart", StringComparison.Ordinal))
                        return button;
            }
            catch { }
        }

        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null || button.mainText == null)
                continue;
            var text = button.mainText.text ?? "";
            if (text.IndexOf("创建", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("adventurer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("冒険者", StringComparison.OrdinalIgnoreCase) >= 0)
                return button;
        }
        return null;
    }

    private static void SetMainMenuInfoButtonText(UIButton button)
    {
        try
        {
            if (button.mainText != null)
                button.mainText.SetText("Elin Modifier");
            if (button.subText != null)
                button.subText.SetText("");
            if (button.subText2 != null)
                button.subText2.SetText("");
            if (button.keyText != null)
                button.keyText.SetText("");
        }
        catch { }
    }

    internal void DestroyButton()
    {
        if (_mainMenuInfoButton == null)
            return;
        try { UnityEngine.Object.Destroy(_mainMenuInfoButton.gameObject); }
        catch { }
        _mainMenuInfoButton = null;
    }

    internal void ScheduleAutoOpen(ElinModifierPlugin host)
    {
        if (!Enabled || _mainMenuInfoAutoOpened || _mainMenuInfoAutoOpenScheduled)
            return;

        LayerTitle? title = null;
        try { title = LayerTitle.Instance; }
        catch { }
        if (title == null)
            return;

        _mainMenuInfoAutoOpenScheduled = true;
        host.StartCoroutine(MainMenuInfoAutoOpenRoutine(host));
    }

    private IEnumerator MainMenuInfoAutoOpenRoutine(ElinModifierPlugin host)
    {
        yield return new WaitForSecondsRealtime(0.5f);
        _mainMenuInfoAutoOpenScheduled = false;

        if (!Enabled || _mainMenuInfoAutoOpened)
            yield break;

        LayerTitle? title = null;
        try { title = LayerTitle.Instance; }
        catch { }
        if (title != null)
            OpenWindow(host);
    }

    private void OpenWindow(ElinModifierPlugin host)
    {
        if (!Enabled)
            return;
        try
        {
            var layer = ELayer.ui.AddLayer("LayerAnnounce");
            if (layer == null)
                return;
            _mainMenuInfoLayer = layer;
            var book = layer.GetComponentInChildren<UIBook>(true);
            if (book == null)
                return;

            var item = new BookList.Item
            {
                title = "Elin Modifier",
                author = "Liset",
                id = "elin_modifier_main_menu_info",
                cat = "Elin Modifier",
                lines = new[]
                {
                    "",
                    host.TranslateModuleText("Version: " + ModMetadata.Version + "    QQ群:771844665", "Version: " + ModMetadata.Version),
                    ModMetadata.Copyright + "    " + ModMetadata.Rights,
                    "",
                    host.TranslateModuleText("欢迎使用 Elin Modifier。", "Welcome to Elin Modifier.") + " " +
                    host.TranslateModuleText("当前开启键: ", "Current hotkey: ") + host.ModuleOpenKeyLabel,
                    host.TranslateModuleText("{link,跳转至官网,https://m9.pw/}", "{link,Visit Official Website,https://m9.pw/}"),
                    "",
                    host.TranslateModuleText("{topic,Elin Modifier 更新日志}", "{topic,Elin Modifier Update Log}"),
                    "■ " + ModMetadata.ReleaseDate + " - v" + ModMetadata.Version,
                    "",
                    host.TranslateModuleText("[更新内容]", "[Changes]"),
                    host.TranslateModuleText(
                        "* 新增\"一键完成委托\"，支持告示板一键完成、任务列表一键完成",
                        "* Added \"One-click quest completion\", supporting one-click completion from the quest board and quest list."),
                    host.TranslateModuleText(
                        "* 新增\"显示物品面板奶的加成\"，计算基准为100有效潜力",
                        "* Added \"Show milk bonuses in item panel\", calculated using a baseline of 100 effective potential."),
                }
            };

            book.mode = UIBook.Mode.Announce;
            book.currentPage = 0;
            book.Show("", "", "Elin Modifier", item);
            RenameAnnouncementHeader(host, book, layer);
            host.StartCoroutine(RenameAnnouncementHeaderRoutine(host, book, layer));
            _mainMenuInfoAutoOpened = true;
        }
        catch { }
    }

    private IEnumerator RenameAnnouncementHeaderRoutine(ElinModifierPlugin host, UIBook book, Layer layer)
    {
        yield return null;
        RenameAnnouncementHeader(host, book, layer);
        yield return null;
        RenameAnnouncementHeader(host, book, layer);
    }

    private void RenameAnnouncementHeader(ElinModifierPlugin host, UIBook book, Layer layer)
    {
        if (book == null || layer == null)
            return;
        try
        {
            if (book.textTitle != null)
            {
                book.textTitle.lang = "";
                book.textTitle.SetText(host.TranslateModuleText("通知和新闻", "Notifications and News"));
            }

            var texts = layer.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null)
                    continue;
                var value = (text.text ?? "").Trim();
                var isChineseHeader = value.IndexOf("通知和新闻", StringComparison.Ordinal) >= 0;
                var isEnglishHeader = value.IndexOf("Elin", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                      value.IndexOf("news", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isChineseHeader || isEnglishHeader)
                    text.text = host.TranslateModuleText("通知和新闻", "Notifications and News");
            }
        }
        catch { }
    }

    internal void RefreshLanguage()
    {
        if (_mainMenuInfoButton != null)
            SetMainMenuInfoButtonText(_mainMenuInfoButton);
    }

    internal void ClearTitleState()
    {
        _mainMenuInfoButton = null;
        _mainMenuInfoLayer = null;
    }
}

[HarmonyPatch(typeof(LayerTitle), "OnInit")]
internal static class MainMenuInfoTitleInitPatch
{
    private static void Postfix()
    {
        var plugin = ElinModifierPlugin.ActiveInstance;
        if (plugin == null)
            return;
        plugin.RefreshMainMenuInfoButton();
        plugin.ScheduleMainMenuInfoAutoOpen();
    }
}

[HarmonyPatch(typeof(LayerTitle), "OnChangeLanguage")]
internal static class MainMenuInfoLanguagePatch
{
    private static void Postfix()
    {
        ElinModifierPlugin.ActiveModules?.MainMenuInfo.RefreshLanguage();
    }
}

[HarmonyPatch(typeof(LayerTitle), "OnKill")]
internal static class MainMenuInfoTitleKillPatch
{
    private static void Postfix()
    {
        ElinModifierPlugin.ActiveModules?.MainMenuInfo.ClearTitleState();
    }
}

public sealed partial class ElinModifierPlugin
{
    internal string TranslateModuleText(string zh, string en) => T(zh, en);
    internal string ModuleOpenKeyLabel => GetKeyLabel(_openKey);

    private bool ShowMainMenuInfo => _modules.MainMenuInfo.Enabled;

    private void SetShowMainMenuInfo(bool value)
    {
        _modules.MainMenuInfo.SetEnabled(value);
        RefreshMainMenuInfoButton();
        if (value)
            ScheduleMainMenuInfoAutoOpen();
    }

    internal void RefreshMainMenuInfoButton()
    {
        _modules.MainMenuInfo.RefreshButton(this);
    }

    internal void ScheduleMainMenuInfoAutoOpen()
    {
        _modules.MainMenuInfo.ScheduleAutoOpen(this);
    }

    private void DestroyMainMenuInfoButton()
    {
        _modules.MainMenuInfo.DestroyButton();
    }
}
