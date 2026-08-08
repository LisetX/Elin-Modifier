using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    internal bool ModuleHostileThreatMarker => _hostileThreatMarker;
    internal bool ModuleHostileThreatBehaviorPrediction => _hostileThreatBehaviorPrediction;
    internal bool ModuleHostileThreatPredecisionLock => _hostileThreatPredecisionLock;
    internal bool ModuleLowPerformanceMode => _lowPerformanceMode;
    internal float ModuleSchedulerNow => SchedulerNow;

    internal string ModuleLog
    {
        get => _log;
        set => _log = value;
    }

    internal bool ShouldModuleScanThreats() => ShouldScanThreats();
    internal bool ShouldModuleRefreshThreatVitals() => ShouldRefreshThreatVitals();
    internal void MarkModuleThreatScanClean() => MarkThreatScanClean();
    internal void InvalidateModuleThreatVitals() => _scheduler.Invalidate(ModifierTask.ThreatVitals);
    internal static Camera? GetModuleSceneCamera() => GetSceneCamera();
    internal static bool IsModuleHostileThreat(Chara? chara, Chara pc) => IsHostileThreat(chara, pc);
    internal static bool CanModulePlayerSee(Chara pc, Chara chara) => CanPlayerCurrentlySee(pc, chara);
    internal static string GetModuleSafeCharaName(Chara chara) => SafeName(chara);
    internal static float GetModuleThreatHpRatio(Chara chara) => GetThreatHpRatio(chara);
    internal static string GetModuleThreatLevelText(Chara chara) => GetThreatLevelText(chara);
    internal static bool NormalizeModuleMarkerRect(ref Rect rect) => NormalizeMarkerRect(ref rect);
    internal static void TightenModuleMarkerRect(ref Rect rect, float widthScale, float heightScale) =>
        TightenMarkerRect(ref rect, widthScale, heightScale);

    private void InitializeThreatOverlay() => _modules.ThreatOverlay.Initialize();
    private void ShutdownThreatOverlay() => _modules.ThreatOverlay.Shutdown();
    private bool IsThreatOverlayInitialized() => _modules.ThreatOverlay.IsInitialized();
    private void NotifyThreatOverlayDirty() => _modules.ThreatOverlay.NotifyDirty();
    private void TickThreatOverlay() => _modules.ThreatOverlay.Tick();
    private void LateTickThreatOverlay() => _modules.ThreatOverlay.LateTick();
}
