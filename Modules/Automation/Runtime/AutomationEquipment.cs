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
    private static List<Thing> GetAutomationHotbarItems(Chara pc, Func<Thing, bool> predicate)
    {
        var result = new List<Thing>();
        try
        {
            foreach (var thing in pc.things)
            {
                if (thing == null || thing.isDestroyed || !thing.IsHotItem || thing.GetRootCard() != pc || !predicate(thing))
                    continue;
                result.Add(thing);
            }
            result.Sort((left, right) => left.invX.CompareTo(right.invX));

            var current = GameAccess.Runtime.Player.currentHotItem?.Thing;
            if (current != null)
            {
                var index = result.IndexOf(current);
                if (index > 0)
                {
                    result.RemoveAt(index);
                    result.Insert(0, current);
                }
            }
        }
        catch { }
        return result;
    }
    private static List<Thing> GetAutomationCombatWeapons(Chara pc)
    {
        var result = GetAutomationHotbarItems(pc, thing => thing.IsWeapon);
        var seen = new HashSet<Thing>(result);
        var backpackWeapons = new List<Thing>();

        try
        {
            foreach (var weapon in pc.things.List((Thing thing) => thing != null && thing.IsWeapon, onlyAccessible: true))
            {
                if (weapon == null || weapon.isDestroyed || weapon.GetRootCard() != pc || seen.Contains(weapon))
                    continue;
                seen.Add(weapon);
                backpackWeapons.Add(weapon);
            }
        }
        catch { }

        backpackWeapons.Sort((left, right) =>
        {
            var rowOrder = left.invY.CompareTo(right.invY);
            return rowOrder != 0 ? rowOrder : left.invX.CompareTo(right.invX);
        });
        result.AddRange(backpackWeapons);
        return result;
    }
    private static void SetAutomationCurrentTool(Chara pc, Func<Thing, bool> predicate)
    {
        try
        {
            var tools = GetAutomationHotbarItems(pc, predicate);
            var seen = new HashSet<Thing>(tools);
            var backpackTools = new List<Thing>();
            foreach (var tool in pc.things.List(thing => thing != null && predicate(thing), onlyAccessible: true))
            {
                if (tool == null || tool.isDestroyed || tool.GetRootCard() != pc || seen.Contains(tool))
                    continue;
                seen.Add(tool);
                backpackTools.Add(tool);
            }

            backpackTools.Sort((left, right) =>
            {
                var rowOrder = left.invY.CompareTo(right.invY);
                return rowOrder != 0 ? rowOrder : left.invX.CompareTo(right.invX);
            });
            tools.AddRange(backpackTools);

            if (tools.Count == 0)
            {
                SetAutomationEmptyHands(pc);
                return;
            }

            var selected = tools[0];
            GameAccess.Runtime.Player.SetCurrentHotItem(selected.trait.GetHotItem());
            if (!ReferenceEquals(GameAccess.Runtime.Player.currentHotItem?.Thing, selected))
                GameAccess.Runtime.Player.EquipTool(selected, true);
        }
        catch { }
    }
    private static void SetAutomationEmptyHands(Chara pc)
    {
        try { GameAccess.Runtime.Player.SetCurrentHotItem(GameAccess.Runtime.Player.hotItemNoItem); }
        catch { }
        try { pc.ranged = null; }
        catch { }
        try
        {
            if (pc.body.slotMainHand?.thing != null)
                pc.body.Unequip(pc.body.slotMainHand);
            if (pc.body.slotOffHand?.thing != null)
                pc.body.Unequip(pc.body.slotOffHand);
        }
        catch { }
    }
    private static void PrepareAutomationCombatEquipment(Chara pc)
    {
        var weapons = GetAutomationCombatWeapons(pc);

        Thing? preferredRanged = null;
        Thing? primaryMelee = null;
        foreach (var weapon in weapons)
        {
            try
            {
                if (weapon.IsRangedWeapon && pc.CanEquipRanged(weapon))
                {
                    preferredRanged = weapon;
                    break;
                }
                if (weapon.IsMeleeWeapon)
                {
                    primaryMelee = weapon;
                    break;
                }
            }
            catch { }
        }

        if (preferredRanged != null)
        {
            try
            {
                GameAccess.Runtime.Player.SetCurrentHotItem(preferredRanged.trait.GetHotItem());
                pc.ranged = preferredRanged;
                return;
            }
            catch { }
        }

        Thing? secondaryMelee = null;
        if (primaryMelee != null)
        {
            foreach (var weapon in weapons)
            {
                if (ReferenceEquals(primaryMelee, weapon))
                    continue;
                try
                {
                    if (!weapon.IsMeleeWeapon)
                        continue;
                    secondaryMelee = weapon;
                    break;
                }
                catch { }
            }
        }

        if (primaryMelee != null && TryEquipAutomationMeleeWeapon(pc, primaryMelee, pc.body.slotMainHand))
        {
            TryEquipAutomationMeleeWeapon(pc, secondaryMelee, pc.body.slotOffHand);
            try
            {
                GameAccess.Runtime.Player.SetCurrentHotItem(primaryMelee.trait.GetHotItem());
                pc.ranged = null;
                return;
            }
            catch { }
        }

        SetAutomationEmptyHands(pc);
    }
    private static bool TryEquipAutomationMeleeWeapon(Chara pc, Thing? weapon, BodySlot? slot)
    {
        if (weapon == null || slot == null)
            return false;
        try
        {
            if (ReferenceEquals(slot.thing, weapon))
                return true;
            return pc.body.Equip(weapon, slot, false) || ReferenceEquals(slot.thing, weapon);
        }
        catch
        {
            return false;
        }
    }
}
