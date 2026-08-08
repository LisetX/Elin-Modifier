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
    private static long GetAutomationPointKey(Point point)
    {
        return ((long)point.x << 32) ^ (uint)point.z;
    }
    private void FinishAutomationAction(bool success, string message)
    {
        var action = _automationCurrentAction;
        var index = _automationActionIndex;
        var delay = action != null && NormalizeAutomationActionType(action.Type) == AutomationTypeWait
            ? Mathf.Clamp(action.DelaySeconds, 0f, 3600f)
            : 0f;
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
        _automationSweepVerificationPass = false;
        _automationSweepCompletedCount = 0;
        _automationZoneMoveRequested = false;
        _automationGameLoadRequested = false;
        _automationNeedsMaintenance = _automationRunning && _automationNeedsDetectionDuringExecution;
        _automationNeedsInitialEatCompleted = false;
        _automationNeedsSleepStarted = false;
        _automationNeedsSleepCompleted = false;
        _automationNeedsResumeDelay = delay;
        _automationNeedsLastFoodUid = 0;
        _automationNeedsHungerBeforeEat = 0;
        _automationNeedsSkippedFoodUids.Clear();
        _automationNeedsSleepMoveAttempts = 0;
        _automationNeedsSleepVisitedPoints.Clear();
        _automationNeedsPostSleepEatAt = 0f;
        _automationNeedsResumeSweep = false;
        _automationNextActionAt = Time.unscaledTime;
        SetAutomationLog((success ? AutomationText("完成 ", "Done ", "完了 ", "Готово ") : AutomationText("跳过 ", "Skipped ", "スキップ ", "Пропущено ")) +
                                (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + message +
                                (delay > 0f ? AutomationText("，延时 ", ", delay ", "、遅延 ", ", задержка ") + delay.ToString("0.###", CultureInfo.InvariantCulture) + "s" : ""));
    }
}
