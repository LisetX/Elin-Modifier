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
    private string GetBlessedStateLabel(int value)
    {
        switch (NormalizeBlessedState(value))
        {
            case BlessedState.Blessed:
                return T("被祝福的", "Blessed");
            case BlessedState.Cursed:
                return T("被诅咒的", "Cursed");
            case BlessedState.Doomed:
                return T("堕落的", "Doomed");
            default:
                return T("普通", "Normal");
        }
    }
    private static BlessedState NormalizeBlessedState(int value)
    {
        switch (value)
        {
            case (int)BlessedState.Blessed:
                return BlessedState.Blessed;
            case (int)BlessedState.Cursed:
                return BlessedState.Cursed;
            case (int)BlessedState.Doomed:
                return BlessedState.Doomed;
            default:
                return BlessedState.Normal;
        }
    }
    private static BlessedState ParseBlessedStateValue(string text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return NormalizeBlessedState(fallback);

        int value;
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return NormalizeBlessedState(value);

        switch (NormalizeAiKey(text))
        {
            case "blessed":
            case "bless":
            case "被祝福的":
            case "祝福":
                return BlessedState.Blessed;
            case "cursed":
            case "curse":
            case "被诅咒的":
            case "诅咒":
                return BlessedState.Cursed;
            case "doomed":
            case "doom":
            case "corrupt":
            case "corrupted":
            case "堕落的":
            case "堕落":
                return BlessedState.Doomed;
            case "normal":
            case "standard":
            case "none":
            case "普通":
            case "无":
                return BlessedState.Normal;
            default:
                return NormalizeBlessedState(fallback);
        }
    }
}
