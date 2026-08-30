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
    private static bool ShouldForcePcFactionTrainerChoice(Chara trainer) =>
        ActiveModules?.Progression.ShouldForcePcFactionTrainerChoice(trainer) == true;
    private static bool IsTrainerIdEmptyForBuild(string trainerId, Chara trainer)
    {
        return !ShouldForcePcFactionTrainerChoice(trainer) && trainerId.IsEmpty();
    }
    private static bool IsUserZoneForTrainerBuild(Zone zone, Chara trainer)
    {
        return !ShouldForcePcFactionTrainerChoice(trainer) && zone.IsUserZone;
    }
    private static Guild? GetCurrentGuildForTrainerBuild(Chara trainer)
    {
        return ShouldForcePcFactionTrainerChoice(trainer) ? null : Guild.GetCurrentGuild();
    }
    [HarmonyPatch(typeof(DramaCustomSequence), "Build")]
    private static class DramaCustomSequencePcFactionTrainerChoicePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var trainerGetter = AccessTools.PropertyGetter(typeof(TraitChara), "IDTrainer");
            var trainerIdEmptyMethod = AccessTools.Method(
                typeof(ElinModifierPlugin),
                nameof(IsTrainerIdEmptyForBuild));
            var userZoneGetter = AccessTools.PropertyGetter(typeof(Zone), "IsUserZone");
            var userZoneMethod = AccessTools.Method(
                typeof(ElinModifierPlugin),
                nameof(IsUserZoneForTrainerBuild));
            var currentGuildGetter = AccessTools.Method(typeof(Guild), "GetCurrentGuild");
            var currentGuildMethod = AccessTools.Method(
                typeof(ElinModifierPlugin),
                nameof(GetCurrentGuildForTrainerBuild));
            if (trainerGetter == null || trainerIdEmptyMethod == null ||
                userZoneGetter == null || userZoneMethod == null ||
                currentGuildGetter == null || currentGuildMethod == null)
                return codes;

            var trainerGetterIndex = -1;
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(trainerGetter))
                {
                    trainerGetterIndex = i;
                    break;
                }
            }

            var choiceIndex = -1;
            for (var i = Math.Max(0, trainerGetterIndex + 1); i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldstr &&
                    string.Equals(codes[i].operand as string, "daTrain", StringComparison.Ordinal))
                {
                    choiceIndex = i;
                    break;
                }
            }

            if (trainerGetterIndex < 0 || choiceIndex < 0 || choiceIndex <= trainerGetterIndex)
                return codes;

            var trainerIdEmptyIndex = -1;
            var userZoneIndex = -1;
            var currentGuildIndices = new List<int>();
            for (var i = trainerGetterIndex + 1; i < choiceIndex; i++)
            {
                if (trainerIdEmptyIndex < 0 &&
                    codes[i].operand is MethodInfo method &&
                    method.Name == "IsEmpty" &&
                    method.ReturnType == typeof(bool))
                {
                    trainerIdEmptyIndex = i;
                    continue;
                }

                if (userZoneIndex < 0 && codes[i].Calls(userZoneGetter))
                {
                    userZoneIndex = i;
                    continue;
                }

                if (codes[i].Calls(currentGuildGetter))
                    currentGuildIndices.Add(i);
            }

            if (trainerIdEmptyIndex < 0 || userZoneIndex < 0 || currentGuildIndices.Count == 0)
                return codes;

            var replacements = new List<(int Index, MethodInfo Method)>
            {
                (trainerIdEmptyIndex, trainerIdEmptyMethod),
                (userZoneIndex, userZoneMethod)
            };
            replacements.AddRange(currentGuildIndices.Select(index => (index, currentGuildMethod)));
            foreach (var replacement in replacements.OrderByDescending(entry => entry.Index))
            {
                codes.Insert(replacement.Index, new CodeInstruction(OpCodes.Ldarg_1));
                codes[replacement.Index + 1].opcode = OpCodes.Call;
                codes[replacement.Index + 1].operand = replacement.Method;
            }
            return codes;
        }
    }
    [HarmonyPatch]
    private static class DramaCustomSequencePcFactionTrainerSkillFilterPatch
    {
        private static MethodBase? _targetMethod;
        private static FieldInfo? _trainerField;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return ResolveTargetMethod() != null;
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

            var nestedTypes = typeof(DramaCustomSequence).GetNestedTypes(BindingFlags.Instance |
                                                                         BindingFlags.Static |
                                                                         BindingFlags.Public |
                                                                         BindingFlags.NonPublic);
            for (var i = 0; i < nestedTypes.Length; i++)
            {
                var nestedType = nestedTypes[i];
                var trainerField = nestedType.GetFields(BindingFlags.Instance |
                                                        BindingFlags.Public |
                                                        BindingFlags.NonPublic)
                    .FirstOrDefault(field => field.FieldType == typeof(Chara));
                if (trainerField == null)
                    continue;
                if (nestedType.GetFields(BindingFlags.Instance |
                                         BindingFlags.Public |
                                         BindingFlags.NonPublic)
                    .All(field => field.FieldType != typeof(bool) ||
                                  !string.Equals(field.Name, "isInGuild", StringComparison.Ordinal)))
                    continue;

                var methods = nestedType.GetMethods(BindingFlags.Instance |
                                                    BindingFlags.Public |
                                                    BindingFlags.NonPublic);
                for (var j = 0; j < methods.Length; j++)
                {
                    var method = methods[j];
                    var parameters = method.GetParameters();
                    if (method.ReturnType != typeof(bool) || parameters.Length != 1 ||
                        parameters[0].ParameterType != typeof(SourceElement.Row) ||
                        method.Name.IndexOf("Build", StringComparison.Ordinal) < 0)
                        continue;

                    _trainerField = trainerField;
                    _targetMethod = method;
                    return _targetMethod;
                }
            }
            return null;
        }

        private static bool Prefix(object __instance, SourceElement.Row __0, ref bool __result)
        {
            try
            {
                var instance = Instance;
                if (instance == null || !instance._modules.Progression.PcFactionTrainerAllSkills ||
                    _trainerField?.GetValue(__instance) is not Chara trainer ||
                    !trainer.IsPCFaction || trainer.trait is not TraitTrainer)
                    return true;

                __result = __0 != null &&
                           (__0.tag == null || !__0.tag.Contains("unused")) &&
                           string.Equals(__0.category, "skill", StringComparison.Ordinal);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
