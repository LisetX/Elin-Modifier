using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal interface ILGuiRowHandler
{
    void OnLGuiRowPrimary(LGuiRowView row);
    void OnLGuiRowAuxiliary(LGuiRowView row);
    void OnLGuiRowToggle(LGuiRowView row, bool value);
    void OnLGuiRowChoice(LGuiRowView row, int choiceIndex);
    void OnLGuiRowDropdown(LGuiRowView row, int optionIndex);
    void OnLGuiRowInput(LGuiRowView row, string value);
    void OnLGuiRowInputCommit(LGuiRowView row, string value);
}

internal sealed class LGuiSafeInputField : InputField
{
    private Text? _safeLabelTextComponent;
    private bool _updatingLabelSafely;

    public void EnableSafeLabelUpdates()
    {
        if (_safeLabelTextComponent == textComponent)
            return;

        DisableSafeLabelUpdates();
        if (textComponent == null || !isActiveAndEnabled)
            return;

        textComponent.UnregisterDirtyVerticesCallback(UpdateLabel);
        textComponent.RegisterDirtyVerticesCallback(UpdateLabelSafely);
        _safeLabelTextComponent = textComponent;
    }

    private void DisableSafeLabelUpdates()
    {
        if (_safeLabelTextComponent == null)
            return;

        _safeLabelTextComponent.UnregisterDirtyVerticesCallback(UpdateLabelSafely);
        _safeLabelTextComponent = null;
    }

    private void UpdateLabelSafely()
    {
        if (_updatingLabelSafely)
            return;

        _updatingLabelSafely = true;
        try
        {
            try
            {
                UpdateLabel();
            }
            catch (ArgumentOutOfRangeException)
            {
                m_CaretPosition = 0;
                m_CaretSelectPosition = 0;
                try { UpdateLabel(); } catch (ArgumentOutOfRangeException) { }
            }
        }
        finally
        {
            _updatingLabelSafely = false;
        }
    }

    public void SetTextSafely(string value)
    {
        value = value ?? "";
        SetTextWithoutNotify(value);
        if (!isFocused)
        {
            m_CaretPosition = 0;
            m_CaretSelectPosition = 0;
        }
        else
        {
            m_CaretPosition = Mathf.Clamp(m_CaretPosition, 0, value.Length);
            m_CaretSelectPosition = Mathf.Clamp(m_CaretSelectPosition, 0, value.Length);
        }
        if (isActiveAndEnabled)
        {
            try { ForceLabelUpdate(); }
            catch (ArgumentOutOfRangeException)
            {
                m_CaretPosition = 0;
                m_CaretSelectPosition = 0;
            }
        }
    }

    public void SetCaretForScroll(int position)
    {
        var length = text?.Length ?? 0;
        position = Mathf.Clamp(position, 0, length);
        m_CaretPosition = position;
        m_CaretSelectPosition = position;
        ForceLabelUpdate();
    }

    protected override void OnEnable()
    {
        m_CaretPosition = 0;
        m_CaretSelectPosition = 0;
        if (textComponent != null)
            textComponent.supportRichText = false;
        try
        {
            base.OnEnable();
        }
        catch (ArgumentOutOfRangeException)
        {
            m_CaretPosition = 0;
            m_CaretSelectPosition = 0;
            try { ForceLabelUpdate(); } catch { }
        }
        EnableSafeLabelUpdates();
    }

    protected override void OnDisable()
    {
        DisableSafeLabelUpdates();
        base.OnDisable();
    }
}

internal sealed class LGuiTextProfile : MonoBehaviour
{
    public int BaseFontSize = 13;
}

internal sealed class LGuiTransparentInputBackground : MonoBehaviour
{
}

internal sealed class LGuiSteppedSlider : Slider
{
    public float StepSize;

    protected override void Set(float input, bool sendCallback = true)
    {
        if (StepSize > 0f)
        {
            var steps = Mathf.Round((input - minValue) / StepSize);
            input = minValue + steps * StepSize;
        }
        base.Set(input, sendCallback);
    }
}

internal sealed class LGuiFadeDriver : MonoBehaviour
{
    private CanvasGroup? _group;
    private float _from;
    private float _target;
    private float _duration;
    private float _elapsed;
    private bool _targetInteractive;
    private bool _fading;
    private Action? _completed;

    public bool IsFading => _fading;

    public void Initialize(CanvasGroup group)
    {
        _group = group;
    }

    public void SetImmediate(float alpha, bool interactive)
    {
        _fading = false;
        _completed = null;
        if (_group == null)
            return;
        _group.alpha = Mathf.Clamp01(alpha);
        _group.blocksRaycasts = interactive;
        _group.interactable = interactive;
    }

    public void FadeTo(float alpha, float duration, bool interactive, Action? completed = null)
    {
        if (_group == null)
        {
            completed?.Invoke();
            return;
        }

        _from = _group.alpha;
        _target = Mathf.Clamp01(alpha);
        _duration = Mathf.Max(0.001f, duration);
        _elapsed = 0f;
        _targetInteractive = interactive;
        _completed = completed;
        _fading = true;
        _group.blocksRaycasts = false;
        _group.interactable = false;
        if (Mathf.Abs(_from - _target) <= 0.001f)
            Finish();
    }

    private void Update()
    {
        if (!_fading || _group == null)
            return;
        _elapsed += Time.unscaledDeltaTime;
        var t = Mathf.Clamp01(_elapsed / _duration);
        t = t * t * (3f - 2f * t);
        _group.alpha = Mathf.Lerp(_from, _target, t);
        if (_elapsed >= _duration)
            Finish();
    }

    private void Finish()
    {
        _fading = false;
        if (_group != null)
        {
            _group.alpha = _target;
            _group.blocksRaycasts = _targetInteractive;
            _group.interactable = _targetInteractive;
        }
        var completed = _completed;
        _completed = null;
        completed?.Invoke();
    }
}

internal sealed class LGuiMultilineScrollbar : MonoBehaviour, IScrollHandler
{
    private LGuiSafeInputField? _input;
    private Text? _text;
    private RectTransform? _viewport;
    private Scrollbar? _scrollbar;
    private ScrollRect? _parentScrollRect;
    private bool _initialized;
    private bool _syncing;
    private bool _layoutPending;
    private bool _pendingMoveToBottom;
    private bool _directTextScrolling;
    private float _contentHeight;
    private float _viewportHeight;

    public bool StickToBottom { get; set; }

    public void Initialize(LGuiSafeInputField input, Text text, RectTransform viewport, Scrollbar scrollbar)
    {
        _input = input;
        _text = text;
        _viewport = viewport;
        _scrollbar = scrollbar;
        _directTextScrolling = input.readOnly;
        var parentScrolls = input.GetComponentsInParent<ScrollRect>(true);
        for (var i = 0; i < parentScrolls.Length; i++)
        {
            if (parentScrolls[i] != null && parentScrolls[i].transform != input.transform)
            {
                _parentScrollRect = parentScrolls[i];
                break;
            }
        }
        _input.onValueChanged.AddListener(OnTextChanged);
        _scrollbar.onValueChanged.AddListener(OnScrollChanged);
        _initialized = true;
        QueueLayoutRefresh(false);
    }

    private void OnDestroy()
    {
        if (!_initialized)
            return;
        _input?.onValueChanged.RemoveListener(OnTextChanged);
        _scrollbar?.onValueChanged.RemoveListener(OnScrollChanged);
    }

    private void Start()
    {
        QueueLayoutRefresh(false);
    }

    private void OnTextChanged(string _)
    {
        QueueLayoutRefresh(StickToBottom);
    }

    private void LateUpdate()
    {
        if (!_layoutPending)
            return;
        var moveToBottom = _pendingMoveToBottom;
        _layoutPending = false;
        _pendingMoveToBottom = false;
        try
        {
            RefreshLayout(moveToBottom);
        }
        catch (InvalidOperationException)
        {
            QueueLayoutRefresh(moveToBottom);
        }
    }

    private void QueueLayoutRefresh(bool moveToBottom)
    {
        _layoutPending = true;
        _pendingMoveToBottom |= moveToBottom;
    }

    private void OnScrollChanged(float value)
    {
        if (_syncing || _input == null)
            return;

        if (_directTextScrolling)
        {
            ApplyDirectTextScroll(value);
            return;
        }

        var length = _input.text?.Length ?? 0;
        var position = Mathf.RoundToInt((1f - Mathf.Clamp01(value)) * length);
        if (position > 0 && position < length && char.IsLowSurrogate(_input.text[position]))
            position--;
        _input.SetCaretForScroll(position);
    }

    private void RefreshLayout(bool moveToBottom)
    {
        if (_input == null || _text == null || _viewport == null || _scrollbar == null)
            return;
        _viewportHeight = Mathf.Max(1f, _viewport.rect.height - 8f);
        _contentHeight = CalculateFullTextHeight();
        _scrollbar.size = Mathf.Clamp01(_viewportHeight / _contentHeight);
        _scrollbar.gameObject.SetActive(true);
        _scrollbar.interactable = _contentHeight > _viewportHeight + 0.5f;

        if (_directTextScrolling)
        {
            var textRect = _text.rectTransform;
            textRect.sizeDelta = new Vector2(textRect.sizeDelta.x, _contentHeight);
            _text.text = _input.text ?? "";

            var directNormalized = _contentHeight <= _viewportHeight + 0.5f
                ? 1f
                : Mathf.Clamp01(_scrollbar.value);
            if (moveToBottom)
                directNormalized = 0f;

            _syncing = true;
            _scrollbar.SetValueWithoutNotify(directNormalized);
            _syncing = false;
            ApplyDirectTextScroll(directNormalized);
            return;
        }

        var length = _input.text?.Length ?? 0;
        var normalized = length <= 0
            ? 1f
            : 1f - Mathf.Clamp01((float)_input.caretPosition / length);
        if (moveToBottom)
        {
            normalized = 0f;
            _input.SetCaretForScroll(length);
        }
        _syncing = true;
        _scrollbar.SetValueWithoutNotify(normalized);
        _syncing = false;
    }

    private void ApplyDirectTextScroll(float normalized)
    {
        if (_input == null || _text == null)
            return;

        var value = _input.text ?? "";
        if (!string.Equals(_text.text, value, StringComparison.Ordinal))
            _text.text = value;

        var range = Mathf.Max(0f, _contentHeight - _viewportHeight);
        var position = _text.rectTransform.anchoredPosition;
        position.y = (1f - Mathf.Clamp01(normalized)) * range;
        _text.rectTransform.anchoredPosition = position;
    }

    private float CalculateFullTextHeight()
    {
        if (_input == null || _text == null || _viewport == null)
            return 1f;

        var value = _input.text ?? "";
        if (value.Length == 0)
            return _viewportHeight;

        var width = Mathf.Max(1f, _viewport.rect.width - 4f);
        var settings = _text.GetGenerationSettings(new Vector2(width, 0f));
        var preferred = _text.cachedTextGeneratorForLayout.GetPreferredHeight(value, settings) / Mathf.Max(0.01f, _text.pixelsPerUnit);
        return Mathf.Max(_viewportHeight, preferred + 8f);
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (_scrollbar == null || _contentHeight <= _viewportHeight + 0.5f)
        {
            ForwardScrollToParent(eventData);
            return;
        }
        var current = _scrollbar.value;
        var next = Mathf.Clamp01(current + eventData.scrollDelta.y * 0.08f);
        if (Mathf.Abs(next - current) <= 0.0001f)
        {
            ForwardScrollToParent(eventData);
            return;
        }
        _scrollbar.value = next;
        eventData.Use();
    }

    private void ForwardScrollToParent(PointerEventData eventData)
    {
        if (_parentScrollRect == null)
            return;
        _parentScrollRect.OnScroll(eventData);
        eventData.Use();
    }
}

internal sealed class LGuiScrollableTextBox : MonoBehaviour
{
    private const int MaxChunkLength = 6000;

    private readonly List<Text> _chunks = new List<Text>();
    private Text? _template;
    private RectTransform? _content;
    private RectTransform? _viewport;
    private ScrollRect? _scroll;
    private bool _layoutPending;
    private float _preservedPosition = 1f;
    private string _value = "";

    public bool StickToBottom { get; set; }
    public string Text => _value;

    public void Initialize(Text text, RectTransform content, RectTransform viewport, ScrollRect scroll)
    {
        _template = text;
        _content = content;
        _viewport = viewport;
        _scroll = scroll;
        _chunks.Add(text);
        text.text = "";
        _layoutPending = true;
    }

    public void SetText(string? value)
    {
        if (_template == null || _content == null || _scroll == null)
            return;
        value ??= "";
        if (string.Equals(_value, value, StringComparison.Ordinal))
            return;
        _preservedPosition = _scroll.verticalNormalizedPosition;
        _value = value;
        ApplyChunks(value);
        _layoutPending = true;
    }

    private void ApplyChunks(string value)
    {
        var parts = SplitIntoChunks(value);
        EnsureChunkCount(Math.Max(1, parts.Count));
        for (var i = 0; i < _chunks.Count; i++)
        {
            var active = i < parts.Count;
            _chunks[i].gameObject.SetActive(active);
            _chunks[i].text = active ? parts[i] : "";
        }
    }

    private void EnsureChunkCount(int count)
    {
        if (_template == null || _content == null)
            return;
        while (_chunks.Count < count)
        {
            var cloneObject = UnityEngine.Object.Instantiate(_template.gameObject, _content, false);
            cloneObject.name = "TextChunk" + _chunks.Count;
            var clone = cloneObject.GetComponent<Text>();
            clone.text = "";
            clone.raycastTarget = false;
            _chunks.Add(clone);
        }
    }

    private static List<string> SplitIntoChunks(string value)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            result.Add("");
            return result;
        }

        var offset = 0;
        while (offset < value.Length)
        {
            var length = Math.Min(MaxChunkLength, value.Length - offset);
            if (offset + length < value.Length)
            {
                var newline = value.LastIndexOf('\n', offset + length - 1, length);
                if (newline >= offset + MaxChunkLength / 2)
                    length = newline - offset + 1;
                if (offset + length < value.Length && length > 0 && char.IsHighSurrogate(value[offset + length - 1]))
                    length--;
            }
            if (length <= 0)
                length = Math.Min(MaxChunkLength, value.Length - offset);
            result.Add(value.Substring(offset, length));
            offset += length;
        }
        return result;
    }

    private void LateUpdate()
    {
        if (!_layoutPending)
            return;
        RefreshLayout();
    }

    private void RefreshLayout()
    {
        if (_template == null || _content == null || _viewport == null || _scroll == null)
            return;
        var viewportHeight = Mathf.Max(1f, _viewport.rect.height);
        var offset = 0f;
        for (var i = 0; i < _chunks.Count; i++)
        {
            var chunk = _chunks[i];
            if (!chunk.gameObject.activeSelf)
                continue;
            var chunkRect = chunk.rectTransform;
            chunkRect.anchorMin = new Vector2(0f, 1f);
            chunkRect.anchorMax = new Vector2(1f, 1f);
            chunkRect.pivot = new Vector2(0.5f, 1f);
            chunkRect.anchoredPosition = new Vector2(0f, -offset);
            chunkRect.sizeDelta = new Vector2(0f, 1f);
            var chunkHeight = Mathf.Max(chunk.fontSize + 4f, chunk.preferredHeight + 2f);
            chunkRect.sizeDelta = new Vector2(0f, chunkHeight);
            offset += chunkHeight;
        }
        var contentHeight = Mathf.Max(viewportHeight, offset + 6f);
        _content.sizeDelta = new Vector2(_content.sizeDelta.x, contentHeight);
        _scroll.StopMovement();
        _scroll.verticalNormalizedPosition = StickToBottom ? 0f : Mathf.Clamp01(_preservedPosition);
        _layoutPending = false;
    }
}

internal sealed class LGuiRowView : MonoBehaviour
{
    public RectTransform Rect = null!;
    public Image Background = null!;
    public Image Accent = null!;
    public Image Separator = null!;
    public Image Icon = null!;
    public Text Label = null!;
    public Text Secondary = null!;
    public InputField Input = null!;
    public Toggle Toggle = null!;
    public Text ToggleLabel = null!;
    public Button Primary = null!;
    public Text PrimaryText = null!;
    public Button Auxiliary = null!;
    public Text AuxiliaryText = null!;
    public Dropdown? Dropdown;
    public Button[] Choices = Array.Empty<Button>();
    public Text[] ChoiceTexts = Array.Empty<Text>();
    public object? BoundData;
    public int BoundIndex = -1;

    private ILGuiRowHandler? _handler;
    private bool _binding;

    public void Initialize(ILGuiRowHandler handler)
    {
        _handler = handler;
        Primary.onClick.AddListener(HandlePrimary);
        Auxiliary.onClick.AddListener(HandleAuxiliary);
        Toggle.onValueChanged.AddListener(HandleToggle);
        Input.onValueChanged.AddListener(HandleInput);
        Input.onEndEdit.AddListener(HandleInputCommit);
        for (var i = 0; i < Choices.Length; i++)
        {
            var choiceIndex = i;
            Choices[i].onClick.AddListener(() => HandleChoice(choiceIndex));
        }
    }

    public void BeginBind()
    {
        _binding = true;
    }

    public void EndBind()
    {
        _binding = false;
    }

    public void SetToggleWithoutNotify(bool value)
    {
        var wasBinding = _binding;
        _binding = true;
        Toggle.SetIsOnWithoutNotify(value);
        _binding = wasBinding;
    }

    public void SetInputWithoutNotify(string value)
    {
        var wasBinding = _binding;
        _binding = true;
        if (Input is LGuiSafeInputField safeInput)
            safeInput.SetTextSafely(value ?? "");
        else
            Input.SetTextWithoutNotify(value ?? "");
        _binding = wasBinding;
    }

    public void SetDropdownWithoutNotify(IReadOnlyList<string> options, int selectedIndex)
    {
        if (Dropdown == null)
            return;

        var wasBinding = _binding;
        _binding = true;
        Dropdown.options.Clear();
        for (var i = 0; i < options.Count; i++)
            Dropdown.options.Add(new Dropdown.OptionData(options[i] ?? ""));
        if (Dropdown.options.Count > 0)
            Dropdown.SetValueWithoutNotify(Mathf.Clamp(selectedIndex, 0, Dropdown.options.Count - 1));
        else
            Dropdown.SetValueWithoutNotify(0);
        Dropdown.RefreshShownValue();
        _binding = wasBinding;
    }

    public void HandleDropdownSelection(int optionIndex)
    {
        if (!_binding)
            _handler?.OnLGuiRowDropdown(this, optionIndex);
    }

    private void HandlePrimary()
    {
        if (!_binding)
            _handler?.OnLGuiRowPrimary(this);
    }

    private void HandleToggle(bool value)
    {
        if (!_binding)
            _handler?.OnLGuiRowToggle(this, value);
    }

    private void HandleAuxiliary()
    {
        if (!_binding)
            _handler?.OnLGuiRowAuxiliary(this);
    }

    private void HandleChoice(int choiceIndex)
    {
        if (!_binding)
            _handler?.OnLGuiRowChoice(this, choiceIndex);
    }

    private void HandleInput(string value)
    {
        if (!_binding)
            _handler?.OnLGuiRowInput(this, value);
    }

    private void HandleInputCommit(string value)
    {
        if (!_binding)
            _handler?.OnLGuiRowInputCommit(this, value);
    }
}
