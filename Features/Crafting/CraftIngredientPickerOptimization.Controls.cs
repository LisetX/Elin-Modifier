using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    internal static void RegisterCraftIngredientPickerRoundedImage(Image image)
    {
        Instance?.RegisterLGuiRoundedImage(image);
    }
}

[HarmonyPatch(typeof(DropdownGrid), "Activate",
    new[] { typeof(Recipe.Ingredient), typeof(List<Thing>) })]
internal static class CraftIngredientPickerActivatePatch
{
    private static void Prefix(
        DropdownGrid __instance,
        Recipe.Ingredient __0,
        ref List<Thing> __1)
    {
        CraftIngredientPickerPager.Prepare(__instance, __0, ref __1);
    }

    private static void Postfix(DropdownGrid __instance)
    {
        CraftIngredientPickerPager.FinishOpen(__instance);
    }
}

