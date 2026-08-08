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
    private void ContinueAutomationSweep(AutomationActionConfig action, string type, bool targetCompleted, bool timedOut)
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
        else if (type == AutomationTypeAutoMine && _automationTargetPoint != null)
        {
            _automationSkippedMinePoints.Add(GetAutomationPointKey(_automationTargetPoint));
        }
        else if (type == AutomationTypeAutoChop && _automationTargetPoint != null)
        {
            _automationSkippedChopPoints.Add(GetAutomationPointKey(_automationTargetPoint));
        }
        else if (type == AutomationTypeAutoHarvest && _automationTargetPoint != null)
        {
            _automationSkippedHarvestPoints.Add(GetAutomationPointKey(_automationTargetPoint));
        }
        else if (type == AutomationTypeAutoKill && _automationTargetChara != null)
        {
            try { _automationSkippedEnemyUids.Add(_automationTargetChara.uid); }
            catch { }
        }

        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetPoint = null;
        _automationTargetChara = null;
        _automationKillApproaching = false;
        _automationActionStartedAt = Time.unscaledTime;

        if (_automationNeedsDetectionDuringExecution && targetCompleted &&
            (type == AutomationTypeAutoMine || type == AutomationTypeAutoChop || type == AutomationTypeAutoHarvest))
        {
            BeginAutomationSweepMaintenance();
            return;
        }

        var started = type == AutomationTypeAutoMine
            ? TryStartNextAutomationMineTarget()
            : type == AutomationTypeAutoChop
                ? TryStartNextAutomationChopTarget()
                : type == AutomationTypeAutoHarvest
                    ? TryStartNextAutomationHarvestTarget()
                    : TryStartNextAutomationKillTarget();
        if (started)
            return;

        if (!_automationSweepVerificationPass)
        {
            _automationSweepVerificationPass = true;
            if (type == AutomationTypeAutoMine)
            {
                _automationSkippedMinePoints.Clear();
            }
            else if (type == AutomationTypeAutoChop)
            {
                _automationSkippedChopPoints.Clear();
            }
            else if (type == AutomationTypeAutoHarvest)
            {
                _automationSkippedHarvestPoints.Clear();
            }
            else
            {
                _automationSkippedEnemyUids.Clear();
                _automationEnemyFailureCounts.Clear();
            }

            started = type == AutomationTypeAutoMine
                ? TryStartNextAutomationMineTarget()
                : type == AutomationTypeAutoChop
                    ? TryStartNextAutomationChopTarget()
                    : type == AutomationTypeAutoHarvest
                        ? TryStartNextAutomationHarvestTarget()
                        : TryStartNextAutomationKillTarget();
            if (started)
                return;
        }

        if (type == AutomationTypeAutoKill)
        {
            _automationKillWaitingForEmptyRecheck = true;
            _automationKillEmptyRecheckCount = 0;
            _automationKillNextEmptyRecheckAt = Time.unscaledTime + AutomationKillEmptyRecheckDelaySeconds;
            return;
        }

        var count = _automationSweepCompletedCount.ToString(CultureInfo.InvariantCulture);
        FinishAutomationAction(true, type == AutomationTypeAutoMine
            ? AutomationText("全图已无可挖掘目标，共完成 ", "No mineable targets remain on the map; completed ", "マップ上に採掘可能な対象はありません。完了数: ", "На карте больше нет доступных целей для добычи; выполнено: ") + count
            : type == AutomationTypeAutoChop
                ? AutomationText("全图已无可砍伐树木，共完成 ", "No trees remain to chop on the map; completed ", "マップ上に伐採可能な木はありません。完了数: ", "На карте больше нет деревьев для рубки; выполнено: ") + count
                : type == AutomationTypeAutoHarvest
                    ? AutomationText("全图已无可采集目标，共完成 ", "No gatherable targets remain on the map; completed ", "マップ上に採集可能な対象はありません。完了数: ", "На карте больше нет доступных целей для сбора; выполнено: ") + count
                    : AutomationText("全图已无敌对目标，共完成 ", "No hostile targets remain on the map; completed ", "マップ上に敵対対象はありません。完了数: ", "На карте больше нет враждебных целей; выполнено: ") + count);
    }
}
