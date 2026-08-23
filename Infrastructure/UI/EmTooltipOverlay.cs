using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed class EmTooltipContent
{
    internal EmTooltipContent(string title, Sprite? icon, string description)
    {
        Title = title ?? "";
        Icon = icon;
        Description = description ?? "";
    }

    internal string Title { get; }
    internal string TitleSuffix { get; set; } = "";
    internal string DescriptionHeader { get; set; } = "";
    internal int BaseFontSize { get; set; } = 13;
    internal Sprite? Icon { get; }
    internal string Description { get; }
    internal bool UsePlainTextColors { get; set; }
    internal List<EmTooltipLine> Lines { get; } = new List<EmTooltipLine>();
    internal List<EmTooltipFooterItem> FooterItems { get; } = new List<EmTooltipFooterItem>();
}

internal readonly struct EmTooltipLine
{
    internal EmTooltipLine(string text, Sprite? icon = null, Color? color = null)
    {
        Prefix = "";
        Text = text ?? "";
        Icon = icon;
        Color = color ?? new Color(0.92f, 0.94f, 0.96f, 1f);
    }

    internal EmTooltipLine(string prefix, string text, Sprite? icon, Color? color = null)
    {
        Prefix = prefix ?? "";
        Text = text ?? "";
        Icon = icon;
        Color = color ?? new Color(0.92f, 0.94f, 0.96f, 1f);
    }

    internal string Prefix { get; }
    internal string Text { get; }
    internal Sprite? Icon { get; }
    internal Color Color { get; }
}

internal readonly struct EmTooltipFooterItem
{
    internal EmTooltipFooterItem(string text, Sprite? icon, Color? color = null)
    {
        Text = text ?? "";
        Icon = icon;
        Color = color ?? new Color(0.78f, 0.94f, 0.82f, 1f);
    }

    internal string Text { get; }
    internal Sprite? Icon { get; }
    internal Color Color { get; }
}

internal readonly struct EmTooltipVisualStyle
{
    internal EmTooltipVisualStyle(
        bool roundedCorners,
        Sprite? backgroundSprite,
        Color backgroundColor,
        Color textColor,
        Color accentColor,
        bool lightTheme)
    {
        RoundedCorners = roundedCorners;
        BackgroundSprite = backgroundSprite;
        BackgroundColor = backgroundColor;
        TextColor = textColor;
        AccentColor = accentColor;
        LightTheme = lightTheme;
        HasPalette = true;
    }

    internal bool RoundedCorners { get; }
    internal Sprite? BackgroundSprite { get; }
    internal Color BackgroundColor { get; }
    internal Color TextColor { get; }
    internal Color AccentColor { get; }
    internal bool LightTheme { get; }
    internal bool HasPalette { get; }

    internal Color ResolveTitleColor()
    {
        if (!HasPalette)
            return new Color(0.78f, 0.94f, 0.91f, 1f);
        return LightTheme
            ? Color.Lerp(TextColor, AccentColor, 0.22f)
            : Color.Lerp(TextColor, Color.white, 0.10f);
    }

    internal Color ResolveDescriptionColor()
    {
        if (!HasPalette)
            return new Color(0.84f, 0.86f, 0.89f, 1f);
        return LightTheme
            ? Color.Lerp(TextColor, AccentColor, 0.12f)
            : Color.Lerp(TextColor, Color.white, 0.04f);
    }

    internal Color ResolveContentColor(Color requested, bool plain)
    {
        if (!HasPalette)
            return plain ? Color.white : requested;
        if (plain)
            return TextColor;
        return LightTheme
            ? Color.Lerp(TextColor, requested, 0.18f)
            : Color.Lerp(requested, AccentColor, 0.08f);
    }
}

internal sealed class EmTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private EmTooltipContent? _content;
    private Font? _font;
    private EmTooltipOverlay? _overlay;
    private Func<EmTooltipVisualStyle>? _styleProvider;

    internal void Initialize(
        EmTooltipContent content,
        Font? font,
        Func<EmTooltipVisualStyle>? styleProvider = null)
    {
        _content = content;
        _font = font;
        _styleProvider = styleProvider;
    }

    internal EmTooltipVisualStyle ResolveVisualStyle()
    {
        try { return _styleProvider?.Invoke() ?? default; }
        catch { return default; }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_content == null)
            return;
        _overlay = EmTooltipOverlay.GetOrCreate(this, _font);
        _overlay?.Show(this, _content);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _overlay?.Hide(this);
    }

    private void OnDisable()
    {
        _overlay?.Hide(this);
    }

    private void OnDestroy()
    {
        _overlay?.Hide(this);
    }
}

internal sealed class EmTooltipOverlay : MonoBehaviour
{
    private const float Width = 460f;
    private const float Margin = 14f;
    private const float FadeInSeconds = 0.14f;
    private const float FadeOutSeconds = 0.12f;
    private Canvas? _canvas;
    private RectTransform? _bounds;
    private RectTransform? _panel;
    private CanvasGroup? _group;
    private Image? _background;
    private Font? _font;
    private EmTooltipTarget? _owner;
    private float _fadeFrom;
    private float _fadeTo;
    private float _fadeElapsed;
    private float _fadeDuration;
    private bool _fading;
    private int _transition;

    internal static EmTooltipOverlay? GetOrCreate(Component target, Font? font)
    {
        Canvas? canvas;
        try
        {
            canvas = target.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.rootCanvas != null)
                canvas = canvas.rootCanvas;
        }
        catch
        {
            canvas = null;
        }
        if (canvas == null)
            return null;
        var overlay = canvas.GetComponent<EmTooltipOverlay>();
        if (overlay == null)
            overlay = canvas.gameObject.AddComponent<EmTooltipOverlay>();
        overlay.EnsureView(canvas, font);
        return overlay;
    }

    internal void Show(EmTooltipTarget owner, EmTooltipContent content)
    {
        if (_panel == null || _group == null || _font == null)
            return;
        _owner = owner;
        _transition++;
        _panel.gameObject.SetActive(true);
        var style = owner.ResolveVisualStyle();
        ApplyVisualStyle(style);
        ClearRows();
        BuildContent(content, style);
        _panel.SetAsLastSibling();
        UpdatePosition();
        BeginFade(1f, FadeInSeconds);
    }

    internal void Hide(EmTooltipTarget owner)
    {
        if (!ReferenceEquals(_owner, owner) || _panel == null)
            return;
        _owner = null;
        var transition = ++_transition;
        BeginFade(0f, FadeOutSeconds, () =>
        {
            if (_panel != null && _owner == null && transition == _transition)
                _panel.gameObject.SetActive(false);
        });
    }

    private void EnsureView(Canvas canvas, Font? font)
    {
        _canvas = canvas;
        _bounds = canvas.transform as RectTransform;
        _font = font ?? GameUiFontResolver.ResolveCurrentUiFont() ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_panel != null)
            return;
        var panelObject = new GameObject("ElinModifier.EmTooltip", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        _panel = panelObject.GetComponent<RectTransform>();
        _panel.SetParent(canvas.transform, false);
        _panel.anchorMin = new Vector2(0f, 1f);
        _panel.anchorMax = new Vector2(0f, 1f);
        _panel.pivot = new Vector2(0f, 1f);
        _panel.sizeDelta = new Vector2(Width, 100f);
        _background = panelObject.GetComponent<Image>();
        _background.color = new Color(0.025f, 0.03f, 0.04f, 0.98f);
        _background.raycastTarget = false;
        _group = panelObject.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;
        panelObject.SetActive(false);
    }

    private void BuildContent(EmTooltipContent content, EmTooltipVisualStyle style)
    {
        if (_panel == null || _font == null)
            return;
        var width = _bounds == null ? Width : Mathf.Clamp(_bounds.rect.width - 24f, 260f, Width);
        var baseFontSize = Mathf.Clamp(content.BaseFontSize, 1, 60);
        var titleFontSize = Mathf.Clamp(baseFontSize + 4, 1, 60);
        var descriptionFontSize = Mathf.Clamp(baseFontSize + 1, 1, 60);
        var footerFontSize = Mathf.Clamp(baseFontSize + 3, 1, 60);
        var y = Margin;
        var headerHeight = content.Icon != null
            ? Mathf.Max(48f, titleFontSize + 10f)
            : Mathf.Max(28f, titleFontSize + 10f);
        var titleColor = content.UsePlainTextColors
            ? style.ResolveContentColor(Color.white, true)
            : style.ResolveTitleColor();
        var descriptionColor = content.UsePlainTextColors
            ? style.ResolveContentColor(Color.white, true)
            : style.ResolveDescriptionColor();
        if (content.Icon != null)
            CreateImage("HeaderIcon", content.Icon, Margin, y, 48f, 48f);
        var titleX = content.Icon != null ? 74f : Margin;
        var titleValue = content.Title;
        if (!string.IsNullOrWhiteSpace(content.TitleSuffix))
            titleValue += "  <size=" + baseFontSize + ">" + content.TitleSuffix + "</size>";
        var title = CreateText("Title", titleValue, titleFontSize, FontStyle.Normal, titleColor);
        headerHeight = Mathf.Max(
            headerHeight,
            Measure(title, width - titleX - Margin, titleFontSize + 10f, 180f));
        SetTopLeft(title.rectTransform, titleX, y, width - titleX - Margin, headerHeight);
        y += headerHeight + 10f;
        if (!string.IsNullOrWhiteSpace(content.DescriptionHeader))
        {
            var descriptionHeader = CreateText(
                "DescriptionHeader",
                content.DescriptionHeader,
                descriptionFontSize,
                FontStyle.Normal,
                descriptionColor);
            var height = Measure(
                descriptionHeader,
                width - Margin * 2f,
                Mathf.Max(22f, descriptionFontSize + 8f),
                180f);
            SetTopLeft(descriptionHeader.rectTransform, Margin, y, width - Margin * 2f, height);
            y += height + 10f;
        }
        if (!string.IsNullOrWhiteSpace(content.Description))
        {
            var description = CreateText("Description", content.Description, descriptionFontSize, FontStyle.Italic, descriptionColor);
            var height = Measure(description, width - Margin * 2f, Mathf.Max(22f, descriptionFontSize + 8f), 180f);
            SetTopLeft(description.rectTransform, Margin, y, width - Margin * 2f, height);
            y += height + 10f;
        }
        for (var i = 0; i < content.Lines.Count; i++)
        {
            var line = content.Lines[i];
            if (string.IsNullOrWhiteSpace(line.Text))
                continue;
            var lineColor = style.ResolveContentColor(line.Color, content.UsePlainTextColors);
            if (line.Icon != null && !string.IsNullOrWhiteSpace(line.Prefix))
            {
                var prefix = CreateText("LinePrefix" + i, line.Prefix, baseFontSize, FontStyle.Normal, lineColor);
                prefix.alignment = TextAnchor.MiddleLeft;
                SetTopLeft(prefix.rectTransform, Margin, y, width - Margin * 2f, 24f);
                LayoutRebuilder.ForceRebuildLayoutImmediate(prefix.rectTransform);
                var prefixWidth = Mathf.Clamp(Mathf.Ceil(prefix.preferredWidth), 1f, width - Margin * 2f - 52f);
                var iconX = Margin + prefixWidth + 7f;
                var inlineTextX = iconX + 27f;
                var inlineTextWidth = width - inlineTextX - Margin;
                var inlineText = CreateText("Line" + i, line.Text, baseFontSize, FontStyle.Normal, lineColor);
                inlineText.alignment = TextAnchor.MiddleLeft;
                var inlineHeight = Measure(inlineText, inlineTextWidth, Mathf.Max(24f, baseFontSize + 8f), 160f);
                SetTopLeft(prefix.rectTransform, Margin, y, prefixWidth, inlineHeight);
                CreateImage("LineIcon" + i, line.Icon, iconX, y + Mathf.Max(0f, (inlineHeight - 22f) * 0.5f), 22f, 22f);
                SetTopLeft(inlineText.rectTransform, inlineTextX, y, inlineTextWidth, inlineHeight);
                y += inlineHeight + 5f;
                continue;
            }
            var hasIcon = line.Icon != null;
            var textX = hasIcon ? 44f : Margin;
            var textWidth = width - textX - Margin;
            var text = CreateText("Line" + i, line.Text, baseFontSize, FontStyle.Normal, lineColor);
            var height = Measure(text, textWidth, Mathf.Max(24f, baseFontSize + 8f), 160f);
            if (hasIcon)
                CreateImage("LineIcon" + i, line.Icon!, Margin, y + Mathf.Max(0f, (height - 22f) * 0.5f), 22f, 22f);
            SetTopLeft(text.rectTransform, textX, y, textWidth, height);
            y += height + 5f;
        }
        var footerItems = content.FooterItems.FindAll(item =>
            item.Icon != null && !string.IsNullOrWhiteSpace(item.Text));
        if (footerItems.Count > 0)
        {
            y += 3f;
            var footerHeight = Mathf.Max(24f, footerFontSize + 8f);
            var texts = new List<Text>(footerItems.Count);
            var itemWidths = new float[footerItems.Count];
            var totalWidth = 0f;
            for (var i = 0; i < footerItems.Count; i++)
            {
                var item = footerItems[i];
                var footerColor = style.ResolveContentColor(item.Color, content.UsePlainTextColors);
                var text = CreateText("FooterText" + i, item.Text, footerFontSize, FontStyle.Normal, footerColor);
                text.alignment = TextAnchor.MiddleLeft;
                SetTopLeft(text.rectTransform, 0f, 0f, 88f, footerHeight);
                LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
                var textWidth = Mathf.Clamp(Mathf.Ceil(text.preferredWidth), 12f, 88f);
                var itemWidth = 29f + textWidth;
                texts.Add(text);
                itemWidths[i] = itemWidth;
                totalWidth += itemWidth;
            }
            totalWidth += Mathf.Max(0, footerItems.Count - 1) * 12f;
            var x = Mathf.Max(Margin, width - Margin - totalWidth);
            for (var i = 0; i < footerItems.Count; i++)
            {
                var item = footerItems[i];
                CreateImage("FooterIcon" + i, item.Icon!, x, y + Mathf.Max(0f, (footerHeight - 24f) * 0.5f), 24f, 24f);
                SetTopLeft(texts[i].rectTransform, x + 29f, y, itemWidths[i] - 29f, footerHeight);
                x += itemWidths[i] + 12f;
            }
            y += footerHeight + 5f;
        }
        _panel.sizeDelta = new Vector2(width, y + Margin - 5f);
    }

    private Text CreateText(string name, string value, int size, FontStyle style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(_panel, false);
        var text = go.GetComponent<Text>();
        text.font = _font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = value ?? "";
        return text;
    }

    private void CreateImage(string name, Sprite sprite, float x, float y, float width, float height)
    {
        if (_panel == null)
            return;
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(_panel, false);
        SetTopLeft(rect, x, y, width, height);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static float Measure(Text text, float width, float minimum, float maximum)
    {
        SetTopLeft(text.rectTransform, 0f, 0f, width, maximum);
        LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
        return Mathf.Clamp(Mathf.Ceil(text.preferredHeight), minimum, maximum);
    }

    private void ClearRows()
    {
        if (_panel == null)
            return;
        for (var i = _panel.childCount - 1; i >= 0; i--)
        {
            var child = _panel.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }

    private void BeginFade(float target, float duration, Action? completed = null)
    {
        if (_group == null)
        {
            completed?.Invoke();
            return;
        }
        _fadeFrom = _group.alpha;
        _fadeTo = Mathf.Clamp01(target);
        _fadeDuration = Mathf.Max(0.001f, duration);
        _fadeElapsed = 0f;
        _fading = true;
        _fadeCompleted = completed;
    }

    private Action? _fadeCompleted;

    private void Update()
    {
        if (!_fading || _group == null)
            return;
        _fadeElapsed += Time.unscaledDeltaTime;
        var t = Mathf.Clamp01(_fadeElapsed / _fadeDuration);
        t = t * t * (3f - 2f * t);
        _group.alpha = Mathf.Lerp(_fadeFrom, _fadeTo, t);
        if (_fadeElapsed < _fadeDuration)
            return;
        _fading = false;
        _group.alpha = _fadeTo;
        var completed = _fadeCompleted;
        _fadeCompleted = null;
        completed?.Invoke();
    }

    private void LateUpdate()
    {
        if (_owner != null)
        {
            ApplyVisualStyle(_owner.ResolveVisualStyle());
            UpdatePosition();
        }
    }

    private void ApplyVisualStyle(EmTooltipVisualStyle style)
    {
        if (_background == null)
            return;
        _background.color = style.HasPalette
            ? style.BackgroundColor
            : new Color(0.025f, 0.03f, 0.04f, 0.98f);
        if (style.RoundedCorners && style.BackgroundSprite != null)
        {
            _background.sprite = style.BackgroundSprite;
            _background.type = Image.Type.Sliced;
            _background.fillCenter = true;
            return;
        }
        _background.sprite = null;
        _background.type = Image.Type.Simple;
        _background.fillCenter = true;
    }

    private void UpdatePosition()
    {
        if (_bounds == null || _panel == null)
            return;
        var camera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_bounds, Input.mousePosition, camera, out var cursor))
            return;
        var bounds = _bounds.rect;
        var width = _panel.rect.width;
        var height = _panel.rect.height;
        var x = cursor.x + 22f;
        var y = cursor.y - 18f;
        if (x + width > bounds.xMax - 12f)
            x = cursor.x - width - 22f;
        if (y - height < bounds.yMin + 12f)
            y = cursor.y + height + 18f;
        x = Mathf.Clamp(x, bounds.xMin + 12f, bounds.xMax - width - 12f);
        y = Mathf.Clamp(y, bounds.yMin + height + 12f, bounds.yMax - 12f);
        _panel.localPosition = new Vector3(x, y, 0f);
    }

    private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
