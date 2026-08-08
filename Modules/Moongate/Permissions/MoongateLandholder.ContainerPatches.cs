using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

[HarmonyPatch(typeof(TraitContainer), "TryOpen")]
internal static class MoongateLandholderContainerOpenPatch
{
    private static bool Prefix(TraitContainer __instance)
    {
        var owner = __instance?.owner;
        if (owner == null || !owner.ExistsOnMap ||
            !MoongateLandholderPrivilegeContext.IsActive(GameAccess.World.CurrentZone))
            return true;

        if (owner.c_lockLv > 0)
            return true;

        __instance.Open();
        return false;
    }
}

internal struct MoongateContainerPermissionState
{
    private Card? _first;
    private Card? _second;
    private Card? _third;
    private Card? _fourth;

    internal void Suppress(Card? card)
    {
        if (card == null || !card.isNPCProperty ||
            ReferenceEquals(card, _first) || ReferenceEquals(card, _second) ||
            ReferenceEquals(card, _third) || ReferenceEquals(card, _fourth))
            return;

        if (_first == null)
            _first = card;
        else if (_second == null)
            _second = card;
        else if (_third == null)
            _third = card;
        else if (_fourth == null)
            _fourth = card;
        else
            return;

        card.isNPCProperty = false;
    }

    internal void Restore()
    {
        Restore(_first);
        Restore(_second);
        Restore(_third);
        Restore(_fourth);
    }

    private static void Restore(Card? card)
    {
        if (card != null && !card.isDestroyed)
            card.isNPCProperty = true;
    }
}

internal static class MoongateLandholderInventoryAccess
{
    internal static bool IsWorldContainerInventory(InvOwner? inv)
    {
        if (inv == null || !MoongateLandholderPrivilegeContext.IsActive(GameAccess.World.CurrentZone))
            return false;

        return IsWorldContainer(inv.owner) || IsWorldContainer(inv.Container);
    }

    internal static void Suppress(InvOwner? inv, ref MoongateContainerPermissionState state)
    {
        if (!IsWorldContainerInventory(inv))
            return;

        state.Suppress(inv!.owner);
        state.Suppress(inv.Container);
    }

    internal static void SuppressActiveContainerPair(
        InvOwner? first,
        InvOwner? second,
        ref MoongateContainerPermissionState state)
    {
        Suppress(first, ref state);
        Suppress(second, ref state);
        Suppress(InvOwner.Trader, ref state);
    }

    private static bool IsWorldContainer(Card? card)
    {
        return card is Thing thing && thing.ExistsOnMap && thing.trait is TraitContainer;
    }
}

[HarmonyPatch]
internal static class MoongateLandholderInventoryInteractionPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.PropertyGetter(typeof(InvOwner), "AllowTransfer");
        yield return AccessTools.Method(typeof(InvOwner), "AllowHold");
        yield return AccessTools.Method(typeof(InvOwner), "OnClick");
        yield return AccessTools.Method(typeof(InvOwner), "CanShiftClick");
        yield return AccessTools.Method(typeof(InvOwner), "CanCtrlClick");
        yield return AccessTools.Method(typeof(InvOwner), "CanAltClick");
        yield return AccessTools.Method(typeof(InvOwner), "OnCancelDrag");
        yield return AccessTools.Method(
            typeof(InvOwner),
            "ListInteractions",
            new[] { typeof(ButtonGrid), typeof(bool) });
        yield return AccessTools.Method(
            typeof(InvOwner),
            "ListInteractions",
            new[]
            {
                typeof(InvOwner.ListInteraction),
                typeof(Thing),
                typeof(Trait),
                typeof(ButtonGrid),
                typeof(bool)
            });
    }

    private static void Prefix(InvOwner __instance, ref MoongateContainerPermissionState __state)
    {
        MoongateLandholderInventoryAccess.SuppressActiveContainerPair(
            __instance,
            null,
            ref __state);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        MoongateContainerPermissionState __state)
    {
        __state.Restore();
        return __exception;
    }
}

[HarmonyPatch]
internal static class MoongateLandholderContainerTransferPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(InvOwner.Transaction), "IsValid");
        yield return AccessTools.Method(typeof(InvOwner.Transaction), "Process");
    }

    private static void Prefix(
        InvOwner.Transaction __instance,
        ref MoongateContainerPermissionState __state)
    {
        InvOwner? destination = null;
        try
        {
            destination = __instance.destInv;
        }
        catch
        {
        }

        MoongateLandholderInventoryAccess.SuppressActiveContainerPair(
            __instance.inv,
            destination,
            ref __state);

        if (MoongateLandholderPrivilegeContext.IsActive(GameAccess.World.CurrentZone) &&
            __instance.thing?.parent is Thing parent &&
            parent.ExistsOnMap && parent.trait is TraitContainer)
            __state.Suppress(parent);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        MoongateContainerPermissionState __state)
    {
        __state.Restore();
        return __exception;
    }
}

[HarmonyPatch(typeof(UIInventory), "RefreshMenu")]
internal static class MoongateLandholderContainerMenuPatch
{
    private static void Prefix(
        UIInventory __instance,
        ref MoongateContainerPermissionState __state)
    {
        MoongateLandholderInventoryAccess.Suppress(__instance.owner, ref __state);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return MoongateLandholderPrivilegeContext.ReplaceZoneOwnershipChecks(instructions);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        MoongateContainerPermissionState __state)
    {
        __state.Restore();
        return __exception;
    }
}

