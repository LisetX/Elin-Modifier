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
    private void TickAutomationNeedsMaintenance()
    {
        var pc = GetSafePc();
        if (pc == null)
            return;

        if (_automationNeedsSleepStarted)
        {
            try
            {
                if (pc.conSleep != null)
                    return;
            }
            catch { }
            _automationNeedsSleepStarted = false;
            try { pc.stamina.Set(pc.stamina.max); }
            catch { }
            _automationNeedsSleepCompleted = true;
            _automationActionAi = null;
            _automationNeedsPostSleepEatAt = Time.unscaledTime + AutomationPostSleepEatDelaySeconds;
        }
        else if (_automationActionAi != null)
        {
            var ai = _automationActionAi;
            if (ReferenceEquals(pc.ai, ai) && ai.IsRunning)
                return;

            if (_automationNeedsLastFoodUid != 0)
            {
                var hungerImproved = false;
                try { hungerImproved = pc.hunger.value < _automationNeedsHungerBeforeEat; }
                catch { }
                if (!hungerImproved)
                    _automationNeedsSkippedFoodUids.Add(_automationNeedsLastFoodUid);
            }
            _automationNeedsLastFoodUid = 0;
            _automationNeedsHungerBeforeEat = 0;
            _automationActionAi = null;
        }

        if (!_automationNeedsInitialEatCompleted)
        {
            if (TryStartAutomationEat(pc))
                return;
            _automationNeedsInitialEatCompleted = true;
        }

        if (!_automationNeedsSleepCompleted && ShouldAutomationSleep(pc))
        {
            var canSleep = false;
            try { canSleep = pc.CanSleep(); }
            catch { }
            if (canSleep)
            {
                try
                {
                    pc.Sleep();
                    _automationNeedsSleepStarted = pc.conSleep != null;
                    if (_automationNeedsSleepStarted)
                        return;
                }
                catch { }
            }
            if (_automationNeedsSleepMoveAttempts < AutomationAutoSleepMoveAttemptLimit &&
                TryStartAutomationSleepSearchMove(pc))
            {
                return;
            }
        }

        if (!_automationNeedsSleepCompleted)
        {
            CompleteAutomationNeedsMaintenance();
            return;
        }

        if (_automationNeedsPostSleepEatAt > 0f)
        {
            if (Time.unscaledTime < _automationNeedsPostSleepEatAt)
                return;
            _automationNeedsPostSleepEatAt = 0f;
        }

        if (TryStartAutomationEat(pc))
            return;

        CompleteAutomationNeedsMaintenance();
    }
    private bool TryStartAutomationEat(Chara pc)
    {
        if (!ShouldAutomationEat(pc))
            return false;

        var food = FindAutomationFood(pc);
        if (food == null)
            return false;

        var foodUid = 0;
        var hungerBeforeEat = 0;
        try { foodUid = food.uid; }
        catch { }
        try { hungerBeforeEat = pc.hunger.value; }
        catch { }

        try
        {
            pc.InstantEat(food);
            var hungerImproved = false;
            try { hungerImproved = pc.hunger.value < hungerBeforeEat; }
            catch { }
            if (!hungerImproved && foodUid != 0)
                _automationNeedsSkippedFoodUids.Add(foodUid);
        }
        catch
        {
            try
            {
                if (foodUid != 0)
                    _automationNeedsSkippedFoodUids.Add(foodUid);
            }
            catch { }
        }
        return true;
    }
    private static bool ShouldAutomationEat(Chara pc)
    {
        try { return pc.hunger.value > AutomationAutoEatHungerThreshold; }
        catch { return false; }
    }
    private static bool ShouldAutomationSleep(Chara pc)
    {
        try
        {
            var staminaMax = Math.Max(1, pc.stamina.max);
            var staminaBelowTwentyPercent = (long)pc.stamina.value * 100L <
                                             (long)staminaMax * AutomationAutoSleepStaminaPercent;
            return pc.sleepiness.GetPhase() >= AutomationAutoSleepSleepinessPhase ||
                   pc.stamina.GetPhase() <= AutomationAutoSleepStaminaPhase ||
                   staminaBelowTwentyPercent;
        }
        catch { return false; }
    }
    private Thing? FindAutomationFood(Chara pc)
    {
        try
        {
            var hotbarFood = FindAutomationFoodIn(pc,
                GetAutomationHotbarItems(pc, thing => !thing.c_isImportant));
            if (hotbarFood != null)
                return hotbarFood;
        }
        catch { }

        try
        {
            return FindAutomationFoodIn(pc,
                pc.things.List((Thing t) => t != null && !t.c_isImportant, onlyAccessible: true));
        }
        catch { return null; }
    }
    private Thing? FindAutomationFoodIn(Chara pc, IEnumerable<Thing> foods)
    {
        Thing? fallback = null;
        foreach (var food in foods)
        {
            try
            {
                if (food == null || food.isDestroyed || _automationNeedsSkippedFoodUids.Contains(food.uid))
                    continue;
                if (pc.CanEat(food, shouldEat: true))
                    return food;
                if (fallback == null && pc.CanEat(food))
                    fallback = food;
            }
            catch { }
        }
        return fallback;
    }
    private bool TryStartAutomationSleepSearchMove(Chara pc)
    {
        if (_automationNeedsSleepMoveAttempts >= AutomationAutoSleepMoveAttemptLimit)
            return false;

        try
        {
            _automationNeedsSleepVisitedPoints.Add(GetAutomationPointKey(pc.pos));
            var offsetCount = AutomationSleepMoveOffsets.GetLength(0);
            var start = _automationNeedsSleepMoveAttempts % offsetCount;

            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < offsetCount; i++)
                {
                    var offsetIndex = (start + i) % offsetCount;
                    var point = new Point(
                        pc.pos.x + AutomationSleepMoveOffsets[offsetIndex, 0],
                        pc.pos.z + AutomationSleepMoveOffsets[offsetIndex, 1]);
                    if (!point.IsValid || !point.IsInBounds || point.HasChara || point.Equals(pc.pos))
                        continue;

                    var key = GetAutomationPointKey(point);
                    if (pass == 0 && _automationNeedsSleepVisitedPoints.Contains(key))
                        continue;
                    if (!pc.CanMoveTo(point, false))
                        continue;

                    var move = new AI_Goto(point, 0);
                    pc.SetAIImmediate(move);
                    _automationActionAi = move;
                    _automationNeedsSleepMoveAttempts++;
                    _automationNeedsSleepVisitedPoints.Add(key);
                    return true;
                }
            }
        }
        catch { }

        _automationNeedsSleepMoveAttempts = AutomationAutoSleepMoveAttemptLimit;
        return false;
    }
    private void CompleteAutomationNeedsMaintenance()
    {
        var delay = _automationNeedsResumeDelay;
        var resumeSweep = _automationNeedsResumeSweep;
        var resumeAction = resumeSweep ? _automationCurrentAction : null;
        _automationNeedsMaintenance = false;
        _automationNeedsInitialEatCompleted = false;
        _automationNeedsSleepStarted = false;
        _automationNeedsSleepCompleted = false;
        _automationNeedsResumeDelay = 0f;
        _automationNeedsLastFoodUid = 0;
        _automationNeedsHungerBeforeEat = 0;
        _automationNeedsSkippedFoodUids.Clear();
        _automationNeedsSleepMoveAttempts = 0;
        _automationNeedsSleepVisitedPoints.Clear();
        _automationNeedsPostSleepEatAt = 0f;
        _automationNeedsResumeSweep = false;
        _automationActionAi = null;

        if (resumeSweep && _automationRunning && resumeAction != null)
        {
            var type = NormalizeAutomationActionType(resumeAction.Type);
            if (type == AutomationTypeAutoMine || type == AutomationTypeAutoChop || type == AutomationTypeAutoHarvest)
            {
                ContinueAutomationSweep(resumeAction, type, false, false);
                return;
            }
        }

        _automationNextActionAt = Time.unscaledTime + Mathf.Max(0f, delay);
    }
    private void BeginAutomationSweepMaintenance()
    {
        _automationNeedsMaintenance = _automationRunning && _automationNeedsDetectionDuringExecution;
        _automationNeedsInitialEatCompleted = false;
        _automationNeedsSleepStarted = false;
        _automationNeedsSleepCompleted = false;
        _automationNeedsResumeDelay = 0f;
        _automationNeedsLastFoodUid = 0;
        _automationNeedsHungerBeforeEat = 0;
        _automationNeedsSkippedFoodUids.Clear();
        _automationNeedsSleepMoveAttempts = 0;
        _automationNeedsSleepVisitedPoints.Clear();
        _automationNeedsPostSleepEatAt = 0f;
        _automationNeedsResumeSweep = _automationNeedsMaintenance;
        _automationNextActionAt = Time.unscaledTime;
    }
}
