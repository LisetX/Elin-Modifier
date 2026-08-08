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
    private sealed class LGuiModalThemeRefresh : MonoBehaviour
    {
        public ElinModifierPlugin Owner = null!;

        private void Start()
        {
            Owner?.ApplyLGuiVisualSettings();
            Destroy(this);
        }
    }
    private int _lGuiFaithPage;
    private string _lGuiMaterialFilter = "";
    private int _lGuiMaterialPage;
    private int _lGuiDebugRootPage;
    private int _lGuiDebugConfigFileIndex;
    private int _lGuiDebugConfigEntryPage;
    private Text CreateLGuiFieldLabel(Transform parent, string label, float x, float y, float width)
    {
        var text = CreateLGuiText(parent, "FieldLabel", label, 14, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(text.rectTransform, x, y, width, 26f);
        return text;
    }
    private string GetLGuiPageStatus()
    {
        string value;
        switch (_lGuiPage)
        {
            case LGuiPage.Items: value = _itemLog; break;
            case LGuiPage.Npcs:
                value = string.IsNullOrWhiteSpace(_npcLog)
                    ? T("Insert 打开/关闭UI", "Insert toggles UI")
                    : _npcLog;
                break;
            case LGuiPage.PlayerInfo: value = _playerInfoLog; break;
            case LGuiPage.Home: value = _homeLog; break;
            case LGuiPage.Probability: value = _modules.Probability.Log; break;
            case LGuiPage.Automation: value = AutomationLog; break;
            case LGuiPage.Nightly: value = _modules.Nightly?.Log ?? ""; break;
            case LGuiPage.Moongate: value = _modules.Moongate.Log; break;
            case LGuiPage.NpcInfo: value = _modules.NpcInfo.Log; break;
            case LGuiPage.Ai: value = _aiLog; break;
            case LGuiPage.Debug: value = _debugLog; break;
            case LGuiPage.Emp: value = _pluginManagerLog; break;
            case LGuiPage.Settings: value = _configLog; break;
            default: value = _log; break;
        }
        return string.IsNullOrWhiteSpace(value) ? T("Ready", "Ready") : value;
    }
    private RectTransform CreateLGuiCompleteModal(string name, string title, out RectTransform content, float width = 1540f, float height = 980f)
    {
        CloseLGuiEditorModal(true);
        if (_lGuiWindow == null)
        {
            content = null!;
            return null!;
        }

        _lGuiEditorModal = new GameObject(name, typeof(RectTransform), typeof(Image));
        _lGuiEditorModal.AddComponent<LGuiModalThemeRefresh>().Owner = this;
        _lGuiEditorModal.transform.SetParent(_lGuiRoot!.transform, false);
        PrepareLGuiStandaloneModal(_lGuiEditorModal);
        var modal = (RectTransform)_lGuiEditorModal.transform;
        modal.anchorMin = new Vector2(0.5f, 0.5f);
        modal.anchorMax = new Vector2(0.5f, 0.5f);
        modal.pivot = new Vector2(0.5f, 0.5f);
        modal.sizeDelta = new Vector2(width, height);
        modal.anchoredPosition = Vector2.zero;
        modal.GetComponent<Image>().color = GetLGuiRowColor(0, true);

        var titleText = CreateLGuiText(modal, "Title", title, 22, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(titleText.rectTransform, 24f, 14f, width - 120f, 48f);
        EnableLGuiModalDragging(modal, titleText);
        CreateLGuiButton(modal, "Close", "×", width - 76f, 14f, 52f, 44f, CloseLGuiEditorModal);

        var body = CreateLGuiRect(modal, "Body");
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(22f, 76f);
        body.offsetMax = new Vector2(-22f, -72f);
        var scroll = CreateLGuiScroll(body, "Scroll", 0f);
        content = scroll.content!;
        content.sizeDelta = new Vector2(0f, 900f);
        ApplyLGuiVisualSettings();
        return modal;
    }
    private Button CreateLGuiModalButton(RectTransform modal, string name, string label, float x, float width, Action action)
    {
        return CreateLGuiButton(modal, name, label, x, modal.rect.height - 58f, width, 44f, action);
    }
    private float AddLGuiReadOnlyRow(RectTransform content, string label, string value, float y, float labelWidth = 220f)
    {
        var caption = CreateLGuiText(content, "ReadLabel", label, 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(caption.rectTransform, 0f, y, labelWidth, 40f);
        var text = CreateLGuiText(content, "ReadValue", value ?? "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(text.rectTransform, labelWidth + 10f, y, 1080f, 40f);
        return y + 42f;
    }
    private float AddLGuiInlineInput(RectTransform content, string label, Func<string> read, Action<string> write, float x, float y, float labelWidth = 130f, float inputWidth = 160f)
    {
        var caption = CreateLGuiText(content, "InlineLabel", label, 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(caption.rectTransform, x, y, labelWidth, 42f);
        var input = CreateLGuiInput(content, "InlineInput", label, x + labelWidth, y, inputWidth, 42f);
        input.text = read() ?? "";
        input.onValueChanged.AddListener(value => write(value ?? ""));
        return x + labelWidth + inputWidth;
    }
    private void EnsureLGuiEditorVisible()
    {
        if (!IsLGuiInitialized())
            return;
        ShowLGui();
    }
}
