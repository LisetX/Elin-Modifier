using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

internal sealed class CharacterProtectionModule
{
    [ThreadStatic] private static int _lovePotionProtectionDepth;
    [ThreadStatic] private static Card? _friendlyFireAreaDamageSource;

    internal bool IgnoreFriendlyFire { get; private set; }
    internal bool AffinityOnlyIncrease { get; private set; }
    internal bool KarmaOnlyIncrease { get; private set; }
    internal bool AttackCannotBeInterrupted { get; private set; }
    internal bool AttackCannotBeInterruptedIncludeParty { get; private set; } = true;

    internal void Load(
        bool ignoreFriendlyFire,
        bool affinityOnlyIncrease,
        bool karmaOnlyIncrease,
        bool attackCannotBeInterrupted,
        bool attackCannotBeInterruptedIncludeParty)
    {
        IgnoreFriendlyFire = ignoreFriendlyFire;
        AffinityOnlyIncrease = affinityOnlyIncrease;
        KarmaOnlyIncrease = karmaOnlyIncrease;
        AttackCannotBeInterrupted = attackCannotBeInterrupted;
        AttackCannotBeInterruptedIncludeParty = attackCannotBeInterruptedIncludeParty;
    }

    internal void Reset()
    {
        IgnoreFriendlyFire = false;
        AffinityOnlyIncrease = false;
        KarmaOnlyIncrease = false;
        AttackCannotBeInterrupted = false;
        AttackCannotBeInterruptedIncludeParty = true;
    }

    internal bool SetIgnoreFriendlyFire(bool enabled)
    {
        if (IgnoreFriendlyFire == enabled)
            return false;
        IgnoreFriendlyFire = enabled;
        return true;
    }

    internal bool SetAffinityOnlyIncrease(bool enabled)
    {
        if (AffinityOnlyIncrease == enabled)
            return false;
        AffinityOnlyIncrease = enabled;
        return true;
    }

    internal bool SetKarmaOnlyIncrease(bool enabled)
    {
        if (KarmaOnlyIncrease == enabled)
            return false;
        KarmaOnlyIncrease = enabled;
        return true;
    }

    internal bool SetAttackCannotBeInterrupted(bool enabled)
    {
        if (AttackCannotBeInterrupted == enabled)
            return false;
        AttackCannotBeInterrupted = enabled;
        return true;
    }

    internal void SetAttackCannotBeInterruptedIncludeParty(bool enabled)
    {
        AttackCannotBeInterruptedIncludeParty = enabled;
    }

    internal bool ShouldPreventAffinityLoss()
    {
        return AffinityOnlyIncrease;
    }

    internal bool TryEnterLovePotionProtection(Card? source)
    {
        try
        {
            if (!AffinityOnlyIncrease || source == null ||
                (!source.IsPC && !source.IsPCParty))
                return false;
            _lovePotionProtectionDepth++;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsLovePotion(Thing? thing)
    {
        try
        {
            if (thing == null)
                return false;
            if (string.Equals(thing.id, "lovepotion", StringComparison.Ordinal))
                return true;

            var drink = thing.trait as TraitDrink;
            return drink != null &&
                   (drink.IdEffect == EffectId.Love || drink.IdEffect == EffectId.LovePlus);
        }
        catch
        {
            return false;
        }
    }

    internal void ExitLovePotionProtection(bool entered)
    {
        if (entered && _lovePotionProtectionDepth > 0)
            _lovePotionProtectionDepth--;
    }

    internal bool ShouldPreventLovePotionHostility(Chara? actor, Card? target)
    {
        try
        {
            var targetIsPcSide = target != null && (target.IsPC || target.IsPCParty);
            return AffinityOnlyIncrease &&
                   _lovePotionProtectionDepth > 0 &&
                   actor != null &&
                   target != null &&
                   (actor.IsPC || actor.IsPCParty || targetIsPcSide);
        }
        catch
        {
            return false;
        }
    }

    internal bool ShouldPreventKarmaLoss()
    {
        return KarmaOnlyIncrease;
    }

    internal bool ShouldPreventFriendlyFire(Card? source, Card? target)
    {
        try
        {
            if (!IgnoreFriendlyFire || source == null || target == null ||
                ReferenceEquals(source, target))
                return false;
            return IsFriendlyFireProtectedActor(source) &&
                   IsFriendlyFireProtectedActor(target);
        }
        catch
        {
            return false;
        }
    }

    internal static Card? EnterFriendlyFireAreaDamage(Card? source, Chara? origin)
    {
        var previous = _friendlyFireAreaDamageSource;
        try
        {
            _friendlyFireAreaDamageSource = origin ?? source?.Chara ?? source;
        }
        catch
        {
            _friendlyFireAreaDamageSource = origin ?? source;
        }
        return previous;
    }

    internal static void ExitFriendlyFireAreaDamage(Card? previous)
    {
        _friendlyFireAreaDamageSource = previous;
    }

    internal static Card? FriendlyFireAreaDamageSource => _friendlyFireAreaDamageSource;

    private static bool IsFriendlyFireProtectedActor(Card card)
    {
        var chara = card as Chara;
        return chara != null &&
               (chara.IsPCFactionOrMinion || chara.IsPCParty || chara.IsPCPartyMinion);
    }

    internal bool ShouldPreventAttackInterruption(Chara? attacker)
    {
        try
        {
            if (!AttackCannotBeInterrupted || attacker == null)
                return false;
            if (attacker.IsPC)
                return true;
            return AttackCannotBeInterruptedIncludeParty && attacker.IsPCParty;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsAbilityOrSpellAction(AIAct? action)
    {
        try
        {
            for (var current = action; current != null; current = current.parent)
            {
                var group = current.source?.group;
                if (string.Equals(group, "ABILITY", StringComparison.Ordinal) ||
                    string.Equals(group, "SPELL", StringComparison.Ordinal))
                    return true;
            }
        }
        catch
        {
        }
        return false;
    }
}

internal static class CharacterProtectionPatchContext
{
    internal static CharacterProtectionModule? Current =>
        ElinModifierPlugin.ActiveModules?.CharacterProtection;
}

[HarmonyPatch(typeof(Chara), "ModAffinity")]
internal static class CharaModAffinityOnlyIncreasePatch
{
    private static void Prefix(ref int __1)
    {
        var module = CharacterProtectionPatchContext.Current;
        if (__1 < 0 && module != null && module.ShouldPreventAffinityLoss())
            __1 = 0;
    }
}

internal sealed class LovePotionHostilityState
{
    internal bool Entered;
    internal Chara? Target;
    internal Hostility Hostility;
    internal Hostility OriginalHostility;
    internal Chara? Enemy;
}

[HarmonyPatch(
    typeof(Chara),
    "GiveLovePotion",
    new[] { typeof(Chara), typeof(Thing) })]
internal static class CharaGiveLovePotionAffinityProtectionPatch
{
    private static void Prefix(Chara __instance, Chara __0, out LovePotionHostilityState __state)
    {
        __state = new LovePotionHostilityState();
        var module = CharacterProtectionPatchContext.Current;
        if (module == null || !module.TryEnterLovePotionProtection(__instance))
            return;

        __state.Entered = true;
        __state.Target = __0;
        if (__0 == null)
            return;
        __state.Hostility = __0.hostility;
        __state.OriginalHostility = __0.c_originalHostility;
        __state.Enemy = __0.enemy;
    }

    private static Exception? Finalizer(Exception? __exception, LovePotionHostilityState __state)
    {
        var module = CharacterProtectionPatchContext.Current;
        try
        {
            var target = __state?.Target;
            if (__state != null && __state.Entered && target != null)
            {
                target.hostility = __state.Hostility;
                target.c_originalHostility = __state.OriginalHostility;
                target.enemy = __state.Enemy;
            }
        }
        catch
        {
        }
        finally
        {
            module?.ExitLovePotionProtection(__state != null && __state.Entered);
        }
        return __exception;
    }
}

[HarmonyPatch(
    typeof(ActEffect),
    "ProcAt",
    new[]
    {
        typeof(EffectId), typeof(int), typeof(BlessedState), typeof(Card),
        typeof(Card), typeof(Point), typeof(bool), typeof(ActRef)
    })]
internal static class ActEffectProcAtLovePotionAffinityProtectionPatch
{
    private static void Prefix(EffectId __0, Card __3, out bool __state)
    {
        var module = CharacterProtectionPatchContext.Current;
        __state = module != null &&
                  (__0 == EffectId.Love || __0 == EffectId.LovePlus) &&
                  module.TryEnterLovePotionProtection(__3);
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        CharacterProtectionPatchContext.Current?.ExitLovePotionProtection(__state);
        return __exception;
    }
}

[HarmonyPatch(
    typeof(ActThrow),
    "Throw",
    new[] { typeof(Card), typeof(Point), typeof(Card), typeof(Thing), typeof(ThrowMethod) })]
internal static class ActThrowLovePotionAffinityProtectionPatch
{
    private static void Prefix(Card __0, Thing __3, out bool __state)
    {
        var module = CharacterProtectionPatchContext.Current;
        __state = module != null &&
                  CharacterProtectionModule.IsLovePotion(__3) &&
                  module.TryEnterLovePotionProtection(__0);
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        CharacterProtectionPatchContext.Current?.ExitLovePotionProtection(__state);
        return __exception;
    }
}

[HarmonyPatch(
    typeof(Chara),
    "DoHostileAction",
    new[] { typeof(Card), typeof(bool) })]
internal static class CharaDoHostileActionLovePotionAffinityProtectionPatch
{
    private static bool Prefix(Chara __instance, Card __0)
    {
        var module = CharacterProtectionPatchContext.Current;
        return module == null ||
               (!module.ShouldPreventLovePotionHostility(__instance, __0) &&
                !module.ShouldPreventFriendlyFire(__instance, __0));
    }
}

[HarmonyPatch(typeof(Card), "DamageHP", new[]
{
    typeof(long), typeof(int), typeof(int), typeof(AttackSource), typeof(Card),
    typeof(bool), typeof(Thing), typeof(Chara), typeof(int)
})]
internal static class CardDamageHpFriendlyFireProtectionPatch
{
    private static bool Prefix(Card __instance, long __0, Card __4)
    {
        var module = CharacterProtectionPatchContext.Current;
        return __0 <= 0 || module == null ||
               !module.ShouldPreventFriendlyFire(__4, __instance);
    }
}

[HarmonyPatch(
    typeof(AttackProcess),
    "Perform",
    new[] { typeof(int), typeof(bool), typeof(float), typeof(bool), typeof(bool) })]
internal static class AttackProcessPerformFriendlyFireProtectionPatch
{
    private static bool Prefix(AttackProcess __instance, ref bool __result)
    {
        var module = CharacterProtectionPatchContext.Current;
        if (module == null ||
            !module.ShouldPreventFriendlyFire(__instance?.CC, __instance?.TC))
            return true;

        __result = true;
        return false;
    }
}

[HarmonyPatch]
internal static class ActEffectDamageEleFriendlyFireProtectionPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        const BindingFlags flags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var stableMethod = typeof(ActEffect).GetMethod(
            "DamageEle",
            flags,
            null,
            new[]
            {
                typeof(Card), typeof(EffectId), typeof(int), typeof(Element),
                typeof(List<Point>), typeof(ActRef), typeof(string)
            },
            null);
        if (stableMethod != null)
            return stableMethod;

        var nightlyMethod = typeof(ActEffect).GetMethod(
            "DamageEle",
            flags,
            null,
            new[]
            {
                typeof(Card), typeof(EffectId), typeof(int), typeof(Element),
                typeof(List<Point>), typeof(ActRef), typeof(string), typeof(Point)
            },
            null);
        if (nightlyMethod != null)
            return nightlyMethod;

        throw new MissingMethodException(
            "ActEffect.DamageEle Stable/Nightly signature was not found.");
    }

    private static void Prefix(Card __0, ActRef __5, out Card? __state)
    {
        __state = CharacterProtectionModule.EnterFriendlyFireAreaDamage(__0, __5.origin);
    }

    private static Exception? Finalizer(Exception? __exception, Card? __state)
    {
        CharacterProtectionModule.ExitFriendlyFireAreaDamage(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Point), "ListCards", new[] { typeof(bool) })]
internal static class PointListCardsFriendlyFireProtectionPatch
{
    private static void Postfix(List<Card> __result)
    {
        var module = CharacterProtectionPatchContext.Current;
        var source = CharacterProtectionModule.FriendlyFireAreaDamageSource;
        if (module == null || source == null || __result == null)
            return;

        for (var i = __result.Count - 1; i >= 0; i--)
            if (module.ShouldPreventFriendlyFire(source, __result[i]))
                __result.RemoveAt(i);
    }
}

[HarmonyPatch(typeof(Player), "ModKarma")]
internal static class PlayerModKarmaOnlyIncreasePatch
{
    private static void Prefix(ref int __0)
    {
        var module = CharacterProtectionPatchContext.Current;
        if (__0 < 0 && module != null && module.ShouldPreventKarmaLoss())
            __0 = 0;
    }
}

[HarmonyPatch(typeof(ActMelee), "AllowParry", MethodType.Getter)]
internal static class ActMeleeAllowParryAttackCannotBeInterruptedPatch
{
    private static void Postfix(ref bool __result)
    {
        var module = CharacterProtectionPatchContext.Current;
        if (__result && module != null && module.ShouldPreventAttackInterruption(Act.CC))
            __result = false;
    }
}

[HarmonyPatch(typeof(AIAct), "TryCancel")]
internal static class AIActTryCancelAttackCannotBeInterruptedPatch
{
    private static bool Prefix(AIAct __instance, ref bool __result)
    {
        var module = CharacterProtectionPatchContext.Current;
        if (module == null ||
            !module.ShouldPreventAttackInterruption(__instance?.owner) ||
            !CharacterProtectionModule.IsAbilityOrSpellAction(__instance))
            return true;

        __result = false;
        return false;
    }
}
