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
    private void ContinueAutomationPickup(AutomationActionConfig action, bool targetCompleted, bool timedOut, bool backpackFull)
    {
        if (timedOut)
        {
            try
            {
                var currentPc = GetSafePc();
                if (currentPc != null && _automationActionAi != null && ReferenceEquals(currentPc.ai, _automationActionAi) && currentPc.ai.IsRunning)
                    currentPc.ai.Cancel();
            }
            catch { }
        }

        var target = _automationTargetThing;
        var replacedLowerValueItem = false;
        var droppedForBurden = false;
        var targetIsPreferred = target != null && IsAutomationPreferredPickup(action, target);
        if (targetCompleted)
        {
            _automationSweepCompletedCount++;
            if (target != null)
            {
                try { _automationProducedPickupUids.Remove(target.uid); }
                catch { }
            }
        }

        var pc = GetSafePc();
        if (pc != null)
            droppedForBurden = TryRelieveAutomationPickupBurden(pc, targetCompleted ? null : target, action);

        if (!targetCompleted && !droppedForBurden && backpackFull && target != null &&
                 (targetIsPreferred || IsAutomationPickupReplacementEnabled(action)))
        {
            if (pc != null)
                replacedLowerValueItem = TryReplaceAutomationLowestValueItem(pc, target, action, targetIsPreferred);
        }

        if (!targetCompleted && !droppedForBurden && !replacedLowerValueItem && target != null)
        {
            try { _automationSkippedPickupUids.Add(target.uid); }
            catch { }
        }

        _automationActionAi = null;
        _automationTargetThing = null;
        _automationTargetPoint = null;
        _automationTargetChara = null;
        _automationActionStartedAt = Time.unscaledTime;

        var count = _automationSweepCompletedCount.ToString(CultureInfo.InvariantCulture);
        if (backpackFull && !droppedForBurden && !replacedLowerValueItem)
        {
            // The nearest ground item may be cheaper than every replaceable backpack item.
            // Skip only that target and keep scanning instead of stopping the whole pickup action,
            // otherwise a more valuable item farther away is never evaluated.
            if ((targetIsPreferred || IsAutomationPickupReplacementEnabled(action)) && TryStartNextAutomationPickupTarget(action))
                return;

            if (TryStartAutomationPickupVerificationPass(action))
                return;

            FinishAutomationAction(true,
                AutomationText("背包已满，停止拾取，共拾取 ", "Backpack is full; pickup stopped. Picked up ", "バックパックが満杯のため取得を停止しました。取得数: ", "Рюкзак заполнен; подбор остановлен. Подобрано: ") + count);
            return;
        }

        if (TryStartNextAutomationPickupTarget(action))
            return;

        if (TryStartAutomationPickupVerificationPass(action))
            return;

        FinishAutomationAction(true,
            AutomationText("范围内已无符合条件的物品，共拾取 ", "No qualifying items remain in range; picked up ", "範囲内に条件を満たすアイテムはありません。取得数: ", "В радиусе больше нет подходящих предметов; подобрано: ") + count);
    }
    private static bool IsAutomationPickupReplacementEnabled(AutomationActionConfig action)
    {
        var value = (action.Param3 ?? "").Trim();
        return value == "1" ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
    private static HashSet<string> GetAutomationPreferredPickupIds(AutomationActionConfig action)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = action.Param4 ?? "";
        foreach (var item in text.Replace('；', ';').Split(';'))
        {
            var id = item.Trim();
            if (id.Length > 0)
                result.Add(id);
        }
        return result;
    }
    private static bool IsAutomationPreferredPickup(AutomationActionConfig action, Thing thing)
    {
        try
        {
            var id = (thing.id ?? "").Trim();
            return id.Length > 0 && GetAutomationPreferredPickupIds(action).Contains(id);
        }
        catch { return false; }
    }
    private static string GetAutomationPickupGroupKey(Thing thing)
    {
        try
        {
            var id = (thing.id ?? "").Trim();
            if (id.Length > 0)
                return id;
            return "#uid:" + thing.uid.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "#object:" + thing.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }
    }
    private static long AddAutomationValue(long left, long right)
    {
        if (right <= 0L)
            return left;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
    private static bool TryGetAutomationStackValue(Card card, out long totalValue)
    {
        return TryGetAutomationStackValue(card, 0, out totalValue);
    }
    private static bool TryGetAutomationStackValue(Card card, int depth, out long totalValue)
    {
        totalValue = 0L;
        try
        {
            var unitValue = Math.Max(0L, card.GetValue(PriceType.Default, false));
            var amount = Math.Max(1L, card.Num);
            totalValue = unitValue > long.MaxValue / amount ? long.MaxValue : unitValue * amount;

            // A carried container is still one backpack item. Include its contents in the
            // comparison so a cheap box containing valuable items is not selected as the minimum.
            if (depth < 16 && card.IsContainer && card.things != null)
            {
                foreach (var child in card.things)
                {
                    if (child == null || child.isDestroyed || !TryGetAutomationStackValue(child, depth + 1, out var childValue))
                        continue;
                    totalValue = totalValue > long.MaxValue - childValue ? long.MaxValue : totalValue + childValue;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
    private bool TryGetAutomationGroundGroupValue(AutomationActionConfig action, Thing groundTarget, out long totalValue)
    {
        totalValue = 0L;
        var map = GameAccess.World.CurrentMap;
        if (map == null || _automationPickupOrigin == null)
            return false;

        var targetKey = GetAutomationPickupGroupKey(groundTarget);
        var radius = ParseAutomationInt(action.Param2, 30, 1, 200);
        try
        {
            foreach (var thing in map.things)
            {
                if (thing == null || thing.isDestroyed || !thing.ExistsOnMap || thing.placeState != PlaceState.roaming)
                    continue;
                if (_automationSkippedPickupUids.Contains(thing.uid))
                    continue;
                if (!_automationProducedPickupUids.Contains(thing.uid) &&
                    _automationPickupOrigin.Distance(thing.pos) > radius)
                    continue;
                if (!string.Equals(GetAutomationPickupGroupKey(thing), targetKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryGetAutomationStackValue(thing, out var stackValue))
                    continue;
                totalValue = AddAutomationValue(totalValue, stackValue);
            }
            return totalValue > 0L;
        }
        catch
        {
            return false;
        }
    }
    private bool TryReplaceAutomationLowestValueItem(Chara pc, Thing groundTarget,
        AutomationActionConfig action, bool forceForPreferredItem)
    {
        if (pc.things == null)
            return false;
        if (!TryGetAutomationGroundGroupValue(action, groundTarget, out var groundValue) &&
            !TryGetAutomationStackValue(groundTarget, out groundValue))
            return false;

        Thing? lowest = null;
        var lowestValue = long.MaxValue;
        var protectedIds = GetAutomationPreferredPickupIds(action);
        try
        {
            foreach (var thing in pc.things)
            {
                if (thing == null || thing.isDestroyed || !pc.things.ShouldShowOnGrid(thing))
                    continue;
                if (thing.c_isImportant || thing.isEquipped || thing.IsHotItem)
                    continue;
                if (IsAutomationProtectedFromAutoDiscard(thing))
                    continue;
                if (protectedIds.Contains((thing.id ?? "").Trim()))
                    continue;
                if (thing.trait == null || !thing.trait.CanBeDropped || thing.trait.CanOnlyCarry || thing.trait is TraitAbility)
                    continue;
                if (!TryGetAutomationStackValue(thing, out var totalValue))
                    continue;

                if (totalValue >= lowestValue)
                    continue;
                lowest = thing;
                lowestValue = totalValue;
            }
        }
        catch
        {
            return false;
        }

        if (lowest == null || (!forceForPreferredItem && groundValue <= lowestValue))
            return false;

        var droppedUid = lowest.uid;
        try
        {
            var zone = GameAccess.World.CurrentZone;
            if (zone == null || !ReferenceEquals(lowest.parent, pc))
                return false;

            // DropThing() may return without moving the item for several trait-specific paths.
            // All unsafe cases were filtered above, so move the selected direct backpack child
            // to the current zone explicitly and verify the parent change before retrying pickup.
            lowest.ignoreAutoPick = true;
            zone.AddCard(lowest, pc.pos);
            if (lowest.isDestroyed || !lowest.ExistsOnMap)
                return false;
        }
        catch
        {
            return false;
        }

        _automationSkippedPickupUids.Add(droppedUid);
        _automationDiscardedPickupUids.Add(droppedUid);
        return true;
    }
    private bool TryRelieveAutomationPickupBurden(Chara pc, Thing? pendingTarget,
        AutomationActionConfig action)
    {
        var droppedAny = false;
        for (var attempt = 0; attempt < 64; attempt++)
        {
            if (!IsAutomationPickupBurdened(pc, pendingTarget))
                break;
            if (!TryDropAutomationLowestValuePerWeightItem(pc, action))
                break;
            droppedAny = true;
        }
        return droppedAny;
    }
    private static bool IsAutomationPickupBurdened(Chara pc, Thing? pendingTarget)
    {
        try
        {
            if (pc.GetBurden() > StatsBurden.None)
                return true;
            return pendingTarget != null && !pendingTarget.isDestroyed && pendingTarget.ExistsOnMap &&
                   pc.GetBurden(pendingTarget) > StatsBurden.None;
        }
        catch { return false; }
    }
    private bool TryDropAutomationLowestValuePerWeightItem(Chara pc,
        AutomationActionConfig action)
    {
        if (pc.things == null || GameAccess.World.CurrentZone == null)
            return false;

        var protectedIds = GetAutomationPreferredPickupIds(action);
        var candidates = new List<(Thing Item, long Weight, long Value)>();
        try
        {
            foreach (var thing in pc.things)
            {
                if (thing == null || thing.isDestroyed || !pc.things.ShouldShowOnGrid(thing))
                    continue;
                if (thing.c_isImportant || thing.isEquipped || thing.IsHotItem)
                    continue;
                if (IsAutomationProtectedFromAutoDiscard(thing))
                    continue;
                if (protectedIds.Contains((thing.id ?? "").Trim()))
                    continue;
                if (thing.trait == null || !thing.trait.CanBeDropped || thing.trait.CanOnlyCarry || thing.trait is TraitAbility)
                    continue;

                var weight = Math.Max(0L, (long)thing.ChildrenAndSelfWeight);
                if (weight <= 0L || !TryGetAutomationStackValue(thing, out var totalValue))
                    continue;
                candidates.Add((thing, weight, totalValue));
            }
        }
        catch
        {
            return false;
        }

        if (candidates.Count == 0)
            return false;

        candidates.Sort((left, right) =>
        {
            var weightOrder = right.Weight.CompareTo(left.Weight);
            return weightOrder != 0 ? weightOrder : left.Value.CompareTo(right.Value);
        });

        var candidateCount = Math.Min(5, candidates.Count);
        var selected = candidates[0];
        for (var i = 1; i < candidateCount; i++)
        {
            var candidate = candidates[i];
            var candidateRatio = (decimal)candidate.Value * selected.Weight;
            var selectedRatio = (decimal)selected.Value * candidate.Weight;
            if (candidateRatio < selectedRatio ||
                (candidateRatio == selectedRatio && candidate.Weight > selected.Weight))
            {
                selected = candidate;
            }
        }

        var droppedUid = selected.Item.uid;
        try
        {
            if (!ReferenceEquals(selected.Item.parent, pc))
                return false;
            selected.Item.ignoreAutoPick = true;
            GameAccess.World.AddCard(GameAccess.World.CurrentZone, selected.Item, pc.pos);
            if (selected.Item.isDestroyed || !selected.Item.ExistsOnMap)
                return false;
        }
        catch
        {
            return false;
        }

        _automationSkippedPickupUids.Add(droppedUid);
        _automationDiscardedPickupUids.Add(droppedUid);
        return true;
    }
    private static bool IsAutomationProtectedFromAutoDiscard(Thing thing)
    {
        if (thing == null)
            return true;

        try
        {
            if (thing.IsWeapon || thing.IsEquipment)
                return true;
        }
        catch { }

        try
        {
            if (IsToolThing(thing))
                return true;
        }
        catch { }

        try
        {
            if (thing.IsFood)
                return true;
        }
        catch { }

        try
        {
            if (thing.trait is TraitPotion)
                return true;
        }
        catch { }

        return false;
    }
    private void StartAutomationPickup(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        if (pc == null)
        {
            FinishAutomationAction(false, AutomationText("未获取到玩家", "Player unavailable", "プレイヤーを取得できません", "Игрок недоступен"));
            return;
        }

        _automationSkippedPickupUids.Clear();
        _automationDiscardedPickupUids.Clear();
        EnsureAutomationProducedPickupScope();
        _automationSweepVerificationPass = false;
        _automationSweepCompletedCount = 0;
        _automationPickupOrigin = pc.pos.Copy();
        if (!TryStartNextAutomationPickupTarget(action) &&
            !TryStartAutomationPickupVerificationPass(action))
            FinishAutomationAction(true, AutomationText("范围内没有符合价值条件的物品", "No item in range meets the value requirement", "範囲内に価値条件を満たすアイテムがありません", "В радиусе нет предметов, соответствующих условию стоимости"));
    }
    private bool TryStartAutomationPickupVerificationPass(AutomationActionConfig action)
    {
        if (_automationSweepVerificationPass)
            return false;

        _automationSweepVerificationPass = true;
        _automationSkippedPickupUids.Clear();
        foreach (var uid in _automationDiscardedPickupUids)
            _automationSkippedPickupUids.Add(uid);
        return TryStartNextAutomationPickupTarget(action);
    }
    private bool TryStartNextAutomationPickupTarget(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        var map = GameAccess.World.CurrentMap;
        if (pc == null || map == null || _automationPickupOrigin == null)
            return false;

        var minimumValue = ParseAutomationInt(action.Param1, 0, 0, int.MaxValue);
        var radius = ParseAutomationInt(action.Param2, 30, 1, 200);
        var preferredIds = GetAutomationPreferredPickupIds(action);
        var candidates = new List<Thing>();
        var groupValues = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        Thing? best = null;
        var bestValue = long.MinValue;
        var bestDistance = int.MaxValue;
        var bestIsPreferred = false;
        try
        {
            foreach (var thing in map.things)
            {
                if (thing == null || thing.isDestroyed || !thing.ExistsOnMap || thing.placeState != PlaceState.roaming)
                    continue;
                if (_automationSkippedPickupUids.Contains(thing.uid))
                    continue;
                if (!_automationProducedPickupUids.Contains(thing.uid) &&
                    _automationPickupOrigin.Distance(thing.pos) > radius)
                    continue;

                if (!TryGetAutomationStackValue(thing, out var stackValue))
                    continue;
                candidates.Add(thing);
                var groupKey = GetAutomationPickupGroupKey(thing);
                groupValues.TryGetValue(groupKey, out var previousValue);
                groupValues[groupKey] = AddAutomationValue(previousValue, stackValue);
            }

            foreach (var thing in candidates)
            {
                var groupKey = GetAutomationPickupGroupKey(thing);
                if (!groupValues.TryGetValue(groupKey, out var value))
                    continue;
                var itemId = (thing.id ?? "").Trim();
                var isPreferred = itemId.Length > 0 && preferredIds.Contains(itemId);
                var distance = pc.Dist(thing);
                if (!isPreferred && value < minimumValue)
                    continue;
                if (best != null)
                {
                    if (bestIsPreferred && !isPreferred)
                        continue;
                    if (bestIsPreferred == isPreferred &&
                        (distance > bestDistance || (distance == bestDistance && value <= bestValue)))
                        continue;
                }
                best = thing;
                bestValue = value;
                bestDistance = distance;
                bestIsPreferred = isPreferred;
            }
        }
        catch { }

        if (best == null)
            return false;

        _automationTargetThing = best;
        var goal = new AI_Grab { target = best, num = -1, pickHeld = true };
        _automationActionStartedAt = Time.unscaledTime;
        pc.SetAIImmediate(goal);
        _automationActionAi = goal;
        return true;
    }
    private void EnsureAutomationProducedPickupScope()
    {
        Map? currentMap = null;
        try { currentMap = GameAccess.World.CurrentMap; }
        catch { }

        if (ReferenceEquals(_automationProducedPickupMap, currentMap))
            return;

        _automationProducedPickupUids.Clear();
        _automationProducedPickupMap = currentMap;
    }
}
