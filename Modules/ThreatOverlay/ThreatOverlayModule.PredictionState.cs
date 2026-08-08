using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class ThreatOverlayModule
{
    private void RefreshThreatPredictionState(ThreatMarker marker, Chara chara)
    {
        if (!_hostileThreatBehaviorPrediction)
        {
            HideThreatPrediction(marker);
            return;
        }

        if (!marker.Prediction.gameObject.activeSelf)
            marker.Prediction.gameObject.SetActive(true);
        RefreshThreatPrediction(marker, chara);
    }
    private static void HideThreatPrediction(ThreatMarker marker)
    {
        if (!marker.Prediction.gameObject.activeSelf && marker.PredictedMovePoint == null &&
            !marker.MoveCellRoot.activeSelf)
        {
            return;
        }

        marker.Prediction.gameObject.SetActive(false);
        ResetThreatPredictionCache(marker);
        HideThreatMoveCell(marker);
    }
    private static void ResetThreatPredictionCache(ThreatMarker marker)
    {
        marker.LastPrediction = string.Empty;
        marker.PredictedMovePoint = null;
        marker.PredictionAi = null;
        marker.PredictionCurrent = null;
        marker.PredictionStatus = default;
        marker.PredictionTurn = int.MinValue;
        marker.PredictionOwnerX = int.MinValue;
        marker.PredictionOwnerZ = int.MinValue;
        marker.PredictionTargetX = int.MinValue;
        marker.PredictionTargetZ = int.MinValue;
        marker.PredictionActionCount = -1;
        marker.PredictionDecisionVersion = -1;
        marker.PredictionCaptureSerial = -1;
        marker.PredictionLockSerial = -1;
        marker.PredictionCaptureActive = false;
    }
    private void RefreshThreatPrediction(ThreatMarker marker, Chara chara)
    {
        AIAct? ai;
        AIAct? current;
        Chara? target;
        Point? ownerPoint;
        try
        {
            ai = chara.ai;
            current = ai?.Current;
            target = GetThreatCombatTarget(chara, ai, current);
            ownerPoint = chara.pos;
        }
        catch
        {
            ai = null;
            current = null;
            target = null;
            ownerPoint = null;
        }

        var ownerX = ownerPoint?.x ?? int.MinValue;
        var ownerZ = ownerPoint?.z ?? int.MinValue;
        var targetX = target?.pos?.x ?? int.MinValue;
        var targetZ = target?.pos?.z ?? int.MinValue;
        var turn = SafeThreatTurn(chara);
        var status = ai?.status ?? default;
        var actionCount = GetThreatPredictedActionCount(chara);
        var uid = GetThreatUid(chara);
        var lockActive = TryGetOrCreateLockedThreatAction(
            chara,
            ai,
            current,
            target,
            out var lockedAction,
            out var lockSerial);
        CapturedThreatAction? capturedAction = null;
        var captureActive = false;
        if (lockActive)
            capturedAction = lockedAction;
        else
            captureActive = TryGetActiveThreatAction(uid, out capturedAction);
        _threatDecisionVersions.TryGetValue(uid, out var decisionVersion);
        var captureSerial = captureActive && capturedAction != null ? capturedAction.Serial : -1;
        if (ReferenceEquals(marker.PredictionAi, ai) &&
            ReferenceEquals(marker.PredictionCurrent, current) &&
            marker.PredictionStatus == status &&
            marker.PredictionTurn == turn &&
            marker.PredictionOwnerX == ownerX &&
            marker.PredictionOwnerZ == ownerZ &&
            marker.PredictionTargetX == targetX &&
            marker.PredictionTargetZ == targetZ &&
            marker.PredictionActionCount == actionCount &&
            marker.PredictionDecisionVersion == decisionVersion &&
            marker.PredictionCaptureSerial == captureSerial &&
            marker.PredictionLockSerial == lockSerial &&
            marker.PredictionCaptureActive == captureActive)
        {
            return;
        }

        marker.PredictionAi = ai;
        marker.PredictionCurrent = current;
        marker.PredictionStatus = status;
        marker.PredictionTurn = turn;
        marker.PredictionOwnerX = ownerX;
        marker.PredictionOwnerZ = ownerZ;
        marker.PredictionTargetX = targetX;
        marker.PredictionTargetZ = targetZ;
        marker.PredictionActionCount = actionCount;
        marker.PredictionDecisionVersion = decisionVersion;
        marker.PredictionCaptureSerial = captureSerial;
        marker.PredictionLockSerial = lockSerial;
        marker.PredictionCaptureActive = captureActive;

        var action = PredictThreatActions(
            chara,
            ai,
            current,
            target,
            actionCount,
            lockActive || captureActive ? capturedAction : null,
            out var movePoint);
        var text = action;
        if (!string.Equals(marker.LastPrediction, text, StringComparison.Ordinal))
        {
            marker.LastPrediction = text;
            marker.Prediction.text = text;
        }

        var pointChanged = !SameThreatPoint(marker.PredictedMovePoint, movePoint);
        if (!pointChanged)
            return;

        marker.PredictedMovePoint = movePoint;
        marker.PredictionVisualDirty = true;
        if (movePoint == null)
        {
            marker.MoveCell.ClearQuad();
            marker.MoveCellRoot.SetActive(false);
        }
    }
    private bool TryGetActiveThreatAction(int uid, out CapturedThreatAction? captured)
    {
        if (uid > 0 && _capturedThreatActions.TryGetValue(uid, out captured))
        {
            if (SchedulerNow - captured.Time <= 0.8f)
                return true;
            _capturedThreatActions.Remove(uid);
        }

        captured = null;
        return false;
    }
    private string PredictThreatActions(
        Chara chara,
        AIAct? ai,
        AIAct? current,
        Chara? target,
        int actionCount,
        CapturedThreatAction? captured,
        out Point? movePoint)
    {
        string first;
        string firstRaw;
        if (captured != null)
        {
            firstRaw = captured.Name;
            movePoint = captured.MovePoint == null
                ? null
                : new Point(captured.MovePoint.x, captured.MovePoint.z);
            first = FormatConfirmedThreatAction(firstRaw);
            if (captured.LockedSequence != null && captured.LockedSequence.Count > 0)
            {
                var lockedActions = new List<string>(captured.LockedSequence.Count);
                for (var i = 0; i < captured.LockedSequence.Count; i++)
                    lockedActions.Add(FormatConfirmedThreatAction(captured.LockedSequence[i]));
                return string.Join(" → ", lockedActions);
            }
        }
        else
        {
            firstRaw = PredictThreatAction(chara, ai, current, target, out movePoint);
            if (IsThreatCurrentActionConfirmed(ai, current))
            {
                first = FormatConfirmedThreatAction(firstRaw);
            }
            else
            {
                var combatForCandidates = current as GoalCombat ?? ai as GoalCombat;
                if (combatForCandidates != null && target != null)
                {
                    first = BuildThreatCandidateDisplay(
                        combatForCandidates,
                        chara,
                        target,
                        firstRaw,
                        out firstRaw,
                        out movePoint);
                }
                else
                {
                    var firstConfidence = EstimateThreatActionConfidence(
                        combatForCandidates,
                        chara,
                        target,
                        firstRaw,
                        movePoint != null);
                    first = FormatEstimatedThreatAction(firstRaw, firstConfidence);
                }
            }
        }

        if (actionCount <= 1)
            return first;

        var actions = new List<string>(Math.Min(actionCount, 10)) { first };
        var combat = current as GoalCombat ?? ai as GoalCombat;
        if (combat == null || target == null || target.pos == null || chara.pos == null)
        {
            for (var i = 1; i < actionCount; i++)
                actions.Add(FormatEstimatedThreatAction(firstRaw, 50));
            return string.Join(" → ", actions);
        }

        var simulatedDistance = chara.Dist(target);
        var desiredDistance = GetThreatDesiredCombatDistance(combat, target);
        if (movePoint != null)
            simulatedDistance = StepThreatDistance(simulatedDistance, desiredDistance);

        for (var i = 1; i < actionCount; i++)
        {
            var followUp = PredictThreatCombatFollowUp(combat, simulatedDistance, desiredDistance, out var moved);
            var confidence = EstimateThreatActionConfidence(
                combat,
                chara,
                target,
                followUp,
                moved);
            actions.Add(FormatEstimatedThreatAction(followUp, confidence));
            if (moved)
                simulatedDistance = StepThreatDistance(simulatedDistance, desiredDistance);
        }
        return string.Join(" → ", actions);
    }
    private static bool IsThreatCurrentActionConfirmed(AIAct? ai, AIAct? current)
    {
        if (ai == null || current == null)
            return false;
        if (current is GoalCombat || ReferenceEquals(current, ai) && ai is GoalCombat)
            return false;
        return current.status == AIAct.Status.Running;
    }
    private string FormatConfirmedThreatAction(string action)
    {
        return "[100%] " + action;
    }
    private string FormatEstimatedThreatAction(string action, int confidence)
    {
        return "[" + Mathf.Clamp(confidence, 1, 99)
               .ToString(CultureInfo.InvariantCulture) + "%] " + action;
    }
}
