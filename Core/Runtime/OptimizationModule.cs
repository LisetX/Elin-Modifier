using System;
using UnityEngine;

internal sealed class OptimizationModule
{
    private readonly ElinModifierPlugin _host;
    private readonly ModifierScheduler _scheduler = new ModifierScheduler();
    private readonly ModifierDirtyState _characterDirty = new ModifierDirtyState();
    private readonly ModifierDirtyState _nearbyNpcDirty = new ModifierDirtyState();
    private readonly ModifierDirtyState _threatDirty = new ModifierDirtyState();
    private bool _empApplyPending = true;
    private Map? _observedMap;
    private int _observedPcUid = -1;
    private int _observedCharaCount = -1;

    internal OptimizationModule(ElinModifierPlugin host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    internal ModifierScheduler Scheduler => _scheduler;

    internal float Now
    {
        get
        {
            try { return Time.unscaledTime; }
            catch { return 0f; }
        }
    }

    internal void Initialize()
    {
        _scheduler.InvalidateAll();
        _empApplyPending = true;
        _characterDirty.MarkDirty();
        _nearbyNpcDirty.MarkDirty();
        _threatDirty.MarkDirty();
        ProbeSceneState(true);
    }

    internal void Shutdown()
    {
        _scheduler.InvalidateAll();
        _empApplyPending = false;
    }

    internal void Tick(bool lowPerformanceMode)
    {
        var interval = lowPerformanceMode ? 0.5f : 0.25f;
        if (_scheduler.IsDue(ModifierTask.SceneProbe, Now, interval))
            ProbeSceneState(false);
    }

    private void ProbeSceneState(bool force)
    {
        Map? map = null;
        Chara? pc = null;
        var pcUid = -1;
        var charaCount = -1;
        try
        {
            map = GameAccess.World.CurrentMap;
            pc = GameAccess.Characters.PlayerCharacter;
            if (pc != null)
                pcUid = pc.uid;
            if (map?.charas != null)
                charaCount = map.charas.Count;
        }
        catch
        {
        }

        if (!force &&
            ReferenceEquals(map, _observedMap) &&
            pcUid == _observedPcUid &&
            charaCount == _observedCharaCount)
            return;

        var mapChanged = !ReferenceEquals(map, _observedMap);
        _observedMap = map;
        _observedPcUid = pcUid;
        _observedCharaCount = charaCount;
        _host.HandleOptimizationSceneChange(pc);
        if (mapChanged)
            _scheduler.Invalidate(ModifierTask.InfiniteSight);
    }

    internal void MarkCharacterDataDirty()
    {
        _characterDirty.MarkDirty();
        _nearbyNpcDirty.MarkDirty();
        _scheduler.Invalidate(ModifierTask.CharacterSnapshot);
        _scheduler.Invalidate(ModifierTask.NearbyNpcSnapshot);
    }

    internal void MarkEmpPending()
    {
        _empApplyPending = true;
        _scheduler.Invalidate(ModifierTask.EmpPendingApply);
    }

    internal bool ShouldApplyEmp(bool force, bool definitionsDirty)
    {
        if (!force && !definitionsDirty && !_empApplyPending)
            return false;
        return force || _scheduler.IsDue(ModifierTask.EmpPendingApply, Now, 0.05f);
    }

    internal void CompleteEmpApply(bool pending)
    {
        _empApplyPending = pending;
        if (pending)
            _scheduler.Invalidate(ModifierTask.EmpPendingApply);
    }

    internal bool IsDue(ModifierTask task, bool lowPerformanceMode, float normalInterval, float lowPerformanceInterval)
    {
        return _scheduler.IsDue(task, Now, lowPerformanceMode ? lowPerformanceInterval : normalInterval);
    }

    internal bool ShouldRefreshLGuiSlowValues(bool lowPerformanceMode)
    {
        var interval = lowPerformanceMode ? 1f : 0.5f;
        return _characterDirty.IsDirty ||
               _scheduler.IsDue(ModifierTask.LGuiSlowValues, Now, interval);
    }

    internal void MarkLGuiValuesClean()
    {
        _characterDirty.MarkClean();
        _nearbyNpcDirty.MarkClean();
    }

    internal bool ShouldScanThreats(bool lowPerformanceMode)
    {
        var interval = lowPerformanceMode ? 0.75f : 0.25f;
        return _threatDirty.IsDirty ||
               _scheduler.IsDue(ModifierTask.ThreatScan, Now, interval);
    }

    internal void MarkThreatScanClean()
    {
        _threatDirty.MarkClean();
    }

    internal void InvalidateThreatData()
    {
        _threatDirty.MarkDirty();
        _scheduler.Invalidate(ModifierTask.ThreatScan);
        _scheduler.Invalidate(ModifierTask.ThreatVitals);
    }
}

public sealed partial class ElinModifierPlugin
{
    private ModifierScheduler _scheduler => _modules.Optimization.Scheduler;
    internal float SchedulerNow => _modules.Optimization.Now;

    private void InitializeOptimization() => _modules.Optimization.Initialize();
    private void ShutdownOptimization() => _modules.Optimization.Shutdown();
    internal void TickOptimization() => _modules.Optimization.Tick(_lowPerformanceMode);

    internal void HandleOptimizationSceneChange(Chara? pc)
    {
        if (_ignoreBuffEffects && pc != null)
            RemoveIgnoredBuffEffects(pc);
        MarkCharacterDataDirty();
        InvalidateNpcMoreInfoCaches();
        InvalidateItemMoreInfoCache();
        InvalidateNearbyNpcCache();
        InvalidateThreatData();
    }

    private void MarkCharacterDataDirty()
    {
        _modules.Optimization.MarkCharacterDataDirty();
        NotifyLGuiDataDirty();
    }

    private void MarkEmpPending() => _modules.Optimization.MarkEmpPending();

    private void ApplySavedEmpPluginStatesScheduled(bool force = false)
    {
        if (!_modules.Optimization.ShouldApplyEmp(force, _pluginDefinitionsDirty))
            return;

        RefreshEmpPluginDefinitionsIfNeeded();
        ApplySavedEmpPluginStates(false);
        _modules.Optimization.CompleteEmpApply(HasPendingEmpPluginStateWork());
    }

    private bool HasPendingEmpPluginStateWork()
    {
        foreach (var plugin in _pluginDefinitions.Values)
        {
            if (plugin == null || !plugin.IsValid)
                continue;
            foreach (var function in plugin.Functions)
            {
                if (function == null || !function.IsValid || function.Kind == EmpFunctionKind.Button)
                    continue;
                var key = GetEmpFunctionKey(plugin, function);
                if (_empFunctionStates.TryGetValue(key, out var state) &&
                    state != null &&
                    (state.PendingApply || !state.Initialized))
                    return true;
            }
        }
        return false;
    }

    private void ApplyLocksScheduled()
    {
        if (_locks.Count == 0)
            return;
        if (_modules.Optimization.IsDue(ModifierTask.Locks, _lowPerformanceMode, 0.05f, 0.2f))
            ApplyLocks();
    }

    private void ApplyInfinitePlayerSightScheduled()
    {
        if (!_infinitePlayerSight)
            return;
        if (_modules.Optimization.IsDue(ModifierTask.InfiniteSight, _lowPerformanceMode, 0.5f, 1f))
            ApplyInfinitePlayerSight();
    }

    private void ApplyUnlockFrameRateScheduled()
    {
        if (!_unlockFrameRate)
            return;
        if (_modules.Optimization.IsDue(ModifierTask.FrameRateUnlock, _lowPerformanceMode, 0.25f, 0.5f))
            ApplyUnlockFrameRate();
    }

    private void ApplyDebugLocksScheduled()
    {
        if (!_debugAuthorized || _debugLocks.Count == 0)
            return;
        if (_modules.Optimization.IsDue(ModifierTask.DebugLocks, _lowPerformanceMode, 0.1f, 0.25f))
            ApplyDebugLocks();
    }

    private void MaintainIgnoreBuffEffectsScheduled()
    {
        if (!_ignoreBuffEffects)
            return;
        if (_modules.Optimization.IsDue(ModifierTask.IgnoreBuffEffects, _lowPerformanceMode, 0.25f, 0.5f))
            RemoveIgnoredBuffEffectsFromTargets();
    }

    private bool ShouldRefreshLGuiDynamicValues()
    {
        return _modules.Optimization.IsDue(ModifierTask.LGuiDynamicValues, _lowPerformanceMode, 0.1f, 0.25f);
    }

    private bool ShouldRefreshLGuiSlowValues()
    {
        return _modules.Optimization.ShouldRefreshLGuiSlowValues(_lowPerformanceMode);
    }

    private void MarkLGuiValuesClean() => _modules.Optimization.MarkLGuiValuesClean();

    internal bool ShouldScanThreats() => _modules.Optimization.ShouldScanThreats(_lowPerformanceMode);

    private bool ShouldRefreshThreatVitals()
    {
        return _modules.Optimization.IsDue(ModifierTask.ThreatVitals, _lowPerformanceMode, 0.1f, 0.25f);
    }

    private void MarkThreatScanClean() => _modules.Optimization.MarkThreatScanClean();

    private void InvalidateThreatData()
    {
        _modules.Optimization.InvalidateThreatData();
        NotifyThreatOverlayDirty();
    }
}
