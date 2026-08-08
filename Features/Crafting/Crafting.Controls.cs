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
    private void SetNoCraftMaterials(bool enabled)
    {
        _noCraftMaterials = enabled;
        if (!enabled)
            ClearCraftVirtualIngredients();
        RefreshCraftingUi();
        _log = enabled
            ? T("无需材料制作已开启", "No-material crafting enabled")
            : T("无需材料制作已关闭", "No-material crafting disabled");
    }
    private void SetUnlockAllCraftMaterials(bool enabled)
    {
        _unlockAllCraftMaterials = enabled;
        if (!enabled)
            ClearCraftVirtualIngredients();
        RefreshCraftingUi();
        _log = enabled
            ? T("解锁全部制作材料已开启", "Unlock all crafting materials enabled")
            : T("解锁全部制作材料已关闭", "Unlock all crafting materials disabled");
    }
    private void SetUnlockAllCraftRecipes(bool enabled)
    {
        _unlockAllCraftRecipes = enabled;
        RefreshCraftingUi();
        _log = enabled
            ? T("解锁全部制作配方已开启", "Unlock all crafting recipes enabled")
            : T("解锁全部制作配方已关闭", "Unlock all crafting recipes disabled");
    }
}
