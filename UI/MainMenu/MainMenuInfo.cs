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
                    "∧,_,∧",
                    "(｡＞ᴗ＜｡)",
                    host.TranslateModuleText(
                        "提前祝大家中秋节、国庆节快乐！",
                        "Wishing everyone an early Happy Mid-Autumn Festival and National Day!"),
                    "",
                    host.TranslateModuleText("[更新内容]", "[Changes]"),
                    host.TranslateModuleText(
                        "* 新增\"世界地图\"模块",
                        "* Added the \"World Map\" module"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块新增\"区块地图\"",
                        "* Added \"Zone map\" to the \"World Map\" module"),
                    host.TranslateModuleText(
                        "* \"世界地图\"完成功能上线前的二次重构优化",
                        "* Refactored and optimized \"World Map\" a second time before release"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块新增\"标记系统\"",
                        "* Added the \"Pin system\" to the \"World Map\" module"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块新增更多信息展示",
                        "* Added more information display to the \"World Map\" module"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块支持将世界地图、区块地图导出为PNG",
                        "* \"World Map\" can now export the world map and the zone map as PNG"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块导出PNG适配更多信息展示",
                        "* PNG export from \"World Map\" now carries the extra information display"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块新增\"强制读取\"，开启后安全模拟区块生成，不会真正提前生成区块",
                        "* Added \"Force read\" to the \"World Map\" module; it safely simulates zone generation without actually generating zones early"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块新增\"查询\"，支持采集物、NPC、区块查询",
                        "* Added \"Search\" to the \"World Map\" module, covering gatherables, NPCs and zones"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块\"查询\"新增支持模糊检索",
                        "* \"Search\" in the \"World Map\" module now supports fuzzy matching"),
                    host.TranslateModuleText(
                        "* \"世界地图\"模块新增\"目的地导航\"",
                        "* Added \"Route guide\" to the \"World Map\" module"),
                    host.TranslateModuleText(
                        "* EMG更新，增加多个接口",
                        "* Updated EMG with several new interfaces"),
                    host.TranslateModuleText(
                        "* \"AI辅助\"拓展接入\"NPC图鉴\"",
                        "* Extended \"AI Assistant\" with \"NPC Compendium\" access"),
                    host.TranslateModuleText(
                        "* \"AI辅助\"拓展接入\"世界地图\"",
                        "* Extended \"AI Assistant\" with \"World Map\" access"),
                    host.TranslateModuleText(
                        "* 优化\"AI辅助\"的游戏数据检索准确性",
                        "* Improved the accuracy of game data lookups in \"AI Assistant\""),
                    host.TranslateModuleText(
                        "* \"调试模式\"UI优化，目前继续保持不对玩家开放",
                        "* Improved the \"Debug mode\" UI; it stays unavailable to players for now"),
                    host.TranslateModuleText(
                        "* 修复部分UI异常表现",
                        "* Fixed several UI display glitches"),
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
