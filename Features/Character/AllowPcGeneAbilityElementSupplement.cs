using System;
using System.Collections.Generic;
using HarmonyLib;

internal sealed class PcGeneAbilityElementSupplement
{
    private const int SlimeGeneFeatId = 1274;

    private readonly ICharacterGameAccess _characters;
    private readonly IBoundGameValue<CharaAbility> _characterAbility;
    private readonly IBoundGameValue<List<int>> _storedAbilities;
    private readonly IBoundGameValue<int> _elementPotential;
    private readonly IBoundGameMethod _getElement;
    private readonly IBoundGameMethod _modBase;
    private readonly IBoundGameMethod _hasElement;

    internal PcGeneAbilityElementSupplement(
        ICharacterGameAccess characters,
        IGameMemberBinder binder)
    {
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _characterAbility = binder.BindInstanceValue<CharaAbility>(
            typeof(Chara),
            GameValueAccess.Read,
            "ability");
        _storedAbilities = binder.BindInstanceValue<List<int>>(
            typeof(Chara),
            GameValueAccess.Read,
            "_listAbility");
        _elementPotential = binder.BindInstanceValue<int>(
            typeof(Element),
            GameValueAccess.ReadWrite,
            "vPotential");
        _getElement = binder.BindInstanceMethod(
            typeof(ElementContainer),
            typeof(Element),
            new[] { typeof(int) },
            "GetElement");
        _modBase = binder.BindInstanceMethod(
            typeof(ElementContainer),
            typeof(Element),
            new[] { typeof(int), typeof(int) },
            "ModBase");
        _hasElement = binder.BindInstanceMethod(
            typeof(Card),
            typeof(bool),
            new[] { typeof(int), typeof(bool) },
            "HasElement");
    }

    internal bool NeedsSupplement(Chara character)
    {
        return character != null &&
               IsReady &&
               ReferenceEquals(character, _characters.PlayerCharacter) &&
               !HasElement(character, SlimeGeneFeatId);
    }

    internal bool TryGrant(CharaAbility ability, int id)
    {
        var scope = PcGeneAbilityScope.Current;
        if (id <= 0 || scope == null || scope.Reverse ||
            !IsScopeCharacterAbility(ability, scope))
            return false;

        var elements = _characters.GetElements(scope.Character);
        if (elements == null || GetElement(elements, id) != null)
            return false;

        return _modBase.TryInvoke(elements, new object?[] { id, 1 }, out _);
    }

    internal bool TryRevoke(CharaAbility ability, int id)
    {
        var scope = PcGeneAbilityScope.Current;
        if (id == 0 || scope == null || !scope.Reverse ||
            !IsScopeCharacterAbility(ability, scope))
            return false;

        var character = scope.Character;
        if (!HasElement(character, id) || HasStoredAbility(character, id))
            return false;

        var elements = _characters.GetElements(character);
        var element = elements == null ? null : GetElement(elements, id);
        if (element == null || element is Spell ||
            !_elementPotential.TryGet(element, out var potential) ||
            potential == -1)
            return false;

        return _elementPotential.TrySet(element, -1);
    }

    private bool IsReady =>
        _characterAbility.IsBound &&
        _storedAbilities.IsBound &&
        _elementPotential.IsBound &&
        _getElement.IsBound &&
        _modBase.IsBound &&
        _hasElement.IsBound;

    private bool IsScopeCharacterAbility(
        CharaAbility ability,
        PcGeneAbilityScope.Context scope)
    {
        return ability != null &&
               _characterAbility.TryGet(scope.Character, out var owned) &&
               ReferenceEquals(ability, owned);
    }

    private bool HasStoredAbility(Chara character, int id)
    {
        return _storedAbilities.TryGet(character, out var stored) &&
               stored != null &&
               stored.IndexOf(id) >= 0;
    }

    private Element? GetElement(ElementContainer elements, int id)
    {
        return _getElement.TryInvoke(elements, new object?[] { id }, out var value)
            ? value as Element
            : null;
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
}

internal static class PcGeneAbilityScope
{
    internal sealed class Context
    {
        internal Context(Chara character, bool reverse)
        {
            Character = character;
            Reverse = reverse;
        }

        internal Chara Character { get; }
        internal bool Reverse { get; }
    }

    internal sealed class State
    {
        internal State(Context? previous)
        {
            Previous = previous;
        }

        internal Context? Previous { get; }
    }

    [ThreadStatic]
    private static Context? _current;

    internal static Context? Current => _current;

    internal static State Enter(Chara character, bool reverse)
    {
        var state = new State(_current);
        _current = AllowPcGeneImplantPatchContext.NeedsGeneAbilitySupplement(character)
            ? new Context(character, reverse)
            : null;
        return state;
    }

    internal static void Exit(State state)
    {
        _current = state?.Previous;
    }
}

internal sealed partial class AllowPcGeneImplantModule
{
    internal bool NeedsGeneAbilitySupplement(Chara character)
    {
        return Enabled && _abilitySupplement.NeedsSupplement(character);
    }

    internal bool TryGrantGeneAbilityElement(CharaAbility ability, int id)
    {
        return Enabled && _abilitySupplement.TryGrant(ability, id);
    }

    internal bool TryRevokeGeneAbilityElement(CharaAbility ability, int id)
    {
        return Enabled && _abilitySupplement.TryRevoke(ability, id);
    }
}

internal static partial class AllowPcGeneImplantPatchContext
{
    internal static bool NeedsGeneAbilitySupplement(Chara character)
    {
        return Current?.NeedsGeneAbilitySupplement(character) == true;
    }

    internal static bool TryGrantGeneAbilityElement(CharaAbility ability, int id)
    {
        return Current?.TryGrantGeneAbilityElement(ability, id) == true;
    }

    internal static bool TryRevokeGeneAbilityElement(CharaAbility ability, int id)
    {
        return Current?.TryRevokeGeneAbilityElement(ability, id) == true;
    }

    internal static void RedrawAbilityLayer()
    {
        try
        {
            LayerAbility.Redraw();
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(DNA), "Apply", new[] { typeof(Chara), typeof(bool) })]
internal static class DnaApplyPcGeneAbilityScopePatch
{
    private static void Prefix(
        Chara __0,
        bool __1,
        out PcGeneAbilityScope.State __state)
    {
        __state = PcGeneAbilityScope.Enter(__0, __1);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        PcGeneAbilityScope.State __state)
    {
        PcGeneAbilityScope.Exit(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(CharaAbility), "Add", new[]
{
    typeof(int), typeof(int), typeof(bool)
})]
internal static class CharaAbilityAddPcGeneElementSupplementPatch
{
    private static void Postfix(CharaAbility __instance, int __0)
    {
        if (AllowPcGeneImplantPatchContext.TryGrantGeneAbilityElement(__instance, __0))
            AllowPcGeneImplantPatchContext.RedrawAbilityLayer();
    }
}

[HarmonyPatch(typeof(CharaAbility), "Remove", new[] { typeof(int) })]
internal static class CharaAbilityRemovePcGeneElementSupplementPatch
{
    private static void Postfix(CharaAbility __instance, int __0)
    {
        if (AllowPcGeneImplantPatchContext.TryRevokeGeneAbilityElement(__instance, __0))
            AllowPcGeneImplantPatchContext.RedrawAbilityLayer();
    }
}
