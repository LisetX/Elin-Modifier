using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(Trait), "Contains", typeof(RecipeSource))]
internal static class AllPurposeWorkbenchRecipePatch
{
    private static void Postfix(Trait __instance, RecipeSource r, ref bool __result)
    {
        if (__result || AllPurposeWorkbenchPatchContext.Current?.Enabled != true ||
            __instance?.owner?.id != "workbench" || r == null)
            return;

        try
        {
            if (AllPurposeWorkbenchTabPager.HasWorkbenchFactory(r))
                __result = true;
        }
        catch
        {
        }
    }
}

