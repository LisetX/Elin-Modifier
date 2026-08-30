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
    private static bool ShouldCustomItemEditor()
    {
        return Instance != null && Instance._customItemEditor;
    }
    private static bool ShouldCustomFoodEditor()
    {
        return Instance != null && Instance._customFoodEditor;
    }
    private static bool ShouldCustomWeaponEditor()
    {
        return Instance != null && Instance._customWeaponEditor;
    }
    private static bool ShouldCustomGeneEditor()
    {
        return Instance != null && Instance._customGeneEditor;
    }
}
