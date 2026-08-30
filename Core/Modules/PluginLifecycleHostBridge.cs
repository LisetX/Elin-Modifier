using System;
using BepInEx.Logging;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    internal void InitializeModuleDebugAuthorization()
    {
        _debugAuthorized = CheckDebugAuthorizationOnce();
        InstallDebugErrorLogCaptureIfAuthorized();
    }

    internal void InitializeModuleEmpWorkspace()
    {
        EnsureEmpPluginWorkspace();
        RefreshEmpPluginDefinitions(true);
    }

    internal void LoadModuleConfiguration() => LoadConfig();
    internal void InitializeModuleOptimization() => InitializeOptimization();
    internal void ApplyModuleEmpStates() => ApplySavedEmpPluginStatesScheduled(true);
    internal void InstallModuleHarmonyPatches() => InstallHarmonyPatches();
    internal void InitializeModuleMainMenuInfo()
    {
        RefreshMainMenuInfoButton();
        ScheduleMainMenuInfoAutoOpen();
    }

    internal void InitializeLifecycleLGui() => InitializeLGui();
    internal void InitializeModuleWatermark() => InitializeWatermark();
    internal void InitializeModuleThreatOverlay() => InitializeThreatOverlay();

    internal void TickModuleInput()
    {
        if (!Input.GetKeyDown(_openKey))
            return;
        ToggleLGui();
        _lastOpenKeyToggleFrame = Time.frameCount;
    }

    internal void LateTickModuleInput() => ApplyForceGameUnfocus();

    internal void TickModuleGameplayMaintenance()
    {
        ApplyForceGameUnfocus();
        ApplyUnlockFrameRateScheduled();
        MaintainIgnoreBuffEffectsScheduled();
        ApplyLocksScheduled();
        ApplySavedEmpPluginStatesScheduled();
        TickCwlErrorNotificationPatch();
        ApplyDebugLocksScheduled();
        ExecutePendingTeleportRequest();
        ApplyInfinitePlayerSightScheduled();
        MaintainHighReliabilityHarmonyPatches();
    }

    internal void TickModuleKillGrowthSaveContext() => TickKillGrowthSaveContext();
    internal void TickModuleLGui() => TickLGui();
    internal void TickModuleWatermark() => TickWatermark();
    internal void TickModuleThreatOverlay() => TickThreatOverlay();
    internal void LateTickModuleThreatOverlay() => LateTickThreatOverlay();

    internal void RestoreModuleProbabilityValues() => RestoreAllProbabilityValues(false);
    internal void ShutdownModuleMainMenuInfo() => DestroyMainMenuInfoButton();
    internal void ShutdownAndPersistModuleWatermark()
    {
        PersistWatermarkStateIfChanged(false);
        ShutdownWatermark();
    }
    internal void ShutdownModuleThreatOverlay() => ShutdownThreatOverlay();
    internal void ShutdownModuleLGui() => ShutdownLGui();
    internal void ShutdownModuleOptimization() => ShutdownOptimization();
    internal void UnpatchModuleAiChanges() => UnpatchAllAiRuntimePatches();
    internal void RemoveModuleDebugCapture() => RemoveDebugErrorLogCapture();
    internal void RestoreModuleFrameRate() => RestoreFrameRateLimit();
    internal void ClearModuleFoodRotOverlays() => ClearFoodRotOverlays();
    internal void ShutdownModuleEquipmentComparison() => DestroyEquipmentComparisonTooltip();

    internal void ClearModuleSingleton()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    internal void DestroyModuleSkin()
    {
        if (_modifierSkin == null)
            return;
        UnityEngine.Object.Destroy(_modifierSkin);
        _modifierSkin = null;
    }
}
