using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class ThreatOverlayModule
{
    private static string SafeThreatActName(Act act)
    {
        try
        {
            var name = act.Name;
            if (!IsThreatVoidText(name))
                return name.Trim();
        }
        catch
        {
        }

        try
        {
            var name = act.source?.GetName();
            return IsThreatVoidText(name) ? string.Empty : name.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
    private static bool IsThreatVoidText(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ||
               string.Equals(text.Trim(), "虚无", StringComparison.Ordinal) ||
               string.Equals(text.Trim(), "虚無", StringComparison.Ordinal) ||
               string.Equals(text.Trim(), "void", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text.Trim(), "none", StringComparison.OrdinalIgnoreCase);
    }
    private static Point? GetThreatApproachPoint(Chara chara, Point destination)
    {
        try
        {
            var pathPoint = chara.GetFirstStep(destination, PathManager.MoveType.Combat);
            if (pathPoint != null && pathPoint.IsValid &&
                !pathPoint.HasChara &&
                (pathPoint.x != chara.pos.x || pathPoint.z != chara.pos.z))
            {
                return new Point(pathPoint.x, pathPoint.z);
            }

            var direct = chara.pos.GetPointTowards(destination);
            if (IsThreatWalkablePoint(chara, direct))
                return new Point(direct.x, direct.z);

            return FindThreatAdjacentPoint(chara, destination, approach: true);
        }
        catch
        {
        }
        return null;
    }
    private static Point? GetThreatRetreatPoint(Chara chara, Point threat)
    {
        try
        {
            var dx = Math.Sign(threat.x - chara.pos.x);
            var dz = Math.Sign(threat.z - chara.pos.z);
            var direct = new Point(chara.pos.x - dx, chara.pos.z - dz);
            if (IsThreatWalkablePoint(chara, direct))
                return direct;

            return FindThreatAdjacentPoint(chara, threat, approach: false);
        }
        catch
        {
            return null;
        }
    }
    private static Point? FindThreatAdjacentPoint(Chara chara, Point target, bool approach)
    {
        Point? best = null;
        var bestDistance = approach ? int.MaxValue : int.MinValue;
        for (var x = chara.pos.x - 1; x <= chara.pos.x + 1; x++)
        {
            for (var z = chara.pos.z - 1; z <= chara.pos.z + 1; z++)
            {
                if (x == chara.pos.x && z == chara.pos.z)
                    continue;
                var candidate = new Point(x, z);
                if (!IsThreatWalkablePoint(chara, candidate))
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
    private static bool IsThreatWalkablePoint(Chara chara, Point? point)
    {
        if (point == null || !point.IsValid || point.HasChara)
            return false;
        try
        {
            return chara.CanMoveTo(point, true);
        }
        catch
        {
            try { return !point.IsBlocked; }
            catch { return false; }
        }
    }
    private static Chara? GetThreatCombatTarget(Chara chara, AIAct? ai, AIAct? current)
    {
        try
        {
            var combat = current as GoalCombat ?? ai as GoalCombat;
            return combat?.tc ?? combat?.destEnemy ?? chara.enemy;
        }
        catch
        {
            return null;
        }
    }
    private static int SafeThreatTurn(Chara chara)
    {
        try { return chara.turn; }
        catch { return int.MinValue; }
    }
    private static string SafeThreatActionText(AIAct action)
    {
        try
        {
            var text = action.GetCurrentActionText();
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }
        catch
        {
            try
            {
                var text = action.Name;
                return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
    private static bool IsThreatIdleText(string text)
    {
        return IsThreatVoidText(text) ||
               string.Equals(text, "idle", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "待机", StringComparison.Ordinal) ||
               string.Equals(text, "待機", StringComparison.Ordinal);
    }
    private static bool TryGetThreatNextMovePoint(Chara chara, AIAct ai, out Point? point)
    {
        point = null;
        try
        {
            var currentPos = chara.pos;
            var path = chara.path;
            if (currentPos != null && path != null &&
                path.state == PathProgress.State.PathReady &&
                path.nodes != null &&
                path.nodeIndex >= 0 &&
                path.nodeIndex < path.nodes.Count)
            {
                var index = path.nodeIndex;
                var node = path.nodes[index];
                if (node.X == currentPos.x && node.Z == currentPos.z && index > 0)
                    node = path.nodes[index - 1];
                if (node.X != currentPos.x || node.Z != currentPos.z)
                {
                    point = new Point(node.X, node.Z);
                    return true;
                }
            }

            var destination = ai.GetDestination();
            if (currentPos == null || destination == null ||
                (currentPos.x == destination.x && currentPos.z == destination.z))
            {
                return false;
            }

            var next = currentPos.GetPointTowards(destination);
            if (next == null || (next.x == currentPos.x && next.z == currentPos.z))
                return false;
            point = new Point(next.x, next.z);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static bool SameThreatPoint(Point? left, Point? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null)
            return false;
        return left.x == right.x && left.z == right.z;
    }
}
