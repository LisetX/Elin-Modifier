using System;
using System.Collections.Generic;
using HarmonyLib;

internal static class PcSlimeUnlockScope
{
    [ThreadStatic]
    private static int _allow;

    [ThreadStatic]
    private static int _suppress;

    internal static bool Active => _allow > 0 && _suppress == 0;

    internal static void EnterAllow()
    {
        _allow++;
    }

    internal static void ExitAllow()
    {
        if (_allow > 0)
            _allow--;
    }

    internal static void EnterSuppress()
    {
        _suppress++;
    }

    internal static void ExitSuppress()
    {
        if (_suppress > 0)
            _suppress--;
    }
}

internal sealed class PcSlimeMechanicsUnlock
{
    private const int SlimeGeneFeatId = 1274;
    private const int DevourAbilityId = 6608;

    private readonly ICharacterGameAccess _characters;
    private readonly IBoundGameValue<List<int>> _storedAbilities;
    private readonly IBoundGameMethod _hasElement;
    private readonly IBoundGameMethod _hasBase;
    private readonly IBoundGameMethod _setBase;

    internal PcSlimeMechanicsUnlock(
        ICharacterGameAccess characters,
        IGameMemberBinder binder)
    {
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _storedAbilities = binder.BindInstanceValue<List<int>>(
            typeof(Chara),
            GameValueAccess.Read,
            "_listAbility");
        _hasElement = binder.BindInstanceMethod(
            typeof(Card),
            typeof(bool),
            new[] { typeof(int), typeof(bool) },
            "HasElement");
        _hasBase = binder.BindInstanceMethod(
            typeof(ElementContainer),
            typeof(bool),
            new[] { typeof(int) },
            "HasBase");
        _setBase = binder.BindInstanceMethod(
            typeof(ElementContainer),
            typeof(Element),
            new[] { typeof(int), typeof(int), typeof(int) },
            "SetBase");
    }

    internal bool AllowSlimeMechanics(Card card)
    {
        return card != null &&
               IsReady &&
               PcSlimeUnlockScope.Active &&
               ReferenceEquals(card, _characters.PlayerCharacter);
    }

    internal void SyncDevourAbility(bool enabled)
    {
        var player = _characters.PlayerCharacter;
        if (player == null)
            return;
        SyncDevourAbility(enabled, _characters.GetElements(player), player);
    }

    internal void SyncDevourAbility(
        bool enabled,
        ElementContainer? elements,
        Chara? player)
    {
        if (!IsReady || elements == null || player == null ||
            !ReferenceEquals(player, _characters.PlayerCharacter) ||
            HasElement(player, SlimeGeneFeatId))
            return;

        var granted = HasBase(elements, DevourAbilityId);
        if (enabled)
        {
            if (granted)
                return;
            if (_setBase.TryInvoke(
                    elements,
                    new object?[] { DevourAbilityId, 1, 0 },
                    out _))
                AllowPcGeneImplantPatchContext.RedrawAbilityLayer();
            return;
        }

        if (!granted || HasStoredAbility(player, DevourAbilityId))
            return;
        if (_setBase.TryInvoke(
                elements,
                new object?[] { DevourAbilityId, 0, 0 },
                out _))
            AllowPcGeneImplantPatchContext.RedrawAbilityLayer();
    }

    private bool IsReady =>
        _storedAbilities.IsBound &&
        _hasElement.IsBound &&
        _hasBase.IsBound &&
        _setBase.IsBound;

    private bool HasBase(ElementContainer elements, int id)
    {
        return _hasBase.TryInvoke(elements, new object?[] { id }, out var value) &&
               value is bool result &&
               result;
    }

    private bool HasElement(Chara character, int id)
    {
        return _hasElement.TryInvoke(
                   character,
                   new object?[] { id, false },
                   out var value) &&
               value is bool result &&
               result;
    }

    private bool HasStoredAbility(Chara character, int id)
    {
        return _storedAbilities.TryGet(character, out var stored) &&
               stored != null &&
               (stored.IndexOf(id) >= 0 || stored.IndexOf(-id) >= 0);
    }
}

internal sealed partial class AllowPcGeneImplantModule
{
    internal bool AllowPcSlimeMechanics(Card card)
    {
        return Enabled && _slimeUnlock.AllowSlimeMechanics(card);
    }

    internal void SyncPcDevourAbility()
    {
        _slimeUnlock.SyncDevourAbility(Enabled);
    }

    internal void SyncPcDevourAbility(ElementContainer? elements, Chara? player)
    {
        _slimeUnlock.SyncDevourAbility(Enabled, elements, player);
    }
}

internal static partial class AllowPcGeneImplantPatchContext
{
    internal static bool AllowPcSlimeMechanics(Card card)
    {
        return Current?.AllowPcSlimeMechanics(card) == true;
    }

    internal static void SyncPcDevourAbility(ElementContainerCard container)
    {
        if (container == null)
            return;
        Current?.SyncPcDevourAbility(container, container.owner as Chara);
    }
}

[HarmonyPatch(typeof(Card), "IsSlimeEvolvable", MethodType.Getter)]
internal static class CardIsSlimeEvolvablePcSlimeUnlockPatch
{
    private static void Postfix(Card __instance, ref bool __result)
    {
        if (!__result && AllowPcGeneImplantPatchContext.AllowPcSlimeMechanics(__instance))
            __result = true;
    }
}

[HarmonyPatch(typeof(FoodEffect), "Proc", new[]
{
    typeof(Chara), typeof(Thing), typeof(bool)
})]
internal static class FoodEffectProcPcSlimeUnlockScopePatch
{
    private static void Prefix()
    {
        PcSlimeUnlockScope.EnterAllow();
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        PcSlimeUnlockScope.ExitAllow();
        return __exception;
    }
}

[HarmonyPatch(typeof(WindowChara), "RefreshSkill", new[] { typeof(int) })]
internal static class WindowCharaRefreshSkillPcSlimeUnlockScopePatch
{
    private static void Prefix()
    {
        PcSlimeUnlockScope.EnterAllow();
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        PcSlimeUnlockScope.ExitAllow();
        return __exception;
    }
}

[HarmonyPatch(typeof(Chara), "MaxGeneSlot", MethodType.Getter)]
internal static class CharaMaxGeneSlotPcSlimeUnlockSuppressPatch
{
    private static void Prefix()
    {
        PcSlimeUnlockScope.EnterSuppress();
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        PcSlimeUnlockScope.ExitSuppress();
        return __exception;
    }
}

[HarmonyPatch(typeof(ElementContainerCard), "CheckSkillActions")]
internal static class ElementContainerCardCheckSkillActionsPcSlimeUnlockPatch
{
    private static void Postfix(ElementContainerCard __instance)
    {
        AllowPcGeneImplantPatchContext.SyncPcDevourAbility(__instance);
    }
}
