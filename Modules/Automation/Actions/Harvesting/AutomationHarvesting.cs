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
    private void StartAutomationHarvest(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        if (pc != null)
            SetAutomationEmptyHands(pc);
        _automationSkippedHarvestPoints.Clear();
        _automationSweepVerificationPass = false;
        _automationSweepCompletedCount = 0;
        if (!TryStartNextAutomationHarvestTarget())
            FinishAutomationAction(true, AutomationText("全图没有可采集目标", "No gatherable targets on the map", "マップ上に採集可能な対象がありません", "На карте нет доступных целей для сбора"));
    }
    private bool TryStartNextAutomationHarvestTarget()
    {
        var pc = GetSafePc();
        var map = GameAccess.World.CurrentMap;
        if (pc == null || map == null)
            return false;

        TaskHarvest? bestTask = null;
        Point? bestPoint = null;
        var bestDistance = int.MaxValue;
        try
        {
            var bounds = map.bounds;
            for (var x = bounds.x; x <= bounds.maxX; x++)
            {
                for (var z = bounds.z; z <= bounds.maxZ; z++)
                {
                    var point = new Point(x, z);
                    if (_automationSkippedHarvestPoints.Contains(GetAutomationPointKey(point)))
                        continue;
                    var task = GetAutomationHarvestTask(pc, point);
                    if (task == null)
                        continue;
                    var distance = point.Distance(pc.pos);
                    if (distance >= bestDistance)
                        continue;
                    bestTask = task;
                    bestPoint = point;
                    bestDistance = distance;
                }
            }
        }
        catch { }

        if (bestTask == null || bestPoint == null)
            return false;

        _automationTargetThing = bestTask.target;
        _automationTargetPoint = bestPoint.Copy();
        _automationActionStartedAt = Time.unscaledTime;
        pc.SetAIImmediate(bestTask);
        _automationActionAi = bestTask;
        return true;
    }
    private static TaskHarvest? GetAutomationHarvestTask(Chara pc, Point point)
    {
        try
        {
            if (!point.IsValid || !point.IsInBounds)
                return null;
            return TaskHarvest.TryGetAct(pc, point);
        }
        catch { return null; }
    }
    private static bool CanAutomationHarvestPoint(Chara pc, Point point)
    {
        return GetAutomationHarvestTask(pc, point) != null;
    }
}
