using System;
using HarmonyLib;

internal sealed class PcGeneAbilityMutationIsolation
{
    private readonly ICharacterGameAccess _characters;
    private readonly IBoundGameValue<CharaAbility> _characterAbility;

    internal PcGeneAbilityMutationIsolation(
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
    }

    internal bool IsPlayerCharacter(Chara character)
    {
        return character != null &&
               ReferenceEquals(character, _characters.PlayerCharacter);
    }

    internal bool TryAdd(
        CharaAbility ability,
        int id,
        bool negative)
    {
        return id > 0 && CanHandle(ability, false);
    }

    internal bool TryRemove(CharaAbility ability, int id)
    {
        return id != 0 && CanHandle(ability, true);
    }

    private bool CanHandle(CharaAbility ability, bool reverse)
    {
        var scope = PcGeneAbilityMutationScope.Current;
        return ability != null && scope != null && scope.Reverse == reverse &&
               _characterAbility.IsBound &&
               _characterAbility.TryGet(scope.Character, out var playerAbility) &&
               ReferenceEquals(ability, playerAbility);
    }
}

internal static class PcGeneAbilityMutationScope
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
        _current = AllowPcGeneImplantPatchContext.CanProjectGeneFor(character)
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
    internal bool CanProjectGeneFor(Chara character)
    {
        return Enabled && _abilityMutationIsolation.IsPlayerCharacter(character);
    }

    internal bool TryAddProjectedGeneAbility(
        CharaAbility ability,
        int id,
        bool negative)
    {
        return Enabled && _abilityMutationIsolation.TryAdd(ability, id, negative);
    }

    internal bool TryRemoveProjectedGeneAbility(CharaAbility ability, int id)
    {
        return Enabled && _abilityMutationIsolation.TryRemove(ability, id);
    }
}

internal static partial class AllowPcGeneImplantPatchContext
{
    internal static bool CanProjectGeneFor(Chara character)
    {
        return Current?.CanProjectGeneFor(character) == true;
    }

    internal static bool TryAddProjectedGeneAbility(
        CharaAbility ability,
        int id,
        bool negative)
    {
        return Current?.TryAddProjectedGeneAbility(ability, id, negative) == true;
    }

    internal static bool TryRemoveProjectedGeneAbility(
        CharaAbility ability,
        int id)
    {
        return Current?.TryRemoveProjectedGeneAbility(ability, id) == true;
    }
}

[HarmonyPatch(typeof(DNA), "Apply", new[] { typeof(Chara), typeof(bool) })]
internal static class DnaApplyPcGeneAbilityMutationScopePatch
{
    private static void Prefix(
        Chara __0,
        bool __1,
        out PcGeneAbilityMutationScope.State __state)
    {
        __state = PcGeneAbilityMutationScope.Enter(__0, __1);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        PcGeneAbilityMutationScope.State __state)
    {
        PcGeneAbilityMutationScope.Exit(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(CharaAbility), "Add", new[]
{
    typeof(int), typeof(int), typeof(bool)
})]
internal static class CharaAbilityAddPcGeneAbilityMutationIsolationPatch
{
    private static bool Prefix(
        CharaAbility __instance,
        int __0,
        bool __2)
    {
        return !AllowPcGeneImplantPatchContext.TryAddProjectedGeneAbility(
            __instance,
            __0,
            __2);
    }
}

[HarmonyPatch(typeof(CharaAbility), "Remove", new[] { typeof(int) })]
internal static class CharaAbilityRemovePcGeneAbilityMutationIsolationPatch
{
    private static bool Prefix(
        CharaAbility __instance,
        int __0)
    {
        return !AllowPcGeneImplantPatchContext.TryRemoveProjectedGeneAbility(
            __instance,
            __0);
    }
}
