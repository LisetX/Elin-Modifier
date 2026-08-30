using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private static bool CanUseRodStackingTarget(Thing? thing)
    {
        try
        {
            var supportedId = thing?.trait is TraitSpellbook ||
                              string.Equals(thing?.id, "spellbook", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "spellbook_random", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "rod", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "rod_random", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "rod_wish", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "stethoscope", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "lockpick", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "blanket_cold", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "blanket_fire", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "whip_love", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "whip_egg", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(thing?.id, "whip_interest", StringComparison.OrdinalIgnoreCase);
            return thing != null &&
                   !thing.isDestroyed &&
                   !IsCraftVirtualThing(thing) &&
                   supportedId &&
                   thing.trait.HasCharges;
        }
        catch
        {
            return false;
        }
    }
    private static bool IsRodInPlayerInventory(Thing? thing)
    {
        if (!CanUseRodStackingTarget(thing))
            return false;
        try { return ReferenceEquals(thing!.GetRootCard(), GameAccess.Characters.PlayerCharacter); }
        catch { return false; }
    }
    private static string GetRodStackingEffectName(Thing thing)
    {
        try
        {
            var sourceName = thing.trait.GetRefElement()?.GetName();
            if (!string.IsNullOrWhiteSpace(sourceName))
                return sourceName.Trim();
        }
        catch { }

        try
        {
            var rod = thing.trait as TraitRod;
            var sourceName = rod?.source?.GetName();
            if (!string.IsNullOrWhiteSpace(sourceName))
                return sourceName.Trim();
        }
        catch { }

        try
        {
            var rod = thing.trait as TraitRod;
            if (rod != null)
                return rod.IdEffect.ToString() + "|" + (rod.N1 ?? "");
        }
        catch { }
        return "";
    }
    private static bool RodPropertiesMatch(Thing? left, Thing? right)
    {
        if (!CanUseRodStackingTarget(left) || !CanUseRodStackingTarget(right) || ReferenceEquals(left, right))
            return false;
        try
        {
            var leftWish = string.Equals(left!.id, "rod_wish", StringComparison.OrdinalIgnoreCase);
            var rightWish = string.Equals(right!.id, "rod_wish", StringComparison.OrdinalIgnoreCase);
            if (leftWish || rightWish)
                return leftWish && rightWish;

            var leftIsNormalRod = string.Equals(left.id, "rod", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(left.id, "rod_random", StringComparison.OrdinalIgnoreCase);
            var rightIsNormalRod = string.Equals(right.id, "rod", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(right.id, "rod_random", StringComparison.OrdinalIgnoreCase);
            if (leftIsNormalRod || rightIsNormalRod)
            {
                if (!leftIsNormalRod || !rightIsNormalRod)
                    return false;
                var leftRodName = GetRodStackingEffectName(left);
                var rightRodName = GetRodStackingEffectName(right);
                return !string.IsNullOrWhiteSpace(leftRodName) &&
                       string.Equals(leftRodName, rightRodName, StringComparison.Ordinal);
            }

            var leftIsSpellbook = left.trait is TraitSpellbook;
            var rightIsSpellbook = right.trait is TraitSpellbook;
            if (leftIsSpellbook || rightIsSpellbook)
            {
                if (!leftIsSpellbook || !rightIsSpellbook)
                    return false;
                var leftSpellName = GetRodStackingEffectName(left);
                var rightSpellName = GetRodStackingEffectName(right);
                return !string.IsNullOrWhiteSpace(leftSpellName) &&
                       string.Equals(leftSpellName, rightSpellName, StringComparison.Ordinal);
            }

            return string.Equals(left.id, right.id, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    private List<Thing> GetRodStackingCandidates(Thing? target)
    {
        var result = new List<Thing>();
        if (!IsRodInPlayerInventory(target))
            return result;
        try
        {
            foreach (var thing in EnumerateRodStackingInventoryThings())
            {
                if (!IsRodInPlayerInventory(thing) ||
                    ReferenceEquals(thing, target) ||
                    thing.isEquipped ||
                    ReferenceEquals(GameAccess.Characters.PlayerCharacter?.held, thing) ||
                    thing.c_charges <= 0 ||
                    !RodPropertiesMatch(target, thing))
                    continue;
                result.Add(thing);
            }
        }
        catch { }

        result.Sort((left, right) =>
        {
            var chargeCompare = right.c_charges.CompareTo(left.c_charges);
            return chargeCompare != 0
                ? chargeCompare
                : string.Compare(SafeThingName(left), SafeThingName(right), StringComparison.CurrentCulture);
        });
        return result;
    }
    private IEnumerable<Thing> EnumerateRodStackingInventoryThings()
    {
        var result = new List<Thing>();
        var seen = new HashSet<object>(ReferenceObjectComparer.Instance);

        void AddThing(Thing? thing)
        {
            if (thing == null || thing.isDestroyed || !seen.Add(thing))
                return;
            result.Add(thing);
        }

        foreach (var thing in EnumerateAiInventoryThings())
        {
            AddThing(thing);
            try
            {
                thing.things?.Foreach(child => AddThing(child), true);
            }
            catch { }
        }

        return result;
    }
    private void OpenRodStackingWindow(Thing thing)
    {
        if (!_rodStacking || !IsRodInPlayerInventory(thing))
            return;

        _rodStackingTarget = thing;
        _rodStackingCandidatePage = 0;
        _rodStackingSource = GetRodStackingCandidates(thing).FirstOrDefault();
        if (IsLGuiInitialized())
        {
            EnsureLGuiEditorVisible();
            OpenLGuiRodStackingEditor();
        }
    }
    private bool ApplyRodStacking()
    {
        try
        {
            var target = _rodStackingTarget;
            var source = _rodStackingSource;
            if (!_rodStacking || !IsRodInPlayerInventory(target))
            {
                _log = T("被充能物品不存在", "The receiving item no longer exists");
                return false;
            }
            if (!IsRodInPlayerInventory(source))
            {
                _log = T("消耗物品不存在", "The consumed item no longer exists");
                return false;
            }
            if (source!.isEquipped || ReferenceEquals(GameAccess.Characters.PlayerCharacter?.held, source))
            {
                _log = T("消耗物品正在使用中", "The consumed item is currently in use");
                return false;
            }
            if (!RodPropertiesMatch(target, source))
            {
                _log = T("物品充能类型不一致", "Item charge types do not match");
                return false;
            }
            if (source.c_charges <= 0)
            {
                _log = T("消耗物品没有充能", "The consumed item has no charges");
                return false;
            }

            var targetCharges = Math.Max(0, target!.c_charges);
            var sourceCharges = Math.Max(0, source.c_charges);
            var combinedCharges = (int)Math.Min(int.MaxValue, (long)targetCharges + sourceCharges);
            source.Destroy();
            target.c_charges = combinedCharges;
            _rodStackingSource = null;
            RefreshInventoryUi();
            _log = T("充能堆叠完成: ", "Charge stacking completed: ") +
                   SafeThingName(target) + " " + targetCharges.ToString(CultureInfo.InvariantCulture) +
                   " + " + sourceCharges.ToString(CultureInfo.InvariantCulture) +
                   " = " + combinedCharges.ToString(CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex)
        {
            _log = T("充能堆叠失败: ", "Charge stacking failed: ") + ex.Message;
            return false;
        }
    }
}
