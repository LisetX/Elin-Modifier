using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

internal sealed partial class ThreatOverlayModule
{
    private static bool CanExecuteLockedThreatDecision(
        Chara actor,
        GoalCombat combat,
        LockedThreatDecision decision,
        bool allowEquipRanged)
    {
        try
        {
            if (actor == null || actor.isDead || !actor.IsAliveInCurrentZone)
                return false;

            if (decision.Kind == LockedThreatDecisionKind.Move)
            {
                return decision.MovePoint != null &&
                       IsLockedThreatWalkablePoint(actor, decision.MovePoint);
            }

            var combatTarget = decision.CombatTarget;
            if (combatTarget == null || combatTarget.isDead || !combatTarget.ExistsOnMap)
                return false;

            if (decision.Kind == LockedThreatDecisionKind.Ranged)
            {
                if (allowEquipRanged && !actor.TryEquipRanged())
                    return false;
                var weapon = actor.ranged;
                return weapon != null &&
                       weapon.CanAutoFire(actor, combatTarget) &&
                       ACT.Ranged.CanPerform(actor, combatTarget, combatTarget.pos);
            }

            var act = decision.Act;
            if (act == null || actor.HasCooldown(act.id))
                return false;

            var abilityTarget = decision.AbilityTarget;
            if (abilityTarget == null || abilityTarget.Chara?.isDead == true)
                abilityTarget = combatTarget;
            if (abilityTarget == null)
                return false;

            var partyCount = 1;
            if (decision.PartyTarget)
            {
                partyCount = 0;
                var hostileParty = act.IsTargetHostileParty();
                var charas = GameAccess.World.CurrentCharacters;
                if (charas != null)
                {
                    for (var i = 0; i < charas.Count; i++)
                    {
                        var candidate = charas[i];
                        if (candidate == null || candidate.isDead)
                            continue;
                        if (hostileParty
                                ? actor.IsHostile(candidate)
                                : ReferenceEquals(candidate, actor) ||
                                  actor.IsFriendOrAbove(candidate))
                        {
                            partyCount++;
                        }
                    }
                }
                if (partyCount <= 0)
                    return false;
            }

            var cost = act.GetCost(actor);
            if (cost.cost > 0)
            {
                var required = cost.cost;
                if (!act.TargetType.ForceParty && partyCount > 1)
                {
                    var multiplier = actor.IsPC
                        ? partyCount * 100
                        : 50 + partyCount * 50;
                    required = required * multiplier / 100;
                }

                if (cost.type == Act.CostType.MP && actor.mana.value < required)
                    return false;
                if (cost.type == Act.CostType.SP && actor.stamina.value < required)
                    return false;
            }

            var targetPoint = abilityTarget.pos ?? combatTarget.pos;
            return act.ValidatePerform(actor, abilityTarget, targetPoint) &&
                   act.CanPerform(actor, abilityTarget, targetPoint);
        }
        catch
        {
            return false;
        }
    }
    private bool TryGetOrCreateLockedThreatAction(
        Chara chara,
        AIAct? ai,
        AIAct? current,
        Chara? target,
        out CapturedThreatAction? action,
        out int serial)
    {
        action = null;
        serial = -1;
        if (!HostileThreatPredecisionLockEnabled || !TryGetTrackedThreatUid(chara, out var uid))
            return false;

        var combat = current as GoalCombat ?? ai as GoalCombat;
        if (combat == null)
        {
            RemoveLockedDecision(uid);
            return false;
        }

        target ??= GetThreatCombatTarget(chara, ai, current);
        var decisionSet = EnsureLockedDecisionSet(uid, chara, combat, target);
        if (decisionSet == null)
            return false;

        var scenario = decisionSet.ExecutionScenario >= 0
            ? decisionSet.ExecutionScenario
            : _threatPreviewScenario;
        var sequence = SelectLockedSequence(decisionSet, scenario);
        if (sequence == null || sequence.NextIndex >= sequence.Steps.Count)
            return false;

        if (decisionSet.ExecutionScenario >= 0)
            EnsureCurrentLockedDecisionExecutable(decisionSet, sequence, allowEquipRanged: false);
        if (sequence.NextIndex >= sequence.Steps.Count)
            return false;

        var decision = sequence.Steps[sequence.NextIndex];
        var lockedNames = new List<string>(sequence.Steps.Count - sequence.NextIndex);
        for (var i = sequence.NextIndex; i < sequence.Steps.Count; i++)
            lockedNames.Add(sequence.Steps[i].Name);

        serial = decision.Serial;
        action = new CapturedThreatAction
        {
            Serial = decision.Serial,
            Frame = decision.Frame,
            Time = SchedulerNow,
            Name = decision.Name,
            MovePoint = decision.MovePoint == null
                ? null
                : new Point(decision.MovePoint.x, decision.MovePoint.z),
            LockedSequence = lockedNames
        };
        return true;
    }
    private LockedThreatDecisionSet? EnsureLockedDecisionSet(
        int uid,
        Chara actor,
        GoalCombat combat,
        Chara? target)
    {
        if (!HostileThreatPredecisionLockEnabled || uid <= 0 || target == null)
            return null;

        if (_lockedThreatDecisions.TryGetValue(uid, out var existing))
        {
            if (IsLockedDecisionSetValid(existing, actor, combat, target))
                return existing;
            _lockedThreatDecisions.Remove(uid);
        }

        var created = CreateLockedDecisionSet(uid, actor, combat, target);
        if (created == null)
            return null;

        _lockedThreatDecisions[uid] = created;
        BumpThreatDecisionVersion(uid);
        return created;
    }
    private LockedThreatDecisionSet? CreateLockedDecisionSet(
        int uid,
        Chara actor,
        GoalCombat combat,
        Chara target)
    {
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            if (actor.isDead || target.isDead || actor.pos == null || target.pos == null ||
                pc?.pos == null)
            {
                return null;
            }

            if (combat.abilities == null)
            {
                combat.abilities = new List<GoalCombat.ItemAbility>();
                combat.BuildAbilityList();
            }

            var actionCount = GetThreatPredictedActionCount(actor);
            var decisionSet = new LockedThreatDecisionSet
            {
                ActorUid = uid,
                Actor = actor,
                Combat = combat,
                CombatTarget = target,
                ActorX = actor.pos.x,
                ActorZ = actor.pos.z,
                PcOriginX = pc.pos.x,
                PcOriginZ = pc.pos.z,
                PcActionEpoch = _pcActionEpoch,
                PlannedActionCount = actionCount,
                TargetIsPc = ReferenceEquals(target, pc)
            };

            var decisionSeed = GameAccess.Random.Next(1000000);
            for (var scenario = 0; scenario < LockedThreatScenarioCount; scenario++)
            {
                var hypotheticalTarget = scenario == LockedThreatStationaryScenario
                    ? new Point(target.pos.x, target.pos.z)
                    : decisionSet.TargetIsPc
                        ? CreateThreatScenarioPoint(decisionSet.PcOriginX, decisionSet.PcOriginZ, scenario)
                        : new Point(target.pos.x, target.pos.z);
                decisionSet.Sequences[scenario] = CreateLockedDecisionSequence(
                    uid,
                    actor,
                    combat,
                    target,
                    hypotheticalTarget,
                    decisionSeed,
                    actionCount);
            }
            return decisionSet;
        }
        catch
        {
            return null;
        }
    }
    private LockedThreatDecisionSequence CreateLockedDecisionSequence(
        int uid,
        Chara actor,
        GoalCombat combat,
        Chara target,
        Point hypotheticalTarget,
        int decisionSeed,
        int actionCount)
    {
        var sequence = new LockedThreatDecisionSequence();
        var simulatedActor = new Point(actor.pos.x, actor.pos.z);
        var simulatedMana = SafeResourceValue(actor.mana);
        var simulatedStamina = SafeResourceValue(actor.stamina);

        for (var actionIndex = 0; actionIndex < actionCount; actionIndex++)
        {
            var decision = CreateLockedDecision(
                uid,
                actor,
                combat,
                target,
                simulatedActor,
                hypotheticalTarget,
                decisionSeed,
                actionIndex,
                simulatedMana,
                simulatedStamina);
            sequence.Steps.Add(decision);
            SimulateLockedThreatDecision(
                actor,
                decision,
                ref simulatedActor,
                ref simulatedMana,
                ref simulatedStamina);
        }
        return sequence;
    }
    private LockedThreatDecision CreateLockedDecision(
        int uid,
        Chara actor,
        GoalCombat combat,
        Chara target,
        Point simulatedActor,
        Point hypotheticalTarget,
        int decisionSeed,
        int actionIndex,
        int simulatedMana,
        int simulatedStamina)
    {
        try
        {
            var distance = simulatedActor.Distance(hypotheticalTarget);
            var desiredDistance = GetThreatDesiredCombatDistance(combat, target);
            var moveChance = Mathf.Clamp(combat.tactics?.ChanceMove ?? 0, 0, 100);
            var moveRoll = LockedThreatRoll(decisionSeed, actionIndex, 17, 100);
            if (distance != desiredDistance && moveChance > moveRoll)
            {
                var movePoint = distance > desiredDistance
                    ? GetLockedThreatApproachPoint(actor, simulatedActor, hypotheticalTarget)
                    : GetLockedThreatRetreatPoint(actor, simulatedActor, hypotheticalTarget);
                if (movePoint != null)
                    return CreateLockedMoveDecision(uid, actor, combat, target, movePoint);
            }

            GoalCombat.ItemAbility? selected = null;
            Card? selectedTarget = null;
            var selectedScore = int.MinValue;
            var abilities = combat.abilities;
            if (abilities != null)
            {
                for (var i = 0; i < abilities.Count; i++)
                {
                    var item = abilities[i];
                    var act = item?.act;
                    if (act == null)
                        continue;
                    var abilityTarget = ResolveLockedAbilityTarget(actor, target, act);
                    var score = EvaluateLockedAbilityScore(
                        actor,
                        combat,
                        target,
                        item!,
                        abilityTarget,
                        distance,
                        decisionSeed,
                        actionIndex,
                        simulatedMana,
                        simulatedStamina);
                    if (score <= selectedScore)
                        continue;
                    selected = item;
                    selectedTarget = abilityTarget;
                    selectedScore = score;
                }
            }

            if (selected?.act != null && selectedScore > 0)
            {
                return CreateLockedAbilityDecision(
                    uid,
                    actor,
                    combat,
                    target,
                    selected,
                    selectedTarget);
            }

            if (distance != desiredDistance)
            {
                var fallbackMove = distance > desiredDistance
                    ? GetLockedThreatApproachPoint(actor, simulatedActor, hypotheticalTarget)
                    : GetLockedThreatRetreatPoint(actor, simulatedActor, hypotheticalTarget);
                if (fallbackMove != null)
                    return CreateLockedMoveDecision(uid, actor, combat, target, fallbackMove);
            }
        }
        catch
        {
        }

        return CreateLockedAbilityDecision(
            uid,
            actor,
            combat,
            target,
            null,
            actor,
            ACT.Wait,
            T("等待", "Wait"));
    }
}
