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
    private void StartAutomationMine(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        if (pc != null)
            SetAutomationCurrentTool(pc, thing => thing.HasElement(220));
        _automationSkippedMinePoints.Clear();
        _automationSweepVerificationPass = false;
        _automationSweepCompletedCount = 0;
        if (!TryStartNextAutomationMineTarget())
            FinishAutomationAction(true, AutomationText("全图没有可挖掘目标", "No mineable targets on the map", "マップ上に採掘可能な対象がありません", "На карте нет доступных целей для добычи"));
    }
    private bool TryStartNextAutomationMineTarget()
    {
        var pc = GetSafePc();
        var map = GameAccess.World.CurrentMap;
        if (pc == null || map == null)
            return false;

        SetAutomationCurrentTool(pc, thing => thing.HasElement(220));

        var point = FindNearestAutomationMinePoint(pc);
        if (point == null)
            return false;

        var task = new AutomationMineTask { pos = point.Copy() };
        _automationTargetPoint = point.Copy();
        _automationActionStartedAt = Time.unscaledTime;
        pc.SetAIImmediate(task);
        _automationActionAi = task;
        return true;
    }
    private Point? FindNearestAutomationMinePoint(Chara pc)
    {
        var map = GameAccess.World.CurrentMap;
        if (map == null)
            return null;

        Point? best = null;
        var bestDistance = int.MaxValue;
        var bounds = map.bounds;
        for (var x = bounds.x; x <= bounds.maxX; x++)
        {
            for (var z = bounds.z; z <= bounds.maxZ; z++)
            {
                var point = new Point(x, z);
                if (_automationSkippedMinePoints.Contains(GetAutomationPointKey(point)) || !CanAutomationMinePoint(point))
                    continue;
                var distance = point.Distance(pc.pos);
                if (distance >= bestDistance)
                    continue;
                best = point;
                bestDistance = distance;
            }
        }
        return best;
    }
    private static bool CanAutomationMinePoint(Point point)
    {
        try
        {
            return point.IsValid && point.IsInBounds && point.HasBlock;
        }
        catch { return false; }
    }
}
