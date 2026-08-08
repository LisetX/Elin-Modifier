using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed class WatermarkErrorAlertInput : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
{
    private Action? _leftClick;
    private Action? _rightClick;
    private Action<bool>? _hoverChanged;
    private Action<float>? _scroll;

    public void Initialize(Action leftClick, Action rightClick, Action<bool> hoverChanged, Action<float> scroll)
    {
        _leftClick = leftClick;
        _rightClick = rightClick;
        _hoverChanged = hoverChanged;
        _scroll = scroll;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            _leftClick?.Invoke();
        else if (eventData.button == PointerEventData.InputButton.Right)
            _rightClick?.Invoke();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hoverChanged?.Invoke(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hoverChanged?.Invoke(false);
    }

    public void OnScroll(PointerEventData eventData)
    {
        _scroll?.Invoke(eventData.scrollDelta.y);
    }
}

