using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed class LGuiFocusModule
{
    private readonly HashSet<LGuiInputFocusTracker> _focused =
        new HashSet<LGuiInputFocusTracker>();

    internal void Bind(InputField input)
    {
        if (input == null)
            return;
        var tracker = input.GetComponent<LGuiInputFocusTracker>();
        if (tracker == null)
            tracker = input.gameObject.AddComponent<LGuiInputFocusTracker>();
        tracker.Initialize(this);
    }

    internal bool HasFocusedInputWithin(Transform root)
    {
        if (root == null || _focused.Count == 0)
            return false;

        _focused.RemoveWhere(tracker => tracker == null);
        foreach (var tracker in _focused)
            if (tracker.transform == root || tracker.transform.IsChildOf(root))
                return true;
        return false;
    }

    internal void SetFocused(LGuiInputFocusTracker tracker, bool focused)
    {
        if (tracker == null)
            return;
        if (focused)
            _focused.Add(tracker);
        else
            _focused.Remove(tracker);
    }

    internal void Clear()
    {
        _focused.Clear();
    }
}

internal sealed class LGuiInputFocusTracker : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    private LGuiFocusModule? _owner;
    private bool _focused;

    internal void Initialize(LGuiFocusModule owner)
    {
        if (ReferenceEquals(_owner, owner))
            return;
        if (_focused)
            _owner?.SetFocused(this, false);
        _owner = owner;
        _focused = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (_focused)
            return;
        _focused = true;
        _owner?.SetFocused(this, true);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        ReleaseFocus();
    }

    private void OnDisable()
    {
        ReleaseFocus();
    }

    private void OnDestroy()
    {
        ReleaseFocus();
        _owner = null;
    }

    private void ReleaseFocus()
    {
        if (!_focused)
            return;
        _focused = false;
        _owner?.SetFocused(this, false);
    }
}
