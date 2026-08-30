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
    private static bool AddRodStackingInteraction(object? interactionList, ButtonGrid? button)
    {
        if (!ShouldRodStacking() || Instance == null || interactionList == null)
            return false;

        try
        {
            var thing = GetInteractionThing(interactionList) ?? button?.Card as Thing;
            if (!CanUseRodStackingTarget(thing))
                return false;

            return TryAddCachedInteraction(
                interactionList,
                Instance.T("充能堆叠", "Stack charges"),
                9006,
                new Action(() => Instance?.OpenRodStackingWindow(thing!)),
                "ElinModifierRodStacking"
            );
        }
        catch
        {
            return false;
        }
    }
}
