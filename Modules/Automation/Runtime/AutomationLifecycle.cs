using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class AutomationModule
{
    internal void TickAutomation()
    {
        if (Input.GetKeyDown(_automationStopKey))
        {
            if (_automationRunning)
                StopAutomation(true, true);
            return;
        }

        if (Input.GetKeyDown(_automationRunKey))
        {
            if (!_automationRunning)
                StartAutomation();
            return;
        }

        if (!_automationRunning)
            return;

        if (!HasCharacterData())
        {
            StopAutomation(false, false);
            SetAutomationLog(AutomationText("已离开游戏存档，自动化已中止", "Left the active save; automation stopped", "セーブデータを離れたため自動化を停止しました", "Активное сохранение закрыто; автоматизация остановлена"));
            return;
        }

        MaintainAutomationFeatureOverrides();

        if (_automationRetaliating || _automationRetaliationQueue.Count > 0)
        {
            TickAutomationRetaliation();
            return;
        }

        if (_automationNeedsMaintenance)
        {
            if (!_automationNeedsDetectionDuringExecution && !_automationNeedsSleepStarted)
                CompleteAutomationNeedsMaintenance();
            else
                TickAutomationNeedsMaintenance();
            return;
        }

        if (_automationCurrentAction != null)
        {
            TickAutomationCurrentAction();
            return;
        }

        if (Time.unscaledTime < _automationNextActionAt)
            return;

        StartNextAutomationAction();
    }
    private void StartAutomation()
    {
        EnsureAutomationProfiles();
        if (!HasCharacterData())
        {
            SetAutomationLog(AutomationText("当前不在游戏存档内", "No active game save", "ゲームのセーブデータ内ではありません", "Нет активного сохранения"));
            return;
        }

        var profile = GetCurrentAutomationProfile();
        if (!profile.Actions.Any(action => action.Enabled))
        {
            SetAutomationLog(AutomationText("当前配置没有可执行项", "The current profile has no enabled actions", "現在の設定に実行可能な項目がありません", "В текущем профиле нет включенных действий"));
            return;
        }

        StopAutomation(false, false);
        BeginAutomationFeatureOverrides();
        _automationRunning = true;
        _automationActionIndex = -1;
        _automationNextActionAt = Time.unscaledTime;
        SetAutomationLog(AutomationText("自动化已启动: ", "Automation started: ", "自動化を開始: ", "Автоматизация запущена: ") + profile.Name);
    }
    private void StopAutomation(bool cancelCurrentGameAction, bool userRequested)
    {
        if (cancelCurrentGameAction)
        {
            try
            {
                var currentPc = GetSafePc();
                if (currentPc != null && _automationActionAi != null && ReferenceEquals(currentPc.ai, _automationActionAi) && currentPc.ai.IsRunning)
                    currentPc.ai.Cancel();
            }
            catch { }
        }

        _automationRunning = false;
        _automationActionIndex = -1;
        _automationCurrentAction = null;
        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetChara = null;
        _automationTargetPoint = null;
        _automationSkippedMinePoints.Clear();
        _automationSkippedChopPoints.Clear();
        _automationSkippedHarvestPoints.Clear();
        _automationSkippedFertilizePoints.Clear();
        _automationSkippedContainerUids.Clear();
        _automationSkippedInteractUids.Clear();
        _automationInteractedThingUids.Clear();
        _automationSkippedEnemyUids.Clear();
        _automationSkippedPickupUids.Clear();
        _automationDiscardedPickupUids.Clear();
        _automationEnemyFailureCounts.Clear();
        _automationPickupOrigin = null;
        _automationKillApproaching = false;
        _automationInteractionPerformedForTarget = false;
        _automationKillWaitingForEmptyRecheck = false;
        _automationKillEmptyRecheckCount = 0;
        _automationKillNextEmptyRecheckAt = 0f;
        _automationRetaliationQueue.Clear();
        _automationRetaliating = false;
        _automationRetaliationTarget = null;
        _automationRetaliationResumeAction = null;
        _automationRetaliationResumeDelay = 0f;
        _automationRetaliationFailureCount = 0;
        _automationNeedsMaintenance = false;
        _automationNeedsInitialEatCompleted = false;
        _automationNeedsSleepStarted = false;
        _automationNeedsSleepCompleted = false;
        _automationNeedsResumeDelay = 0f;
        _automationNeedsLastFoodUid = 0;
        _automationNeedsHungerBeforeEat = 0;
        _automationNeedsSkippedFoodUids.Clear();
        _automationNeedsSleepMoveAttempts = 0;
        _automationNeedsSleepVisitedPoints.Clear();
        _automationNeedsPostSleepEatAt = 0f;
        _automationNeedsResumeSweep = false;
        _automationSweepVerificationPass = false;
        _automationSweepCompletedCount = 0;
        _automationZoneMoveRequested = false;
        _automationGameLoadRequested = false;
        _automationNextActionAt = 0f;
        EndAutomationFeatureOverrides();
        if (userRequested)
            SetAutomationLog(AutomationText("自动化已中止", "Automation stopped", "自動化を停止しました", "Автоматизация остановлена"));
    }
    private void BeginAutomationFeatureOverrides()
    {
        if (_automationFeatureOverrideActive)
            EndAutomationFeatureOverrides();

        _automationOriginalInfinitePlayerSight = _infinitePlayerSight;
        _automationOriginalHostileThreatMarker = _hostileThreatMarker;
        _automationFeatureOverrideActive = true;
        ApplyAutomationFeatureOverrideStates(true, true);
        MaintainAutomationIgnoreWeightOverride();
    }
    private void MaintainAutomationFeatureOverrides()
    {
        if (_automationFeatureOverrideActive && (!_infinitePlayerSight || !_hostileThreatMarker))
            ApplyAutomationFeatureOverrideStates(true, true);
        MaintainAutomationIgnoreWeightOverride();
    }
    private void EndAutomationFeatureOverrides()
    {
        if (!_automationFeatureOverrideActive)
            return;

        var infinitePlayerSight = _automationOriginalInfinitePlayerSight;
        var hostileThreatMarker = _automationOriginalHostileThreatMarker;
        _automationFeatureOverrideActive = false;
        ApplyAutomationFeatureOverrideStates(infinitePlayerSight, hostileThreatMarker);
        SetAutomationIgnoreWeightOverride(false);
    }
    private void MaintainAutomationIgnoreWeightOverride()
    {
        SetAutomationIgnoreWeightOverride(
            _automationFeatureOverrideActive && _automationIgnoreWeightDuringExecution);
    }
    private static void SetAutomationIgnoreWeightOverride(bool enabled)
    {
        try { ElinModifierPlugin.ActiveModules?.IgnoreEncumbrance.SetAutomationOverride(enabled); }
        catch { }
    }
    private void ApplyAutomationFeatureOverrideStates(bool infinitePlayerSight, bool hostileThreatMarker)
    {
        var previousLog = _log;
        try
        {
            if (_infinitePlayerSight != infinitePlayerSight)
                SetInfinitePlayerSight(infinitePlayerSight);
            if (_hostileThreatMarker != hostileThreatMarker)
                SetHostileThreatMarker(hostileThreatMarker);
        }
        finally
        {
            _log = previousLog;
        }
    }
    internal bool GetAutomationPersistedInfinitePlayerSight()
    {
        return _automationFeatureOverrideActive
            ? _automationOriginalInfinitePlayerSight
            : _infinitePlayerSight;
    }
    internal bool GetAutomationPersistedHostileThreatMarker()
    {
        return _automationFeatureOverrideActive
            ? _automationOriginalHostileThreatMarker
            : _hostileThreatMarker;
    }
}
