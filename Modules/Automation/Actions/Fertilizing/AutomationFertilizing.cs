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
    private void StartAutomationFertilize(AutomationActionConfig action)
    {
        _automationSkippedFertilizePoints.Clear();
        _automationSweepVerificationPass = false;
        _automationSweepCompletedCount = 0;
        if (TryStartNextAutomationFertilizeTarget(out var noFertilizer))
            return;

        FinishAutomationAction(true, noFertilizer
            ? AutomationText("没有可用肥料，已跳过", "No fertilizer is available; skipped", "使用可能な肥料がないためスキップしました", "Нет доступного удобрения; действие пропущено")
            : AutomationText("当前区块没有需要施肥的种子或作物", "No seeds or crops need fertilizing in the current area", "現在のエリアに施肥が必要な種や作物はありません", "В текущей области нет семян или культур, требующих удобрения"));
    }
    private bool TryStartNextAutomationFertilizeTarget(out bool noFertilizer)
    {
        noFertilizer = false;
        var pc = GetSafePc();
        var map = GameAccess.World.CurrentMap;
        if (pc == null || map == null)
            return false;

        var point = FindNearestAutomationFertilizePoint(pc);
        if (point == null)
            return false;

        var fertilizer = FindAutomationFertilizer(pc);
        if (fertilizer == null)
        {
            noFertilizer = true;
            return false;
        }

        try
        {
            if (!pc.TryHoldCard(fertilizer) || pc.held == null || !IsAutomationFertilizer(pc.held.Thing))
            {
                noFertilizer = true;
                return false;
            }

            var held = pc.held.Thing;
            var recipe = held.trait.GetRecipe();
            if (recipe == null)
            {
                noFertilizer = true;
                return false;
            }

            var task = new TaskBuild
            {
                recipe = recipe,
                held = held,
                pos = point.Copy()
            };
            _automationTargetThing = held;
            _automationTargetPoint = point.Copy();
            _automationActionStartedAt = Time.unscaledTime;
            pc.SetAIImmediate(task);
            _automationActionAi = task;
            return true;
        }
        catch
        {
            noFertilizer = FindAutomationFertilizer(pc) == null;
            return false;
        }
    }
    private Point? FindNearestAutomationFertilizePoint(Chara pc)
    {
        var map = GameAccess.World.CurrentMap;
        if (map == null)
            return null;

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
                    if (_automationSkippedFertilizePoints.Contains(GetAutomationPointKey(point)) ||
                        !IsAutomationFertilizeTargetPoint(point))
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
        return best;
    }
    private static bool IsAutomationFertilizeTargetPoint(Point? point)
    {
        if (point == null || !point.IsValid || !point.IsInBounds)
            return false;

        try
        {
            foreach (var thing in point.Things)
            {
                if (thing == null || thing.isDestroyed)
                    continue;
                if (thing.trait is TraitFertilizer)
                    return false;
            }

            var plant = GameAccess.World.CurrentMap?.TryGetPlant(point);
            if (plant != null && plant.seed != null)
                return plant.fert <= 0;

            foreach (var thing in point.Things)
            {
                if (thing != null && !thing.isDestroyed && thing.IsInstalled && thing.trait is TraitSeed)
                    return true;
            }
        }
        catch { }
        return false;
    }
    private static bool IsAutomationFertilizedPoint(Point? point)
    {
        if (point == null || !point.IsValid || !point.IsInBounds)
            return false;
        try
        {
            var plant = GameAccess.World.CurrentMap?.TryGetPlant(point);
            if (plant != null && plant.fert > 0)
                return true;
            foreach (var thing in point.Things)
            {
                if (thing != null && !thing.isDestroyed && thing.trait is TraitFertilizer fertilizer && !fertilizer.Defertilize)
                    return true;
            }
        }
        catch { }
        return false;
    }
    private static bool IsAutomationFertilizer(Thing? thing)
    {
        try
        {
            return thing != null && !thing.isDestroyed && thing.Num > 0 &&
                   thing.trait is TraitFertilizer fertilizer && !fertilizer.Defertilize;
        }
        catch { return false; }
    }
    private static Thing? FindAutomationFertilizer(Chara pc)
    {
        try
        {
            var hotbar = GetAutomationHotbarItems(pc, IsAutomationFertilizer);
            if (hotbar.Count > 0)
                return hotbar[0];
        }
        catch { }

        try
        {
            foreach (var thing in pc.things.List((Thing t) => IsAutomationFertilizer(t), onlyAccessible: true))
            {
                if (IsAutomationFertilizer(thing) && thing.GetRootCard() == pc)
                    return thing;
            }
        }
        catch { }
        return null;
    }
    private void ContinueAutomationFertilize(AutomationActionConfig action, bool targetCompleted, bool timedOut)
    {
        if (timedOut)
        {
            try
            {
                var pc = GetSafePc();
                if (pc != null && _automationActionAi != null && ReferenceEquals(pc.ai, _automationActionAi) && pc.ai.IsRunning)
                    pc.ai.Cancel();
            }
            catch { }
        }

        if (targetCompleted)
        {
            _automationSweepCompletedCount++;
        }
        else if (_automationTargetPoint != null)
        {
            _automationSkippedFertilizePoints.Add(GetAutomationPointKey(_automationTargetPoint));
        }

        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetPoint = null;
        _automationActionStartedAt = Time.unscaledTime;

        if (TryStartNextAutomationFertilizeTarget(out var noFertilizer))
            return;

        if (!noFertilizer && !_automationSweepVerificationPass)
        {
            _automationSweepVerificationPass = true;
            _automationSkippedFertilizePoints.Clear();
            if (TryStartNextAutomationFertilizeTarget(out noFertilizer))
                return;
        }

        var completed = _automationSweepCompletedCount.ToString(CultureInfo.InvariantCulture);
        FinishAutomationAction(true, noFertilizer
            ? AutomationText("肥料已用完，共完成 ", "Fertilizer ran out; completed ", "肥料を使い切りました。完了数: ", "Удобрение закончилось; выполнено: ") + completed
            : AutomationText("当前区块已无需要施肥的种子或作物，共完成 ", "No seeds or crops still need fertilizing in the current area; completed ", "現在のエリアに施肥が必要な種や作物はありません。完了数: ", "В текущей области больше нет семян или культур, требующих удобрения; выполнено: ") + completed);
    }
}
