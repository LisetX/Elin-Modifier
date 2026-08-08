using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class ThreatOverlayModule
{
    private string PredictThreatAction(Chara chara, AIAct? ai, AIAct? current, Chara? target, out Point? movePoint)
    {
        movePoint = null;
        try
        {
            if (ai == null)
                return T("待机", "Idle");

            current ??= ai;
            if (ai.IsMoveAI || current is AI_Goto)
            {
                if (TryGetThreatNextMovePoint(chara, current, out var next))
                    movePoint = next;
                return T("移动", "Move");
            }

            if (!ReferenceEquals(current, ai) &&
                current is not GoalCombat &&
                current is not AI_Wait &&
                current is not GoalEndTurn)
            {
                var currentActionText = SafeThreatActionText(current);
                if (!IsThreatIdleText(currentActionText))
                    return currentActionText;
            }

            var combat = current as GoalCombat ?? ai as GoalCombat;
            if (combat != null)
                return PredictThreatCombatAction(chara, combat, target, out movePoint);

            var actionText = SafeThreatActionText(current);
            if (!IsThreatIdleText(actionText))
                return actionText;

            if (current is AI_Wait || current is GoalEndTurn)
                return T("等待", "Wait");
            return T("待机", "Idle");
        }
        catch
        {
            return T("待机", "Idle");
        }
    }
    private string PredictThreatCombatAction(Chara chara, GoalCombat combat, Chara? target, out Point? movePoint)
    {
        movePoint = null;
        target ??= GetThreatCombatTarget(chara, combat, combat);
        if (target == null || target.pos == null || chara.pos == null)
            return T("战斗决策中", "Selecting combat action");

        try
        {
            var distance = chara.Dist(target);
            var tactics = combat.tactics;
            var desiredDistance = GetThreatDesiredCombatDistance(combat, target);
            var moveChance = Mathf.Clamp(tactics?.ChanceMove ?? 0, 0, 100);
            if (distance != desiredDistance && moveChance > 0)
            {
                if (distance > desiredDistance)
                    movePoint = GetThreatApproachPoint(chara, target.pos);
                else
                    movePoint = GetThreatRetreatPoint(chara, target.pos);
                if (movePoint != null)
                    return T("移动", "Move");
            }

            var likelyAbility = GetThreatLikelyAbilityName(combat, distance);
            if (!string.IsNullOrWhiteSpace(likelyAbility))
                return likelyAbility;
            if (distance <= 1)
                return T("近战攻击", "Melee attack");
            if ((tactics?.RangedChance ?? 0) >= 50)
                return T("远程攻击", "Ranged attack");
            return T("战斗决策中", "Selecting combat action");
        }
        catch
        {
            return T("战斗决策中", "Selecting combat action");
        }
    }
    private string PredictThreatCombatFollowUp(
        GoalCombat combat,
        int distance,
        int desiredDistance,
        out bool moved)
    {
        moved = false;
        try
        {
            var tactics = combat.tactics;
            if (distance != desiredDistance && Mathf.Clamp(tactics?.ChanceMove ?? 0, 0, 100) > 0)
            {
                moved = true;
                return T("移动", "Move");
            }

            var ability = GetThreatLikelyAbilityName(combat, distance);
            if (!string.IsNullOrWhiteSpace(ability))
                return ability;
            if (distance <= 1)
                return T("近战攻击", "Melee attack");
            if ((tactics?.RangedChance ?? 0) >= 50)
                return T("远程攻击", "Ranged attack");
            return T("战斗决策中", "Selecting combat action");
        }
        catch
        {
            return T("战斗决策中", "Selecting combat action");
        }
    }
    private static int StepThreatDistance(int distance, int desiredDistance)
    {
        if (distance > desiredDistance)
            return distance - 1;
        if (distance < desiredDistance)
            return distance + 1;
        return distance;
    }
    private static int GetThreatPredictedActionCount(Chara chara)
    {
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            var player = GameAccess.Runtime.Player;
            if (pc == null || player == null || ReferenceEquals(chara, pc))
                return 1;

            var pcSpeed = Math.Max(1, pc.Speed);
            var ownerSpeed = Math.Max(1, chara.Speed);
            if (ownerSpeed <= pcSpeed)
                return 1;

            var baseActTime = Math.Max(0.0001f, player.baseActTime);
            var ownerActTime = baseActTime * Mathf.Max(0.1f, pcSpeed / (float)ownerSpeed);
            var carriedTime = Mathf.Clamp(chara.roundTimer, 0f, ownerActTime);

            var nextBudget = carriedTime + baseActTime;
            var count = Mathf.FloorToInt(Mathf.Max(0f, nextBudget - 0.00001f) / ownerActTime);
            return Mathf.Clamp(Math.Max(1, count), 1, 10);
        }
        catch
        {
            try
            {
                var pcSpeed = Math.Max(1, GameAccess.Characters.PlayerCharacter?.Speed ?? 1);
                var ownerSpeed = Math.Max(1, chara.Speed);
                return Mathf.Clamp(Mathf.CeilToInt(ownerSpeed / (float)pcSpeed), 1, 10);
            }
            catch
            {
                return 1;
            }
        }
    }
    private static int GetThreatDesiredCombatDistance(GoalCombat combat, Chara target)
    {
        try
        {
            if (target.HasCondition<ConFear>())
                return 1;
        }
        catch
        {
        }

        try
        {
            return Math.Max(1, combat.tactics?.DestDist ?? 1);
        }
        catch
        {
            return 1;
        }
    }
    private static string GetThreatLikelyAbilityName(GoalCombat combat, int distance)
    {
        try
        {
            var abilities = combat.abilities;
            if (abilities == null || abilities.Count == 0)
                return string.Empty;

            GoalCombat.ItemAbility? best = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < abilities.Count; i++)
            {
                var item = abilities[i];
                if (item == null || item.act == null || item.chance <= 0)
                    continue;
                if (item.tg != null && item.tg.isDead)
                    continue;
                var score = item.priority + item.priorityMod + Math.Min(100, item.chance);
                if (item.act is ActMelee && distance <= 1)
                    score += 120;
                else if (item.act is ActRanged && distance > 1)
                    score += 100;
                if (score <= bestScore)
                    continue;
                best = item;
                bestScore = score;
            }

            if (best?.act == null)
                return string.Empty;
            var name = best.act.Name;
            if (!IsThreatVoidText(name))
                return name.Trim();
            name = best.act.source?.GetName();
            return IsThreatVoidText(name) ? string.Empty : name.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
    private string BuildThreatCandidateDisplay(
        GoalCombat combat,
        Chara chara,
        Chara target,
        string fallbackAction,
        out string primaryAction,
        out Point? primaryMovePoint)
    {
        var candidates = new List<ThreatActionCandidate>();
        var distance = chara.Dist(target);
        var desiredDistance = GetThreatDesiredCombatDistance(combat, target);
        var moveWeight = distance == desiredDistance
            ? 0
            : Mathf.Clamp(combat.tactics?.ChanceMove ?? 0, 0, 100);
        if (moveWeight > 0)
        {
            candidates.Add(new ThreatActionCandidate
            {
                Name = T("移动", "Move"),
                Weight = moveWeight,
                IsMove = true
            });
        }

        try
        {
            var abilities = combat.abilities;
            if (abilities != null)
            {
                for (var i = 0; i < abilities.Count; i++)
                {
                    var item = abilities[i];
                    var act = item?.act;
                    if (act == null || item!.chance <= 0)
                        continue;
                    if (act is ActMelee && distance > Math.Max(1, chara.body?.GetMeleeDistance() ?? 1))
                        continue;

                    var name = SafeThreatActName(act);
                    if (IsThreatVoidText(name))
                        continue;
                    var weight = item.priority > 0
                        ? item.priority
                        : Math.Max(1, item.chance + item.priorityMod);
                    AddOrMergeThreatCandidate(candidates, name, weight, isMove: false);
                }
            }
        }
        catch
        {
        }

        if (candidates.Count == 0)
        {
            candidates.Add(new ThreatActionCandidate
            {
                Name = fallbackAction,
                Weight = 100,
                IsMove = false
            });
        }

        candidates.Sort((left, right) => right.Weight.CompareTo(left.Weight));
        var totalWeight = 0;
        for (var i = 0; i < candidates.Count; i++)
            totalWeight += Math.Max(1, candidates[i].Weight);
        totalWeight = Math.Max(1, totalWeight);

        var primary = candidates[0];
        primaryAction = primary.Name;
        primaryMovePoint = null;
        if (primary.IsMove)
        {
            primaryMovePoint = distance > desiredDistance
                ? GetThreatApproachPoint(chara, target.pos)
                : GetThreatRetreatPoint(chara, target.pos);
        }

        var display = new List<string>(Math.Min(3, candidates.Count));
        for (var i = 0; i < candidates.Count && i < 3; i++)
        {
            var candidate = candidates[i];
            var probability = Mathf.Clamp(
                Mathf.RoundToInt(Math.Max(1, candidate.Weight) * 100f / totalWeight),
                1,
                99);
            display.Add(FormatEstimatedThreatAction(candidate.Name, probability));
        }
        return string.Join(" | ", display);
    }
    private static void AddOrMergeThreatCandidate(
        List<ThreatActionCandidate> candidates,
        string name,
        int weight,
        bool isMove)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var existing = candidates[i];
            if (existing.IsMove == isMove &&
                string.Equals(existing.Name, name, StringComparison.Ordinal))
            {
                existing.Weight = Math.Max(existing.Weight, weight);
                return;
            }
        }

        candidates.Add(new ThreatActionCandidate
        {
            Name = name,
            Weight = Math.Max(1, weight),
            IsMove = isMove
        });
    }
    private int EstimateThreatActionConfidence(
        GoalCombat? combat,
        Chara chara,
        Chara? target,
        string action,
        bool isMove)
    {
        if (combat == null || target == null)
            return IsThreatIdleText(action) ? 35 : 70;

        try
        {
            var distance = chara.Dist(target);
            var desiredDistance = GetThreatDesiredCombatDistance(combat, target);
            var moveWeight = distance == desiredDistance
                ? 0
                : Mathf.Clamp(combat.tactics?.ChanceMove ?? 0, 0, 100);
            var totalWeight = moveWeight;
            var selectedWeight = isMove ? moveWeight : 0;
            var abilities = combat.abilities;
            if (abilities != null)
            {
                for (var i = 0; i < abilities.Count; i++)
                {
                    var item = abilities[i];
                    var act = item?.act;
                    if (act == null || item!.chance <= 0)
                        continue;
                    if (act is ActMelee && distance > Math.Max(1, chara.body?.GetMeleeDistance() ?? 1))
                        continue;
                    if (act is ActRanged && distance <= 0)
                        continue;

                    var weight = item.priority > 0
                        ? item.priority
                        : Math.Max(1, item.chance + item.priorityMod);
                    totalWeight += weight;
                    if (!isMove && string.Equals(SafeThreatActName(act), action, StringComparison.Ordinal))
                        selectedWeight = Math.Max(selectedWeight, weight);
                }
            }

            if (selectedWeight <= 0 && !isMove)
            {
                if (string.Equals(action, T("近战攻击", "Melee attack"), StringComparison.Ordinal))
                    selectedWeight = Math.Max(1, combat.tactics?.P_Melee ?? 50);
                else if (string.Equals(action, T("远程攻击", "Ranged attack"), StringComparison.Ordinal))
                    selectedWeight = Math.Max(1, combat.tactics?.P_Range ?? 50);
                else
                    selectedWeight = 35;
                totalWeight += selectedWeight;
            }

            if (selectedWeight <= 0)
                return 25;
            if (totalWeight <= 0)
                return Mathf.Clamp(selectedWeight, 1, 99);
            return Mathf.Clamp(Mathf.RoundToInt(selectedWeight * 100f / totalWeight), 1, 99);
        }
        catch
        {
            return isMove ? 50 : 60;
        }
    }
}
