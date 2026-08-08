using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class ThreatOverlayModule
{
    internal void Initialize()
    {
        if (_threatOverlayInitialized)
            return;
        try
        {
            _threatRoot = new GameObject("ElinModifier.ThreatOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(_threatRoot);
            _threatCanvas = _threatRoot.GetComponent<Canvas>();
            _threatCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _threatCanvas.sortingOrder = 31000;
            var scaler = _threatRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _threatCanvasRect = (RectTransform)_threatRoot.transform;
            _threatOverlayInitialized = true;
            _threatRoot.SetActive(_hostileThreatMarker);
        }
        catch (Exception ex)
        {
            _threatOverlayInitialized = false;
            _log = "Threat uGUI init failed: " + ex.Message;
        }
    }
    internal void Shutdown()
    {
        if (_threatRoot != null)
            UnityEngine.Object.Destroy(_threatRoot);
        _threatRoot = null;
        _threatCanvas = null;
        _threatCanvasRect = null;
        _threatMarkers.Clear();
        _threatByUid.Clear();
        _threatPool.Clear();
        _threatSeen.Clear();
        _capturedThreatActions.Clear();
        _threatDecisionVersions.Clear();
        ClearLockedDecisions();
        _nextThreatActionSerial = 0;
        _threatOverlayInitialized = false;
        _threatCachedCamera = null;
        _threatCameraCacheValid = false;
        _threatLastPositionRefreshTime = -9999f;
        _threatGameUiCanvas = null;
        _threatGameUiCamera = null;
        _threatGameUiSortingLayerId = int.MinValue;
        _threatGameUiTargetDisplay = -1;
    }
    internal bool IsInitialized()
    {
        return _threatOverlayInitialized && _threatRoot != null;
    }
    internal void NotifyDirty()
    {
        _threatOverlayDirty = true;
    }
    internal void ClearPredictionEvents()
    {
        _capturedThreatActions.Clear();
        _threatDecisionVersions.Clear();
        ClearLockedDecisions();
        _nextThreatActionSerial = 0;
        for (var i = 0; i < _threatMarkers.Count; i++)
            ResetThreatPredictionCache(_threatMarkers[i]);
        InvalidateThreatVitals();
    }
    internal void RecordConfirmedMove(Chara? actor, Point? destination, Card.MoveResult result)
    {
        if (_executingLockedThreatDecision)
            return;
        if (result != Card.MoveResult.Success || actor == null || destination == null ||
            !TryGetTrackedThreatUid(actor, out var uid))
        {
            return;
        }

        RemoveLockedDecision(uid);
        CaptureThreatAction(uid, T("移动", "Move"), destination);
    }
    internal void RecordConfirmedAct(Chara? actor, Act? act, bool succeeded)
    {
        if (_executingLockedThreatDecision)
            return;
        if (!succeeded || actor == null || act == null || !TryGetTrackedThreatUid(actor, out var uid))
            return;

        RemoveLockedDecision(uid);
        var name = SafeThreatActName(act);
        if (IsThreatVoidText(name))
            name = act is ActMelee
                ? T("近战攻击", "Melee attack")
                : act is ActRanged
                    ? T("远程攻击", "Ranged attack")
                    : T("战斗决策中", "Selecting combat action");
        CaptureThreatAction(uid, name, null);
    }
    internal void RecordConfirmedAbility(Chara? actor, Act? act, bool succeeded)
    {
        RecordConfirmedAct(actor, act, succeeded);
    }
    internal void RecordSelectedAiAction(AIAct? parent, AIAct? selected)
    {
        if (_executingLockedThreatDecision)
            return;
        var actor = parent?.owner ?? selected?.owner;
        if (actor == null || selected == null || !TryGetTrackedThreatUid(actor, out var uid))
            return;

        BumpThreatDecisionVersion(uid);
        if (selected is GoalCombat)
            return;

        RemoveLockedDecision(uid);
        Point? movePoint = null;
        string name;
        if (selected.IsMoveAI || selected is AI_Goto)
        {
            name = T("移动", "Move");
            if (TryGetThreatNextMovePoint(actor, selected.Current, out var next))
                movePoint = next;
        }
        else if (selected is AI_Wait || selected is GoalEndTurn)
        {
            name = T("等待", "Wait");
        }
        else
        {
            name = SafeThreatActionText(selected.Current);
            if (IsThreatIdleText(name))
                name = SafeThreatActionText(selected);
        }

        if (!IsThreatVoidText(name))
            CaptureThreatAction(uid, name, movePoint);
    }
    internal void RecordAiGoalChanged(Chara? actor, AIAct? goal)
    {
        if (_executingLockedThreatDecision)
            return;
        if (actor == null || !TryGetTrackedThreatUid(actor, out var uid))
            return;

        RemoveLockedDecision(uid);
        BumpThreatDecisionVersion(uid);
        if (goal != null && goal is not GoalCombat)
            RecordSelectedAiAction(goal, goal);
    }
    internal void RecordCombatEvaluation(GoalCombat? combat)
    {
        if (_executingLockedThreatDecision)
            return;
        var actor = combat?.owner;
        if (actor != null && TryGetTrackedThreatUid(actor, out var uid))
            BumpThreatDecisionVersion(uid);
    }
    internal void RecordThreatTargetChanged(Chara? actor)
    {
        if (_executingLockedThreatDecision)
            return;
        if (actor != null && TryGetTrackedThreatUid(actor, out var uid))
        {
            if (HasPendingLockedSequence(uid))
                return;
            RemoveLockedDecision(uid);
            BumpThreatDecisionVersion(uid);
        }
    }
    private bool TryGetTrackedThreatUid(Chara actor, out int uid)
    {
        uid = 0;
        if (!_hostileThreatMarker || !_hostileThreatBehaviorPrediction)
            return false;

        uid = GetThreatUid(actor);
        return uid > 0 && _threatByUid.ContainsKey(uid);
    }
    private void CaptureThreatAction(int uid, string name, Point? movePoint)
    {
        if (uid <= 0 || IsThreatVoidText(name))
            return;

        var frame = Time.frameCount;
        var now = SchedulerNow;
        if (_capturedThreatActions.TryGetValue(uid, out var existing) &&
            existing.Frame == frame &&
            string.Equals(existing.Name, name, StringComparison.Ordinal) &&
            SameThreatPoint(existing.MovePoint, movePoint))
        {
            return;
        }

        var captured = existing ?? new CapturedThreatAction();
        captured.Serial = unchecked(++_nextThreatActionSerial);
        captured.Frame = frame;
        captured.Time = now;
        captured.Name = name.Trim();
        captured.MovePoint = movePoint == null ? null : new Point(movePoint.x, movePoint.z);
        _capturedThreatActions[uid] = captured;
        BumpThreatDecisionVersion(uid);
    }
    private void BumpThreatDecisionVersion(int uid)
    {
        _threatDecisionVersions.TryGetValue(uid, out var version);
        _threatDecisionVersions[uid] = unchecked(version + 1);
        InvalidateThreatVitals();
    }
}
