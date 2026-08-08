using UnityEngine;
using UnityEngine.EventSystems;

internal sealed class LGuiDragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    private RectTransform? _target;
    private Canvas? _canvas;
    private Vector2 _offset;

    public void Initialize(RectTransform target, Canvas canvas)
    {
        _target = target;
        _canvas = canvas;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_target == null || _canvas == null)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)_target.parent,
            eventData.position,
            _canvas.worldCamera,
            out var local);
        _offset = _target.anchoredPosition - local;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_target == null || _canvas == null)
            return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_target.parent,
                eventData.position,
                _canvas.worldCamera,
                out var local))
        {
            _target.anchoredPosition = local + _offset;
        }
    }
}
