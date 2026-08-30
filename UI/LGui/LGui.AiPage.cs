using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiAiPage()
    {
        var scroll = CreateLGuiScroll(_lGuiPageHost!, "AiScroll", 0f);
        var content = scroll.content!;
        var y = 8f;
        CreateLGuiButton(content, "ApiSettingsToggle", (_aiApiSettingsExpanded ? "▼ " : "▶ ") + T("接口设置", "API Settings"), 0f, y, 190f, 44f, () =>
        {
            _aiApiSettingsExpanded = !_aiApiSettingsExpanded;
            SwitchLGuiPage(LGuiPage.Ai);
        });
        y += 56f;

        if (_aiApiSettingsExpanded)
        {
            y = AddLGuiAiInput(content, IndentLGuiText(T("API地址", "API Base"), 1), () => _aiApiBase, value => _aiApiBase = value, y, 1050f);
            y = AddLGuiAiInput(content, IndentLGuiText("API Key", 1), () => _aiApiKey, value => _aiApiKey = value, y, 920f, !_showAiApiKey);
            CreateLGuiButton(content, "ShowApiKey", _showAiApiKey ? T("隐藏", "Hide") : T("显示", "Show"), 1150f, y - 50f, 90f, 44f, () =>
            {
                _showAiApiKey = !_showAiApiKey;
                SwitchLGuiPage(LGuiPage.Ai);
            });
            y = AddLGuiAiInput(content, IndentLGuiText(T("模型名", "Model"), 1), () => _aiModelName, value => _aiModelName = value, y, 720f, false, input => _lGuiAiModelInput = input);
            CreateLGuiButton(content, "FetchModels", _aiFetchModelsInProgress ? T("获取中", "Loading") : T("获取模型名", "Fetch models"), 950f, y - 50f, 140f, 44f, () =>
            {
                if (!_aiFetchModelsInProgress)
                    FetchAiModels();
            });
            y = BuildLGuiAiModelSelector(content, y);

            var reasoningLabel = CreateLGuiText(content, "ReasoningLabel", IndentLGuiText(T("思考强度", "Reasoning"), 1), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(reasoningLabel.rectTransform, 0f, y, 160f, 44f);
            for (var i = 0; i < AiReasoningEfforts.Length; i++)
            {
                var reasoningIndex = i;
                var optionLabel = (i == _aiReasoningEffortIndex ? "-> " : "") + AiReasoningEfforts[i];
                CreateLGuiButton(content, "Reasoning" + i.ToString(CultureInfo.InvariantCulture), optionLabel, 170f + i * 112f, y, 102f, 44f, () =>
                {
                    _aiReasoningEffortIndex = reasoningIndex;
                    SwitchLGuiPage(LGuiPage.Ai);
                });
            }
            y += 52f;
            CreateLGuiToggleControl(content, IndentLGuiText(T("上下文", "Context"), 1), _aiUseContext, y, value => _aiUseContext = value);
            y += 54f;
            CreateLGuiToggleControl(content, IndentLGuiText(T("自动压缩", "Auto compact"), 1), _aiAutoCompressContext, y, value => _aiAutoCompressContext = value);
            var compactLimit = CreateLGuiInput(content, "CompactLimit", T("阈值", "Limit"), 590f, y, 180f, 44f);
            compactLimit.text = _aiContextCompressThresholdText;
            compactLimit.onValueChanged.AddListener(value => _aiContextCompressThresholdText = value ?? "");
            CreateLGuiButton(content, "ApplyCompactLimit", T("应用", "Apply"), 784f, y, 90f, 44f, ApplyAiContextCompressionThresholdText);
            var usage = CreateLGuiText(content, "ContextUsage", GetAiContextUsageLabel(), 16, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(usage.rectTransform, 890f, y, 420f, 44f);
            y += 54f;
            CreateLGuiToggleControl(content, IndentLGuiText(T("流式传输", "Streaming"), 1), _aiUseStreaming, y, value => _aiUseStreaming = value);
            CreateLGuiToggleControl(content, IndentLGuiText(T("EMG流式传输", "EMG streaming"), 1), _aiUseToolStreaming, y + 54f, value => _aiUseToolStreaming = value);
            y += 108f;
            var timeoutLabel = CreateLGuiText(content, "TimeoutLabel", IndentLGuiText(T("HTTP超时", "HTTP timeout"), 1), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(timeoutLabel.rectTransform, 0f, y, 160f, 44f);
            var timeout = CreateLGuiInput(content, "Timeout", T("秒", "sec"), 170f, y, 180f, 44f);
            timeout.text = _aiHttpTimeoutSecondsText;
            timeout.onValueChanged.AddListener(value => _aiHttpTimeoutSecondsText = value ?? "");
            CreateLGuiButton(content, "ApplyTimeout", T("应用", "Apply"), 364f, y, 90f, 44f, () => ApplyAiHttpTimeoutSecondsText());
            y += 56f;

            y = AddLGuiSectionTitle(content, IndentLGuiText(T("最后发送HTTP报文主体", "Last sent HTTP body"), 1), y);
            _lGuiAiLastRequestInput = CreateLGuiScrollableTextBox(content, "LastRequest", 0f, y, 1320f, 140f);
            _lGuiAiLastRequestInput.SetText(_aiLastRequestBody);
            y += 152f;
            y = AddLGuiSectionTitle(content, IndentLGuiText(T("最后接收HTTP报文主体", "Last received HTTP body"), 1), y);
            _lGuiAiLastResponseInput = CreateLGuiScrollableTextBox(content, "LastResponse", 0f, y, 1320f, 140f);
            _lGuiAiLastResponseInput.SetText(_aiLastResponseBody);
            y += 158f;
        }

        y = AddLGuiSectionTitle(content, T("对话框", "Dialog"), y);
        _lGuiAiResponseInput = CreateLGuiScrollableTextBox(content, "Dialog", 0f, y, 1320f, 430f);
        _lGuiAiResponseInput.StickToBottom = true;
        _lGuiAiResponseInput.SetText(_aiResponse);
        y += 442f;
        y = AddLGuiSectionTitle(content, T("输入框", "Input"), y);
        _lGuiAiPromptInput = CreateLGuiMultilineInput(content, "Prompt", 0f, y, 1320f, 150f);
        _lGuiAiPromptInput.gameObject.AddComponent<LGuiTransparentInputBackground>();
        if (_lGuiAiPromptInput.targetGraphic is Image promptBackground)
            promptBackground.color = new Color(0f, 0f, 0f, 0f);
        _lGuiAiPromptInput.text = _aiPrompt;
        _lGuiAiPromptInput.onValueChanged.AddListener(value => _aiPrompt = value ?? "");
        y += 162f;
        CreateLGuiButton(content, "Send", (_aiSendInProgress || _aiCompressionInProgress) ? T("处理中", "Working") : T("发送", "Send"), 0f, y, 100f, 46f, () =>
        {
            if (_lGuiAiPromptInput != null)
                _aiPrompt = _lGuiAiPromptInput.text;
            if (!_aiSendInProgress && !_aiCompressionInProgress)
                SendAiChat();
            if (_lGuiAiPromptInput != null)
                _lGuiAiPromptInput.text = _aiPrompt;
            RefreshLGuiAiControls();
        });
        CreateLGuiButton(content, "Abort", T("中止", "Abort"), 112f, y, 90f, 46f, () =>
        {
            if (_aiSendInProgress || _aiCompressionInProgress)
                CancelAiCurrentOperation();
            RefreshLGuiAiControls();
        });
        CreateLGuiButton(content, "ClearHistory", T("清空历史", "Clear history"), 214f, y, 120f, 46f, () =>
        {
            _aiMessages.Clear();
            _aiResponse = "";
            _aiLog = T("对话历史和上下文已清空", "Chat history and context cleared");
            RefreshLGuiAiControls();
        });
        CreateLGuiButton(content, "Compact", _aiCompressionInProgress ? T("压缩中", "Compacting") : T("压缩上下文", "Compact context"), 346f, y, 140f, 46f, () =>
        {
            if (!_aiSendInProgress && !_aiCompressionInProgress)
                StartManualAiContextCompression();
            RefreshLGuiAiControls();
        });
        _lGuiAiStatusText = CreateLGuiText(content, "AiStatus", _aiLog, 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_lGuiAiStatusText.rectTransform, 506f, y, 800f, 46f);
        y += 58f;

        if (_aiPendingDangerousActions.Count > 0)
        {
            var pending = CreateLGuiText(content, "PendingActions", T("待确认高危操作: ", "Pending high-risk actions: ") + _aiPendingDangerousActions.Count.ToString(CultureInfo.InvariantCulture), 17, TextAnchor.UpperLeft, FontStyle.Normal);
            PlaceLGuiRect(pending.rectTransform, 0f, y, 1250f, 90f);
            y += 96f;
        }
        y = AddLGuiAiDangerousDetails(content, y);
        content.sizeDelta = new Vector2(0f, Math.Max(900f, y + 30f));
    }
    private void BuildLGuiNightlyPage()
    {
        var module = _modules.Nightly;
        if (module == null)
            return;

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "NightlyScroll", 0f);
        var content = scroll.content!;
        CreateLGuiToggleControl(content, T("修复自言自语Bug", "Fix self-talk bug"), module.FixSelfTalkBug, 8f, value =>
        {
            module.FixSelfTalkBug = value;
            if (value)
                EnsureNightlyFeatureHarmonyPatches();
            module.Log = value
                ? T("修复自言自语Bug已开启", "Self-talk bug fix enabled")
                : T("修复自言自语Bug已关闭", "Self-talk bug fix disabled");
            SaveConfig(false);
            NotifyLGuiDataDirty();
        });
        content.sizeDelta = new Vector2(0f, 80f);
    }
    private float AddLGuiAiInput(RectTransform parent, string label, Func<string> read, Action<string> write, float y, float width, bool password = false, Action<InputField>? capture = null)
    {
        var caption = CreateLGuiText(parent, "AiLabel", label, 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(caption.rectTransform, 0f, y, 160f, 44f);
        var input = CreateLGuiInput(parent, "AiInput", label, 170f, y, width, 44f);
        input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
        input.text = read() ?? "";
        input.onValueChanged.AddListener(value => write(value ?? ""));
        capture?.Invoke(input);
        return y + 52f;
    }
    private float BuildLGuiAiModelSelector(RectTransform parent, float y)
    {
        var label = CreateLGuiText(parent, "ModelSelectorLabel", IndentLGuiText(T("模型列表", "Model list"), 1), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(label.rectTransform, 0f, y, 160f, 44f);

        var options = new List<string>();
        if (!string.IsNullOrWhiteSpace(_aiModelName))
            options.Add(_aiModelName.Trim());
        for (var i = 0; i < _aiModels.Count; i++)
        {
            var model = (_aiModels[i] ?? "").Trim();
            if (model.Length > 0 && !options.Exists(value => string.Equals(value, model, StringComparison.OrdinalIgnoreCase)))
                options.Add(model);
        }
        if (options.Count == 0)
            options.Add(T("请先获取模型名", "Fetch models first"));

        var selectedIndex = options.FindIndex(value => string.Equals(value, _aiModelName, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
            selectedIndex = 0;
        CreateAutomationDropdown(parent, "ModelSelector", options, selectedIndex, 170f, y, 1072f, 44f, optionIndex =>
        {
            if (optionIndex < 0 || optionIndex >= options.Count || _aiModels.Count == 0 && string.IsNullOrWhiteSpace(_aiModelName))
                return;
            _aiModelName = options[optionIndex];
            if (_lGuiAiModelInput != null && !string.Equals(_lGuiAiModelInput.text, _aiModelName, StringComparison.Ordinal))
                _lGuiAiModelInput.text = _aiModelName;
        });
        return y + 52f;
    }
    private void RefreshLGuiAiControls()
    {
        if (_lGuiPage != LGuiPage.Ai)
            return;
        if (_lGuiAiResponseInput != null && !string.Equals(_lGuiAiResponseInput.Text, _aiResponse, StringComparison.Ordinal))
            _lGuiAiResponseInput.SetText(_aiResponse);
        if (_lGuiAiLastRequestInput != null && !string.Equals(_lGuiAiLastRequestInput.Text, _aiLastRequestBody, StringComparison.Ordinal))
            _lGuiAiLastRequestInput.SetText(_aiLastRequestBody);
        if (_lGuiAiLastResponseInput != null && !string.Equals(_lGuiAiLastResponseInput.Text, _aiLastResponseBody, StringComparison.Ordinal))
            _lGuiAiLastResponseInput.SetText(_aiLastResponseBody);
        if (_lGuiAiStatusText != null)
            _lGuiAiStatusText.text = _aiLog ?? "";
    }
}
