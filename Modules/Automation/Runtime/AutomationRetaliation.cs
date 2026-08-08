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
    private void QueueAutomationRetaliation(Chara victim, Chara attacker)
    {
        if (!_automationRunning || victim == null || attacker == null)
            return;

        Chara? pc;
        try { pc = GetSafePc(); }
        catch { return; }
        if (pc == null || !ReferenceEquals(victim, pc) || !IsAutomationEnemyCandidate(pc, attacker))
            return;

        try
        {
            if (_automationRetaliationTarget != null &&
                _automationRetaliationTarget.uid == attacker.uid)
                return;
            for (var i = 0; i < _automationRetaliationQueue.Count; i++)
            {
                var queued = _automationRetaliationQueue[i];
                if (queued != null && queued.uid == attacker.uid)
                    return;
            }
            _automationRetaliationQueue.Add(attacker);
        }
        catch { }
    }
    private bool TryDequeueAutomationRetaliationTarget(Chara pc, out Chara? target)
    {
        target = null;
        while (_automationRetaliationQueue.Count > 0)
        {
            var candidate = _automationRetaliationQueue[0];
            _automationRetaliationQueue.RemoveAt(0);
            if (!IsAutomationEnemyCandidate(pc, candidate))
                continue;
            target = candidate;
            return true;
        }
        return false;
    }
    private void TickAutomationRetaliation()
    {
        var pc = GetSafePc();
        if (pc == null)
            return;

        if (!_automationRetaliating)
        {
            if (!TryDequeueAutomationRetaliationTarget(pc, out var queuedTarget) || queuedTarget == null)
                return;
            StartAutomationRetaliationTarget(pc, queuedTarget, true);
            return;
        }

        var target = _automationRetaliationTarget;
        if (target == null || target.isDead || !target.ExistsOnMap || !IsAutomationEnemyCandidate(pc, target))
        {
            CompleteAutomationRetaliation(pc);
            return;
        }

        var retaliationAi = _automationActionAi;
        if (retaliationAi != null && ReferenceEquals(pc.ai, retaliationAi) && retaliationAi.IsRunning)
            return;

        if (_automationKillApproaching)
        {
            var canSee = false;
            try { canSee = pc.CanSee(target); }
            catch { }
            if (canSee)
            {
                StartAutomationKillCombat(pc, target);
                return;
            }
        }

        _automationRetaliationFailureCount++;
        if (TryRetryAutomationKillTarget(pc, target, _automationRetaliationFailureCount))
            return;

        // The attacker had already managed to damage the player, so it is normally reachable.
        // Keep automation from getting permanently stuck if the map changes after the hit.
        CompleteAutomationRetaliation(pc);
    }
    private void StartAutomationRetaliationTarget(Chara pc, Chara target, bool captureResumeContext)
    {
        if (captureResumeContext)
        {
            _automationRetaliationResumeAction = _automationCurrentAction;
            _automationRetaliationResumeDelay = _automationCurrentAction == null
                ? Mathf.Max(0f, _automationNextActionAt - Time.unscaledTime)
                : 0f;
        }

        try
        {
            if (_automationActionAi != null && ReferenceEquals(pc.ai, _automationActionAi) && pc.ai.IsRunning)
                pc.ai.Cancel();
        }
        catch { }

        PrepareAutomationCombatEquipment(pc);
        _automationRetaliating = true;
        _automationRetaliationTarget = target;
        _automationRetaliationFailureCount = 0;
        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetPoint = null;
        _automationTargetChara = target;
        _automationKillApproaching = false;
        StartAutomationKillTarget(pc, target);
    }
    private void CompleteAutomationRetaliation(Chara pc)
    {
        try
        {
            if (_automationActionAi != null && ReferenceEquals(pc.ai, _automationActionAi) && pc.ai.IsRunning)
                pc.ai.Cancel();
        }
        catch { }

        _automationRetaliating = false;
        _automationRetaliationTarget = null;
        _automationRetaliationFailureCount = 0;
        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetPoint = null;
        _automationTargetChara = null;
        _automationKillApproaching = false;

        if (TryDequeueAutomationRetaliationTarget(pc, out var nextTarget) && nextTarget != null)
        {
            StartAutomationRetaliationTarget(pc, nextTarget, false);
            return;
        }

        var resumeAction = _automationRetaliationResumeAction;
        var resumeDelay = _automationRetaliationResumeDelay;
        _automationRetaliationResumeAction = null;
        _automationRetaliationResumeDelay = 0f;
        if (!_automationRunning)
            return;

        if (resumeAction != null && ReferenceEquals(_automationCurrentAction, resumeAction))
        {
            RestartAutomationCurrentAction(resumeAction);
            return;
        }

        _automationNextActionAt = Time.unscaledTime + Mathf.Max(0f, resumeDelay);
    }
    private void RestartAutomationCurrentAction(AutomationActionConfig action)
    {
        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetChara = null;
        _automationTargetPoint = null;
        _automationKillApproaching = false;
        _automationInteractionPerformedForTarget = false;
        _automationKillWaitingForEmptyRecheck = false;
        _automationZoneMoveRequested = false;
        _automationGameLoadRequested = false;
        _automationActionStartedAt = Time.unscaledTime;
        try { _automationStartZoneUid = GameAccess.World.CurrentZone?.uid ?? 0; }
        catch { _automationStartZoneUid = 0; }

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
}
