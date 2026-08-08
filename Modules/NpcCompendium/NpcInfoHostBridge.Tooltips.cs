using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private sealed class LGuiNpcTooltipView : MonoBehaviour
    {
        private const float MinimumWidth = 180f;
        private const float MaximumWidth = 420f;
        private const float FadeInSeconds = 0.14f;
        private const float FadeOutSeconds = 0.12f;
        private RectTransform? _bounds;
        private RectTransform? _panel;
        private Canvas? _canvas;
        private CanvasGroup? _group;
        private LGuiFadeDriver? _fade;
        private Text? _title;
        private Text? _body;
        private LGuiNpcTooltipTarget? _owner;
        private int _transition;

        internal void Initialize(
            RectTransform bounds,
            RectTransform panel,
            Canvas? canvas,
            CanvasGroup group,
            LGuiFadeDriver fade,
            Text title,
            Text body)
        {
            _bounds = bounds;
            _panel = panel;
            _canvas = canvas;
            _group = group;
            _fade = fade;
            _title = title;
            _body = body;
            _fade.SetImmediate(0f, false);
        }

        internal void Show(LGuiNpcTooltipTarget owner, string title, string body)
        {
            if (_panel == null || _fade == null || _title == null || _body == null)
                return;
            _owner = owner;
            _transition++;
            _panel.gameObject.SetActive(true);
            _title.text = title ?? "";
            _body.text = body ?? "";
            RefreshLayout();
            _panel.SetAsLastSibling();
            UpdatePosition();
            _fade.FadeTo(1f, FadeInSeconds, false);
        }

        internal void Hide(LGuiNpcTooltipTarget owner)
        {
            if (!ReferenceEquals(_owner, owner) || _panel == null || _fade == null)
                return;
            _owner = null;
            var transition = ++_transition;
            _fade.FadeTo(0f, FadeOutSeconds, false, () =>
            {
                if (_panel != null && _owner == null && transition == _transition)
                    _panel.gameObject.SetActive(false);
            });
        }

        private void LateUpdate()
        {
            if (_owner != null)
                UpdatePosition();
        }

        private void RefreshLayout()
        {
            if (_panel == null || _title == null || _body == null)
                return;
            SetTopLeft(_title.rectTransform, 14f, 8f, MaximumWidth - 28f, 24f);
            SetTopLeft(_body.rectTransform, 14f, 33f, MaximumWidth - 28f, 220f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_title.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body.rectTransform);
            var width = Mathf.Clamp(
                Mathf.Ceil(Mathf.Max(_title.preferredWidth, _body.preferredWidth)) + 28f,
                MinimumWidth,
                MaximumWidth);
            SetTopLeft(_title.rectTransform, 14f, 8f, width - 28f, 24f);
            SetTopLeft(_body.rectTransform, 14f, 33f, width - 28f, 220f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body.rectTransform);
            var bodyHeight = Mathf.Clamp(_body.preferredHeight, 20f, 220f);
            var panelHeight = 43f + bodyHeight;
            _panel.sizeDelta = new Vector2(width, panelHeight);
            SetTopLeft(_body.rectTransform, 14f, 33f, width - 28f, bodyHeight);
        }

        private void UpdatePosition()
        {
            if (_bounds == null || _panel == null)
                return;
            var camera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _bounds,
                    Input.mousePosition,
                    camera,
                    out var cursor))
                return;

            var bounds = _bounds.rect;
            var width = _panel.rect.width;
            var height = _panel.rect.height;
            var x = cursor.x + 20f;
            var y = cursor.y - 18f;
            if (x + width > bounds.xMax - 12f)
                x = cursor.x - width - 20f;
            if (y - height < bounds.yMin + 12f)
                y = cursor.y + height + 18f;
            x = Mathf.Clamp(x, bounds.xMin + 12f, bounds.xMax - width - 12f);
            y = Mathf.Clamp(y, bounds.yMin + height + 12f, bounds.yMax - 12f);
            _panel.localPosition = new Vector3(x, y, 0f);
        }

        private static void SetTopLeft(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }

    private sealed class LGuiNpcTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private LGuiNpcTooltipView? _tooltip;
        private string _title = "";
        private string _body = "";

        internal void Initialize(LGuiNpcTooltipView tooltip, string title, string body)
        {
            _tooltip = tooltip;
            _title = title ?? "";
            _body = body ?? "";
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _tooltip?.Show(this, _title, _body);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _tooltip?.Hide(this);
        }

        private void OnDisable()
        {
            _tooltip?.Hide(this);
        }
    }
}
