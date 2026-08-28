using System;

internal sealed class IgnoreEncumbranceModule
{
    private readonly IGameRuntimeContext _runtime;
    private readonly ICharacterGameAccess _characters;
    private CoreDebug? _boundDebug;
    private bool _originalIgnoreWeight;
    private bool _overrideApplied;
    private bool _automationOverride;

    internal IgnoreEncumbranceModule(
        IGameRuntimeContext runtime,
        ICharacterGameAccess characters)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
    }

    internal bool Enabled { get; private set; }

    internal void Load(bool enabled)
    {
        Enabled = enabled;
        ApplyState();
    }

    internal void Reset()
    {
        Enabled = false;
        _automationOverride = false;
        ApplyState();
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        ApplyState();
        return true;
    }

    internal void SetAutomationOverride(bool enabled)
    {
        _automationOverride = enabled;
        ApplyState();
    }

    internal void Tick()
    {
        ApplyState();
    }

    private void ApplyState()
    {
        CoreDebug? debug;
        try
        {
            debug = _runtime.Debug;
        }
        catch
        {
            return;
        }

        if (Enabled || _automationOverride)
        {
            if (debug == null)
                return;

            if (!_overrideApplied || !ReferenceEquals(_boundDebug, debug))
            {
                RestoreBoundDebug();
                _boundDebug = debug;
                _originalIgnoreWeight = debug.ignoreWeight;
                _overrideApplied = true;
            }

            if (!debug.ignoreWeight)
            {
                debug.ignoreWeight = true;
                RecalculatePlayerBurden();
            }
            return;
        }

        RestoreBoundDebug();
    }

    private void RestoreBoundDebug()
    {
        if (!_overrideApplied)
            return;

        try
        {
            if (_boundDebug != null)
                _boundDebug.ignoreWeight = _originalIgnoreWeight;
        }
        catch
        {
        }

        _boundDebug = null;
        _overrideApplied = false;
        RecalculatePlayerBurden();
    }

    private void RecalculatePlayerBurden()
    {
        try
        {
            _characters.PlayerCharacter?.CalcBurden();
        }
        catch
        {
        }
    }
}
