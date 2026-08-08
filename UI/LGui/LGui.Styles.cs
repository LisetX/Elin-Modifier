using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void ApplyLGuiVisualSettings()
    {
        if (!IsLGuiInitialized())
            return;

        if (_lGuiCanvasScaler != null)
        {
            _lGuiCanvasScaler.uiScaleMode = _adaptiveUiScale
                ? CanvasScaler.ScaleMode.ScaleWithScreenSize
                : CanvasScaler.ScaleMode.ConstantPixelSize;
            _lGuiCanvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _lGuiCanvasScaler.scaleFactor = _adaptiveUiScale ? 1f : GetCustomUiScaleFactor();
        }

        if (_lGuiWindowGroup != null)
        {
            var targetAlpha = _lGuiModalHidesMain ? 0f : Clamp(_uiAlpha, 0.2f, 1f);
            if (_lGuiModalHidesMain)
            {
                if (_lGuiWindowFade != null)
                    _lGuiWindowFade.SetImmediate(0f, false);
                else
                {
                    _lGuiWindowGroup.alpha = 0f;
                    _lGuiWindowGroup.blocksRaycasts = false;
                    _lGuiWindowGroup.interactable = false;
                }
            }
            else if (_lGuiWindowFade == null || !_lGuiWindowFade.IsFading)
            {
                _lGuiWindowGroup.alpha = targetAlpha;
                _lGuiWindowGroup.blocksRaycasts = true;
                _lGuiWindowGroup.interactable = true;
            }
        }
        if (_lGuiBlockerImage != null)
            _lGuiBlockerImage.raycastTarget = _forceGameUnfocus;

        var lightTheme = _uiStyleIndex == 5;
        var accent = _uiStyleIndex >= 0 && _uiStyleIndex < UiStyleColors.Length
            ? UiStyleColors[_uiStyleIndex]
            : Color.white;
        var windowColor = lightTheme
            ? new Color(0.88f, 0.88f, 0.85f, 1f)
            : Color.Lerp(new Color(0.035f, 0.04f, 0.047f, 1f), accent, 0.10f);
        var headerColor = lightTheme
            ? new Color(0.98f, 0.98f, 0.94f, 1f)
            : Color.Lerp(new Color(0.085f, 0.09f, 0.105f, 1f), accent, 0.20f);
        var sidebarColor = lightTheme
            ? new Color(0.78f, 0.79f, 0.77f, 1f)
            : Color.Lerp(new Color(0.055f, 0.06f, 0.072f, 1f), accent, 0.14f);
        var buttonColor = lightTheme
            ? new Color(0.72f, 0.74f, 0.72f, 1f)
            : Color.Lerp(new Color(0.13f, 0.145f, 0.17f, 1f), accent, 0.23f);
        var inputColor = lightTheme
            ? new Color(0.96f, 0.96f, 0.93f, 1f)
            : Color.Lerp(new Color(0.025f, 0.03f, 0.038f, 1f), accent, 0.08f);
        var modalBaseColor = lightTheme
            ? Color.Lerp(windowColor, headerColor, 0.22f)
            : Color.Lerp(windowColor, headerColor, 0.34f);
        var modalHeaderColor = lightTheme
            ? Color.Lerp(headerColor, accent, 0.06f)
            : Color.Lerp(headerColor, accent, 0.12f);
        var modalInputColor = lightTheme
            ? Color.Lerp(inputColor, accent, 0.035f)
            : Color.Lerp(inputColor, headerColor, 0.16f);
        ApplyLGuiWindowCornerStyle();
        ApplyLGuiModalCornerStyle();
        if (_lGuiEditorModal != null)
            ApplyLGuiModalChrome(modalHeaderColor, accent);
        ApplyLGuiRegisteredCornerStyles();
        ApplyLGuiSliderCornerGeometry();
        if (_lGuiWindowImage != null) _lGuiWindowImage.color = windowColor;
        if (_lGuiHeaderImage != null) _lGuiHeaderImage.color = headerColor;
        if (_lGuiSidebarImage != null) _lGuiSidebarImage.color = sidebarColor;
        if (_lGuiEditorModal != null)
        {
            var modalImage = _lGuiEditorModal.GetComponent<Image>();
            if (modalImage != null)
                modalImage.color = modalBaseColor;
        }

        var buttons = _lGuiRoot!.GetComponentsInChildren<Button>(true);
        for (var i = 0; i < buttons.Length; i++)
            if (buttons[i].targetGraphic is Image image)
                image.color = _lGuiEditorModal != null && buttons[i].transform.IsChildOf(_lGuiEditorModal.transform)
                    ? GetLGuiModalButtonColor(buttons[i], buttonColor, accent, lightTheme)
                    : buttonColor;

        var inputs = _lGuiRoot.GetComponentsInChildren<InputField>(true);
        for (var i = 0; i < inputs.Length; i++)
            if (inputs[i].targetGraphic is Image image)
                image.color = inputs[i].GetComponent<LGuiTransparentInputBackground>() != null
                    ? Color.clear
                    : _lGuiEditorModal != null && inputs[i].transform.IsChildOf(_lGuiEditorModal.transform)
                        ? modalInputColor
                        : inputColor;

        var scrolls = _lGuiRoot.GetComponentsInChildren<ScrollRect>(true);
        for (var i = 0; i < scrolls.Length; i++)
        {
            var image = scrolls[i].GetComponent<Image>();
            if (image != null)
                image.color = Color.clear;
        }

        var textColor = GetActiveUiTextColor();
        var fontScale = GetEffectiveUiFontSize() / (float)UiFontSizeDefault;
        var profiles = _lGuiRoot.GetComponentsInChildren<LGuiTextProfile>(true);
        for (var i = 0; i < profiles.Length; i++)
        {
            var text = profiles[i].GetComponent<Text>();
            if (text == null)
                continue;
            text.color = textColor;
            text.fontSize = Clamp(Mathf.RoundToInt(profiles[i].BaseFontSize * fontScale), 1, 60);
        }
        ApplyLGuiModalTextColors(textColor, accent, lightTheme);
        LayoutLGuiHeaderTexts();
        UpdateLGuiNavButtons();
        _lGuiFeatureList?.RefreshBoundRows();
        _lGuiCharacterList?.RefreshBoundRows();
        _lGuiItemList?.RefreshBoundRows();
        _lGuiNpcList?.RefreshBoundRows();
        _lGuiHomeList?.RefreshBoundRows();
        _modules.Probability.RefreshVisibleRows();
        _lGuiDebugList?.RefreshBoundRows();
        _lGuiEmpList?.RefreshBoundRows();
        ApplyWatermarkVisualSettings();
    }
    private void ApplyLGuiModalChrome(Color headerColor, Color accent)
    {
        if (_lGuiEditorModal == null)
            return;
        var modal = (RectTransform)_lGuiEditorModal.transform;
        var header = modal.Find("ModalChromeHeader") as RectTransform;
        if (header == null)
        {
            header = CreateLGuiRect(modal, "ModalChromeHeader");
            var headerImage = header.gameObject.AddComponent<Image>();
            headerImage.raycastTarget = false;
            RegisterLGuiRoundedImage(headerImage);
        }
        AnchorLGuiTop(header, 0f, 72f, 0f, 0f);
        header.SetAsFirstSibling();
        header.GetComponent<Image>().color = headerColor;

        var accentLine = modal.Find("ModalChromeAccent") as RectTransform;
        if (accentLine == null)
        {
            accentLine = CreateLGuiRect(modal, "ModalChromeAccent");
            var accentImage = accentLine.gameObject.AddComponent<Image>();
            accentImage.raycastTarget = false;
            RegisterLGuiRoundedImage(accentImage);
        }
        AnchorLGuiTop(accentLine, 70f, 3f, 0f, 0f);
        accentLine.SetSiblingIndex(Math.Min(1, modal.childCount - 1));
        accentLine.GetComponent<Image>().color = Color.Lerp(headerColor, accent, 0.55f);
    }
    private Color GetLGuiModalButtonColor(Button button, Color baseColor, Color accent, bool lightTheme)
    {
        var name = button.gameObject.name ?? "";
        var label = button.GetComponentInChildren<Text>(true)?.text ?? "";
        if (string.Equals(name, "Close", StringComparison.OrdinalIgnoreCase))
            return lightTheme ? new Color(0.78f, 0.59f, 0.60f, 1f) : new Color(0.28f, 0.10f, 0.12f, 1f);
        if (name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0 || label.IndexOf(T("删除", "Delete"), StringComparison.OrdinalIgnoreCase) >= 0)
            return lightTheme ? new Color(0.79f, 0.61f, 0.61f, 1f) : new Color(0.30f, 0.11f, 0.12f, 1f);
        if (name.IndexOf("Restore", StringComparison.OrdinalIgnoreCase) >= 0 || label.IndexOf(T("恢复", "Restore"), StringComparison.OrdinalIgnoreCase) >= 0)
            return lightTheme ? new Color(0.61f, 0.70f, 0.80f, 1f) : new Color(0.10f, 0.21f, 0.32f, 1f);
        if (name.IndexOf("Apply", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0)
            return lightTheme ? new Color(0.59f, 0.75f, 0.64f, 1f) : new Color(0.10f, 0.28f, 0.18f, 1f);
        if (label.TrimStart().StartsWith("→", StringComparison.Ordinal))
            return Color.Lerp(baseColor, accent, 0.46f);
        return Color.Lerp(baseColor, accent, 0.07f);
    }
    private void ApplyLGuiModalTextColors(Color textColor, Color accent, bool lightTheme)
    {
        if (_lGuiEditorModal == null)
            return;
        var texts = _lGuiEditorModal.GetComponentsInChildren<Text>(true);
        for (var i = 0; i < texts.Length; i++)
        {
            var name = texts[i].gameObject.name;
            if (string.Equals(name, "Title", StringComparison.Ordinal))
                texts[i].color = lightTheme ? Color.Lerp(textColor, accent, 0.28f) : Color.Lerp(textColor, Color.white, 0.12f);
            else if (string.Equals(name, "Section", StringComparison.Ordinal))
                texts[i].color = Color.Lerp(textColor, accent, lightTheme ? 0.34f : 0.28f);
        }
    }
    private void ApplyLGuiWindowCornerStyle()
    {
        if (_lGuiWindowImage == null || _lGuiWindowMask == null)
            return;

        if (!_uiRoundedCorners)
        {
            _lGuiWindowMask.enabled = false;
            _lGuiWindowImage.sprite = null;
            _lGuiWindowImage.type = Image.Type.Simple;
            return;
        }

        EnsureLGuiRoundedWindowSprite();
        if (_lGuiRoundedWindowSprite == null)
            return;
        _lGuiWindowImage.sprite = _lGuiRoundedWindowSprite;
        _lGuiWindowImage.type = Image.Type.Sliced;
        _lGuiWindowImage.fillCenter = true;
        _lGuiWindowMask.enabled = true;
    }
    private void ApplyLGuiModalCornerStyle()
    {
        if (_lGuiEditorModal == null)
            return;
        var image = _lGuiEditorModal.GetComponent<Image>();
        if (image == null)
            return;
        var mask = _lGuiEditorModal.GetComponent<Mask>();

        if (!_uiRoundedCorners)
        {
            if (mask != null)
                mask.enabled = false;
            image.sprite = null;
            image.type = Image.Type.Simple;
            return;
        }

        EnsureLGuiRoundedWindowSprite();
        if (_lGuiRoundedWindowSprite == null)
            return;
        if (mask == null)
            mask = _lGuiEditorModal.AddComponent<Mask>();
        mask.showMaskGraphic = true;
        image.sprite = _lGuiRoundedWindowSprite;
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
        mask.enabled = true;
    }
    private void RegisterLGuiRoundedImage(Image image, LGuiRoundedImageStyle style = LGuiRoundedImageStyle.Standard)
    {
        if (image == null)
            return;
        var target = image.GetComponent<LGuiRoundedImageTarget>();
        if (target == null)
            target = image.gameObject.AddComponent<LGuiRoundedImageTarget>();
        target.Style = style;
        target.Capture(image);
        ApplyLGuiRoundedImageStyle(target, image);
    }
    private void ApplyLGuiRegisteredCornerStyles()
    {
        if (_uiRoundedCorners)
            EnsureLGuiRoundedWindowSprite();

        ApplyLGuiRegisteredCornerStyles(_lGuiRoot);
    }
    private void ApplyLGuiRegisteredCornerStyles(GameObject? root)
    {
        if (root == null)
            return;

        var targets = root.GetComponentsInChildren<LGuiRoundedImageTarget>(true);
        for (var i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (target == null)
                continue;
            var image = target.GetComponent<Image>();
            if (image == null)
                continue;
            target.Capture(image);
            ApplyLGuiRoundedImageStyle(target, image);
        }
    }
    private void ApplyLGuiSliderCornerGeometry()
    {
        if (_lGuiRoot == null)
            return;
        var sliders = _lGuiRoot.GetComponentsInChildren<Slider>(true);
        for (var i = 0; i < sliders.Length; i++)
        {
            var fillArea = sliders[i].transform.Find("FillArea") as RectTransform;
            if (fillArea != null)
                StretchLGuiRect(fillArea, _uiRoundedCorners ? 0f : 5f, 7f, _uiRoundedCorners ? 0f : 5f, 7f);
            var handleArea = sliders[i].transform.Find("HandleArea");
            var handle = handleArea == null ? null : handleArea.Find("Handle") as RectTransform;
            if (handle != null)
                handle.sizeDelta = _uiRoundedCorners ? new Vector2(20f, 20f) : new Vector2(20f, 26f);
        }
    }
    private void ApplyLGuiRoundedImageStyle(LGuiRoundedImageTarget target, Image image)
    {
        if (_uiRoundedCorners)
        {
            var sprite = GetLGuiRoundedSprite(target.Style);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.fillCenter = true;
            }
        }
        else
        {
            image.sprite = target.OriginalSprite;
            image.type = target.OriginalType;
            image.fillCenter = target.OriginalFillCenter;
        }
    }
    private Sprite? GetLGuiRoundedSprite(LGuiRoundedImageStyle style)
    {
        if (style == LGuiRoundedImageStyle.Capsule)
        {
            EnsureLGuiRoundedCapsuleSprite();
            return _lGuiRoundedCapsuleSprite;
        }
        EnsureLGuiRoundedWindowSprite();
        return _lGuiRoundedWindowSprite;
    }
    private void EnsureLGuiRoundedWindowSprite()
    {
        if (_lGuiRoundedWindowSprite != null)
            return;

        const int size = 64;
        const float radius = 18f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "ElinModifier.RoundedWindow";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.DontSave;
        var pixels = new Color[size * size];
        var half = size * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                var py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                var outsideX = Mathf.Max(px, 0f);
                var outsideY = Mathf.Max(py, 0f);
                var distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY) + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - distance));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        var border = new Vector4(radius, radius, radius, radius);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, border);
        sprite.name = "ElinModifier.RoundedWindow";
        sprite.hideFlags = HideFlags.DontSave;
        _lGuiRoundedWindowTexture = texture;
        _lGuiRoundedWindowSprite = sprite;
    }
    private void EnsureLGuiRoundedCapsuleSprite()
    {
        if (_lGuiRoundedCapsuleSprite != null)
            return;

        const int size = 64;
        // Keep the sliced border close to the slider track's half-height.
        // A near-circular 31px border is stretched into a long ellipse by
        // Unity's sliced Image, which makes both track ends look pointed.
        // A 6px corner produces blunt semicircular track caps and a softly
        // rounded square handle at the current value position.
        const float radius = 6f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "ElinModifier.RoundedCapsule";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.DontSave;
        var pixels = new Color[size * size];
        var half = size * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                var py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                var outsideX = Mathf.Max(px, 0f);
                var outsideY = Mathf.Max(py, 0f);
                var distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY) + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - distance));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        var border = new Vector4(radius, radius, radius, radius);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, border);
        sprite.name = "ElinModifier.RoundedCapsule";
        sprite.hideFlags = HideFlags.DontSave;
        _lGuiRoundedCapsuleTexture = texture;
        _lGuiRoundedCapsuleSprite = sprite;
    }
}
