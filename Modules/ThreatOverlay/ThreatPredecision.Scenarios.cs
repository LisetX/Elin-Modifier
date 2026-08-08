using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

internal sealed partial class ThreatOverlayModule
{
    private static int SafeResourceValue(Stats resource)
    {
        try
        {
            return Math.Max(0, resource.value);
        }
        catch
        {
            return 0;
        }
    }
    private static int LockedThreatRoll(int seed, int actionIndex, int salt, int maximum)
    {
        if (maximum <= 1)
            return 0;
        unchecked
        {
            uint value = (uint)seed;
            value ^= (uint)(actionIndex + 1) * 0x9E3779B9u;
            value ^= (uint)salt * 0x85EBCA6Bu;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return (int)(value % (uint)maximum);
        }
    }
    private LockedThreatDecision CreateLockedMoveDecision(
        int uid,
        Chara actor,
        GoalCombat combat,
        Chara target,
        Point movePoint)
    {
        return new LockedThreatDecision
        {
            Serial = unchecked(++_nextLockedThreatDecisionSerial),
            Frame = Time.frameCount,
            ActorUid = uid,
            Actor = actor,
            Combat = combat,
            CombatTarget = target,
            Kind = LockedThreatDecisionKind.Move,
            MovePoint = new Point(movePoint.x, movePoint.z),
            Name = T("移动", "Move")
        };
    }
    private LockedThreatDecision CreateLockedAbilityDecision(
        int uid,
        Chara actor,
        GoalCombat combat,
        Chara target,
        GoalCombat.ItemAbility? selected,
        Card? selectedTarget,
        Act? explicitAct = null,
        string? explicitName = null)
    {
        var act = explicitAct ?? selected?.act;
        var isRanged = act is ActRanged ||
                       string.Equals(act?.source?.alias, "ActRanged", StringComparison.Ordinal);
        Card abilityTarget = selectedTarget ?? selected?.tg ?? target;
        try
        {
            if (act != null && act.TargetType.Range == TargetRange.Self)
                abilityTarget = actor;
        }
        catch
        {
        }

        var name = explicitName ?? (act == null ? string.Empty : SafeThreatActName(act));
        if (IsThreatVoidText(name))
            name = isRanged ? T("远程攻击", "Ranged attack") : T("战斗决策中", "Selecting combat action");
        return new LockedThreatDecision
        {
            Serial = unchecked(++_nextLockedThreatDecisionSerial),
            Frame = Time.frameCount,
            ActorUid = uid,
            Actor = actor,
            Combat = combat,
            CombatTarget = target,
            Kind = isRanged ? LockedThreatDecisionKind.Ranged : LockedThreatDecisionKind.Ability,
            Act = act,
            AbilityTarget = abilityTarget,
            PartyTarget = act != null && selected != null &&
                          ((act.HaveLongPressAction && selected.pt) || selected.aiPt),
            Name = name
        };
    }
    private static LockedThreatDecisionSequence? SelectLockedSequence(
        LockedThreatDecisionSet decisionSet,
        int scenario)
    {
        if (scenario >= 0 && scenario < decisionSet.Sequences.Length &&
            decisionSet.Sequences[scenario] != null)
        {
            return decisionSet.Sequences[scenario];
        }

        return decisionSet.Sequences[LockedThreatStationaryScenario] ??
               decisionSet.Sequences[LockedThreatCenterMovementScenario];
    }
    private static Point? GetLockedThreatApproachPoint(
        Chara actor,
        Point simulatedActor,
        Point target)
    {
        if (SameThreatPoint(actor.pos, simulatedActor))
            return GetThreatApproachPoint(actor, target);

        var direct = StepLockedThreatPoint(simulatedActor, target, approach: true);
        if (IsLockedThreatWalkablePoint(actor, direct))
            return direct;
        return FindLockedThreatAdjacentPoint(actor, simulatedActor, target, approach: true);
    }
    private static Point? GetLockedThreatRetreatPoint(
        Chara actor,
        Point simulatedActor,
        Point target)
    {
        if (SameThreatPoint(actor.pos, simulatedActor))
            return GetThreatRetreatPoint(actor, target);

        var direct = StepLockedThreatPoint(simulatedActor, target, approach: false);
        if (IsLockedThreatWalkablePoint(actor, direct))
            return direct;
        return FindLockedThreatAdjacentPoint(actor, simulatedActor, target, approach: false);
    }
    private static Point StepLockedThreatPoint(Point source, Point target, bool approach)
    {
        var deltaX = target.x - source.x;
        var deltaZ = target.z - source.z;
        var divisor = Math.Max(1, Math.Max(Math.Abs(deltaX), Math.Abs(deltaZ)));
        var stepX = deltaX / divisor;
        var stepZ = deltaZ / divisor;
        return approach
            ? new Point(source.x + stepX, source.z + stepZ)
            : new Point(source.x - stepX, source.z - stepZ);
    }
    private static Point? FindLockedThreatAdjacentPoint(
        Chara actor,
        Point source,
        Point target,
        bool approach)
    {
        Point? best = null;
        var bestDistance = approach ? int.MaxValue : int.MinValue;
        for (var x = source.x - 1; x <= source.x + 1; x++)
        {
            for (var z = source.z - 1; z <= source.z + 1; z++)
            {
                if (x == source.x && z == source.z)
                    continue;
                var candidate = new Point(x, z);
                if (!IsLockedThreatWalkablePoint(actor, candidate))
                    continue;
                var distance = candidate.Distance(target);
                if (approach ? distance >= bestDistance : distance <= bestDistance)
                    continue;
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }
    private static bool IsLockedThreatWalkablePoint(Chara actor, Point? point)
    {
        if (point == null || !point.IsValid)
            return false;
        try
        {
            var charas = point.Charas;
            if (charas != null)
            {
                for (var i = 0; i < charas.Count; i++)
                    if (charas[i] != null && !ReferenceEquals(charas[i], actor))
                        return false;
            }
            return actor.CanMoveTo(point, true);
        }
        catch
        {
            try { return !point.IsBlocked; }
            catch { return false; }
        }
    }
    private int ResolveExecutedThreatScenario(LockedThreatDecisionSet decisionSet)
    {
        try
        {
            if (decisionSet.PcActionEpoch == _resolvedPcActionEpoch)
                return _resolvedPcActionScenario;

            var pc = GameAccess.Characters.PlayerCharacter;
            if (pc?.pos == null)
                return LockedThreatStationaryScenario;

            if (pc.pos.x == decisionSet.PcOriginX && pc.pos.z == decisionSet.PcOriginZ)
                return LockedThreatStationaryScenario;

            return ResolveThreatMovementScenario(
                decisionSet.PcOriginX,
                decisionSet.PcOriginZ,
                pc.pos.x,
                pc.pos.z,
                LockedThreatStationaryScenario);
        }
        catch
        {
            return LockedThreatStationaryScenario;
        }
    }
    private int ResolveMouseThreatScenario()
    {
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            var mouseTarget = GameAccess.Ui.Scene?.mouseTarget;
            if (pc?.pos == null || mouseTarget?.pos == null || !mouseTarget.pos.IsValid ||
                GameAccess.Ui.IsPointerOverUi)
            {
                return LockedThreatStationaryScenario;
            }

            if (_mouseThreatDirectionFrame >= 0 &&
                Time.frameCount - _mouseThreatDirectionFrame <= 2)
            {
                return (_mouseThreatStepZ + 1) * 3 + _mouseThreatStepX + 1;
            }

            if (pc.pos.Distance(mouseTarget.pos) > 1)
            {
                var firstStep = pc.GetFirstStep(mouseTarget.pos, PathManager.MoveType.Default);
                if (firstStep != null && firstStep.IsValid)
                {
                    return ResolveThreatMovementScenario(
                        pc.pos.x,
                        pc.pos.z,
                        firstStep.x,
                        firstStep.z,
                        LockedThreatCenterMovementScenario);
                }
            }

            return ResolveThreatMovementScenario(
                pc.pos.x,
                pc.pos.z,
                mouseTarget.pos.x,
                mouseTarget.pos.z,
                LockedThreatCenterMovementScenario);
        }
        catch
        {
            return LockedThreatStationaryScenario;
        }
    }
    private static int ResolveThreatMovementScenario(
        int originX,
        int originZ,
        int destinationX,
        int destinationZ,
        int sameTileScenario)
    {
        var deltaX = destinationX - originX;
        var deltaZ = destinationZ - originZ;
        if (deltaX == 0 && deltaZ == 0)
            return sameTileScenario;

        var divisor = Math.Max(Math.Abs(deltaX), Math.Abs(deltaZ));
        if (divisor <= 0)
            return sameTileScenario;

        var stepX = deltaX / divisor;
        var stepZ = deltaZ / divisor;
        return (stepZ + 1) * 3 + stepX + 1;
    }
    private static Point CreateThreatScenarioPoint(int originX, int originZ, int scenario)
    {
        var stepX = scenario % 3 - 1;
        var stepZ = scenario / 3 - 1;
        try
        {
            var map = GameAccess.World.CurrentMap;
            if (map != null)
            {
                return new Point(
                    Mathf.Clamp(originX + stepX, 0, map.Size - 1),
                    Mathf.Clamp(originZ + stepZ, 0, map.Size - 1));
            }
        }
        catch
        {
        }
        return new Point(originX + stepX, originZ + stepZ);
    }
    private static bool IsLockedDecisionSetValid(
        LockedThreatDecisionSet decisionSet,
        Chara actor,
        GoalCombat combat,
        Chara? target)
    {
        try
        {
            return ReferenceEquals(decisionSet.Actor, actor) &&
                   ReferenceEquals(decisionSet.Combat, combat) &&
                   target != null &&
                   ReferenceEquals(decisionSet.CombatTarget, target) &&
                   !actor.isDead &&
                   !target.isDead &&
                   actor.IsAliveInCurrentZone &&
                   target.ExistsOnMap &&
                   actor.pos != null &&
                   actor.pos.x == decisionSet.ActorX &&
                   actor.pos.z == decisionSet.ActorZ;
        }
        catch
        {
            return false;
        }
    }
}
