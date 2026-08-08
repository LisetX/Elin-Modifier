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
    [HarmonyPatch(typeof(Element), "AddEncNote")]
    private static class ElementAddEncNoteItemPanelEnchantLevelPatch
    {
        private static void Prefix(Card Card, ref Func<Element, string, string> funcText)
        {
            var instance = Instance;
            var thing = Card as Thing;
            if (instance == null || !instance._showItemPanelEnchantLevels || thing == null)
                return;

            var original = funcText;
            funcText = (element, text) =>
            {
                var current = original == null ? text : original(element, text);
                return AppendItemPanelEnchantLevel(thing, element, current);
            };
        }
    }
}
