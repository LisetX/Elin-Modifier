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
    [HarmonyPatch(typeof(Affinity), "OnTalkRumor")]
    private static class AffinityOnTalkRumorNoInterestLossPatch
    {
        private static void Prefix(out int __state)
        {
            __state = int.MinValue;
            if (!ShouldPreventTalkInterestLoss())
                return;

            try
            {
                if (Affinity.CC != null)
                    __state = Affinity.CC.interest;
            }
            catch
            {
                __state = int.MinValue;
            }
        }

        private static void Postfix(int __state)
        {
            if (__state == int.MinValue || !ShouldPreventTalkInterestLoss())
                return;

            try
            {
                if (Affinity.CC != null)
                    Affinity.CC.interest = __state;
            }
            catch { }
        }
    }
}
