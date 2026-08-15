using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed class OneClickQuestCompletionModule
{
    private const string BoardButtonName = "ElinModifierOneClickQuestBoardButton";
    private const string ActiveButtonName = "ElinModifierOneClickQuestActiveButton";

    private readonly ElinModifierPlugin _host;
    private readonly IGameRuntimeContext _runtime;
    private readonly IBoundGameValue<QuestManager> _questManager;
    private readonly IBoundGameValue<List<Quest>> _activeQuests;
    private readonly IBoundGameValue<List<Quest>> _globalQuests;
    private readonly IBoundGameValue<bool> _isRandomQuest;
    private readonly IBoundGameValue<UIText> _itemDate;
    private readonly IBoundGameValue<List<Window>> _layerWindows;
    private readonly IBoundGameValue<WindowMenu> _windowMenuRight;
    private readonly IBoundGameValue<LayoutGroup> _windowMenuLayout;
    private readonly IBoundGameValue<UIButton> _contentAbandonButton;
    private readonly IBoundGameValue<UIText> _contentClient;
    private readonly IBoundGameValue<UIText> _buttonMainText;
    private readonly IBoundGameValue<int> _windowTab;
    private readonly IBoundGameMethod _startQuest;
    private readonly IBoundGameMethod _completeQuest;
    private readonly IBoundGameMethod _removeGlobalQuest;
    private readonly IBoundGameMethod _refreshBoard;
    private readonly IBoundGameMethod _switchQuestContent;
    private readonly IBoundGameMethod _setText;
    private readonly bool _completionBindingsReady;
    private readonly bool _boardBindingsReady;
    private readonly bool _activeBindingsReady;

    internal OneClickQuestCompletionModule(
        ElinModifierPlugin host,
        IGameRuntimeContext runtime,
        IGameMemberBinder binder)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _questManager = binder.BindInstanceValue<QuestManager>(
            typeof(Game),
            GameValueAccess.Read,
            "quests");
        _activeQuests = binder.BindInstanceValue<List<Quest>>(
            typeof(QuestManager),
            GameValueAccess.Read,
            "list");
        _globalQuests = binder.BindInstanceValue<List<Quest>>(
            typeof(QuestManager),
            GameValueAccess.Read,
            "globalList");
        _isRandomQuest = binder.BindInstanceValue<bool>(
            typeof(Quest),
            GameValueAccess.Read,
            "IsRandomQuest");
        _itemDate = binder.BindInstanceValue<UIText>(
            typeof(ItemQuest),
            GameValueAccess.Read,
            "textDate");
        _layerWindows = binder.BindInstanceValue<List<Window>>(
            typeof(Layer),
            GameValueAccess.Read,
            "windows");
        _windowMenuRight = binder.BindInstanceValue<WindowMenu>(
            typeof(Window),
            GameValueAccess.Read,
            "menuRight");
        _windowMenuLayout = binder.BindInstanceValue<LayoutGroup>(
            typeof(WindowMenu),
            GameValueAccess.Read,
            "layout");
        _contentAbandonButton = binder.BindInstanceValue<UIButton>(
            typeof(ContentQuest),
            GameValueAccess.Read,
            "buttonAbandon");
        _contentClient = binder.BindInstanceValue<UIText>(
            typeof(ContentQuest),
            GameValueAccess.Read,
            "textClient");
        _buttonMainText = binder.BindInstanceValue<UIText>(
            typeof(UIButton),
            GameValueAccess.Read,
            "mainText");
        _windowTab = binder.BindInstanceValue<int>(
            typeof(Window),
            GameValueAccess.Read,
            "idTab");
        _startQuest = binder.BindInstanceMethod(
            typeof(QuestManager),
            typeof(Quest),
            new[] { typeof(Quest) },
            "Start");
        _completeQuest = binder.BindInstanceMethod(
            typeof(QuestManager),
            typeof(void),
            new[] { typeof(Quest) },
            "Complete");
        _removeGlobalQuest = binder.BindInstanceMethod(
            typeof(QuestManager),
            typeof(void),
            new[] { typeof(Quest) },
            "RemoveGlobal");
        _refreshBoard = binder.BindInstanceMethod(
            typeof(LayerQuestBoard),
            typeof(void),
            Type.EmptyTypes,
            "RefreshQuest");
        _switchQuestContent = binder.BindInstanceMethod(
            typeof(ContentQuest),
            typeof(void),
            new[] { typeof(int) },
            "OnSwitchContent");
        _setText = binder.BindInstanceMethod(
            typeof(UIText),
            typeof(void),
            new[] { typeof(string) },
            "SetText");

        _completionBindingsReady = _questManager.IsBound && _activeQuests.IsBound &&
                                   _globalQuests.IsBound && _isRandomQuest.IsBound &&
                                   _startQuest.IsBound && _completeQuest.IsBound &&
                                   _removeGlobalQuest.IsBound;
        _boardBindingsReady = _completionBindingsReady && _itemDate.IsBound &&
                              _layerWindows.IsBound && _windowMenuRight.IsBound &&
                              _windowMenuLayout.IsBound && _buttonMainText.IsBound &&
                              _windowTab.IsBound && _refreshBoard.IsBound && _setText.IsBound;
        _activeBindingsReady = _completionBindingsReady && _contentAbandonButton.IsBound &&
                               _contentClient.IsBound && _buttonMainText.IsBound &&
                               _switchQuestContent.IsBound && _setText.IsBound;
    }

    internal bool Enabled { get; private set; }

    internal void Load(bool enabled)
    {
        SetState(enabled);
    }

    internal void Reset()
    {
        SetState(false);
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        SetState(enabled);
        return true;
    }

    internal void ApplyBoardWindow(LayerQuestBoard? layer, Window? window)
    {
        if (layer == null || window == null)
            return;
        if (!Enabled || !_boardBindingsReady ||
            !_windowTab.TryGet(window, out var tab) || tab != 0)
        {
            RemoveButtons(layer.transform, BoardButtonName);
            return;
        }

        var template = FindBoardButtonTemplate(layer);
        if (template == null)
            return;

        var states = layer.GetComponentsInChildren<OneClickQuestCompletionBoardItemState>(true);
        for (var i = 0; i < states.Length; i++)
        {
            var state = states[i];
            if (state == null || state.Quest == null || !state.gameObject.activeInHierarchy ||
                !state.TryGetComponent<ItemQuest>(out var item))
                continue;
            ApplyBoardItem(item, state.Quest, template);
        }
    }

    internal void ApplyBoardItem(ItemQuest? item, Quest? quest)
    {
        if (item == null)
            return;

        var state = item.GetComponent<OneClickQuestCompletionBoardItemState>() ??
                    item.gameObject.AddComponent<OneClickQuestCompletionBoardItemState>();
        state.Quest = quest;
        RemoveButtons(item.transform, BoardButtonName);
        if (!Enabled || !_boardBindingsReady || !IsRandomQuest(quest))
            return;

        var layer = item.GetComponentInParent<LayerQuestBoard>();
        if (layer == null)
            return;
        var template = FindBoardButtonTemplate(layer);
        if (template != null)
            ApplyBoardItem(item, quest!, template);
    }

    internal void ApplyActiveQuest(ContentQuest? content, Quest? quest)
    {
        if (content == null)
            return;

        RemoveButtons(content.transform, ActiveButtonName);
        if (!Enabled || !_activeBindingsReady || !IsRandomQuest(quest) ||
            !_contentAbandonButton.TryGet(content, out var abandonButton) ||
            abandonButton == null || !_contentClient.TryGet(content, out var clientText) ||
            clientText == null)
            return;

        var cloneObject = UnityEngine.Object.Instantiate(
            abandonButton.gameObject,
            clientText.transform,
            false);
        cloneObject.name = ActiveButtonName;
        cloneObject.AddComponent<OneClickQuestCompletionButtonMarker>();
        var button = cloneObject.GetComponent<UIButton>();
        if (button == null)
        {
            UnityEngine.Object.Destroy(cloneObject);
            return;
        }

        ResetButtonActions(button);
        SetButtonText(button);
        PositionActiveButton(clientText, button);
        button.onClick.AddListener(() => CompleteActiveQuest(content, quest!));
        cloneObject.SetActive(true);
    }

    private void SetState(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
            DestroyAllButtons();
    }

    private bool IsRandomQuest(Quest? quest)
    {
        return quest != null && _isRandomQuest.TryGet(quest, out var isRandom) && isRandom;
    }

    private void ApplyBoardItem(ItemQuest item, Quest quest, UIButton template)
    {
        RemoveButtons(item.transform, BoardButtonName);
        if (!IsRandomQuest(quest) || !_itemDate.TryGet(item, out var dateText) || dateText == null)
            return;

        var parent = dateText.transform.parent;
        if (parent == null)
            return;

        var cloneObject = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        cloneObject.name = BoardButtonName;
        cloneObject.AddComponent<OneClickQuestCompletionButtonMarker>();
        var button = cloneObject.GetComponent<UIButton>();
        if (button == null)
        {
            UnityEngine.Object.Destroy(cloneObject);
            return;
        }

        ResetButtonActions(button);
        SetButtonText(button);
        PositionBoardButton(dateText, button);
        var layer = item.GetComponentInParent<LayerQuestBoard>();
        button.onClick.AddListener(() => CompleteBoardQuest(layer, quest, button));
        cloneObject.SetActive(true);
    }

    private UIButton? FindBoardButtonTemplate(LayerQuestBoard layer)
    {
        if (!_layerWindows.TryGet(layer, out var windows) || windows == null || windows.Count == 0 ||
            windows[0] == null || !_windowMenuRight.TryGet(windows[0], out var menuRight) ||
            menuRight == null || !_windowMenuLayout.TryGet(menuRight, out var menuLayout) ||
            menuLayout == null)
            return null;

        var expected = "rerollQuest".lang("1");
        var buttons = menuLayout.GetComponentsInChildren<UIButton>(true);
        UIButton? fallback = null;
        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null || !_buttonMainText.TryGet(button, out var text) || text == null)
                continue;
            if (string.Equals(text.text, expected, StringComparison.Ordinal))
                return button;
            fallback = button;
        }
        return fallback;
    }

    private void CompleteBoardQuest(LayerQuestBoard? layer, Quest quest, UIButton button)
    {
        if (button != null)
            button.interactable = false;
        if (!TryCompleteQuest(quest))
        {
            if (button != null)
                button.interactable = true;
            return;
        }
        if (layer != null)
            _refreshBoard.TryInvoke(layer, Array.Empty<object?>(), out _);
    }

    private void CompleteActiveQuest(ContentQuest content, Quest quest)
    {
        if (!TryCompleteQuest(quest))
            return;
        _switchQuestContent.TryInvoke(content, new object?[] { 0 }, out _);
    }

    private bool TryCompleteQuest(Quest quest)
    {
        if (!Enabled || !_completionBindingsReady || !IsRandomQuest(quest))
            return false;
        var game = _runtime.Game;
        if (game == null || !_questManager.TryGet(game, out var manager) || manager == null ||
            !_activeQuests.TryGet(manager, out var active) || active == null ||
            !_globalQuests.TryGet(manager, out var global) || global == null)
            return false;

        try
        {
            if (!active.Contains(quest))
            {
                var wasGlobal = global.Contains(quest);
                if (wasGlobal &&
                    !_removeGlobalQuest.TryInvoke(manager, new object?[] { quest }, out _))
                    return false;
                if (!_startQuest.TryInvoke(manager, new object?[] { quest }, out _))
                {
                    if (wasGlobal && !global.Contains(quest))
                        global.Add(quest);
                    return false;
                }
            }
            return _completeQuest.TryInvoke(manager, new object?[] { quest }, out _);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError("[Elin Modifier] One-click quest completion failed: " + ex);
            return false;
        }
    }

    private void SetButtonText(UIButton button)
    {
        if (!_buttonMainText.TryGet(button, out var text) || text == null)
            return;
        var label = T("一键完成", "Complete instantly");
        _setText.TryInvoke(text, new object?[] { label }, out _);
        text.text = label;
        text.enabled = true;
        text.gameObject.SetActive(true);
        text.raycastTarget = false;
        text.alignment = TextAnchor.MiddleCenter;
        var color = text.color;
        color.a = 1f;
        text.color = color;
        text.canvasRenderer.SetAlpha(1f);
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void ResetButtonActions(UIButton button)
    {
        button.onClick.RemoveAllListeners();
        button.onDoubleClick = null;
        button.onRightClick = null;
        button.onInputWheel = null;
    }

    private static void PositionBoardButton(UIText dateText, UIButton button)
    {
        var dateRect = dateText.rectTransform;
        var buttonRect = button.transform as RectTransform;
        if (buttonRect == null)
            return;
        var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        buttonRect.anchorMin = dateRect.anchorMin;
        buttonRect.anchorMax = dateRect.anchorMax;
        buttonRect.pivot = dateRect.pivot;
        buttonRect.sizeDelta = new Vector2(118f, 32f);
        buttonRect.anchoredPosition = dateRect.anchoredPosition +
                                      new Vector2(0f, -Mathf.Max(34f, dateRect.rect.height + 6f));
        buttonRect.localScale = Vector3.one;
    }

    private static void PositionActiveButton(UIText clientText, UIButton button)
    {
        var buttonRect = button.transform as RectTransform;
        if (buttonRect == null)
            return;
        var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.zero;
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.sizeDelta = new Vector2(140f, 34f);
        buttonRect.anchoredPosition = new Vector2(0f, -8f);
        buttonRect.localScale = Vector3.one;
    }

    private static void RemoveButtons(Transform root, string name)
    {
        var markers = root.GetComponentsInChildren<OneClickQuestCompletionButtonMarker>(true);
        for (var i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            if (marker == null || !string.Equals(marker.gameObject.name, name, StringComparison.Ordinal))
                continue;
            marker.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(marker.gameObject);
        }
    }

    private static void DestroyAllButtons()
    {
        var markers = Resources.FindObjectsOfTypeAll<OneClickQuestCompletionButtonMarker>();
        for (var i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            if (marker == null || !marker.gameObject.scene.IsValid())
                continue;
            marker.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(marker.gameObject);
        }
    }

    private string T(string chinese, string english)
    {
        return _host.TranslateModuleText(chinese, english);
    }
}

internal sealed class OneClickQuestCompletionButtonMarker : MonoBehaviour
{
}

internal sealed class OneClickQuestCompletionBoardItemState : MonoBehaviour
{
    internal Quest? Quest { get; set; }
}

internal static class OneClickQuestCompletionPatchContext
{
    internal static OneClickQuestCompletionModule? Current =>
        ElinModifierPlugin.ActiveModules?.OneClickQuestCompletion;
}

[HarmonyPatch(typeof(ItemQuest), "SetQuest")]
internal static class ItemQuestSetQuestOneClickCompletionPatch
{
    private static void Postfix(ItemQuest __instance, Quest __0)
    {
        OneClickQuestCompletionPatchContext.Current?.ApplyBoardItem(__instance, __0);
    }
}

[HarmonyPatch(typeof(LayerQuestBoard), "OnSwitchContent")]
internal static class LayerQuestBoardSwitchOneClickCompletionPatch
{
    private static void Postfix(LayerQuestBoard __instance, Window __0)
    {
        OneClickQuestCompletionPatchContext.Current?.ApplyBoardWindow(__instance, __0);
    }
}

[HarmonyPatch(typeof(ContentQuest), "SelectQuest")]
internal static class ContentQuestSelectOneClickCompletionPatch
{
    private static void Postfix(ContentQuest __instance, Quest __0)
    {
        OneClickQuestCompletionPatchContext.Current?.ApplyActiveQuest(__instance, __0);
    }
}
