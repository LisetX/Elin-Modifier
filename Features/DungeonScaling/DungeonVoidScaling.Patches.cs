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
    private static long CalculateDungeonVoidBaseLevel(int templateLevel, int dangerLevel)
    {
        if (!ShouldOptimizeDungeonVoidScaling())
            return (50L + templateLevel) * Math.Max(1, (dangerLevel - 1) / 50);

        var dangerScale = dangerLevel / 100.0;
        var level = Math.Floor((templateLevel + (dangerScale - 1.0) * 5.0) * dangerScale);
        return Math.Max(1L, (long)level);
    }
    [HarmonyPatch(typeof(Zone), "SpawnMob", new[] { typeof(Point), typeof(SpawnSetting) })]
    private static class ZoneSpawnMobDebugTracePatch
    {
        private static void Postfix(Zone __instance, Point __0, SpawnSetting __1, Chara __result)
        {
            if (__result == null)
                RecordDebugSubmoduleTraceEvent("Zone.SpawnMob", __instance, __result, null, __0, __1);
        }

        private static Exception Finalizer(Zone __instance, Point __0, SpawnSetting __1, Exception __exception)
        {
            if (__exception != null)
                RecordDebugSubmoduleTraceEvent("Zone.SpawnMob", __instance, null, __exception, __0, __1);
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Zone), "SpawnMob", new[] { typeof(Point), typeof(SpawnSetting) })]
    private static class ZoneSpawnMobVoidScalingPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var lvField = AccessTools.Field(typeof(RenderRow), "LV");
            var maxMethod = AccessTools.Method(typeof(Mathf), "Max", new[] { typeof(int), typeof(int) });
            var replacementMethod = AccessTools.Method(typeof(ElinModifierPlugin), nameof(CalculateDungeonVoidBaseLevel));
            if (lvField == null || maxMethod == null || replacementMethod == null)
                return codes;

            for (var i = 0; i <= codes.Count - 16; i++)
            {
                if (!IsIntConstant(codes[i], 50) ||
                    codes[i + 1].opcode != OpCodes.Conv_I8 ||
                    !IsLoadLocal(codes[i + 2]) ||
                    !codes[i + 3].LoadsField(lvField) ||
                    codes[i + 4].opcode != OpCodes.Conv_I8 ||
                    codes[i + 5].opcode != OpCodes.Add ||
                    !IsIntConstant(codes[i + 6], 1) ||
                    !IsLoadLocal(codes[i + 7]) ||
                    !IsIntConstant(codes[i + 8], 1) ||
                    codes[i + 9].opcode != OpCodes.Sub ||
                    !IsIntConstant(codes[i + 10], 50) ||
                    codes[i + 11].opcode != OpCodes.Div ||
                    !codes[i + 12].Calls(maxMethod) ||
                    codes[i + 13].opcode != OpCodes.Conv_I8 ||
                    codes[i + 14].opcode != OpCodes.Mul ||
                    !IsStoreLocal(codes[i + 15]))
                    continue;

                var replacement = new List<CodeInstruction>
                {
                    CloneWithoutMetadata(codes[i + 2]),
                    new CodeInstruction(OpCodes.Ldfld, lvField),
                    CloneWithoutMetadata(codes[i + 7]),
                    new CodeInstruction(OpCodes.Call, replacementMethod),
                    CloneWithoutMetadata(codes[i + 15])
                };
                codes.RemoveRange(i, 16);
                codes.InsertRange(i, replacement);
                break;
            }

            return codes;
        }

        private static bool IsIntConstant(CodeInstruction code, int value)
        {
            if (value == -1 && code.opcode == OpCodes.Ldc_I4_M1) return true;
            if (value == 0 && code.opcode == OpCodes.Ldc_I4_0) return true;
            if (value == 1 && code.opcode == OpCodes.Ldc_I4_1) return true;
            if (value == 2 && code.opcode == OpCodes.Ldc_I4_2) return true;
            if (value == 3 && code.opcode == OpCodes.Ldc_I4_3) return true;
            if (value == 4 && code.opcode == OpCodes.Ldc_I4_4) return true;
            if (value == 5 && code.opcode == OpCodes.Ldc_I4_5) return true;
            if (value == 6 && code.opcode == OpCodes.Ldc_I4_6) return true;
            if (value == 7 && code.opcode == OpCodes.Ldc_I4_7) return true;
            if (value == 8 && code.opcode == OpCodes.Ldc_I4_8) return true;
            if (code.opcode == OpCodes.Ldc_I4_S)
            {
                if (code.operand is sbyte sb) return sb == value;
                if (code.operand is byte b) return b == value;
                if (code.operand is int si) return si == value;
            }
            return code.opcode == OpCodes.Ldc_I4 && code.operand is int i && i == value;
        }

        private static bool IsLoadLocal(CodeInstruction code)
        {
            return code.opcode == OpCodes.Ldloc ||
                   code.opcode == OpCodes.Ldloc_S ||
                   code.opcode == OpCodes.Ldloc_0 ||
                   code.opcode == OpCodes.Ldloc_1 ||
                   code.opcode == OpCodes.Ldloc_2 ||
                   code.opcode == OpCodes.Ldloc_3;
        }

        private static bool IsStoreLocal(CodeInstruction code)
        {
            return code.opcode == OpCodes.Stloc ||
                   code.opcode == OpCodes.Stloc_S ||
                   code.opcode == OpCodes.Stloc_0 ||
                   code.opcode == OpCodes.Stloc_1 ||
                   code.opcode == OpCodes.Stloc_2 ||
                   code.opcode == OpCodes.Stloc_3;
        }

        private static CodeInstruction CloneWithoutMetadata(CodeInstruction code)
        {
            return new CodeInstruction(code.opcode, code.operand);
        }
    }
}
