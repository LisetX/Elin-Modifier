using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class AutomationModule
{
    [HarmonyPatch(typeof(Card), "DamageHP", new[]
    {
        typeof(long), typeof(int), typeof(int), typeof(AttackSource), typeof(Card),
        typeof(bool), typeof(Thing), typeof(Chara), typeof(int)
    })]
    private static class CardDamageHpAutomationRetaliationPatch
    {
        private static void Prefix(Card __instance, out AutomationDamageState __state)
        {
            __state = new AutomationDamageState(__instance as Chara);
        }

        private static void Postfix(Card __instance, long __0, Card __4, AutomationDamageState __state)
        {
            try
            {
                if (__0 <= 0 || __instance is not Chara victim || __4 == null || !__4.isChara ||
                    !__state.WasDamaged(victim))
                    return;

                var module = ElinModifierPlugin.ActiveModules?.Automation;
                if (module == null)
                    return;
                module.QueueAutomationRetaliation(victim, __4.Chara);
            }
            catch { }
        }
    }
}
