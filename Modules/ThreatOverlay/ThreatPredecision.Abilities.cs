using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

internal sealed partial class ThreatOverlayModule
{
    private int EvaluateLockedAbilityScore(
        Chara actor,
        GoalCombat combat,
        Chara target,
        GoalCombat.ItemAbility item,
        Card abilityTarget,
        int distance,
        int decisionSeed,
        int actionIndex,
        int simulatedMana,
        int simulatedStamina)
    {
        var act = item.act;
        if (act == null || item.chance <= 0)
            return int.MinValue;
        if (LockedThreatRoll(decisionSeed, actionIndex, act.id * 31 + 3, 100) >= item.chance)
            return int.MinValue;

        SourceElement.Row? source;
        try
        {
            source = act.source;
        }
        catch
        {
            return int.MinValue;
        }
        if (source == null || source.abilityType == null || source.abilityType.Length == 0)
            return int.MinValue;

        var type = source.abilityType[0];
        var tactics = combat.tactics;
        var score = 0;
        try
        {
            if (actor.isBlind && act.HasTag("reqSight"))
                return int.MinValue;
            if (actor.isBerserk &&
                act is not ActMelee &&
                act is not ActRanged &&
                act is not ActBreathe &&
                act is not ActThrow)
            {
                return int.MinValue;
            }

            switch (type)
            {
                case "any":
                    score = 50;
                    break;
                case "item":
                    score = 20;
                    break;
                case "wait":
                    score = actor.IsPCParty ? 0 : 50;
                    break;
                case "melee":
                    if ((tactics?.source?.melee ?? 0) == 0 ||
                        distance > Math.Max(1, actor.body?.GetMeleeDistance() ?? 1))
                    {
                        return int.MinValue;
                    }
                    score = tactics?.P_Melee ?? 50;
                    if (actor.HasCondition<ConFear>())
                        score /= 2;
                    if (actor.isConfused)
                        score -= 10;
                    if (actor.isBlind)
                        score -= 10;
                    break;
                case "range":
                    if (LockedThreatRoll(decisionSeed, actionIndex, act.id * 31 + 5, 100) >
                        (tactics?.RangedChance ?? 0))
                    {
                        return int.MinValue;
                    }
                    score = tactics?.P_Range ?? 50;
                    if (actor.HasCondition<ConFear>())
                        score /= 2;
                    if (actor.isConfused)
                        score -= 10;
                    if (actor.isBlind)
                        score -= 10;
                    break;
                case "attack":
                case "dot":
                    score = (tactics?.P_Spell ?? 50) + (type == "dot" ? 10 : 0);
                    break;
                case "attackMelee":
                    if (distance > Math.Max(1, actor.body?.GetMeleeDistance() ?? 1))
                        return int.MinValue;
                    score = tactics?.P_Melee ?? 50;
                    break;
                case "attackArea":
                    score = (tactics?.P_Spell ?? 50) - 20;
                    break;
                case "teleport":
                    score = 40;
                    break;
                case "heal":
                case "hot":
                    {
                        var healTarget = abilityTarget.Chara ?? actor;
                        var maximum = Math.Max(1, healTarget.MaxHP);
                        var missingPercent = 100 - healTarget.hp * 100 / maximum;
                        score = missingPercent >= (type == "hot" ? 15 : 25)
                            ? (tactics?.P_Heal ?? 50) + missingPercent / 2
                            : 0;
                        break;
                    }
                case "buff":
                case "buffStats":
                    score = tactics?.P_Buff ?? 50;
                    break;
                case "cure":
                    score = Math.Max(0, (abilityTarget.Chara ?? actor).CountDebuff() * 30);
                    break;
                case "debuff":
                case "debuffStats":
                    score = tactics?.P_Debuff ?? 50;
                    break;
                case "ground":
                    score = 50;
                    break;
                case "summon":
                case "summonAlly":
                    score = tactics?.P_Summon ?? 50;
                    break;
                case "summonSpecial":
                case "summonSpecial2":
                    score = 1000;
                    break;
                case "song":
                case "taunt":
                    score = 50;
                    break;
                case "suicide":
                    score = actor.hp < Math.Max(1, actor.MaxHP) / 2 ? 100 : 0;
                    break;
                default:
                    return int.MinValue;
            }

            if (source.target == "Neighbor" && distance > 1)
                return int.MinValue;
            if (source.proc != null &&
                source.proc.Length > 1 &&
                source.proc[0] == "Debuff" &&
                target.HasCondition(source.proc[1]))
            {
                return int.MinValue;
            }

            if (act is Spell)
            {
                if (actor.HasCondition<ConSilence>())
                    score -= 30;
                if (actor.isConfused || actor.HasCondition<ConDim>())
                    score -= 10;
            }

            if (source.abilityType.Length > 1)
            {
                var modifierIndex = actor.IsPC && source.abilityType.Length > 2 ? 2 : 1;
                score += source.abilityType[modifierIndex].ToInt();
            }

            if (!CanPayLockedAbilityCost(actor, act, simulatedMana, simulatedStamina))
                return int.MinValue;
            if (score <= 0)
                return int.MinValue;

            score += item.priorityMod;
            var randomFactor = Math.Max(0, (tactics?.RandomFacotr ?? 0) + item.priorityMod);
            if (randomFactor > 0)
            {
                score += LockedThreatRoll(
                    decisionSeed,
                    actionIndex,
                    act.id * 31 + 11,
                    randomFactor);
            }
            return score;
        }
        catch
        {
            return int.MinValue;
        }
    }
    private static Card ResolveLockedAbilityTarget(Chara actor, Chara target, Act act)
    {
        try
        {
            if (act.TargetType.Range == TargetRange.Self)
                return actor;

            var abilityType = act.source?.abilityType;
            var type = abilityType != null && abilityType.Length > 0
                ? abilityType[0]
                : string.Empty;
            if (type == "heal" || type == "hot" || type == "buff" ||
                type == "buffStats" || type == "cure" || type == "song")
            {
                Chara best = actor;
                var bestMissingHp = Math.Max(0, actor.MaxHP - actor.hp);
                var charas = GameAccess.World.CurrentCharacters;
                if (charas != null)
                {
                    for (var i = 0; i < charas.Count; i++)
                    {
                        var candidate = charas[i];
                        if (candidate == null || candidate.isDead || !actor.IsFriendOrAbove(candidate))
                            continue;
                        var missingHp = Math.Max(0, candidate.MaxHP - candidate.hp);
                        if (missingHp <= bestMissingHp)
                            continue;
                        best = candidate;
                        bestMissingHp = missingHp;
                    }
                }
                return best;
            }
        }
        catch
        {
        }
        return target;
    }
    private static bool CanPayLockedAbilityCost(
        Chara actor,
        Act act,
        int simulatedMana,
        int simulatedStamina)
    {
        try
        {
            var cost = act.GetCost(actor);
            if (cost.cost <= 0)
                return true;
            if (!actor.IsPCFaction)
                return true;
            return cost.type switch
            {
                Act.CostType.MP => simulatedMana >= cost.cost,
                Act.CostType.SP => simulatedStamina >= cost.cost,
                _ => true
            };
        }
        catch
        {
            return true;
        }
    }
    private static void SimulateLockedThreatDecision(
        Chara actor,
        LockedThreatDecision decision,
        ref Point simulatedActor,
        ref int simulatedMana,
        ref int simulatedStamina)
    {
        if (decision.Kind == LockedThreatDecisionKind.Move && decision.MovePoint != null)
        {
            simulatedActor = new Point(decision.MovePoint.x, decision.MovePoint.z);
            return;
        }
        if (decision.Act == null)
            return;

        try
        {
            var cost = decision.Act.GetCost(actor);
            if (cost.cost <= 0)
                return;
            if (cost.type == Act.CostType.MP)
                simulatedMana = Math.Max(0, simulatedMana - cost.cost);
            else if (cost.type == Act.CostType.SP)
                simulatedStamina = Math.Max(0, simulatedStamina - cost.cost);
        }
        catch
        {
        }
    }
}
