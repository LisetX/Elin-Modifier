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
    private static bool ShouldNoCraftMaterials()
    {
        return Instance != null && Instance._noCraftMaterials;
    }
    private static bool ShouldUnlockAllCraftMaterials()
    {
        return Instance != null && Instance._unlockAllCraftMaterials;
    }
    private static bool ShouldUseUnlockedCraftMaterials()
    {
        return ShouldNoCraftMaterials() && ShouldUnlockAllCraftMaterials();
    }
    private static bool ShouldUnlockAllCraftRecipes()
    {
        return Instance != null && Instance._unlockAllCraftRecipes;
    }
}
