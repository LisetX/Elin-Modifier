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
    private void StartAutomationChop(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        if (pc != null)
            SetAutomationCurrentTool(pc, thing => thing.HasElement(225));
        _automationSkippedChopPoints.Clear();
        _automationSweepVerificationPass = false;
        _automationSweepCompletedCount = 0;
        if (!TryStartNextAutomationChopTarget())
            FinishAutomationAction(true, AutomationText("全图没有可砍伐树木", "No trees to chop on the map", "マップ上に伐採可能な木がありません", "На карте нет деревьев для рубки"));
    }
    private bool TryStartNextAutomationChopTarget()
    {
        var pc = GetSafePc();
        var map = GameAccess.World.CurrentMap;
        if (pc == null || map == null)
            return false;

        SetAutomationCurrentTool(pc, thing => thing.HasElement(225));

        Point? best = null;
        var bestDistance = int.MaxValue;
        try
        {
            var bounds = map.bounds;
            for (var x = bounds.x; x <= bounds.maxX; x++)
            {
                for (var z = bounds.z; z <= bounds.maxZ; z++)
                {
                    var point = new Point(x, z);
                    var key = GetAutomationPointKey(point);
                    if (_automationSkippedChopPoints.Contains(key))
                        continue;
                    if (!IsAutomationTreePoint(point))
                        continue;
                    var distance = point.Distance(pc.pos);
                    if (distance >= bestDistance)
                        continue;
                    best = point;
                    bestDistance = distance;
                }
            }
        }
        catch { }

        if (best == null)
            return false;

        var task = new AutomationChopTask { pos = best.Copy() };
        _automationTargetThing = null;
        _automationTargetPoint = best.Copy();
        _automationActionStartedAt = Time.unscaledTime;
        pc.SetAIImmediate(task);
        _automationActionAi = task;
        return true;
    }
    private static bool IsAutomationTreePoint(Point? point)
    {
        try
        {
            return point != null && point.IsValid && point.IsInBounds && point.HasObj && point.cell.growth.IsTree;
        }
        catch { return false; }
    }
}
