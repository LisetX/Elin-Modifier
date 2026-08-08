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
    private void StartAutomationInteract(AutomationActionConfig action)
    {
        _automationInteractionPerformedForTarget = false;
        if (_automationSweepCompletedCount == 0 &&
            _automationSkippedInteractUids.Count == 0 &&
            _automationInteractedThingUids.Count == 0)
            _automationSweepVerificationPass = false;
        if (!TryStartNextAutomationInteractTarget())
            FinishAutomationAction(true, AutomationText("当前区块没有可自动交互的物品", "No automatically interactable objects were found in the current area", "現在のエリアに自動操作可能なオブジェクトはありません", "В текущей области нет объектов для автоматического взаимодействия"));
    }
    private bool TryStartNextAutomationInteractTarget()
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
                if (!IsAutomationInteractableThing(pc, thing) ||
                    _automationSkippedInteractUids.Contains(thing.uid) ||
                    _automationInteractedThingUids.Contains(thing.uid))
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
        _automationInteractionPerformedForTarget = false;
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
    private static bool IsAutomationInteractableThing(Chara pc, Thing? thing)
    {
        if (thing == null)
            return false;

        try
        {
            if (thing.isDestroyed || !thing.ExistsOnMap || !thing.IsInstalled || thing.trait == null)
                return false;

            if (thing.trait is TraitSwitch trap && trap.CanDisarmTrap)
                return true;

            if (thing.isHidden || thing.isMasked || thing.isRoofItem)
                return false;

            if (IsAutomationSmashableVessel(thing))
                return true;

            if (thing.trait is TraitDoor || thing.trait is TraitContainer || thing.trait is TraitStairsDown ||
                thing.trait is TraitNewZone || thing.trait is TraitTeleporter || thing.trait is TraitElevator)
                return false;

            if (thing.trait.ToggleType != ToggleType.None)
                return true;

            var traitType = thing.trait.GetType();
            if (!AutomationInteractableTraitTypeCache.TryGetValue(traitType, out var interactable))
            {
                var trySetAct = AccessTools.Method(traitType, "TrySetAct", new[] { typeof(ActPlan) });
                var canUse = AccessTools.Method(traitType, "CanUse", new[] { typeof(Chara) });
                interactable = (trySetAct != null && trySetAct.DeclaringType != typeof(Trait)) ||
                               (canUse != null && canUse.DeclaringType != typeof(Trait));
                AutomationInteractableTraitTypeCache[traitType] = interactable;
            }
            return interactable;
        }
        catch { return false; }
    }
    private static bool IsAutomationSmashableVessel(Thing? thing)
    {
        if (thing?.trait == null)
            return false;

        try
        {
            return !thing.isDestroyed && thing.ExistsOnMap && thing.IsInstalled &&
                   thing.trait.CanBeSmashedToDeath && thing.trait.CanBeAttacked;
        }
        catch { return false; }
    }
    private bool TryPerformAutomationInteraction(Chara pc, Thing? target)
    {
        if (target == null)
            return false;

        try
        {
            if (target.isDestroyed || !target.ExistsOnMap || pc.Dist(target) > 1)
                return false;

            var trait = target.trait;
            if (trait == null)
                return false;

            if (trait is TraitSwitch trap && trap.CanDisarmTrap)
            {
                var map = GameAccess.World.CurrentMap;
                var existingAmounts = new Dictionary<int, long>();
                if (map != null)
                {
                    foreach (var thing in map.things)
                    {
                        if (thing != null)
                            existingAmounts[thing.uid] = thing.Num;
                    }
                }

                var disarmed = trap.TryDisarmTrap(pc);
                if (!disarmed && pc.Evalue(1656) < 3 && GameAccess.Random.Next(2) == 0)
                    trap.ActivateTrap(pc);
                TrackAutomationProducedDrops(map, existingAmounts);
                GameAccess.Runtime.Player.EndTurn();
                return disarmed || target.isDestroyed || !target.ExistsOnMap;
            }

            if (IsAutomationSmashableVessel(target))
            {
                var map = GameAccess.World.CurrentMap;
                var existingAmounts = new Dictionary<int, long>();
                if (map != null)
                {
                    foreach (var thing in map.things)
                    {
                        if (thing != null)
                            existingAmounts[thing.uid] = thing.Num;
                    }
                }

                target.DamageHP(1L, AttackSource.Melee, pc);
                TrackAutomationProducedDrops(map, existingAmounts);
                GameAccess.Runtime.Player.EndTurn();
                return target.isDestroyed || !target.ExistsOnMap;
            }

            var toggleType = trait.ToggleType;
            if (toggleType == ToggleType.Lever || toggleType == ToggleType.Curtain ||
                ((toggleType == ToggleType.Fire || toggleType == ToggleType.Light || toggleType == ToggleType.Electronics) && !target.isOn))
            {
                trait.Toggle(!target.isOn);
                return true;
            }

            var plan = new ActPlan
            {
                input = ActInput.LeftMouse,
                altAction = false,
                ignoreAddCondition = false,
                dist = pc.Dist(target)
            };
            plan.pos.Set(target.pos);
            trait.TrySetAct(plan);

            ActPlan.Item? item = null;
            for (var i = 0; i < plan.list.Count; i++)
            {
                if (ReferenceEquals(plan.list[i].tc, target))
                {
                    item = plan.list[i];
                    break;
                }
            }
            if (item == null && plan.list.Count > 0)
                item = plan.list[0];

            if (item != null)
            {
                var previousAi = pc.ai;
                var ui = GameAccess.Ui.Root;
                var previousLayerCount = ui?.layers?.Count ?? 0;
                var previousDialogWarned = Dialog.warned;
                Dialog.warned = true;
                bool endTurn;
                try { endTurn = item.Perform(); }
                finally { Dialog.warned = previousDialogWarned; }
                if (endTurn)
                    GameAccess.Runtime.Player.EndTurn();

                if (pc.ai != null && !ReferenceEquals(pc.ai, previousAi) && pc.ai.IsRunning)
                    _automationActionAi = pc.ai;

                if (ui != null && (ui.layers?.Count ?? 0) > previousLayerCount)
                    ui.CloseLayers();
                return true;
            }

            if (trait.CanUse(pc))
            {
                var previousAi = pc.ai;
                var ui = GameAccess.Ui.Root;
                var previousLayerCount = ui?.layers?.Count ?? 0;
                var endTurn = trait.OnUse(pc);
                if (endTurn)
                    GameAccess.Runtime.Player.EndTurn();
                if (pc.ai != null && !ReferenceEquals(pc.ai, previousAi) && pc.ai.IsRunning)
                    _automationActionAi = pc.ai;
                if (ui != null && (ui.layers?.Count ?? 0) > previousLayerCount)
                    ui.CloseLayers();
                return true;
            }
        }
        catch { }
        return false;
    }
    private void TrackAutomationProducedDrops(Map? map, Dictionary<int, long> existingAmounts)
    {
        if (map == null)
            return;

        try
        {
            EnsureAutomationProducedPickupScope();
            foreach (var thing in map.things)
            {
                if (thing == null || thing.isDestroyed || !thing.ExistsOnMap || thing.placeState != PlaceState.roaming)
                    continue;
                if (existingAmounts.TryGetValue(thing.uid, out var previousAmount) && thing.Num <= previousAmount)
                    continue;

                thing.ignoreAutoPick = false;
                _automationProducedPickupUids.Add(thing.uid);
            }
        }
        catch { }
    }
    private void ContinueAutomationInteract(AutomationActionConfig action, bool targetCompleted, bool timedOut)
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

        if (targetCompleted && _automationTargetThing != null)
        {
            _automationSweepCompletedCount++;
            try { _automationInteractedThingUids.Add(_automationTargetThing.uid); }
            catch { }
        }
        else if (_automationTargetThing != null)
        {
            try { _automationSkippedInteractUids.Add(_automationTargetThing.uid); }
            catch { }
        }

        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetPoint = null;
        _automationInteractionPerformedForTarget = false;
        _automationActionStartedAt = Time.unscaledTime;

        if (TryStartNextAutomationInteractTarget())
            return;

        if (!_automationSweepVerificationPass)
        {
            _automationSweepVerificationPass = true;
            _automationSkippedInteractUids.Clear();
            if (TryStartNextAutomationInteractTarget())
                return;
        }

        FinishAutomationAction(true,
            AutomationText("当前区块的可交互物品已处理完成，共交互 ", "Automatic interaction completed; interacted with ", "現在のエリアの自動操作が完了しました。操作数: ", "Автоматическое взаимодействие завершено; обработано: ") +
            _automationSweepCompletedCount.ToString(CultureInfo.InvariantCulture));
    }
}
