using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private static bool ShouldAllowTerrainMovement(Chara? chara, Point? target)
    {
        if (!ShouldIgnoreTerrainMovement() || chara == null || target == null)
            return false;

        try
        {
            if (!IsIgnoreTerrainMovementActor(chara) || chara.pos == null)
                return false;

            var source = chara.pos;
            if (!source.IsValid || !target.IsValid || !target.IsInBounds)
                return false;

            var dx = Math.Abs(target.x - source.x);
            var dz = Math.Abs(target.z - source.z);
            if (dx == 0 && dz == 0)
                return false;
            if (dx > 1 || dz > 1)
                return false;

            if (IsTerrainMovementInvalid(target))
                return false;
            var terrainBlocked = IsTerrainHeightBlocked(source, target) ||
                                  IsTerrainMovementBlocked(target) ||
                                  IsTerrainStepBlocked(source, target);
            if (!terrainBlocked)
                return false;
            if (!IsPlayerChara(chara) && IsTerrainMovementBlocked(target))
                return false;
            if (target.HasChara)
                return false;
            if (chara.IsEnemyOnPath(target, false))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }
    private static bool ShouldUseDirectTerrainStep(Chara? chara, Point? target)
    {
        return ShouldAllowTerrainMovement(chara, target);
    }
    private static bool TryGetDirectTerrainStep(Chara? chara, Point? destination, out Point? step)
    {
        step = null;
        if (!ShouldIgnoreTerrainMovement() || chara == null || destination == null)
            return false;

        try
        {
            if (!IsIgnoreTerrainMovementActor(chara) || chara.pos == null || !chara.pos.IsValid || !destination.IsValid)
                return false;

            var source = chara.pos;
            var dx = Math.Abs(destination.x - source.x);
            var dz = Math.Abs(destination.z - source.z);
            if (dx == 0 && dz == 0)
                return false;

            Point candidate;
            if (dx <= 1 && dz <= 1)
                candidate = destination;
            else
                candidate = source.GetPointTowards(destination);

            if (candidate == null || !ShouldAllowTerrainMovement(chara, candidate))
                return false;

            step = candidate;
            return true;
        }
        catch
        {
            step = null;
            return false;
        }
    }
    private static bool IsIgnoreTerrainMovementActor(Chara? chara)
    {
        if (chara == null)
            return false;

        try { if (chara.IsPC) return true; } catch { }
        try { if (chara.IsPCParty) return true; } catch { }
        try { if (chara.IsPCPartyMinion) return true; } catch { }

        return false;
    }
    private static bool IsPlayerChara(Chara? chara)
    {
        if (chara == null)
            return false;
        try { return chara.IsPC; }
        catch { return false; }
    }
    private static bool TryHandleIgnoreTerrainGoto(AI_Goto? ai, out AIAct.Status result)
    {
        result = AIAct.Status.Running;
        if (!ShouldIgnoreTerrainMovement() || ai == null)
            return false;

        Chara? owner;
        try { owner = ai.owner; } catch { owner = null; }
        if (owner == null)
            return false;

        try
        {
            if (owner.IsPC || !IsIgnoreTerrainMovementActor(owner))
                return false;
        }
        catch
        {
            return false;
        }

        Point? destination = null;
        try { destination = ai.destCard != null ? ai.destCard.pos : ai.dest; } catch { }
        if (destination == null)
            return false;

        Point? step;
        if (!TryGetDirectTerrainStep(owner, destination, out step) || step == null)
            return false;

        try
        {
            var moveResult = owner.TryMove(step, false);
            if (moveResult != Card.MoveResult.Success)
                return false;

            try
            {
                if (owner.path != null)
                    owner.path.state = PathProgress.State.Idle;
                ai.repath = true;
            }
            catch { }

            try
            {
                result = ai.IsDestinationReached() ? ai.Success() : AIAct.Status.Running;
            }
            catch
            {
                result = AIAct.Status.Running;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static void TrySyncIgnoreTerrainPartyFollowers(Chara? pc)
    {
        if (!ShouldIgnoreTerrainMovement() || pc == null)
            return;

        try
        {
            if (!pc.IsPC || pc.pos == null || GameAccess.World.CurrentZone == null || GameAccess.World.CurrentZone.IsRegion || !GameAccess.World.CurrentZone.PetFollow)
                return;
        }
        catch
        {
            return;
        }

        List<Chara>? members = null;
        try { members = GameAccess.Characters.PlayerCharacter?.party?.members; } catch { }
        if (members == null || members.Count == 0)
            return;

        foreach (var member in members)
        {
            if (!IsIgnoreTerrainFollower(member))
                continue;
            if (!ShouldForceIgnoreTerrainFollowerMove(member, pc))
                continue;

            TryMoveIgnoreTerrainFollower(member, pc);
        }
    }
    private static bool IsIgnoreTerrainFollower(Chara? chara)
    {
        if (chara == null || !IsIgnoreTerrainMovementActor(chara))
            return false;

        try
        {
            if (chara.IsPC || chara.isDead || chara.host != null || chara.IsDisabled || chara.IsInCombat)
                return false;
            if (chara.HasCondition<ConEntangle>())
                return false;
        }
        catch
        {
            return false;
        }

        return true;
    }
    private static bool ShouldForceIgnoreTerrainFollowerMove(Chara follower, Chara pc)
    {
        try
        {
            if (follower.pos == null || pc.pos == null || !follower.pos.IsValid || !pc.pos.IsValid)
                return false;

            if (IsTerrainMovementBlocked(follower.pos))
                return true;

            var distance = follower.Dist(pc);
            if (distance > 1)
            {
                Point? step;
                if (TryGetDirectTerrainStep(follower, pc.pos, out step) &&
                    step != null &&
                    IsValidIgnoreTerrainFollowerDestination(step, follower))
                    return true;

                try
                {
                    if (!follower.CanSeeLos(pc.pos, distance))
                        return true;
                }
                catch { }

                return false;
            }

            return IsTerrainHeightBlocked(follower.pos, pc.pos) || IsTerrainStepBlocked(follower.pos, pc.pos);
        }
        catch
        {
            return false;
        }
    }
    private static bool TryMoveIgnoreTerrainFollower(Chara follower, Chara pc)
    {
        try
        {
            Point? step;
            if (TryGetDirectTerrainStep(follower, pc.pos, out step) &&
                step != null &&
                IsValidIgnoreTerrainFollowerDestination(step, follower))
                return follower.TryMove(step, false) == Card.MoveResult.Success;

            Point? destination;
            if (!TryGetIgnoreTerrainFollowerDestination(follower, pc, out destination) || destination == null)
                return false;

            follower.MoveImmediate(destination, false, true);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static bool TryGetIgnoreTerrainFollowerDestination(Chara follower, Chara pc, out Point? destination)
    {
        destination = null;
        try
        {
            if (follower.pos == null || pc.pos == null)
                return false;

            var candidates = new List<Point>();
            for (var radius = 1; radius <= 3; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dz = -radius; dz <= radius; dz++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                            continue;

                        var point = new Point(pc.pos.x + dx, pc.pos.z + dz);
                        if (!IsValidIgnoreTerrainFollowerDestination(point, follower))
                            continue;

                        candidates.Add(point);
                    }
                }
            }

            candidates.Sort((a, b) =>
            {
                var separationCompare = GetIgnoreTerrainFollowerDestinationSeparationScore(a, pc).CompareTo(GetIgnoreTerrainFollowerDestinationSeparationScore(b, pc));
                if (separationCompare != 0)
                    return separationCompare;

                var pcDistanceCompare = pc.pos.Distance(a).CompareTo(pc.pos.Distance(b));
                if (pcDistanceCompare != 0)
                    return pcDistanceCompare;

                var distanceCompare = follower.pos.Distance(a).CompareTo(follower.pos.Distance(b));
                if (distanceCompare != 0)
                    return distanceCompare;
                return string.CompareOrdinal(a.x.ToString(CultureInfo.InvariantCulture) + "," + a.z.ToString(CultureInfo.InvariantCulture),
                    b.x.ToString(CultureInfo.InvariantCulture) + "," + b.z.ToString(CultureInfo.InvariantCulture));
            });

            if (candidates.Count > 0)
            {
                destination = candidates[0];
                return true;
            }

            var nearest = pc.pos.GetNearestPoint(allowBlock: false, allowChara: false, allowInstalled: true, ignoreCenter: true);
            if (IsValidIgnoreTerrainFollowerDestination(nearest, follower))
            {
                destination = nearest;
                return true;
            }
        }
        catch { }

        return false;
    }
    private static int GetIgnoreTerrainFollowerDestinationSeparationScore(Point point, Chara pc)
    {
        try
        {
            if (pc.pos == null)
                return 10;
            var separated = IsTerrainHeightBlocked(point, pc.pos) ||
                            IsTerrainMovementBlocked(point) ||
                            IsTerrainStepBlocked(point, pc.pos);
            return separated ? 1 : 0;
        }
        catch
        {
            return 10;
        }
    }
    private static bool IsValidIgnoreTerrainFollowerDestination(Point? point, Chara follower)
    {
        if (point == null)
            return false;

        try
        {
            if (!point.IsValid || !point.IsInBounds || point.Equals(follower.pos))
                return false;
            if (point.HasChara || IsTerrainMovementInvalid(point) || IsTerrainMovementBlocked(point))
                return false;
        }
        catch
        {
            return false;
        }

        return true;
    }
    private static bool IsTerrainHeightBlocked(Point source, Point target)
    {
        try
        {
            if (source.IsBlockByHeight(target) || target.IsBlockByHeight(source))
                return true;
        }
        catch { }

        try
        {
            var sourceCell = source.cell;
            var targetCell = target.cell;
            if (sourceCell == null || targetCell == null)
                return false;

            if (Math.Abs(sourceCell.topHeight - targetCell.topHeight) > 8)
                return true;
            if (Math.Abs(sourceCell.minHeight - targetCell.minHeight) > 8)
                return true;

            var sourceSurface = sourceCell.GetSurfaceHeight();
            var targetSurface = targetCell.GetSurfaceHeight();
            return Mathf.Abs(sourceSurface - targetSurface) > 0.08f;
        }
        catch
        {
            return false;
        }
    }
    private static bool IsTerrainMovementInvalid(Point target)
    {
        try
        {
            var cell = target.cell;
            return cell == null || cell.outOfBounds;
        }
        catch
        {
            return true;
        }
    }
    private static bool IsTerrainMovementBlocked(Point target)
    {
        try
        {
            if (target.IsBlocked)
                return true;
        }
        catch { }

        try
        {
            var cell = target.cell;
            if (cell != null && cell.HasWallOrFence)
                return true;
        }
        catch { }

        return false;
    }
    private static bool IsTerrainStepBlocked(Point source, Point target)
    {
        try
        {
            if (source == null || target == null || !source.IsValid || !target.IsValid || !target.IsInBounds)
                return false;

            var dx = Math.Abs(target.x - source.x);
            var dz = Math.Abs(target.z - source.z);
            if (dx == 0 && dz == 0)
                return false;
            if (dx > 1 || dz > 1)
                return false;

            var cells = GameAccess.World.CurrentMap?.cells;
            if (cells == null)
                return false;

            if (cells[target.x, target.z].blocked)
                return true;

            var direction = GetTerrainMoveDirection(source.x, source.z, target.x, target.z);
            if (cells[source.x, source.z].weights[direction] == 0)
                return true;

            if (target.x == source.x || target.z == source.z)
                return false;

            var sideX = target.x;
            var sideZ = source.z;
            var sideDirection = GetTerrainMoveDirection(source.x, source.z, sideX, sideZ);
            if (cells[source.x, source.z].weights[sideDirection] == 0 || cells[sideX, sideZ].blocked)
                return true;

            sideDirection = GetTerrainMoveDirection(target.x, target.z, sideX, sideZ);
            if (cells[target.x, target.z].weights[sideDirection] == 0)
                return true;

            sideX = source.x;
            sideZ = target.z;
            sideDirection = GetTerrainMoveDirection(source.x, source.z, sideX, sideZ);
            if (cells[source.x, source.z].weights[sideDirection] == 0 || cells[sideX, sideZ].blocked)
                return true;

            sideDirection = GetTerrainMoveDirection(target.x, target.z, sideX, sideZ);
            if (cells[target.x, target.z].weights[sideDirection] == 0)
                return true;
        }
        catch { }

        return false;
    }
    private static int GetTerrainMoveDirection(int fromX, int fromZ, int toX, int toZ)
    {
        return toZ >= fromZ ? (toX > fromX ? 1 : (toZ > fromZ ? 2 : 3)) : 0;
    }
    [HarmonyPatch(typeof(Chara), "GetFirstStep", new[] { typeof(Point), typeof(PathManager.MoveType) })]
    private static class CharaGetFirstStepPatch
    {
        private static void Postfix(Chara __instance, Point __0, ref Point __result)
        {
            Point? step;
            if (!TryGetDirectTerrainStep(__instance, __0, out step) || step == null)
                return;

            try
            {
                if (__result == null || !__result.IsValid || __result.Equals(__instance.pos))
                    __result = step.Copy();
            }
            catch
            {
                try { __result = step.Copy(); } catch { }
            }
        }
    }
    [HarmonyPatch(typeof(Chara), "TryMoveTowards", new[] { typeof(Point) })]
    private static class CharaTryMoveTowardsPatch
    {
        private static bool Prefix(Chara __instance, Point __0, ref Card.MoveResult __result)
        {
            Point? step;
            if (!TryGetDirectTerrainStep(__instance, __0, out step) || step == null)
                return true;

            try
            {
                __result = __instance.TryMove(step, false);
                return __result != Card.MoveResult.Success;
            }
            catch
            {
                return true;
            }
        }
    }
    [HarmonyPatch(typeof(AI_Goto), "TryGoTo")]
    private static class AIGotoTryGoToPatch
    {
        private static bool Prefix(AI_Goto __instance, ref AIAct.Status __result)
        {
            if (!TryHandleIgnoreTerrainGoto(__instance, out var result))
                return true;

            __result = result;
            return false;
        }
    }
    [HarmonyPatch(typeof(Chara), "CanMoveTo", new[] { typeof(Point), typeof(bool) })]
    private static class CharaCanMoveToPatch
    {
        private static void Postfix(Chara __instance, Point __0, ref bool __result)
        {
            if (!__result && ShouldAllowTerrainMovement(__instance, __0))
                __result = true;
        }
    }
    [HarmonyPatch(typeof(Chara), "_Move", new[] { typeof(Point), typeof(Card.MoveType) })]
    private static class CharaMovePatch
    {
        private static void Postfix(Chara __instance, Card.MoveResult __result)
        {
            if (__result == Card.MoveResult.Success)
                TrySyncIgnoreTerrainPartyFollowers(__instance);
        }
    }
}
