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
    private static string GetConfigPath()
    {
        return Path.Combine(GetPluginDirectory(), ConfigFileName);
    }
    private static string GetPluginDirectory()
    {
        var location = typeof(ElinModifierPlugin).Assembly.Location;
        var dir = Path.GetDirectoryName(location);
        if (string.IsNullOrEmpty(dir))
            dir = ".";
        return dir;
    }
    private static bool TryParseKeyCode(string text, out KeyCode key)
    {
        key = DefaultOpenKey;
        if (string.IsNullOrEmpty(text))
            return false;
        text = text.Trim();
        foreach (var option in KeyOptions)
        {
            if (string.Equals(option.Label, text, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option.Key.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                key = option.Key;
                return true;
            }
        }
        return false;
    }
    private static string GetKeyLabel(KeyCode key)
    {
        foreach (var option in KeyOptions)
            if (option.Key == key)
                return option.Label;
        return key.ToString();
    }
}
