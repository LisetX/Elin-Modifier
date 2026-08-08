using System;

internal enum ModifierTask
{
    SceneProbe,
    EmpPendingApply,
    Locks,
    InfiniteSight,
    FrameRateUnlock,
    DebugLocks,
    CharacterSnapshot,
    NearbyNpcSnapshot,
    IgnoreBuffEffects,
    ThreatScan,
    ThreatVitals,
    LGuiDynamicValues,
    LGuiSlowValues,
    Count
}

internal sealed class ModifierScheduler
{
    private readonly float[] _nextDue = new float[(int)ModifierTask.Count];
    private readonly bool[] _initialized = new bool[(int)ModifierTask.Count];

    public bool IsDue(ModifierTask task, float now, float intervalSeconds)
    {
        var index = (int)task;
        if (index < 0 || index >= _nextDue.Length)
            return true;

        if (intervalSeconds <= 0f || !_initialized[index] || now >= _nextDue[index])
        {
            _initialized[index] = true;
            _nextDue[index] = now + Math.Max(0f, intervalSeconds);
            return true;
        }

        return false;
    }

    public void Invalidate(ModifierTask task)
    {
        var index = (int)task;
        if (index < 0 || index >= _nextDue.Length)
            return;
        _initialized[index] = false;
        _nextDue[index] = 0f;
    }

    public void InvalidateAll()
    {
        Array.Clear(_initialized, 0, _initialized.Length);
        Array.Clear(_nextDue, 0, _nextDue.Length);
    }
}

internal sealed class ModifierDirtyState
{
    public int Version { get; private set; }
    public bool IsDirty { get; private set; } = true;

    public void MarkDirty()
    {
        unchecked { Version++; }
        IsDirty = true;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }
}
