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
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    [HarmonyPatch(typeof(BaseTileMap), "DrawTile")]
    private static class BaseTileMapDrawTileInfinitePlayerSightPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var pcSyncField = AccessTools.Field(typeof(Cell), "pcSync");
            var visualSyncMethod = AccessTools.Method(
                typeof(BaseTileMapDrawTileInfinitePlayerSightPatch),
                nameof(IsVisuallySynced));
            if (pcSyncField == null || visualSyncMethod == null)
                return codes;

            var replaced = 0;
            for (var i = 0; i < codes.Count; i++)
            {
                var code = codes[i];
                if (code.opcode != OpCodes.Ldfld || !Equals(code.operand, pcSyncField))
                    continue;

                var replacement = new CodeInstruction(code)
                {
                    opcode = OpCodes.Call,
                    operand = visualSyncMethod
                };
                codes[i] = replacement;
                replaced++;
            }

            if (replaced == 0)
                throw new InvalidOperationException("BaseTileMap.DrawTile no longer reads Cell.pcSync.");
            return codes;
        }

        private static bool IsVisuallySynced(Cell cell)
        {
            if (cell == null)
                return false;
            var instance = Instance;
            return cell.pcSync ||
                   (instance != null && instance._infinitePlayerSight && !cell.outOfBounds);
        }
    }
}
