using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

internal static class MoongateLandholderPrivilegeContext
{
    internal static MoongateModule? Current =>
        ElinModifierPlugin.ActiveModules?.Moongate;

    internal static bool IsActive(Zone? zone)
    {
        return Current?.HasLandholderPrivileges(zone) == true;
    }

    internal static bool TreatAsPlayerLand(Zone? zone)
    {
        if (zone == null)
            return false;
        if (IsActive(zone))
            return true;

        return zone.IsPCFaction;
    }

    internal static bool TreatAsPlayerLandOrTent(Zone? zone)
    {
        if (zone == null)
            return false;
        if (IsActive(zone))
            return true;

        return zone.IsPCFactionOrTent;
    }

    internal static FactionBranch? PermissionBranch()
    {
        try
        {
            var zone = GameAccess.Runtime.Core?.game?.activeZone;
            if (IsActive(zone))
                return GameAccess.Characters.PlayerCharacter?.homeBranch;
            return zone?.branch;
        }
        catch
        {
            return null;
        }
    }

    internal static bool TreatAsNpcProperty(Card? card)
    {
        if (card == null)
            return false;

        if (card is Thing thing && thing.ExistsOnMap && IsActive(GameAccess.World.CurrentZone))
            return false;

        return card.isNPCProperty;
    }

    internal static bool TreatAsUserMapBenefitsDisabled(GamePrincipal principal)
    {
        if (IsActive(GameAccess.World.CurrentZone))
            return false;

        return principal.disableUsermapBenefit;
    }

    internal static IEnumerable<CodeInstruction> ReplaceZoneOwnershipChecks(
        IEnumerable<CodeInstruction> instructions)
    {
        var isPlayerLandGetter = AccessTools.PropertyGetter(typeof(Zone), "IsPCFaction");
        var isPlayerLandOrTentGetter = AccessTools.PropertyGetter(typeof(Zone), "IsPCFactionOrTent");
        var playerLandHelper = AccessTools.Method(
            typeof(MoongateLandholderPrivilegeContext),
            nameof(TreatAsPlayerLand));
        var playerLandOrTentHelper = AccessTools.Method(
            typeof(MoongateLandholderPrivilegeContext),
            nameof(TreatAsPlayerLandOrTent));

        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                Equals(instruction.operand, isPlayerLandGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = playerLandHelper;
            }
            else if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                     Equals(instruction.operand, isPlayerLandOrTentGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = playerLandOrTentHelper;
            }

            yield return instruction;
        }
    }

    internal static IEnumerable<CodeInstruction> ReplaceBranchChecks(
        IEnumerable<CodeInstruction> instructions)
    {
        var branchGetter = AccessTools.PropertyGetter(typeof(EClass), "Branch");
        var branchHelper = AccessTools.Method(
            typeof(MoongateLandholderPrivilegeContext),
            nameof(PermissionBranch));

        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                Equals(instruction.operand, branchGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = branchHelper;
            }

            yield return instruction;
        }
    }

    internal static IEnumerable<CodeInstruction> ReplaceNpcPropertyChecks(
        IEnumerable<CodeInstruction> instructions)
    {
        var propertyGetter = AccessTools.PropertyGetter(typeof(Card), "isNPCProperty");
        var propertyHelper = AccessTools.Method(
            typeof(MoongateLandholderPrivilegeContext),
            nameof(TreatAsNpcProperty));

        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                Equals(instruction.operand, propertyGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = propertyHelper;
            }

            yield return instruction;
        }
    }

    internal static IEnumerable<CodeInstruction> ReplaceUserMapBenefitChecks(
        IEnumerable<CodeInstruction> instructions)
    {
        var disabledField = AccessTools.Field(
            typeof(GamePrincipal),
            "disableUsermapBenefit");
        var disabledHelper = AccessTools.Method(
            typeof(MoongateLandholderPrivilegeContext),
            nameof(TreatAsUserMapBenefitsDisabled));

        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, disabledField))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = disabledHelper;
            }

            yield return instruction;
        }
    }

    internal static IEnumerable<MethodBase> EnumerateMethodAndGeneratedMethods(
        Type ownerType,
        string methodName)
    {
        var token = "<" + methodName + ">";
        var pending = new Stack<Type>();
        var seenTypes = new HashSet<Type>();
        var seenMethods = new HashSet<MethodBase>();
        pending.Push(ownerType);

        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seenTypes.Add(type))
                continue;

            foreach (var method in type.GetMethods(
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                if ((method.Name == methodName ||
                     method.Name.IndexOf(token, StringComparison.Ordinal) >= 0) &&
                    seenMethods.Add(method))
                    yield return method;
            }

            foreach (var nestedType in type.GetNestedTypes(
                         BindingFlags.Public | BindingFlags.NonPublic))
                pending.Push(nestedType);
        }
    }
}

[HarmonyPatch(typeof(Zone), "CanEnterBuildMode", MethodType.Getter)]
internal static class MoongateCanEnterBuildModePatch
{
    private static void Postfix(Zone __instance, ref bool __result)
    {
        if (!__result && MoongateLandholderPrivilegeContext.IsActive(__instance))
            __result = true;
    }
}

