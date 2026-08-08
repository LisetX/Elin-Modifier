using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

internal sealed partial class ThreatOverlayModule
{
    internal void PrepareLockedDecision(Chara? actor)
    {
        if (!HostileThreatPredecisionLockEnabled || _executingLockedThreatDecision || actor == null ||
            !TryGetTrackedThreatUid(actor, out var uid))
        {
            return;
        }

        AIAct? ai;
        AIAct? current;
        try
        {
            ai = actor.ai;
            current = ai?.Current;
        }
        catch
        {
            return;
        }

        var combat = current as GoalCombat ?? ai as GoalCombat;
        if (combat == null)
        {
            RemoveLockedDecision(uid);
            return;
        }

        if (TryKeepActiveLockedSequence(uid, actor, combat))
            return;

        var target = GetThreatCombatTarget(actor, ai, current);
        EnsureLockedDecisionSet(uid, actor, combat, target);
    }
    internal bool TryExecuteLockedDecision(GoalCombat? combat, out bool result)
    {
        result = false;
        if (!HostileThreatPredecisionLockEnabled || _executingLockedThreatDecision || combat?.owner == null)
            return false;

        var actor = combat.owner;
        if (!TryGetTrackedThreatUid(actor, out var uid))
            return false;

        LockedThreatDecisionSet? decisionSet;
        Chara? target;
        if (_lockedThreatDecisions.TryGetValue(uid, out var activeSet) &&
            activeSet.ExecutionScenario >= 0 &&
            HasRemainingLockedSteps(activeSet) &&
            IsLockedDecisionSetValid(activeSet, actor, combat, activeSet.CombatTarget))
        {
            decisionSet = activeSet;
            target = activeSet.CombatTarget;
            RestoreLockedCombatTarget(actor, combat, target);
        }
        else
        {
            target = GetThreatCombatTarget(actor, combat, combat);
            decisionSet = EnsureLockedDecisionSet(uid, actor, combat, target);
        }
        if (decisionSet == null || !IsLockedDecisionSetValid(decisionSet, actor, combat, target))
        {
            RemoveLockedDecision(uid);
            return false;
        }

        if (decisionSet.ExecutionScenario < 0)
            decisionSet.ExecutionScenario = ResolveExecutedThreatScenario(decisionSet);
        var sequence = SelectLockedSequence(decisionSet, decisionSet.ExecutionScenario);
        if (sequence == null || sequence.NextIndex >= sequence.Steps.Count)
        {
            RemoveLockedDecision(uid);
            return false;
        }

        EnsureCurrentLockedDecisionExecutable(decisionSet, sequence, allowEquipRanged: true);
        if (sequence.NextIndex >= sequence.Steps.Count)
        {
            RemoveLockedDecision(uid);
            return false;
        }

        var succeeded = false;
        _executingLockedThreatDecision = true;
        try
        {
            var stepIndex = sequence.NextIndex++;
            var decision = sequence.Steps[stepIndex];
            succeeded = ExecuteLockedThreatDecision(actor, combat, decision);
            if (!succeeded)
            {
                var fallback = CreateExecutableLockedFallback(
                    decisionSet,
                    decision.Act,
                    allowEquipRanged: true);
                sequence.Steps[stepIndex] = fallback;
                BumpThreatDecisionVersion(uid);
                succeeded = ExecuteLockedThreatDecision(actor, combat, fallback);
            }
        }
        catch
        {
            succeeded = false;
        }
        finally
        {
            _executingLockedThreatDecision = false;
        }

        decisionSet.ActorX = actor.pos?.x ?? decisionSet.ActorX;
        decisionSet.ActorZ = actor.pos?.z ?? decisionSet.ActorZ;
        if (sequence.NextIndex >= sequence.Steps.Count)
            _lockedThreatDecisions.Remove(uid);
        BumpThreatDecisionVersion(uid);

        result = succeeded;
        return true;
    }
    private bool TryKeepActiveLockedSequence(int uid, Chara actor, GoalCombat combat)
    {
        if (!_lockedThreatDecisions.TryGetValue(uid, out var decisionSet) ||
            decisionSet.ExecutionScenario < 0 ||
            !HasRemainingLockedSteps(decisionSet) ||
            !IsLockedDecisionSetValid(decisionSet, actor, combat, decisionSet.CombatTarget))
        {
            return false;
        }

        RestoreLockedCombatTarget(actor, combat, decisionSet.CombatTarget);
        return true;
    }
    private bool HasPendingLockedSequence(int uid)
    {
        return uid > 0 &&
               _lockedThreatDecisions.TryGetValue(uid, out var decisionSet) &&
               decisionSet.ExecutionScenario >= 0 &&
               HasRemainingLockedSteps(decisionSet);
    }
    private static bool HasRemainingLockedSteps(LockedThreatDecisionSet decisionSet)
    {
        var sequence = SelectLockedSequence(decisionSet, decisionSet.ExecutionScenario);
        return sequence != null && sequence.NextIndex < sequence.Steps.Count;
    }
    private static void RestoreLockedCombatTarget(
        Chara actor,
        GoalCombat combat,
        Chara target)
    {
        if (target.isDead || !target.ExistsOnMap)
            return;
        actor.enemy = target;
        combat.tc = target;
    }
    private static bool ExecuteLockedThreatDecision(
        Chara actor,
        GoalCombat combat,
        LockedThreatDecision decision)
    {
        switch (decision.Kind)
        {
            case LockedThreatDecisionKind.Move:
                if (decision.MovePoint != null)
                {
                    var destination = new Point(decision.MovePoint.x, decision.MovePoint.z);
                    return actor.TryMove(destination, false) != Card.MoveResult.Fail;
                }
                return false;
            case LockedThreatDecisionKind.Ranged:
                if (!CanExecuteLockedThreatDecision(actor, combat, decision, allowEquipRanged: true))
                    return false;
                return combat.TryUseRanged(actor.Dist(decision.CombatTarget));
            case LockedThreatDecisionKind.Ability:
                if (!CanExecuteLockedThreatDecision(actor, combat, decision, allowEquipRanged: false) ||
                    decision.Act == null)
                    return false;
                var abilityTarget = decision.AbilityTarget;
                if (abilityTarget == null || abilityTarget.Chara?.isDead == true)
                    abilityTarget = decision.CombatTarget;
                return actor.UseAbility(
                    decision.Act,
                    abilityTarget,
                    null,
                    decision.PartyTarget);
            default:
                return false;
        }
    }
    private void EnsureCurrentLockedDecisionExecutable(
        LockedThreatDecisionSet decisionSet,
        LockedThreatDecisionSequence sequence,
        bool allowEquipRanged)
    {
        if (sequence.NextIndex < 0 || sequence.NextIndex >= sequence.Steps.Count)
            return;

        var current = sequence.Steps[sequence.NextIndex];
        if (CanExecuteLockedThreatDecision(
                decisionSet.Actor,
                decisionSet.Combat,
                current,
                allowEquipRanged))
        {
            return;
        }

        sequence.Steps[sequence.NextIndex] = CreateExecutableLockedFallback(
            decisionSet,
            current.Act,
            allowEquipRanged);
        BumpThreatDecisionVersion(decisionSet.ActorUid);
    }
    private LockedThreatDecision CreateExecutableLockedFallback(
        LockedThreatDecisionSet decisionSet,
        Act? excludedAct,
        bool allowEquipRanged)
    {
        var actor = decisionSet.Actor;
        var combat = decisionSet.Combat;
        var target = decisionSet.CombatTarget;
        LockedThreatDecision? best = null;
        var bestScore = int.MinValue;

        try
        {
            var abilities = combat.abilities;
            if (abilities != null)
            {
                for (var i = 0; i < abilities.Count; i++)
                {
                    var item = abilities[i];
                    var act = item?.act;
                    if (act == null || ReferenceEquals(act, excludedAct) ||
                        excludedAct != null && act.id == excludedAct.id)
                    {
                        continue;
                    }

                    var abilityTarget = ResolveLockedAbilityTarget(actor, target, act);
                    var candidate = CreateLockedAbilityDecision(
                        decisionSet.ActorUid,
                        actor,
                        combat,
                        target,
                        item,
                        abilityTarget);
                    if (!CanExecuteLockedThreatDecision(
                            actor,
                            combat,
                            candidate,
                            allowEquipRanged))
                    {
                        continue;
                    }

                    var score = item!.priority > 0
                        ? item.priority
                        : Math.Max(1, item.chance) + item.priorityMod;
                    if (score <= bestScore)
                        continue;
                    best = candidate;
                    bestScore = score;
                }
            }
        }
        catch
        {
        }

        if (best != null)
            return best;

        var melee = CreateLockedAbilityDecision(
            decisionSet.ActorUid,
            actor,
            combat,
            target,
            null,
            target,
            ACT.Melee,
            T("近战攻击", "Melee attack"));
        if ((excludedAct == null || excludedAct.id != ACT.Melee.id) &&
            CanExecuteLockedThreatDecision(actor, combat, melee, allowEquipRanged: false))
        {
            return melee;
        }

        var ranged = CreateLockedAbilityDecision(
            decisionSet.ActorUid,
            actor,
            combat,
            target,
            null,
            target,
            ACT.Ranged,
            T("远程攻击", "Ranged attack"));
        if ((excludedAct == null || excludedAct.id != ACT.Ranged.id) &&
            CanExecuteLockedThreatDecision(actor, combat, ranged, allowEquipRanged))
        {
            return ranged;
        }

        try
        {
            var movePoint = GetThreatApproachPoint(actor, target.pos);
            if (movePoint != null && IsLockedThreatWalkablePoint(actor, movePoint))
            {
                return CreateLockedMoveDecision(
                    decisionSet.ActorUid,
                    actor,
                    combat,
                    target,
                    movePoint);
            }
        }
        catch
        {
        }

        return CreateLockedAbilityDecision(
            decisionSet.ActorUid,
            actor,
            combat,
            target,
            null,
            actor,
            ACT.Wait,
            T("等待", "Wait"));
    }
}
