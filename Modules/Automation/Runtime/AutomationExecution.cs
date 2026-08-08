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
    private void StartNextAutomationAction()
    {
        var profile = GetCurrentAutomationProfile();
        if (profile.Actions.Count == 0)
        {
            StopAutomation(false, false);
            SetAutomationLog(AutomationText("当前配置没有执行项", "The current profile has no actions", "現在の設定に実行項目がありません", "В текущем профиле нет действий"));
            return;
        }

        var next = -1;
        for (var i = _automationActionIndex + 1; i < profile.Actions.Count; i++)
        {
            if (!profile.Actions[i].Enabled) continue;
            next = i;
            break;
        }

        if (next < 0 && profile.Loop)
        {
            for (var i = 0; i < profile.Actions.Count; i++)
            {
                if (!profile.Actions[i].Enabled) continue;
                next = i;
                break;
            }
        }

        if (next < 0)
        {
            StopAutomation(false, false);
            SetAutomationLog(AutomationText("自动化配置执行完成", "Automation profile completed", "自動化設定の実行が完了しました", "Профиль автоматизации завершён"));
            return;
        }

        _automationActionIndex = next;
        var action = profile.Actions[next];
        _automationCurrentAction = action;
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
        _automationActionStartedAt = Time.unscaledTime;
        try { _automationStartZoneUid = GameAccess.World.CurrentZone?.uid ?? 0; }
        catch { _automationStartZoneUid = 0; }

        SetAutomationLog(AutomationText("正在执行 ", "Running ", "実行中 ", "Выполняется ") +
                                (next + 1).ToString(CultureInfo.InvariantCulture) + ": " + GetAutomationActionLabel(action.Type));

        try
        {
            switch (NormalizeAutomationActionType(action.Type))
            {
                case AutomationTypeAutoMine:
                    StartAutomationMine(action);
                    break;
                case AutomationTypeAutoChop:
                    StartAutomationChop(action);
                    break;
                case AutomationTypeAutoHarvest:
                    StartAutomationHarvest(action);
                    break;
                case AutomationTypeAutoFertilize:
                    StartAutomationFertilize(action);
                    break;
                case AutomationTypeSearchContainers:
                    StartAutomationSearchContainers(action);
                    break;
                case AutomationTypeAutoInteract:
                    StartAutomationInteract(action);
                    break;
                case AutomationTypeAutoKill:
                    StartAutomationKill(action);
                    break;
                case AutomationTypeMoveTo:
                    StartAutomationMove(action);
                    break;
                case AutomationTypeUseAbility:
                    ExecuteAutomationAbility(action);
                    break;
                case AutomationTypeNextFloor:
                    StartAutomationNextFloor(action);
                    break;
                case AutomationTypePickupByValue:
                    StartAutomationPickup(action);
                    break;
                case AutomationTypeWait:
                    FinishAutomationAction(true, AutomationText("等待完成", "Wait completed", "待機完了", "Ожидание завершено"));
                    break;
                case AutomationTypeSaveGame:
                    ExecuteAutomationSaveGame();
                    break;
                case AutomationTypeLoadGame:
                    StartAutomationLoadGame();
                    break;
                default:
                    FinishAutomationAction(false, AutomationText("未知执行项", "Unknown action", "不明な実行項目", "Неизвестное действие"));
                    break;
            }
        }
        catch (Exception ex)
        {
            FinishAutomationAction(false, AutomationText("执行失败: ", "Execution failed: ", "実行失敗: ", "Ошибка выполнения: ") + ex.Message);
        }
    }
    private void TickAutomationCurrentAction()
    {
        var action = _automationCurrentAction;
        if (action == null)
            return;

        var type = NormalizeAutomationActionType(action.Type);
        if (Time.unscaledTime - _automationActionStartedAt > AutomationActionTimeoutSeconds)
        {
            if (type == AutomationTypeAutoFertilize)
            {
                ContinueAutomationFertilize(action, false, true);
                return;
            }
            if (type == AutomationTypeSearchContainers)
            {
                ContinueAutomationSearchContainers(action, false, true);
                return;
            }
            if (type == AutomationTypeAutoInteract)
            {
                ContinueAutomationInteract(action, false, true);
                return;
            }
            if (type == AutomationTypeAutoMine || type == AutomationTypeAutoChop ||
                type == AutomationTypeAutoHarvest || type == AutomationTypeAutoKill)
            {
                ContinueAutomationSweep(action, type, false, true);
                return;
            }
            if (type == AutomationTypePickupByValue)
            {
                ContinueAutomationPickup(action, false, true, false);
                return;
            }
            FinishAutomationAction(false, AutomationText("执行超时", "Action timed out", "実行がタイムアウトしました", "Время выполнения истекло"));
            return;
        }

        var pc = GetSafePc();
        if (pc == null)
            return;

        if (type == AutomationTypeLoadGame && _automationGameLoadRequested)
            return;

        if (type == AutomationTypeAutoKill && _automationKillWaitingForEmptyRecheck)
        {
            if (Time.unscaledTime < _automationKillNextEmptyRecheckAt)
                return;

            if (TryStartNextAutomationKillTarget())
            {
                _automationKillWaitingForEmptyRecheck = false;
                _automationKillEmptyRecheckCount = 0;
                return;
            }

            _automationKillEmptyRecheckCount++;
            if (_automationKillEmptyRecheckCount < AutomationKillEmptyRecheckLimit)
            {
                _automationKillNextEmptyRecheckAt = Time.unscaledTime + AutomationKillEmptyRecheckDelaySeconds;
                return;
            }

            _automationKillWaitingForEmptyRecheck = false;
            var completedCount = _automationSweepCompletedCount.ToString(CultureInfo.InvariantCulture);
            FinishAutomationAction(true,
                AutomationText("全图已无敌对目标，共完成 ", "No hostile targets remain on the map; completed ", "マップ上に敵対対象はありません。完了数: ", "На карте больше нет враждебных целей; выполнено: ") + completedCount);
            return;
        }

        if (type == AutomationTypeNextFloor)
        {
            int currentZoneUid;
            try { currentZoneUid = GameAccess.World.CurrentZone?.uid ?? 0; }
            catch { currentZoneUid = 0; }
            if (_automationStartZoneUid != 0 && currentZoneUid != 0 && currentZoneUid != _automationStartZoneUid)
            {
                FinishAutomationAction(true, AutomationText("已进入下一层", "Entered the next floor", "次の階層に入りました", "Переход на следующий этаж выполнен"));
                return;
            }
        }

        var ai = _automationActionAi;
        if (ai != null && ReferenceEquals(pc.ai, ai) && ai.IsRunning)
            return;

        if (type == AutomationTypeAutoMine)
        {
            var targetCompleted = ai != null && ai.status == AIAct.Status.Success;
            if (!targetCompleted && _automationTargetPoint != null)
            {
                try { targetCompleted = !_automationTargetPoint.HasBlock; }
                catch { }
            }
            ContinueAutomationSweep(action, type, targetCompleted, false);
            return;
        }

        if (type == AutomationTypeAutoChop)
        {
            var targetCompleted = _automationTargetPoint == null ||
                                  !IsAutomationTreePoint(_automationTargetPoint);
            ContinueAutomationSweep(action, type, targetCompleted, false);
            return;
        }

        if (type == AutomationTypeAutoHarvest)
        {
            var targetCompleted = ai != null && ai.status == AIAct.Status.Success;
            if (!targetCompleted && _automationTargetPoint != null)
                targetCompleted = !CanAutomationHarvestPoint(pc, _automationTargetPoint);
            ContinueAutomationSweep(action, type, targetCompleted, false);
            return;
        }

        if (type == AutomationTypeAutoFertilize)
        {
            var targetCompleted = _automationTargetPoint != null &&
                                  IsAutomationFertilizedPoint(_automationTargetPoint);
            ContinueAutomationFertilize(action, targetCompleted, false);
            return;
        }

        if (type == AutomationTypeSearchContainers)
        {
            var targetCompleted = TryDumpAutomationContainerContents(pc, _automationTargetThing);
            ContinueAutomationSearchContainers(action, targetCompleted, false);
            return;
        }

        if (type == AutomationTypeAutoInteract)
        {
            if (!_automationInteractionPerformedForTarget)
            {
                var invoked = TryPerformAutomationInteraction(pc, _automationTargetThing);
                _automationInteractionPerformedForTarget = invoked;
                if (invoked && _automationActionAi != null && ReferenceEquals(pc.ai, _automationActionAi) && pc.ai.IsRunning)
                    return;
                ContinueAutomationInteract(action, invoked, false);
                return;
            }

            var interactionCompleted = _automationActionAi == null ||
                                       _automationActionAi.status == AIAct.Status.Success;
            ContinueAutomationInteract(action, interactionCompleted, false);
            return;
        }

        if (type == AutomationTypeAutoKill)
        {
            var target = _automationTargetChara;
            var targetCompleted = target == null;
            try
            {
                targetCompleted = targetCompleted || target!.isDead || !target.ExistsOnMap;
            }
            catch { }
            if (targetCompleted)
            {
                ContinueAutomationSweep(action, type, true, false);
                return;
            }

            if (!IsAutomationEnemyCandidate(pc, target!))
            {
                ContinueAutomationSweep(action, type, false, false);
                return;
            }

            if (_automationKillApproaching)
            {
                var visible = FindNearestAutomationEnemy(pc, int.MaxValue, _automationSkippedEnemyUids, true);
                if (visible != null)
                {
                    StartAutomationKillCombat(pc, visible);
                    return;
                }

                var approachTarget = _automationTargetChara;
                if (approachTarget != null)
                {
                    var approachUid = approachTarget.uid;
                    _automationEnemyFailureCounts.TryGetValue(approachUid, out var approachFailures);
                    approachFailures++;
                    _automationEnemyFailureCounts[approachUid] = approachFailures;
                    if (TryRetryAutomationKillTarget(pc, approachTarget, approachFailures))
                        return;
                }
                ContinueAutomationSweep(action, type, false, false);
                return;
            }

            var uid = _automationTargetChara!.uid;
            _automationEnemyFailureCounts.TryGetValue(uid, out var failures);
            failures++;
            _automationEnemyFailureCounts[uid] = failures;
            if (TryRetryAutomationKillTarget(pc, _automationTargetChara, failures))
                return;

            ContinueAutomationSweep(action, type, false, false);
            return;
        }

        if (type == AutomationTypeNextFloor && !_automationZoneMoveRequested)
        {
            var stairs = _automationTargetThing?.trait as TraitStairsDown;
            if (stairs != null && _automationTargetThing != null && _automationTargetThing.ExistsOnMap && pc.Dist(_automationTargetThing) <= 1)
            {
                _automationZoneMoveRequested = true;
                stairs.MoveZone(true);
                return;
            }
        }

        if (type == AutomationTypeNextFloor && _automationZoneMoveRequested)
            return;

        if (type == AutomationTypePickupByValue)
        {
            var backpackWasOverflowing = false;
            try
            {
                var target = _automationTargetThing;
                var targetAlreadyLeftMap = target == null || target.isDestroyed || !target.ExistsOnMap;
                if (pc.held != null && (ReferenceEquals(pc.held, _automationTargetThing) || targetAlreadyLeftMap))
                {
                    backpackWasOverflowing = pc.things != null && pc.things.IsOverflowing();
                    pc.PickHeld();
                }
                else if (target != null && !targetAlreadyLeftMap && ai != null && pc.Dist(target) <= 1)
                {
                    pc.Pick(target, false, true);
                    var targetStillOnMap = !target.isDestroyed && target.ExistsOnMap;
                    backpackWasOverflowing = targetStillOnMap && (pc.things == null || pc.things.IsFull());
                }
            }
            catch { }

            var targetPicked = false;
            try
            {
                targetPicked = _automationTargetThing == null ||
                               _automationTargetThing.isDestroyed ||
                               !_automationTargetThing.ExistsOnMap;
            }
            catch { }

            var pickupCompletedButTargetRemained = false;
            try
            {
                pickupCompletedButTargetRemained = !targetPicked &&
                                                   _automationTargetThing != null &&
                                                   ai != null &&
                                                   pc.Dist(_automationTargetThing) <= 1;
            }
            catch { }

            ContinueAutomationPickup(action, targetPicked, false,
                !targetPicked && (backpackWasOverflowing || pickupCompletedButTargetRemained));
            return;
        }

        var success = ai == null || ai.status == AIAct.Status.Success;
        FinishAutomationAction(success, success
            ? AutomationText("执行完成", "Action completed", "実行完了", "Действие завершено")
            : AutomationText("游戏操作未完成", "Game action did not complete", "ゲーム操作が完了しませんでした", "Игровое действие не завершено"));
    }
}
