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
    [HarmonyPatch(typeof(RecipeManager), "IsKnown")]
    private static class RecipeManagerIsKnownPatch
    {
        private static void Postfix(string __0, ref bool __result)
        {
            if (ShouldUnlockAllCraftRecipes() && IsRecipeSourceLearnable(__0))
                __result = true;
        }
    }
    [HarmonyPatch(typeof(RecipeManager), "GetRecipeLearnState")]
    private static class RecipeManagerGetRecipeLearnStatePatch
    {
        private static void Postfix(string __0, ref RecipeManager.LearnState __result)
        {
            if (ShouldUnlockAllCraftRecipes() && IsRecipeSourceLearnable(__0))
                __result = RecipeManager.LearnState.AlreadyLearned;
        }
    }
    [HarmonyPatch(typeof(RecipeManager), "ListSources")]
    private static class RecipeManagerListSourcesPatch
    {
        private static void Postfix(Thing __0, ref List<RecipeSource> __result)
        {
            if (ShouldUnlockAllCraftRecipes())
            {
                try
                {
                    RecipeManager.BuildList();
                    if (__result == null)
                        __result = new List<RecipeSource>();
                    foreach (var source in RecipeManager.list)
                    {
                        if (IsRecipeSourceVisibleForFactory(source, __0) && !__result.Contains(source))
                            __result.Add(source);
                    }
                }
                catch { }
            }

            try
            {
                AllPurposeWorkbenchTabPager.FilterSources(
                    LayerCraft.Instance,
                    __0,
                    ref __result);
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(Props), "ListThingStack")]
    private static class PropsListThingStackPatch
    {
        private static void Postfix(
            Recipe.Ingredient __0,
            ThingStack __result)
        {
            ReplaceStackWithVirtualIngredients(__0, __result);
        }
    }
    [HarmonyPatch(typeof(DropdownGrid), "BuildIngredients", new[] { typeof(Recipe), typeof(UnityEngine.UI.Image), typeof(Action), typeof(StockSearchMode) })]
    private static class DropdownGridBuildIngredientsPatch
    {
        private static void Prefix(Recipe __0, out CraftLastIngredientsState __state)
        {
            __state = SuppressCraftLastIngredients(__0);
            ClearNonVirtualRecipeIngredients(__0);
        }

        private static void Postfix(CraftLastIngredientsState __state)
        {
            RestoreCraftLastIngredients(__state);
        }
    }
    [HarmonyPatch(typeof(Recipe.Ingredient), "SetThing")]
    private static class RecipeIngredientSetThingPatch
    {
        private static void Prefix(Recipe.Ingredient __instance, ref Thing __0)
        {
            if (!ShouldNoCraftMaterials() || __instance == null || __0 == null || IsCraftVirtualThing(__0))
                return;
            var virtualThing = CreateVirtualCraftIngredientFromThing(__instance, __0);
            if (virtualThing != null)
                __0 = virtualThing;
        }
    }
    [HarmonyPatch(typeof(Recipe), "IsCraftable")]
    private static class RecipeIsCraftablePatch
    {
        private static bool Prefix(Recipe __instance, ref bool __result)
        {
            if (ShouldNoCraftMaterials())
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Recipe), "GetMaxCount")]
    private static class RecipeGetMaxCountPatch
    {
        private static void Postfix(ref int __result)
        {
            if (ShouldNoCraftMaterials())
                __result = Math.Max(__result, 999);
        }
    }
    [HarmonyPatch(typeof(LayerCraft), "OnClickCraft")]
    private static class LayerCraftOnClickCraftPatch
    {
        private static void Prefix(LayerCraft __instance)
        {
            if (__instance != null && ShouldNoCraftMaterials())
                PrepareRecipeVirtualIngredients(__instance.recipe, GetLayerCraftCount(__instance));
        }
    }
    [HarmonyPatch(typeof(LayerCraft), "GetTargets")]
    private static class LayerCraftGetTargetsPatch
    {
        private static bool Prefix(LayerCraft __instance, ref List<Thing> __result)
        {
            if (__instance == null || !ShouldNoCraftMaterials())
                return true;
            PrepareRecipeVirtualIngredients(__instance.recipe, GetLayerCraftCount(__instance));
            __result = BuildRecipeCraftTargets(__instance.recipe);
            return false;
        }
    }
    [HarmonyPatch(typeof(TaskCraft), "IsIngredientsValid", new[] { typeof(bool), typeof(int) })]
    private static class TaskCraftIsIngredientsValidPatch
    {
        private static bool Prefix(TaskCraft __instance, int __1, ref bool __result)
        {
            if (__instance == null || !ShouldNoCraftMaterials())
                return true;
            PrepareRecipeVirtualIngredients(__instance.recipe, Math.Max(1, __1));
            try { __instance.resources?.Clear(); } catch { }
            __result = true;
            return false;
        }
    }
    [HarmonyPatch(typeof(Card), "Split", new[] { typeof(int) })]
    private static class CardSplitPatch
    {
        private static void Postfix(Card __instance, Thing __result)
        {
            if (IsCraftVirtualThing(__instance))
                MarkCraftVirtualThing(__result);
        }
    }
    [HarmonyPatch(typeof(AI_UseCrafter), "OnEnd")]
    private static class AIUseCrafterOnEndPatch
    {
        private static void Prefix(AI_UseCrafter __instance)
        {
            if (__instance != null)
                DestroyCraftVirtualThings(__instance.ings);
        }
    }
}
