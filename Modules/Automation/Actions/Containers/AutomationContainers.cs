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
    private void StartAutomationSearchContainers(AutomationActionConfig action)
    {
        if (_automationSweepCompletedCount == 0 && _automationSkippedContainerUids.Count == 0)
            _automationSweepVerificationPass = false;
        if (!TryStartNextAutomationContainerTarget())
            FinishAutomationAction(true, AutomationText("当前区块没有装有物品的容器", "No containers with items were found in the current area", "現在のエリアにアイテム入りのコンテナはありません", "В текущей области нет контейнеров с предметами"));
    }
    private bool TryStartNextAutomationContainerTarget()
    {
        var pc = GetSafePc();
        var map = GameAccess.World.CurrentMap;
        if (pc == null || map == null)
            return false;

        Thing? best = null;
        var bestDistance = int.MaxValue;
        try
        {
            foreach (var thing in map.things)
            {
                if (!IsAutomationContainerTarget(thing) ||
                    _automationSkippedContainerUids.Contains(thing.uid))
                    continue;

                var distance = pc.Dist(thing);
                if (distance >= bestDistance)
                    continue;
                best = thing;
                bestDistance = distance;
            }
        }
        catch { }

        if (best == null)
            return false;

        _automationTargetThing = best;
        _automationTargetPoint = best.pos.Copy();
        _automationActionStartedAt = Time.unscaledTime;
        if (pc.Dist(best) <= 1)
        {
            _automationActionAi = null;
            return true;
        }

        var approach = new AI_Goto(best, 1);
        pc.SetAIImmediate(approach);
        _automationActionAi = approach;
        return true;
    }
    private static bool IsAutomationContainerTarget(Thing? thing)
    {
        try
        {
            return thing != null && !thing.isDestroyed && thing.ExistsOnMap &&
                   thing.IsContainer && thing.things != null && thing.things.Count > 0;
        }
        catch { return false; }
    }
    private bool TryDumpAutomationContainerContents(Chara pc, Thing? container)
    {
        if (container == null)
            return false;

        try
        {
            if (container.isDestroyed || !container.ExistsOnMap || !container.IsContainer || pc.Dist(container) > 1)
                return false;
            if (container.things == null || container.things.Count == 0)
                return true;

            var dropPoint = container.pos.Copy();
            var contents = container.things.ToList();
            var moved = 0;
            EnsureAutomationProducedPickupScope();
            for (var i = 0; i < contents.Count; i++)
            {
                var item = contents[i];
                if (item == null || item.isDestroyed)
                    continue;
                try
                {
                    var dropped = GameAccess.World.AddCard(GameAccess.World.CurrentZone, item, dropPoint).Thing;
                    if (dropped == null)
                        continue;
                    dropped.SetPlaceState(PlaceState.roaming);
                    dropped.ignoreAutoPick = false;
                    _automationProducedPickupUids.Add(dropped.uid);
                    moved++;
                }
                catch { }
            }
            return moved > 0 || container.things.Count == 0;
        }
        catch
        {
            return false;
        }
    }
    private void ContinueAutomationSearchContainers(AutomationActionConfig action, bool targetCompleted, bool timedOut)
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
        else if (_automationTargetThing != null)
        {
            try { _automationSkippedContainerUids.Add(_automationTargetThing.uid); }
            catch { }
        }

        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetPoint = null;
        _automationActionStartedAt = Time.unscaledTime;

        if (TryStartNextAutomationContainerTarget())
            return;

        if (!_automationSweepVerificationPass)
        {
            _automationSweepVerificationPass = true;
            _automationSkippedContainerUids.Clear();
            if (TryStartNextAutomationContainerTarget())
                return;
        }

        FinishAutomationAction(true,
            AutomationText("当前区块的容器已搜索完成，共处理 ", "Container search completed; processed ", "現在のエリアのコンテナ検索が完了しました。処理数: ", "Поиск контейнеров завершён; обработано: ") +
            _automationSweepCompletedCount.ToString(CultureInfo.InvariantCulture));
    }
}
