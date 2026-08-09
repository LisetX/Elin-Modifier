using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private static bool LGuiFilterMatches(string first, string second, string third, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        return (!string.IsNullOrEmpty(first) && first.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (!string.IsNullOrEmpty(second) && second.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (!string.IsNullOrEmpty(third) && third.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private void EnsureLGuiEventSystem()
    {
        var current = EventSystem.current;
        if (current != null)
        {
            if (_lGuiOwnedEventSystem != null && current.gameObject != _lGuiOwnedEventSystem)
            {
                UnityEngine.Object.Destroy(_lGuiOwnedEventSystem);
                _lGuiOwnedEventSystem = null;
            }
            return;
        }

        if (_lGuiOwnedEventSystem != null)
        {
            if (!_lGuiOwnedEventSystem.activeSelf)
                _lGuiOwnedEventSystem.SetActive(true);
            return;
        }
        _lGuiOwnedEventSystem = new GameObject("ElinModifier.EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        UnityEngine.Object.DontDestroyOnLoad(_lGuiOwnedEventSystem);
    }
    private void SetLGuiOwnedEventSystemActive(bool active)
    {
        if (_lGuiOwnedEventSystem != null && _lGuiOwnedEventSystem.activeSelf != active)
            _lGuiOwnedEventSystem.SetActive(active);
    }
    private Font FindLGuiFont()
    {
        var gameUiFont = GameUiFontResolver.ResolveCurrentUiFont();
        if (gameUiFont != null)
            return gameUiFont;

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
    private void RefreshLGuiFontIfNeeded(bool force)
    {
        var now = Time.realtimeSinceStartup;
        if (!force && now < _lGuiNextFontRefreshAt)
            return;

        _lGuiNextFontRefreshAt = now + 1f;
        var selectedFont = GameUiFontResolver.ResolveCurrentUiFont();
        if (selectedFont == null || selectedFont == _lGuiFont)
            return;

        _lGuiFont = selectedFont;
        ApplyLGuiFontToHierarchy(_lGuiRoot, selectedFont);
        _modules.Watermark.RefreshFont(selectedFont);
        _modules.ThreatOverlay.RefreshFont(selectedFont);
    }
    private static void ApplyLGuiFontToHierarchy(GameObject? root, Font font)
    {
        if (root == null || font == null)
            return;

        var texts = root.GetComponentsInChildren<Text>(true);
        for (var i = 0; i < texts.Length; i++)
            if (texts[i] != null)
                texts[i].font = font;
    }
    private RectTransform CreateLGuiRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
    private Text CreateLGuiText(Transform parent, string name, string value, int size, TextAnchor anchor, FontStyle style)
    {
        var rect = CreateLGuiRect(parent, name);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = _lGuiFont;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = Color.white;
        text.text = value ?? "";
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        var profile = rect.gameObject.AddComponent<LGuiTextProfile>();
        profile.BaseFontSize = Math.Max(1, size);
        return text;
    }
    private Image CreateLGuiImage(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = CreateLGuiRect(parent, name);
        PlaceLGuiRect(rect, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.white;
        return image;
    }
    private Button CreateLGuiButton(Transform parent, string name, string label, float x, float y, float width, float height, Action? action)
    {
        var rect = CreateLGuiRect(parent, name);
        PlaceLGuiRect(rect, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.18f, 0.2f, 0.23f, 1f);
        RegisterLGuiRoundedImage(image);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        if (action != null)
            button.onClick.AddListener(() => action());
        var text = CreateLGuiText(rect, "Text", label, 17, TextAnchor.MiddleCenter, FontStyle.Normal);
        StretchLGuiRect(text.rectTransform, 4f, 2f, 4f, 2f);
        return button;
    }
    private InputField CreateLGuiInput(Transform parent, string name, string placeholder, float x, float y, float width, float height)
    {
        var rect = CreateLGuiRect(parent, name);
        PlaceLGuiRect(rect, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.035f, 0.04f, 0.047f, 1f);
        RegisterLGuiRoundedImage(image);
        var input = rect.gameObject.AddComponent<LGuiSafeInputField>();
        input.targetGraphic = image;
        input.lineType = InputField.LineType.SingleLine;
        var text = CreateLGuiText(rect, "Text", "", 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        text.supportRichText = false;
        StretchLGuiRect(text.rectTransform, 10f, 3f, 10f, 3f);
        var hint = CreateLGuiText(rect, "Placeholder", placeholder, 16, TextAnchor.MiddleLeft, FontStyle.Italic);
        hint.color = new Color(0.58f, 0.6f, 0.64f, 1f);
        StretchLGuiRect(hint.rectTransform, 10f, 3f, 10f, 3f);
        input.textComponent = text;
        input.EnableSafeLabelUpdates();
        input.placeholder = hint;
        _modules.LGuiFocus.Bind(input);
        return input;
    }
    private InputField CreateLGuiMultilineInput(Transform parent, string name, float x, float y, float width, float height, bool readOnly = false)
    {
        var rect = CreateLGuiRect(parent, name);
        PlaceLGuiRect(rect, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.035f, 0.04f, 0.047f, 1f);
        RegisterLGuiRoundedImage(image);
        var input = rect.gameObject.AddComponent<LGuiSafeInputField>();
        input.targetGraphic = image;
        input.contentType = InputField.ContentType.Standard;
        input.lineType = InputField.LineType.MultiLineNewline;
        input.readOnly = readOnly;

        var viewport = CreateLGuiRect(rect, "Viewport");
        StretchLGuiRect(viewport, 8f, 6f, 28f, 6f);
        var viewportMaskImage = viewport.gameObject.AddComponent<Image>();
        viewportMaskImage.color = Color.white;
        viewportMaskImage.raycastTarget = false;
        var viewportMask = viewport.gameObject.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;
        var text = CreateLGuiText(viewport, "Text", "", 17, TextAnchor.UpperLeft, FontStyle.Normal);
        text.rectTransform.anchorMin = new Vector2(0f, 1f);
        text.rectTransform.anchorMax = new Vector2(1f, 1f);
        text.rectTransform.pivot = new Vector2(0.5f, 1f);
        text.rectTransform.anchoredPosition = Vector2.zero;
        text.rectTransform.sizeDelta = new Vector2(0f, Math.Max(32f, height - 12f));
        text.resizeTextForBestFit = false;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var hint = CreateLGuiText(viewport, "Placeholder", "", 16, TextAnchor.UpperLeft, FontStyle.Italic);
        StretchLGuiRect(hint.rectTransform, 0f, 0f, 0f, 0f);
        hint.color = new Color(0.58f, 0.6f, 0.64f, 1f);
        input.textComponent = text;
        input.EnableSafeLabelUpdates();
        input.placeholder = hint;
        _modules.LGuiFocus.Bind(input);

        var scrollbarRect = CreateLGuiRect(rect, "Scrollbar");
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-18f, 28f);
        scrollbarRect.offsetMax = new Vector2(-3f, -28f);
        var scrollbarImage = scrollbarRect.gameObject.AddComponent<Image>();
        scrollbarImage.color = new Color(0.08f, 0.085f, 0.095f, 1f);
        RegisterLGuiRoundedImage(scrollbarImage, LGuiRoundedImageStyle.Capsule);
        var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.value = 1f;
        var sliding = CreateLGuiRect(scrollbarRect, "SlidingArea");
        StretchLGuiRect(sliding, 2f, 2f, 2f, 2f);
        var handle = CreateLGuiRect(sliding, "Handle");
        StretchLGuiRect(handle, 0f, 0f, 0f, 0f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.35f, 0.38f, 0.43f, 1f);
        RegisterLGuiRoundedImage(handleImage, LGuiRoundedImageStyle.Capsule);
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        var driver = rect.gameObject.AddComponent<LGuiMultilineScrollbar>();
        driver.Initialize(input, text, viewport, scrollbar);
        return input;
    }
    private LGuiScrollableTextBox CreateLGuiScrollableTextBox(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = CreateLGuiRect(parent, name);
        PlaceLGuiRect(rect, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.035f, 0.04f, 0.047f, 1f);
        RegisterLGuiRoundedImage(image);

        var scroll = rect.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 42f;

        var viewport = CreateLGuiRect(rect, "Viewport");
        StretchLGuiRect(viewport, 8f, 6f, 28f, 6f);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = CreateLGuiRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, Math.Max(1f, height - 12f));
        var text = CreateLGuiText(content, "Text", "", 17, TextAnchor.UpperLeft, FontStyle.Normal);
        StretchLGuiRect(text.rectTransform, 0f, 0f, 0f, 0f);
        text.resizeTextForBestFit = false;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var scrollbarRect = CreateLGuiRect(rect, "Scrollbar");
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-18f, 3f);
        scrollbarRect.offsetMax = new Vector2(-3f, -3f);
        var scrollbarImage = scrollbarRect.gameObject.AddComponent<Image>();
        scrollbarImage.color = new Color(0.08f, 0.085f, 0.095f, 1f);
        RegisterLGuiRoundedImage(scrollbarImage, LGuiRoundedImageStyle.Capsule);
        var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var sliding = CreateLGuiRect(scrollbarRect, "SlidingArea");
        StretchLGuiRect(sliding, 2f, 2f, 2f, 2f);
        var handle = CreateLGuiRect(sliding, "Handle");
        StretchLGuiRect(handle, 0f, 0f, 0f, 0f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.35f, 0.38f, 0.43f, 1f);
        RegisterLGuiRoundedImage(handleImage, LGuiRoundedImageStyle.Capsule);
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.verticalNormalizedPosition = 1f;

        var driver = rect.gameObject.AddComponent<LGuiScrollableTextBox>();
        driver.Initialize(text, content, viewport, scroll);
        return driver;
    }
    private Toggle CreateLGuiToggle(Transform parent, string name, float x, float y, float width, float height, out Text label)
    {
        var rect = CreateLGuiRect(parent, name);
        PlaceLGuiRect(rect, x, y, width, height);
        var toggle = rect.gameObject.AddComponent<Toggle>();
        toggle.toggleTransition = Toggle.ToggleTransition.None;
        var box = CreateLGuiImage(rect, "Box", 2f, 7f, 28f, 28f);
        box.color = new Color(0.16f, 0.18f, 0.21f, 1f);
        RegisterLGuiRoundedImage(box);
        var check = CreateLGuiImage(box.rectTransform, "Check", 5f, 5f, 18f, 18f);
        check.color = new Color(0.25f, 0.84f, 0.48f, 1f);
        RegisterLGuiRoundedImage(check);
        toggle.targetGraphic = box;
        toggle.graphic = check;
        label = CreateLGuiText(rect, "Label", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(label.rectTransform, 38f, 0f, Math.Max(20f, width - 38f), height);
        return toggle;
    }
    private Slider CreateLGuiSlider(Transform parent, string name, float x, float y, float width, float height, float min, float max, float value, float step = 0f)
    {
        var rect = CreateLGuiRect(parent, name);
        PlaceLGuiRect(rect, x, y, width, height);
        Slider slider;
        if (step > 0f)
        {
            var steppedSlider = rect.gameObject.AddComponent<LGuiSteppedSlider>();
            steppedSlider.StepSize = step;
            slider = steppedSlider;
        }
        else
        {
            slider = rect.gameObject.AddComponent<Slider>();
        }
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;

        var background = CreateLGuiRect(rect, "Background");
        StretchLGuiRect(background, 0f, 7f, 0f, 7f);
        var backgroundImage = background.gameObject.AddComponent<Image>();
        backgroundImage.color = new Color(0.10f, 0.11f, 0.13f, 1f);
        RegisterLGuiRoundedImage(backgroundImage, LGuiRoundedImageStyle.Capsule);

        var fillArea = CreateLGuiRect(rect, "FillArea");
        StretchLGuiRect(fillArea, 0f, 7f, 0f, 7f);
        var fill = CreateLGuiRect(fillArea, "Fill");
        StretchLGuiRect(fill, 0f, 0f, 0f, 0f);
        var fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = new Color(0.25f, 0.78f, 0.48f, 1f);
        RegisterLGuiRoundedImage(fillImage, LGuiRoundedImageStyle.Capsule);

        var handleArea = CreateLGuiRect(rect, "HandleArea");
        StretchLGuiRect(handleArea, 8f, 0f, 8f, 0f);
        var handle = CreateLGuiRect(handleArea, "Handle");
        handle.sizeDelta = new Vector2(20f, 20f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.88f, 0.9f, 0.94f, 1f);
        RegisterLGuiRoundedImage(handleImage, LGuiRoundedImageStyle.Capsule);

        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.value = Clamp(value, min, max);
        return slider;
    }
    private void CreateLGuiToggleControl(Transform parent, string label, bool value, float y, Action<bool> changed)
    {
        var toggle = CreateLGuiToggle(parent, "ToggleControl", 0f, y, 560f, 48f, out var text);
        text.text = label;
        toggle.isOn = value;
        toggle.onValueChanged.AddListener(next => changed(next));
    }
    private string GetLGuiNpcMoreInfoOrderLabel(string key)
    {
        switch (key)
        {
            case "level": return T("等级", "Level");
            case "identity": return T("身份信息", "Identity");
            case "relation": return T("更多身份信息", "Additional identity info");
            case "vitals": return T("状态", "Status");
            case "attributes": return T("主属性", "Main Attributes");
            case "buffs": return "Buff";
            case "resists": return T("抗性", "Resistances");
            case "skills": return T("技能", "Skills");
            case "abilities": return T("能力", "Abilities");
            case "feats": return T("专长", "Feats");
            case "combat": return T("交战推演", "Combat Simulation");
            default: return key;
        }
    }
    private void SetLGuiNpcMoreInfoOrderItemEnabled(string key, bool value)
    {
        switch (key)
        {
            case "level": _showNpcMoreInfoLevel = value; break;
            case "identity": _showNpcMoreInfoIdentity = value; break;
            case "relation": _showNpcMoreInfoRelationFaith = value; break;
            case "vitals": _showNpcMoreInfoVitals = value; break;
            case "attributes": _showNpcMoreInfoAttributes = value; break;
            case "buffs": _showNpcMoreInfoBuffs = value; break;
            case "resists": _showNpcMoreInfoResists = value; break;
            case "skills": _showNpcMoreInfoSkills = value; break;
            case "abilities": _showNpcMoreInfoAbilities = value; break;
            case "feats": _showNpcMoreInfoFeats = value; break;
            case "combat": _showNpcMoreInfoCombatSimulation = value; break;
        }
        InvalidateNpcMoreInfoCaches();
    }
    private static void AddLGuiEventTrigger(EventTrigger trigger, EventTriggerType eventType, Action<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = eventType };
        entry.callback.AddListener(data => action(data));
        trigger.triggers.Add(entry);
    }
    private void CreateLGuiNpcMoreInfoSortableRow(RectTransform content, string key, int index, float y)
    {
        const float rowHeight = 48f;
        const float rowStep = 58f;
        const float top = 10f;
        var row = CreateLGuiRect(content, "NpcMoreInfoSort_" + key);
        PlaceLGuiRect(row, 0f, y, 1180f, rowHeight);
        var rowImage = row.gameObject.AddComponent<Image>();
        rowImage.color = GetLGuiRowColor(index, false);
        rowImage.raycastTarget = false;
        RegisterLGuiRoundedImage(rowImage);

        var handle = CreateLGuiRect(row, "SortHandle");
        PlaceLGuiRect(handle, 0f, 0f, 48f, rowHeight);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.20f, 0.23f, 0.27f, 1f);
        RegisterLGuiRoundedImage(handleImage);
        var handleText = CreateLGuiText(handle, "HandleText", "≡", 24, TextAnchor.MiddleCenter, FontStyle.Normal);
        StretchLGuiRect(handleText.rectTransform, 0f, 0f, 0f, 0f);
        handleText.raycastTarget = false;

        var toggle = CreateLGuiToggle(row, "Toggle", 64f, 0f, 430f, rowHeight, out var label);
        label.text = GetLGuiNpcMoreInfoOrderLabel(key);
        toggle.isOn = IsNpcMoreInfoOrderItemEnabled(key);
        toggle.onValueChanged.AddListener(value => SetLGuiNpcMoreInfoOrderItemEnabled(key, value));

        var extraFontSizeLabel = CreateLGuiText(row, "ExtraFontSizeLabel", T("额外字体大小", "Extra font size"), 16, TextAnchor.MiddleRight, FontStyle.Normal);
        PlaceLGuiRect(extraFontSizeLabel.rectTransform, 500f, 0f, 160f, rowHeight);
        var extraFontSizeInput = CreateLGuiInput(row, "ExtraFontSizeInput", "", 674f, 4f, 76f, 40f);
        extraFontSizeInput.contentType = InputField.ContentType.IntegerNumber;
        extraFontSizeInput.characterLimit = 2;
        extraFontSizeInput.text = GetNpcMoreInfoExtraFontSize(key).ToString(CultureInfo.InvariantCulture);
        extraFontSizeInput.onEndEdit.AddListener(value =>
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                SetNpcMoreInfoExtraFontSize(key, parsed);
            extraFontSizeInput.text = GetNpcMoreInfoExtraFontSize(key).ToString(CultureInfo.InvariantCulture);
        });

        if (IsNpcMoreInfoMultiEntryKey(key))
        {
            var perLineLabel = CreateLGuiText(row, "PerLineLabel", T("每行最大数量", "Max per line"), 16, TextAnchor.MiddleRight, FontStyle.Normal);
            PlaceLGuiRect(perLineLabel.rectTransform, 770f, 0f, 170f, rowHeight);
            var perLineInput = CreateLGuiInput(row, "PerLineInput", "", 954f, 4f, 92f, 40f);
            perLineInput.contentType = InputField.ContentType.IntegerNumber;
            perLineInput.characterLimit = 2;
            perLineInput.text = GetNpcMoreInfoPerLine(key).ToString(CultureInfo.InvariantCulture);
            perLineInput.onEndEdit.AddListener(value =>
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    SetNpcMoreInfoPerLine(key, parsed);
                perLineInput.text = GetNpcMoreInfoPerLine(key).ToString(CultureInfo.InvariantCulture);
            });
        }

        var trigger = handle.gameObject.AddComponent<EventTrigger>();
        trigger.triggers = new List<EventTrigger.Entry>();
        var targetIndex = index;
        var scroll = content.GetComponentInParent<ScrollRect>();
        var scrollWasVertical = true;
        var canvasGroup = row.gameObject.AddComponent<CanvasGroup>();

        Action<BaseEventData> updateDrag = data =>
        {
            if (!(data is PointerEventData pointer) || _lGuiCanvas == null)
                return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, pointer.position, _lGuiCanvas.worldCamera, out var local))
                return;
            var dragY = Mathf.Clamp(-local.y - rowHeight * 0.5f, top, top + (NpcMoreInfoOrderKeys.Length - 1) * rowStep);
            targetIndex = Clamp(Mathf.RoundToInt((dragY - top) / rowStep), 0, NpcMoreInfoOrderKeys.Length - 1);
            row.anchoredPosition = new Vector2(0f, -dragY);
        };

        AddLGuiEventTrigger(trigger, EventTriggerType.BeginDrag, data =>
        {
            if (scroll != null)
            {
                scrollWasVertical = scroll.vertical;
                scroll.vertical = false;
                scroll.StopMovement();
            }
            row.SetAsLastSibling();
            canvasGroup.alpha = 0.9f;
            updateDrag(data);
        });
        AddLGuiEventTrigger(trigger, EventTriggerType.Drag, updateDrag);
        AddLGuiEventTrigger(trigger, EventTriggerType.EndDrag, data =>
        {
            updateDrag(data);
            if (scroll != null)
                scroll.vertical = scrollWasVertical;
            canvasGroup.alpha = 1f;
            MoveNpcMoreInfoOrderItem(key, targetIndex);
            OpenLGuiFeatureConfiguration(LGuiFeatureId.ShowNpcMoreInfo);
        });
    }
    private ScrollRect CreateLGuiScroll(RectTransform parent, string name, float top)
    {
        var root = CreateLGuiRect(parent, name);
        root.anchorMin = new Vector2(0f, 0f);
        root.anchorMax = new Vector2(1f, 1f);
        root.offsetMin = Vector2.zero;
        root.offsetMax = new Vector2(0f, -top);
        var rootImage = root.gameObject.AddComponent<Image>();
        rootImage.color = Color.clear;
        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 42f;

        var viewport = CreateLGuiRect(root, "Viewport");
        StretchLGuiRect(viewport, 0f, 0f, 21f, 0f);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = CreateLGuiRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        var scrollbarRect = CreateLGuiRect(root, "Scrollbar");
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-18f, 3f);
        scrollbarRect.offsetMax = new Vector2(-3f, -3f);
        var scrollbarImage = scrollbarRect.gameObject.AddComponent<Image>();
        scrollbarImage.color = new Color(0.08f, 0.085f, 0.095f, 1f);
        RegisterLGuiRoundedImage(scrollbarImage, LGuiRoundedImageStyle.Capsule);
        var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var sliding = CreateLGuiRect(scrollbarRect, "SlidingArea");
        StretchLGuiRect(sliding, 2f, 2f, 2f, 2f);
        var handle = CreateLGuiRect(sliding, "Handle");
        StretchLGuiRect(handle, 0f, 0f, 0f, 0f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.35f, 0.38f, 0.43f, 1f);
        RegisterLGuiRoundedImage(handleImage, LGuiRoundedImageStyle.Capsule);
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return scroll;
    }
    private void CreateLGuiNavButton(RectTransform parent, LGuiPage page, string label, float y)
    {
        var button = CreateLGuiButton(parent, "Nav_" + page, label, 15f, y, 220f, 48f, () => SwitchLGuiPage(page));
        _lGuiNavButtons[page] = button;
        var text = button.GetComponentInChildren<Text>(true);
        if (text != null)
            _lGuiNavLabels[page] = text;
    }
    private static void PlaceLGuiRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
    private static void AnchorLGuiTop(RectTransform rect, float top, float height, float left, float right)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }
    private static void StretchLGuiRect(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
    private static void DestroyLGuiChildren(RectTransform parent)
    {
        for (var i = parent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
    }
    private void UpdateLGuiImeMode()
    {
        try
        {
            if (EventSystem.current?.currentSelectedGameObject?.GetComponent<InputField>() != null)
                Input.imeCompositionMode = IMECompositionMode.On;
        }
        catch { }
    }
    private void RestoreLGuiImeMode()
    {
        try { Input.imeCompositionMode = IMECompositionMode.Auto; }
        catch { }
    }
}
