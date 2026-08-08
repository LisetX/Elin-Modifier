using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(LayerCraft), "RefreshCategory")]
internal static class AllPurposeWorkbenchCategoryPagerPatch
{
    private static void Postfix(LayerCraft __instance)
    {
        try
        {
            AllPurposeWorkbenchTabPager.Refresh(__instance);
        }
        catch
        {
        }
    }
}

