using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

internal sealed class VirtualList<T> : IDisposable
{
    internal delegate RectTransform RowFactory(RectTransform parent);
    internal delegate void RowBinder(RectTransform row, T item, int index);

    private readonly ScrollRect _scroll;
    private readonly RectTransform _content;
    private readonly float _rowHeight;
    private readonly RowBinder _binder;
    private readonly List<RectTransform> _rows = new List<RectTransform>();
    private readonly List<int> _boundIndices = new List<int>();
    private readonly UnityAction<Vector2> _scrollListener;
    private IReadOnlyList<T> _items = Array.Empty<T>();
    private int _firstVisible = -1;
    private bool _disposed;

    public VirtualList(ScrollRect scroll, float rowHeight, int poolSize, RowFactory factory, RowBinder binder)
    {
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _content = scroll.content ?? throw new ArgumentException("ScrollRect content is required", nameof(scroll));
        _rowHeight = Math.Max(18f, rowHeight);
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        poolSize = Math.Max(4, poolSize);

        for (var i = 0; i < poolSize; i++)
        {
            var row = factory(_content);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, _rowHeight);
            row.gameObject.SetActive(false);
            _rows.Add(row);
            _boundIndices.Add(-1);
        }

        _scrollListener = _ => Refresh(false);
        _scroll.onValueChanged.AddListener(_scrollListener);
    }

    public void SetItems(IReadOnlyList<T>? items)
    {
        _items = items ?? Array.Empty<T>();
        var size = _content.sizeDelta;
        size.y = _items.Count * _rowHeight;
        _content.sizeDelta = size;
        _firstVisible = -1;
        Refresh(true);
    }

    public void RefreshBoundRows()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var index = _boundIndices[i];
            if (index >= 0 && index < _items.Count)
                _binder(_rows[i], _items[index], index);
        }
    }

    public void Refresh(bool force)
    {
        if (_disposed)
            return;

        var y = Math.Max(0f, _content.anchoredPosition.y);
        var first = Math.Max(0, (int)Math.Floor(y / _rowHeight) - 1);
        if (!force && first == _firstVisible)
            return;
        _firstVisible = first;

        for (var i = 0; i < _rows.Count; i++)
        {
            var index = first + i;
            var row = _rows[i];
            if (index < 0 || index >= _items.Count)
            {
                _boundIndices[i] = -1;
                row.gameObject.SetActive(false);
                continue;
            }

            var needsBind = force || _boundIndices[i] != index;
            if (needsBind && row.gameObject.activeSelf)
                row.gameObject.SetActive(false);
            row.anchoredPosition = new Vector2(0f, -index * _rowHeight);
            if (needsBind)
            {
                _boundIndices[i] = index;
                _binder(row, _items[index], index);
            }
            if (!row.gameObject.activeSelf)
                row.gameObject.SetActive(true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_scroll != null)
            _scroll.onValueChanged.RemoveListener(_scrollListener);
        _items = Array.Empty<T>();
        _rows.Clear();
        _boundIndices.Clear();
    }
}
