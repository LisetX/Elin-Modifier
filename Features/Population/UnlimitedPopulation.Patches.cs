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
    [HarmonyPatch(typeof(FactionBranch), "get_MaxPopulation")]
    private static class FactionBranchMaxPopulationUnlimitedPatch
    {
        private static void Postfix(ref int __result)
        {
            var instance = Instance;
            if (instance != null && instance._unlimitedHomeResidentCap)
                __result = 9999;
        }
    }
    [HarmonyPatch(typeof(Player), "get_MaxAlly")]
    private static class PlayerMaxAllyUnlimitedPatch
    {
        private static void Postfix(ref int __result)
        {
            var instance = Instance;
            if (instance != null && instance._unlimitedPartyMemberCap)
                __result = 9999;
        }
    }
}
