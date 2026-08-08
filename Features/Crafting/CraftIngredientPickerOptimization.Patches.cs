using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

[HarmonyPatch(typeof(DropdownGrid), "Deactivate")]
internal static class CraftIngredientPickerDeactivatePatch
{
    private static void Postfix(DropdownGrid __instance)
    {
        CraftIngredientPickerPager.Cleanup(__instance);
    }
}

[HarmonyPatch(typeof(DropdownGrid), "OnDestroy")]
internal static class CraftIngredientPickerDestroyPatch
{
    private static void Prefix(DropdownGrid __instance)
    {
        CraftIngredientPickerPager.Cleanup(__instance);
    }
}

[HarmonyPatch(typeof(TraitCrafter), "IsCraftIngredient")]
internal static class NonStandardCrafterIngredientPatch
{
    private static bool Prefix(
        TraitCrafter __instance,
        Card __0,
        int __1,
        ref bool __result)
    {
        if (!NonStandardCrafterIngredientOptimizer.TryEvaluate(
                __instance,
                __0,
                __1,
                out var optimizedResult))
            return true;

        __result = optimizedResult;
        return false;
    }
}

[HarmonyPatch(typeof(UIDragGridIngredients), "Refresh")]
internal static class NonStandardCrafterIngredientListRefreshPatch
{
    private static bool Prefix(UIDragGridIngredients __instance)
    {
        return !NonStandardCrafterIngredientPager.TryRefreshFromGameRefresh(__instance);
    }
}

[HarmonyPatch(typeof(LayerDragGrid), "SetInv",
    new[] { typeof(InvOwnerDraglet), typeof(bool) })]
internal static class NonStandardCrafterIngredientLayerReadyPatch
{
    private static void Postfix(LayerDragGrid __instance)
    {
        if (!ElinModifierPlugin.IsWorkbenchIngredientReadingOptimizationEnabled() ||
            __instance?.owner is not InvOwnerCraft ||
            __instance.uiIngredients == null)
            return;

        try
        {
            NonStandardCrafterIngredientPager.InitializeAfterSetInv(
                __instance.uiIngredients);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(LayerDragGrid), "OnKill")]
internal static class NonStandardCrafterIngredientListCleanupPatch
{
    private static void Prefix(LayerDragGrid __instance)
    {
        if (__instance?.uiIngredients != null)
            NonStandardCrafterIngredientPager.Cleanup(__instance.uiIngredients);
    }
}

