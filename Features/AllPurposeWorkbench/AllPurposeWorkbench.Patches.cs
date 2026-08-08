using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(LayerCraft), "OnKill")]
internal static class AllPurposeWorkbenchCategoryPagerCleanupPatch
{
    private static void Prefix(LayerCraft __instance)
    {
        try
        {
            AllPurposeWorkbenchTabPager.Cleanup(__instance);
        }
        catch
        {
        }
    }
}

