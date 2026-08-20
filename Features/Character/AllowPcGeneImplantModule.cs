using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

internal sealed partial class AllowPcGeneImplantModule
{
    private sealed class PendingImmediateImplant
    {
        internal PendingImmediateImplant(Chara target, Thing gene)
        {
            Target = target;
            Gene = gene;
        }

        internal Chara Target { get; }
        internal Thing Gene { get; }
    }

    private readonly ICharacterGameAccess _characters;
    private readonly IBoundGameMethod _getSuspendCondition;
    private readonly IBoundGameMethod _addToList;
    private readonly IBoundGameValue<Card> _traitOwner;
    private readonly IBoundGameValue<Chara> _geneInventoryTarget;
    private readonly IBoundGameValue<DNA> _geneDna;
    private readonly IBoundGameMethod _createDragGrid;
    private readonly IBoundGameMethod _applyDna;
    private readonly IBoundGameMethod _destroyCard;
    private readonly ConstructorInfo? _geneInventoryConstructor;
    private readonly PcGeneAbilityProjection _abilityProjection;
    private readonly PcGeneAbilityMutationIsolation _abilityMutationIsolation;
    private readonly ConditionalWeakTable<InvOwnerGene, PendingImmediateImplant>
        _pendingImmediateImplants =
            new ConditionalWeakTable<InvOwnerGene, PendingImmediateImplant>();

    internal AllowPcGeneImplantModule(
        IGameRuntimeContext runtime,
        IGameSourceRepository sources,
        ICharacterGameAccess characters,
        IGameMemberBinder binder)
    {
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _getSuspendCondition = binder.BindInstanceGenericMethod(
            typeof(Chara),
            typeof(ConSuspend),
            new[] { typeof(ConSuspend) },
            Type.EmptyTypes,
            "GetCondition");
        _addToList = binder.BindInstanceMethod(
            typeof(BaseList),
            typeof(void),
            new[] { typeof(object) },
            "Add");
        _traitOwner = binder.BindInstanceValue<Card>(
            typeof(Trait),
            GameValueAccess.Read,
            "owner");
        _geneInventoryTarget = binder.BindInstanceValue<Chara>(
            typeof(InvOwnerGene),
            GameValueAccess.Read,
            "tg");
        _geneDna = binder.BindInstanceValue<DNA>(
            typeof(Card),
            GameValueAccess.Read,
            "c_DNA");
        _createDragGrid = binder.BindStaticMethod(
            typeof(LayerDragGrid),
            typeof(LayerDragGrid),
            new[] { typeof(InvOwnerDraglet), typeof(bool) },
            "Create");
        _applyDna = binder.BindInstanceMethod(
            typeof(DNA),
            typeof(void),
            new[] { typeof(Chara) },
            "Apply");
        _destroyCard = binder.BindInstanceMethod(
            typeof(Card),
            typeof(void),
            Type.EmptyTypes,
            "Destroy");
        _geneInventoryConstructor = AccessTools.Constructor(
            typeof(InvOwnerGene),
            new[] { typeof(Card), typeof(Chara) });
        _abilityProjection = new PcGeneAbilityProjection(
            runtime,
            sources,
            characters,
            binder);
        _abilityMutationIsolation = new PcGeneAbilityMutationIsolation(
            characters,
            binder);
    }

    internal bool Enabled { get; private set; }

    internal void Load(bool enabled)
    {
        Enabled = enabled;
        _abilityProjection.Synchronize(Enabled, true);
    }

    internal void Reset()
    {
        Enabled = false;
        _abilityProjection.Reset(true);
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        _abilityProjection.Synchronize(Enabled, true);
        return true;
    }

    internal void Tick()
    {
        _abilityProjection.Synchronize(Enabled, true);
    }

    internal void AppendPlayerCharacter(BaseList list)
    {
        if (!Enabled || list == null || !IsReady)
            return;
        var player = _characters.PlayerCharacter;
        if (player == null)
            return;
        if (_getSuspendCondition.TryInvoke(player, Array.Empty<object?>(), out var condition) &&
            condition is ConSuspend)
            return;
        _addToList.TryInvoke(list, new object?[] { player }, out _);
    }

    internal bool HandlePlayerSelection(TraitGeneMachine machine, Chara target)
    {
        if (!Enabled || machine == null || target == null ||
            !ReferenceEquals(target, _characters.PlayerCharacter))
            return false;
        if (!IsReady ||
            !_traitOwner.TryGet(machine, out var owner) ||
            owner == null ||
            _geneInventoryConstructor == null)
            return true;
        try
        {
            var inventory = _geneInventoryConstructor.Invoke(
                new object?[] { owner, target }) as InvOwnerDraglet;
            if (inventory != null)
                _createDragGrid.TryInvoke(
                    null,
                    new object?[] { inventory, false },
                    out _);
        }
        catch
        {
        }
        return true;
    }

    internal void BeginGeneProcessing(InvOwnerGene inventory)
    {
        if (inventory != null)
            _pendingImmediateImplants.Remove(inventory);
    }

    internal ConSuspend PrepareImmediateImplant(
        ConSuspend original,
        InvOwnerGene inventory,
        Thing gene)
    {
        if (!Enabled || inventory == null || gene == null ||
            !_geneInventoryTarget.TryGet(inventory, out var target) ||
            target == null ||
            !ReferenceEquals(target, _characters.PlayerCharacter))
            return original;
        _pendingImmediateImplants.Remove(inventory);
        _pendingImmediateImplants.Add(
            inventory,
            new PendingImmediateImplant(target, gene));
        return new ConSuspend();
    }

    internal void CompletePlayerImplantImmediately(InvOwnerGene inventory)
    {
        if (inventory == null ||
            !_pendingImmediateImplants.TryGetValue(inventory, out var pending))
            return;
        _pendingImmediateImplants.Remove(inventory);
        if (!Enabled ||
            !ReferenceEquals(pending.Target, _characters.PlayerCharacter))
            return;
        if (_geneDna.TryGet(pending.Gene, out var dna) && dna != null &&
            _applyDna.TryInvoke(
                dna,
                new object?[] { pending.Target },
                out _))
        {
            _destroyCard.TryInvoke(
                pending.Gene,
                Array.Empty<object?>(),
                out _);
            _abilityProjection.Synchronize(Enabled, true);
        }
    }

    private bool IsReady =>
        _getSuspendCondition.IsBound &&
        _addToList.IsBound &&
        _traitOwner.IsBound &&
        _geneInventoryTarget.IsBound &&
        _geneDna.IsBound &&
        _createDragGrid.IsBound &&
        _applyDna.IsBound &&
        _destroyCard.IsBound &&
        _geneInventoryConstructor != null;
}

internal static partial class AllowPcGeneImplantPatchContext
{
    internal static AllowPcGeneImplantModule? Current =>
        ElinModifierPlugin.ActiveModules?.AllowPcGeneImplant;

    internal static void AppendPlayerCharacter(BaseList list)
    {
        Current?.AppendPlayerCharacter(list);
    }

    internal static bool HandlePlayerSelection(
        TraitGeneMachine machine,
        Chara target)
    {
        return Current?.HandlePlayerSelection(machine, target) == true;
    }

    internal static void BeginGeneProcessing(InvOwnerGene inventory)
    {
        Current?.BeginGeneProcessing(inventory);
    }

    internal static ConSuspend PrepareImmediateImplant(
        ConSuspend original,
        InvOwnerGene inventory,
        Thing gene)
    {
        return Current?.PrepareImmediateImplant(original, inventory, gene) ?? original;
    }

    internal static void CompletePlayerImplantImmediately(
        InvOwnerGene inventory)
    {
        Current?.CompletePlayerImplantImmediately(inventory);
    }

}

internal static partial class AllowPcGeneImplantReflection
{
    internal static readonly Lazy<MethodInfo?> TargetListCallback =
        new Lazy<MethodInfo?>(ResolveTargetListCallback);

    internal static readonly Lazy<MethodInfo?> TargetSelectedCallback =
        new Lazy<MethodInfo?>(ResolveTargetSelectedCallback);

    private static MethodInfo? ResolveTargetListCallback()
    {
        var methods = EnumerateMethods().Where(
            method =>
            {
                var parameters = method.GetParameters();
                return method.ReturnType == typeof(void) &&
                       parameters.Length == 1 &&
                       parameters[0].ParameterType == typeof(BaseList);
            }).ToList();
        var exact = methods.FirstOrDefault(
            method => string.Equals(
                method.Name,
                "<OnUse>b__11_0",
                StringComparison.Ordinal));
        if (exact != null)
            return exact;

        var isPcGetter = AccessTools.PropertyGetter(typeof(Card), "IsPC");
        var addMethod = AccessTools.Method(
            typeof(BaseList),
            "Add",
            new[] { typeof(object) });
        return isPcGetter == null || addMethod == null
            ? null
            : methods.FirstOrDefault(
                method => ReadsOperand(method, isPcGetter) &&
                          ReadsOperand(method, addMethod));
    }

    private static MethodInfo? ResolveTargetSelectedCallback()
    {
        var methods = EnumerateMethods().Where(
            method =>
            {
                var parameters = method.GetParameters();
                return !method.IsStatic &&
                       method.ReturnType == typeof(void) &&
                       parameters.Length == 1 &&
                       parameters[0].ParameterType == typeof(Chara);
            }).ToList();
        var exact = methods.FirstOrDefault(
            method => string.Equals(
                method.Name,
                "<OnUse>b__11_1",
                StringComparison.Ordinal));
        if (exact != null)
            return exact;

        var isPcPartyGetter = AccessTools.PropertyGetter(typeof(Card), "IsPCParty");
        return isPcPartyGetter == null
            ? null
            : methods.FirstOrDefault(
                method => ReadsOperand(method, isPcPartyGetter) &&
                          ReadsMethod(method, "RemoveMember", typeof(Chara)));
    }

    private static IEnumerable<MethodInfo> EnumerateMethods()
    {
        var flags = BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic;
        var types = new List<Type> { typeof(TraitGeneMachine) };
        try
        {
            types.AddRange(typeof(TraitGeneMachine).GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic));
        }
        catch
        {
        }
        foreach (var type in types)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(flags);
            }
            catch
            {
                continue;
            }
            foreach (var method in methods)
                yield return method;
        }
    }

    private static bool ReadsMethod(
        MethodInfo method,
        string name,
        Type parameterType)
    {
        try
        {
            foreach (var entry in PatchProcessor.ReadMethodBody(method))
            {
                if (entry.Value is not MethodInfo candidate ||
                    !string.Equals(candidate.Name, name, StringComparison.Ordinal))
                    continue;
                var parameters = candidate.GetParameters();
                if (parameters.Length == 1 &&
                    parameters[0].ParameterType == parameterType)
                    return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool ReadsOperand(MethodInfo method, MemberInfo target)
    {
        try
        {
            foreach (var entry in PatchProcessor.ReadMethodBody(method))
                if (entry.Value is MemberInfo member && IsSameMember(member, target))
                    return true;
        }
        catch
        {
        }
        return false;
    }

    private static bool IsSameMember(MemberInfo left, MemberInfo right)
    {
        if (ReferenceEquals(left, right) || Equals(left, right))
            return true;
        try
        {
            if (left.Module == right.Module && left.MetadataToken == right.MetadataToken)
                return true;
        }
        catch
        {
        }
        return left.MemberType == right.MemberType &&
               string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(
                   left.DeclaringType?.FullName,
                   right.DeclaringType?.FullName,
                   StringComparison.Ordinal);
    }
}

[HarmonyPatch]
internal static class TraitGeneMachineTargetListAllowPcGeneImplantPatch
{
    private static MethodBase? TargetMethod()
    {
        return AllowPcGeneImplantReflection.TargetListCallback.Value;
    }

    private static void Postfix(BaseList __0)
    {
        AllowPcGeneImplantPatchContext.AppendPlayerCharacter(__0);
    }
}

[HarmonyPatch]
internal static class TraitGeneMachineTargetSelectedAllowPcGeneImplantPatch
{
    private static MethodBase? TargetMethod()
    {
        return AllowPcGeneImplantReflection.TargetSelectedCallback.Value;
    }

    private static bool Prefix(TraitGeneMachine __instance, Chara __0)
    {
        return !AllowPcGeneImplantPatchContext.HandlePlayerSelection(
            __instance,
            __0);
    }
}

[HarmonyPatch(typeof(InvOwnerGene), "_OnProcess", new[] { typeof(Thing) })]
internal static class InvOwnerGeneProcessAllowPcGeneImplantPatch
{
    private static void Prefix(InvOwnerGene __instance)
    {
        AllowPcGeneImplantPatchContext.BeginGeneProcessing(__instance);
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var result = new List<CodeInstruction>(codes.Count + 3);
        var helper = AccessTools.Method(
            typeof(AllowPcGeneImplantPatchContext),
            "PrepareImmediateImplant");
        if (helper == null)
            return codes;

        var patched = false;
        foreach (var code in codes)
        {
            result.Add(code);
            if (patched || !IsSuspendConditionGetter(code))
                continue;
            result.Add(new CodeInstruction(OpCodes.Ldarg_0));
            result.Add(new CodeInstruction(OpCodes.Ldarg_1));
            result.Add(new CodeInstruction(OpCodes.Call, helper));
            patched = true;
        }
        return result;
    }

    private static bool IsSuspendConditionGetter(CodeInstruction code)
    {
        if (code.operand is not MethodInfo method ||
            !method.IsGenericMethod ||
            !string.Equals(method.Name, "GetCondition", StringComparison.Ordinal))
            return false;
        var arguments = method.GetGenericArguments();
        return arguments.Length == 1 && arguments[0] == typeof(ConSuspend);
    }

    private static void Postfix(InvOwnerGene __instance)
    {
        AllowPcGeneImplantPatchContext.CompletePlayerImplantImmediately(
            __instance);
    }
}


