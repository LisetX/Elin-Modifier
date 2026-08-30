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
    [HarmonyPatch(typeof(Card), "c_charges", MethodType.Setter)]
    private static class CardInfiniteChargePatch
    {
        private static bool Prefix(Card __instance, int __0)
        {
            try
            {
                var instance = Instance;
                if (instance == null || !instance._infiniteChargeAndAmmo)
                    return true;
                var thing = __instance as Thing;
                if (thing == null || thing.trait == null || !thing.trait.HasCharges)
                    return true;
                return __0 >= thing.c_charges;
            }
            catch
            {
                return true;
            }
        }
    }
    [HarmonyPatch(typeof(Card), "c_ammo", MethodType.Setter)]
    private static class CardInfiniteAmmoPatch
    {
        private static bool Prefix(Card __instance, int __0)
        {
            try
            {
                var instance = Instance;
                if (instance == null || !instance._infiniteChargeAndAmmo)
                    return true;
                var thing = __instance as Thing;
                var ranged = thing?.trait as TraitToolRange;
                if (thing == null || ranged == null || !ranged.NeedAmmo)
                    return true;
                return __0 >= thing.c_ammo;
            }
            catch
            {
                return true;
            }
        }
    }
}
