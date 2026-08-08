using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

public sealed partial class ElinModifierPlugin
{
    [ThreadStatic]
    private static AI_Steal? _activeStealHandRun;

    private static bool IsPlayerStealHandAction(AI_Steal? action)
    {
        try
        {
            if (action?.owner != null)
                return action.owner.IsPC;
            return Act.CC?.IsPC == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldIgnoreStealHandTargetLimits(AI_Steal? action)
    {
        var instance = Instance;
        return instance?._stealHandNoTargetLimit == true && IsPlayerStealHandAction(action);
    }

    private static bool ShouldPreventStealHandDiscovery(AI_Steal? action)
    {
        var instance = Instance;
        return instance?._stealHandUndetectable == true && IsPlayerStealHandAction(action);
    }

    [HarmonyPatch(typeof(AI_Steal), "IsValidTC", new[] { typeof(Card) })]
    private static class StealHandTargetValidationPatch
    {
        private static bool Prefix(AI_Steal __instance, Card __0, ref bool __result)
        {
            if (!ShouldIgnoreStealHandTargetLimits(__instance))
                return true;

            __result = __0 != null;
            return false;
        }
    }

    [HarmonyPatch(typeof(AI_Steal), "CanPerform")]
    private static class StealHandCanPerformPatch
    {
        private static bool Prefix(AI_Steal __instance, ref bool __result)
        {
            if (!ShouldIgnoreStealHandTargetLimits(__instance))
                return true;

            __result = Act.TC != null;
            return false;
        }
    }

    [HarmonyPatch]
    private static class StealHandRunTargetRestrictionPatch
    {
        private static MethodBase? _targetMethod;
        private static FieldInfo? _actionField;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return ResolveTargetMethod() != null && _actionField != null;
        }

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod()
        {
            return ResolveTargetMethod();
        }

        private static MethodBase? ResolveTargetMethod()
        {
            if (_targetMethod != null)
                return _targetMethod;

            var runMethod = AccessTools.Method(typeof(AI_Steal), "Run", Type.EmptyTypes);
            var moveNext = runMethod == null ? null : AccessTools.EnumeratorMoveNext(runMethod);
            if (moveNext?.DeclaringType == null)
                return null;

            _actionField = moveNext.DeclaringType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(field => field.FieldType == typeof(AI_Steal));
            if (_actionField == null)
                return null;

            _targetMethod = moveNext;
            return _targetMethod;
        }

        private static void Prefix(object __instance, out AI_Steal? __state)
        {
            __state = _activeStealHandRun;
            try
            {
                _activeStealHandRun = _actionField?.GetValue(__instance) as AI_Steal;
            }
            catch
            {
                _activeStealHandRun = null;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(
                typeof(Card),
                "HasElement",
                new[] { typeof(int), typeof(bool) });
            var replacement = AccessTools.Method(
                typeof(ElinModifierPlugin),
                nameof(HasElementForStealHandTarget));

            foreach (var instruction in instructions)
            {
                if (original != null && replacement != null && instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }

        private static Exception? Finalizer(Exception? __exception, AI_Steal? __state)
        {
            _activeStealHandRun = __state;
            return __exception;
        }
    }

    private static bool HasElementForStealHandTarget(Card card, int elementId, bool includeTemp)
    {
        if (ShouldIgnoreStealHandTargetLimits(_activeStealHandRun) &&
            (elementId == 426 || elementId == 1292 || elementId == 1290))
            return false;
        return card != null && card.HasElement(elementId, includeTemp);
    }

    [HarmonyPatch]
    private static class StealHandDiscoveryPatch
    {
        private static MethodBase? _targetMethod;
        private static FieldInfo? _actionField;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return ResolveTargetMethod() != null && _actionField != null;
        }

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod()
        {
            return ResolveTargetMethod();
        }

        private static MethodBase? ResolveTargetMethod()
        {
            if (_targetMethod != null)
                return _targetMethod;

            var nestedTypes = typeof(AI_Steal).GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic);
            for (var i = 0; i < nestedTypes.Length; i++)
            {
                var fields = nestedTypes[i].GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var actionField = fields.FirstOrDefault(field => field.FieldType == typeof(AI_Steal));
                if (actionField == null)
                    continue;

                var methods = nestedTypes[i].GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                for (var j = 0; j < methods.Length; j++)
                {
                    var method = methods[j];
                    var parameters = method.GetParameters();
                    if (method.ReturnType != typeof(bool) || parameters.Length != 1 ||
                        parameters[0].ParameterType != typeof(Chara) ||
                        method.Name.IndexOf("<Run>", StringComparison.Ordinal) < 0)
                        continue;

                    _actionField = actionField;
                    _targetMethod = method;
                    return _targetMethod;
                }
            }
            return null;
        }

        private static void Postfix(object __instance, ref bool __result)
        {
            try
            {
                if (_actionField?.GetValue(__instance) is AI_Steal action &&
                    ShouldPreventStealHandDiscovery(action))
                    __result = false;
            }
            catch
            {
            }
        }
    }
}
