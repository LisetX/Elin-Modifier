using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class AutomationModule
{
    private void StartAutomationKill(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        if (pc != null)
            PrepareAutomationCombatEquipment(pc);
        _automationSkippedEnemyUids.Clear();
        _automationEnemyFailureCounts.Clear();
        _automationKillApproaching = false;
        _automationKillWaitingForEmptyRecheck = false;
        _automationKillEmptyRecheckCount = 0;
        _automationKillNextEmptyRecheckAt = 0f;
        _automationSweepVerificationPass = false;
        _automationSweepCompletedCount = 0;
        if (!TryStartNextAutomationKillTarget())
            FinishAutomationAction(true, AutomationText("全图没有敌对目标", "No hostile targets on the map", "マップ上に敵対対象がありません", "На карте нет враждебных целей"));
    }
    private bool TryStartNextAutomationKillTarget()
    {
        var pc = GetSafePc();
        if (pc == null)
            return false;
        var target = FindNearestAutomationEnemy(pc, int.MaxValue, _automationSkippedEnemyUids, true) ??
                     FindNearestAutomationEnemy(pc, int.MaxValue, _automationSkippedEnemyUids, false);
        if (target == null)
            return false;

        _automationKillWaitingForEmptyRecheck = false;
        _automationKillEmptyRecheckCount = 0;
        StartAutomationKillTarget(pc, target);
        return true;
    }
    private void StartAutomationKillTarget(Chara pc, Chara target)
    {
        var canSee = false;
        try { canSee = pc.CanSee(target); }
        catch { }
        if (canSee)
        {
            StartAutomationKillCombat(pc, target);
            return;
        }

        _automationTargetChara = target;
        _automationKillApproaching = true;
        _automationActionStartedAt = Time.unscaledTime;
        var approach = new AI_Goto(target, 1);
        pc.SetAIImmediate(approach);
        _automationActionAi = approach;
    }
    private void StartAutomationKillCombat(Chara pc, Chara target)
    {
        _automationTargetChara = target;
        _automationKillApproaching = false;
        _automationActionStartedAt = Time.unscaledTime;
        var goal = new AutomationCombatGoal(target);
        pc.SetAIImmediate(goal);
        _automationActionAi = goal;
    }
    private bool TryRetryAutomationKillTarget(Chara pc, Chara target, int failureCount)
    {
        TryOpenNearbyAutomationDoor(pc);

        if (failureCount <= AutomationKillPathRetryLimit)
        {
            StartAutomationKillTarget(pc, target);
            return true;
        }

        if (failureCount == AutomationKillPathRetryLimit + 1)
        {
            TryTeleportAutomationPlayerBesideTarget(pc, target);
            StartAutomationKillTarget(pc, target);
            return true;
        }

        if (failureCount <= AutomationKillPathRetryLimit + AutomationKillTeleportRetryLimit)
        {
            StartAutomationKillTarget(pc, target);
            return true;
        }

        return false;
    }
    private static bool TryTeleportAutomationPlayerBesideTarget(Chara pc, Chara target)
    {
        try
        {
            if (target == null || target.isDead || !target.ExistsOnMap)
                return false;
            var destination = target.pos.GetNearestPoint(allowBlock: false, allowChara: false,
                allowInstalled: true, ignoreCenter: true);
            if (destination == null || !destination.IsValid || !destination.IsInBounds || destination.HasChara || destination.cell.blocked)
                return false;
            pc.Teleport(destination, silent: true, force: true);
            return pc.pos.Equals(destination);
        }
        catch
        {
            return false;
        }
    }
    private static bool TryOpenNearbyAutomationDoor(Chara pc)
    {
        try
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    var point = new Point(pc.pos.x + dx, pc.pos.z + dz);
                    if (!point.IsValid || !point.IsInBounds)
                        continue;
                    foreach (var thing in point.Things)
                    {
                        if (thing?.trait is not TraitDoor door || door.IsOpen() || thing.c_lockLv > 0)
                            continue;
                        door.TryOpen(pc);
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }
    private static bool IsAutomationEnemyCandidate(Chara pc, Chara? chara)
    {
        if (chara == null || ReferenceEquals(chara, pc))
            return false;

        try
        {
            if (chara.isDead || !chara.ExistsOnMap || !chara.IsAliveInCurrentZone ||
                chara.IsPCFactionOrMinion || chara.IsPCParty)
                return false;

            if (pc.IsHostile(chara) || chara.IsHostile(pc) || chara.hostility <= Hostility.Enemy ||
                ReferenceEquals(pc.enemy, chara) || ReferenceEquals(chara.enemy, pc))
                return true;

            var enemy = chara.enemy;
            if (enemy != null && (enemy.IsPCParty || enemy.IsPCFactionOrMinion))
                return true;

            var partyMembers = pc.party?.members;
            if (partyMembers != null)
            {
                for (var i = 0; i < partyMembers.Count; i++)
                {
                    var member = partyMembers[i];
                    if (member != null && ReferenceEquals(member.enemy, chara))
                        return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    private static Chara? FindNearestAutomationEnemy(Chara pc, int radius, HashSet<int>? excludedUids = null, bool requireVisible = false)
    {
        Chara? best = null;
        var bestDistance = int.MaxValue;
        try
        {
            var charas = GameAccess.World.CurrentCharacters!;
            for (var i = 0; i < charas.Count; i++)
            {
                try
                {
                    var chara = charas[i];
                    if (!IsAutomationEnemyCandidate(pc, chara) ||
                        (excludedUids != null && excludedUids.Contains(chara.uid)))
                        continue;
                    if (requireVisible)
                    {
                        if (!pc.CanSee(chara))
                            continue;
                    }
                    var distance = pc.Dist(chara);
                    if (distance > radius || distance >= bestDistance)
                        continue;
                    best = chara;
                    bestDistance = distance;
                }
                catch { }
            }
        }
        catch { }
        return best;
    }
}
