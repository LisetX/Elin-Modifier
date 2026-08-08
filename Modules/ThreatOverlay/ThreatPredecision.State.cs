using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

internal sealed partial class ThreatOverlayModule
{
    // 0..8 are the PC's 3 x 3 movement intents. 9 is the explicit
    // no-movement branch used after the PC completes a non-movement action.
    private const int LockedThreatMovementScenarioCount = 9;
    private const int LockedThreatStationaryScenario = 9;
    private const int LockedThreatScenarioCount = 10;
    private const int LockedThreatCenterMovementScenario = 4;
    private enum LockedThreatDecisionKind
    {
        Move,
        Ability,
        Ranged
    }
    private sealed class LockedThreatDecision
    {
        public int Serial;
        public int Frame;
        public int ActorUid;
        public Chara Actor = null!;
        public GoalCombat Combat = null!;
        public Chara CombatTarget = null!;
        public LockedThreatDecisionKind Kind;
        public Act? Act;
        public Card? AbilityTarget;
        public Point? MovePoint;
        public bool PartyTarget;
        public string Name = string.Empty;
    }
    private sealed class LockedThreatDecisionSequence
    {
        public readonly List<LockedThreatDecision> Steps = new List<LockedThreatDecision>();
        public int NextIndex;
    }
    private sealed class LockedThreatDecisionSet
    {
        public int ActorUid;
        public Chara Actor = null!;
        public GoalCombat Combat = null!;
        public Chara CombatTarget = null!;
        public int ActorX;
        public int ActorZ;
        public int PcOriginX;
        public int PcOriginZ;
        public int PcActionEpoch;
        public int ExecutionScenario = -1;
        public int PlannedActionCount;
        public bool TargetIsPc;
        public readonly LockedThreatDecisionSequence?[] Sequences =
            new LockedThreatDecisionSequence?[LockedThreatScenarioCount];
    }
    private readonly Dictionary<int, LockedThreatDecisionSet> _lockedThreatDecisions =
        new Dictionary<int, LockedThreatDecisionSet>();
    private int _nextLockedThreatDecisionSerial;
    private int _threatPreviewScenario = LockedThreatStationaryScenario;
    private int _pcActionEpoch;
    private int _resolvedPcActionEpoch = -1;
    private int _resolvedPcActionScenario = LockedThreatStationaryScenario;
    private int _resolvedPcActionFrame = -1;
    private int _resolvedPcActionTurn = int.MinValue;
    private int _resolvedPcActionCount = int.MinValue;
    private bool _resolvedPcActionWasMove;
    private int _mouseThreatStepX;
    private int _mouseThreatStepZ;
    private int _mouseThreatDirectionFrame = -1;
    [ThreadStatic] private static bool _executingLockedThreatDecision;
    private bool HostileThreatPredecisionLockEnabled =>
        _hostileThreatMarker &&
        _hostileThreatBehaviorPrediction &&
        _host.ModuleHostileThreatPredecisionLock;
    internal void ClearLockedDecisions()
    {
        if (_lockedThreatDecisions.Count == 0 &&
            _nextLockedThreatDecisionSerial == 0 &&
            _threatPreviewScenario == LockedThreatStationaryScenario &&
            _pcActionEpoch == 0 &&
            _resolvedPcActionEpoch < 0)
        {
            return;
        }

        _lockedThreatDecisions.Clear();
        _nextLockedThreatDecisionSerial = 0;
        _threatPreviewScenario = LockedThreatStationaryScenario;
        _pcActionEpoch = 0;
        _resolvedPcActionEpoch = -1;
        _resolvedPcActionScenario = LockedThreatStationaryScenario;
        _resolvedPcActionFrame = -1;
        _resolvedPcActionTurn = int.MinValue;
        _resolvedPcActionCount = int.MinValue;
        _resolvedPcActionWasMove = false;
        _mouseThreatStepX = 0;
        _mouseThreatStepZ = 0;
        _mouseThreatDirectionFrame = -1;
        InvalidateThreatVitals();
    }
    internal void RecordMouseThreatDirection(int stepX, int stepZ)
    {
        if (!HostileThreatPredecisionLockEnabled)
            return;

        _mouseThreatStepX = Math.Max(-1, Math.Min(1, stepX));
        _mouseThreatStepZ = Math.Max(-1, Math.Min(1, stepZ));
        _mouseThreatDirectionFrame = Time.frameCount;
    }
    internal void RecordResolvedPlayerMove(
        Chara? actor,
        Point? origin,
        Card.MoveResult result)
    {
        if (!HostileThreatPredecisionLockEnabled || actor == null ||
            !ReferenceEquals(actor, GameAccess.Characters.PlayerCharacter) || origin == null || !origin.IsValid)
        {
            return;
        }

        var scenario = LockedThreatStationaryScenario;
        var moved = result == Card.MoveResult.Success &&
                    actor.pos != null &&
                    (actor.pos.x != origin.x || actor.pos.z != origin.z);
        if (moved)
        {
            scenario = ResolveThreatMovementScenario(
                origin.x,
                origin.z,
                actor.pos.x,
                actor.pos.z,
                LockedThreatStationaryScenario);
        }
        RecordResolvedPlayerAction(actor, scenario, moved);
    }
    internal void RecordResolvedPlayerAct(Chara? actor, bool succeeded)
    {
        if (!succeeded || !HostileThreatPredecisionLockEnabled || actor == null ||
            !ReferenceEquals(actor, GameAccess.Characters.PlayerCharacter))
        {
            return;
        }
        RecordResolvedPlayerAction(actor, LockedThreatStationaryScenario, moved: false);
    }
    private void RecordResolvedPlayerAction(Chara actor, int scenario, bool moved)
    {
        var frame = Time.frameCount;
        var turn = actor.turn;
        var actionCount = AM_Adv.actCount;
        var sameAction = _resolvedPcActionFrame == frame &&
                         _resolvedPcActionTurn == turn &&
                         _resolvedPcActionCount == actionCount;
        if (sameAction)
        {
            if (!moved || _resolvedPcActionWasMove)
                return;

            _resolvedPcActionScenario = scenario;
            _resolvedPcActionWasMove = true;
            ApplyResolvedPlayerScenario(_resolvedPcActionEpoch, scenario);
            return;
        }

        _resolvedPcActionEpoch = _pcActionEpoch;
        _pcActionEpoch = unchecked(_pcActionEpoch + 1);
        _resolvedPcActionScenario = scenario;
        _resolvedPcActionFrame = frame;
        _resolvedPcActionTurn = turn;
        _resolvedPcActionCount = actionCount;
        _resolvedPcActionWasMove = moved;
        ApplyResolvedPlayerScenario(_resolvedPcActionEpoch, scenario);
    }
    private void ApplyResolvedPlayerScenario(int epoch, int scenario)
    {
        var changed = false;
        foreach (var decisionSet in _lockedThreatDecisions.Values)
        {
            if (decisionSet.PcActionEpoch != epoch)
                continue;
            decisionSet.ExecutionScenario = scenario;
            changed = true;
        }
        if (changed)
            InvalidateThreatVitals();
    }
    private void RemoveLockedDecision(int uid)
    {
        if (uid > 0 && _lockedThreatDecisions.Remove(uid))
            InvalidateThreatVitals();
    }
    internal void RefreshLockedDecisionPreviewScenario()
    {
        var scenario = HostileThreatPredecisionLockEnabled
            ? ResolveMouseThreatScenario()
            : LockedThreatStationaryScenario;
        if (_threatPreviewScenario == scenario)
            return;

        _threatPreviewScenario = scenario;
        InvalidateThreatVitals();
    }
}
