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
    private static int GetLayerCraftCount(LayerCraft layer)
    {
        try { return Math.Max(1, layer.inputNum == null ? 1 : layer.inputNum.Num); }
        catch { return 1; }
    }
    private static int GetIngredientRequiredCount(Recipe.Ingredient ingredient, int count)
    {
        try
        {
            var required = (long)Math.Max(1, ingredient.req) * Math.Max(1, count);
            return (int)Math.Min(VirtualCraftMaterialCount, Math.Max(1L, required));
        }
        catch
        {
            return 1;
        }
    }
    private static void PrepareRecipeVirtualIngredients(Recipe? recipe, int count)
    {
        if (!ShouldNoCraftMaterials() || recipe == null || recipe.ingredients == null) return;
        try
        {
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient == null) continue;
                var shouldFill = !ingredient.optional || ingredient.thing != null;
                if (!shouldFill) continue;

                var required = GetIngredientRequiredCount(ingredient, count);
                var current = ingredient.thing;
                if (current != null && IsCraftVirtualThing(current) && !current.isDestroyed && current.Num >= required)
                    continue;

                var virtualThing = CreateVirtualCraftIngredient(recipe, ingredient, required);
                if (virtualThing != null)
                    ingredient.SetThing(virtualThing);
            }
            try { recipe.OnChangeIngredient(); } catch { }
        }
        catch { }
    }
    private static List<Thing> BuildRecipeCraftTargets(Recipe? recipe)
    {
        var result = new List<Thing>();
        if (recipe == null || recipe.ingredients == null) return result;
        try
        {
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient == null) continue;
                var thing = ingredient.thing;
                if (ingredient.optional && (thing == null || thing.isDestroyed))
                    continue;
                if (thing == null || thing.isDestroyed)
                {
                    thing = CreateVirtualCraftIngredient(recipe, ingredient, GetIngredientRequiredCount(ingredient, 1));
                    if (thing != null)
                        ingredient.SetThing(thing);
                }
                if (thing != null)
                    result.Add(thing);
            }
        }
        catch { }
        return result;
    }
    private readonly struct CraftIngredientOption
    {
        public readonly string Id;
        public readonly bool UseCategory;
        public readonly string MaterialAlias;

        public CraftIngredientOption(string id, bool useCategory, string materialAlias)
        {
            Id = id;
            UseCategory = useCategory;
            MaterialAlias = materialAlias;
        }
    }
    private static Thing? CreateVirtualCraftIngredient(Recipe? recipe, Recipe.Ingredient? ingredient, int required, CraftIngredientOption? option = null)
    {
        if (ingredient == null) return null;
        try
        {
            var id = option.HasValue ? option.Value.Id : GetIngredientThingId(ingredient);
            var useCategory = option.HasValue ? option.Value.UseCategory : ingredient.useCat;
            var materialAlias = option.HasValue ? option.Value.MaterialAlias : "";
            if (string.IsNullOrEmpty(id)) return null;

            Thing? thing = null;
            if (useCategory)
            {
                thing = GameAccess.Spawn.CreateThingFromCategory(id, -1);
            }
            else if (!string.IsNullOrEmpty(materialAlias))
            {
                thing = GameAccess.Spawn.CreateThing(id, materialAlias, -1);
            }
            else if (ingredient.mat >= 0)
            {
                thing = GameAccess.Spawn.CreateThing(id, ingredient.mat, -1);
            }
            else
            {
                var defaultMaterialAlias = "";
                try { defaultMaterialAlias = recipe == null ? "" : recipe.DefaultMaterial.alias; } catch { }
                if (!string.IsNullOrEmpty(defaultMaterialAlias))
                {
                    try { thing = GameAccess.Spawn.CreateThing(id, defaultMaterialAlias, -1); } catch { thing = null; }
                }
                if (thing == null)
                    thing = GameAccess.Spawn.CreateThing(id, -1, -1);
            }

            if (thing == null) return null;
            SetCardNum(thing, Math.Max(VirtualCraftMaterialCount, required));
            MarkCraftVirtualThing(thing);
            return thing;
        }
        catch
        {
            return null;
        }
    }
    private static string GetIngredientThingId(Recipe.Ingredient ingredient)
    {
        try
        {
            var idThing = ingredient.IdThing;
            if (!string.IsNullOrEmpty(idThing)) return idThing;
        }
        catch { }
        try { return ingredient.id ?? ""; }
        catch { return ""; }
    }
    private static List<CraftIngredientOption> GetIngredientOptions(Recipe.Ingredient ingredient)
    {
        var options = new List<CraftIngredientOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddOption(string? id, bool useCategory, string? materialAlias)
        {
            if (string.IsNullOrEmpty(id)) return;
            materialAlias ??= "";
            var key = (useCategory ? "cat:" : "thing:") + id + "|mat:" + materialAlias;
            if (!seen.Add(key)) return;
            options.Add(new CraftIngredientOption(id, useCategory, materialAlias));
        }

        void AddMaterialOptions(string? id, bool useCategory)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!ShouldUseUnlockedCraftMaterials() || useCategory)
            {
                AddOption(id, useCategory, "");
                return;
            }

            var addedMaterial = false;
            foreach (var materialAlias in ExpandCraftMaterialAliases(ingredient))
            {
                AddOption(id, false, materialAlias);
                addedMaterial = true;
            }
            if (!addedMaterial)
                AddOption(id, false, "");
        }

        void AddExpandedId(string? id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (ingredient.useCat)
            {
                if (ShouldUseUnlockedCraftMaterials())
                {
                    var added = false;
                    foreach (var expandedId in ExpandCraftCategoryThingIds(id))
                    {
                        AddMaterialOptions(expandedId, false);
                        added = true;
                    }
                    if (!added)
                        AddMaterialOptions(id, true);
                }
                else
                {
                    AddMaterialOptions(id, true);
                }
                return;
            }
            foreach (var expandedId in ExpandCraftOriginThingIds(id))
                AddMaterialOptions(expandedId, false);
        }

        try { AddExpandedId(ingredient.id); } catch { }
        try
        {
            if (ingredient.idOther != null)
            {
                foreach (var id in ingredient.idOther)
                    AddExpandedId(id);
            }
        }
        catch { }
        if (options.Count == 0)
        {
            try { AddExpandedId(ingredient.IdThing); } catch { }
        }
        return options;
    }
    private static List<string> ExpandCraftOriginThingIds(string id)
    {
        if (Instance == null)
            return new List<string> { id };
        if (Instance._craftOriginThingIdsCache.TryGetValue(id, out var cached))
            return cached;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void AddId(string? value)
        {
            if (string.IsNullOrEmpty(value) || !seen.Add(value)) return;
            result.Add(value);
        }

        AddId(id);
        try
        {
            foreach (var row in EnumerateSourceThingRows())
            {
                var rowId = GetString(row, "id");
                if (string.IsNullOrEmpty(rowId)) continue;
                if (string.Equals(GetString(row, "_origin"), id, StringComparison.Ordinal))
                    AddId(rowId);
            }
        }
        catch { }

        Instance._craftOriginThingIdsCache[id] = result;
        return result;
    }
    private static List<string> ExpandCraftCategoryThingIds(string id)
    {
        if (Instance == null)
            return new List<string>();
        if (Instance._craftCategoryThingIdsCache.TryGetValue(id, out var cached))
            return cached;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void AddId(string? value)
        {
            if (string.IsNullOrEmpty(value) || !seen.Add(value)) return;
            result.Add(value);
        }

        try
        {
            foreach (var row in EnumerateSourceThingRows())
            {
                var rowId = GetString(row, "id");
                if (string.IsNullOrEmpty(rowId)) continue;
                if (SourceThingRowIsInCategory(row, id))
                    AddId(rowId);
            }
        }
        catch { }

        Instance._craftCategoryThingIdsCache[id] = result;
        return result;
    }
    private static List<string> ExpandCraftMaterialAliases(Recipe.Ingredient ingredient)
    {
        if (Instance == null)
            return new List<string>();

        var fixedMat = -1;
        var tag = "";
        try { tag = ingredient.tag ?? ""; } catch { }
        var cacheKey = fixedMat >= 0 ? "mat:" + fixedMat.ToString(CultureInfo.InvariantCulture) : "tag:" + tag;
        if (Instance._craftMaterialAliasesCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var row in EnumerateSourceMaterialRows())
            {
                if (!SourceMaterialRowMatchesIngredient(row, fixedMat, tag)) continue;
                var alias = GetString(row, "alias");
                if (string.IsNullOrEmpty(alias) || !seen.Add(alias)) continue;
                result.Add(alias);
            }
        }
        catch { }

        Instance._craftMaterialAliasesCache[cacheKey] = result;
        return result;
    }
    private static bool SourceMaterialRowMatchesIngredient(object row, int fixedMat, string tag)
    {
        if (row == null) return false;
        try
        {
            if (fixedMat >= 0)
                return GetInt(row, "id") == fixedMat;
        }
        catch { return false; }
        if (string.IsNullOrEmpty(tag))
            return true;
        try
        {
            foreach (var value in GetStringArray(row, "tag"))
                if (string.Equals(value, tag, StringComparison.Ordinal))
                    return true;
        }
        catch { }
        return false;
    }
    private static bool SourceThingRowIsInCategory(object row, string categoryId)
    {
        if (row == null || string.IsNullOrEmpty(categoryId)) return false;
        try
        {
            if (string.Equals(GetString(row, "category"), categoryId, StringComparison.Ordinal))
                return true;
        }
        catch { }
        try
        {
            if (string.Equals(GetString(row, "categorySub"), categoryId, StringComparison.Ordinal))
                return true;
        }
        catch { }
        try
        {
            var category = row.GetType().GetProperty("Category", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(row, null);
            var method = category?.GetType().GetMethod("IsChildOf", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (method != null && method.Invoke(category, new object[] { categoryId }) is bool matched && matched)
                return true;
        }
        catch { }
        return false;
    }
    private static bool IsExcludedCraftIngredient(Thing? thing, Recipe.Ingredient ingredient)
    {
        if (thing == null) return true;
        try
        {
            var method = thing.GetType().GetMethod("IsExcludeFromCraft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Recipe.Ingredient) }, null);
            if (method != null && method.Invoke(thing, new object[] { ingredient }) is bool excluded)
                return excluded;
        }
        catch { }
        return false;
    }
    private static bool IsValidCraftIngredient(Thing? thing, Recipe.Ingredient ingredient)
    {
        if (thing == null) return false;
        try
        {
            if (ingredient.IsValidIngredient(thing))
                return true;
        }
        catch { }
        return ThingMatchesIngredientSearchRule(thing, ingredient);
    }
    private static bool ThingMatchesIngredientSearchRule(Thing thing, Recipe.Ingredient ingredient)
    {
        try
        {
            if (!IngredientMaterialTagMatches(thing, ingredient))
                return false;
            if (IngredientAcceptsCreativeFood(ingredient, thing))
                return true;
            if (ingredient.useCat)
            {
                foreach (var id in EnumerateIngredientIds(ingredient))
                    if (thing.category.IsChildOf(id))
                        return true;
                return false;
            }

            foreach (var id in EnumerateIngredientIds(ingredient))
                if (ThingIdOrOriginMatches(thing, id))
                    return true;
        }
        catch { }
        return false;
    }
    private static bool IngredientMaterialTagMatches(Thing thing, Recipe.Ingredient ingredient)
    {
        var tag = "";
        try { tag = ingredient.tag ?? ""; } catch { }
        if (string.IsNullOrEmpty(tag)) return true;
        try
        {
            var tags = thing.material == null ? null : thing.material.tag;
            if (tags == null) return false;
            foreach (var value in tags)
                if (string.Equals(value, tag, StringComparison.Ordinal))
                    return true;
        }
        catch { }
        return false;
    }
    private static bool IngredientAcceptsCreativeFood(Recipe.Ingredient ingredient, Thing thing)
    {
        try
        {
            if (ingredient.ingType != Recipe.IngType.CreativeFood)
                return false;
            if (!thing.HasElement(10, false))
                return false;
            if (thing.category.IsChildOf("seasoning") || thing.category.IsChildOf("meal"))
                return false;
            if (thing.trait is TraitFoodFishSlice)
                return false;
            return true;
        }
        catch { }
        return false;
    }
    private static IEnumerable<string> EnumerateIngredientIds(Recipe.Ingredient ingredient)
    {
        var id = "";
        try { id = ingredient.id ?? ""; } catch { }
        if (!string.IsNullOrEmpty(id))
            yield return id;
        List<string>? idOther = null;
        try { idOther = ingredient.idOther; } catch { }
        if (idOther == null) yield break;
        foreach (var other in idOther)
            if (!string.IsNullOrEmpty(other))
                yield return other;
    }
    private static bool ThingIdOrOriginMatches(Thing thing, string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        try
        {
            if (string.Equals(thing.id, id, StringComparison.Ordinal))
                return true;
        }
        catch { }
        try
        {
            if (string.Equals(thing.source._origin, id, StringComparison.Ordinal))
                return true;
        }
        catch { }
        return false;
    }
    private static string GetThingMaterialAlias(Thing? thing)
    {
        if (thing == null) return "";
        try { return thing.material == null ? "" : thing.material.alias ?? ""; }
        catch { return ""; }
    }
    private static bool ThingMatchesCraftOption(Thing? thing, CraftIngredientOption option)
    {
        if (thing == null) return false;
        try
        {
            if (option.UseCategory)
            {
                if (!thing.category.IsChildOf(option.Id))
                    return false;
            }
            else
            {
                var sameId = string.Equals(thing.id, option.Id, StringComparison.Ordinal);
                try { sameId = sameId || string.Equals(thing.source._origin, option.Id, StringComparison.Ordinal); } catch { }
                if (!sameId)
                    return false;
            }
        }
        catch { return false; }

        if (!string.IsNullOrEmpty(option.MaterialAlias) &&
            !string.Equals(GetThingMaterialAlias(thing), option.MaterialAlias, StringComparison.Ordinal))
            return false;
        return true;
    }
    private static bool StackContainsCraftOption(ThingStack stack, CraftIngredientOption option)
    {
        if (stack.list == null) return false;
        try
        {
            foreach (var thing in stack.list)
                if (ThingMatchesCraftOption(thing, option))
                    return true;
        }
        catch { }
        return false;
    }
    private static void AddVirtualIngredientToStack(Recipe.Ingredient? ingredient, ThingStack? stack)
    {
        if (!ShouldNoCraftMaterials() || ingredient == null || stack == null) return;
        try
        {
            if (stack.list == null)
                stack.list = new List<Thing>();

            foreach (var option in GetIngredientOptions(ingredient))
            {
                if (StackContainsCraftOption(stack, option)) continue;
                var virtualThing = CreateVirtualCraftIngredient(null, ingredient, GetIngredientRequiredCount(ingredient, 1), option);
                if (virtualThing == null) continue;
                if (!IsValidCraftIngredient(virtualThing, ingredient) || IsExcludedCraftIngredient(virtualThing, ingredient))
                {
                    DestroyCraftVirtualThing(virtualThing);
                    continue;
                }
                stack.Add(virtualThing);
            }
        }
        catch { }
    }
    private static void ReplaceStackWithVirtualIngredients(Recipe.Ingredient? ingredient, ThingStack? stack)
    {
        if (!ShouldNoCraftMaterials() || ingredient == null || stack == null) return;
        try
        {
            if (stack.list == null)
                stack.list = new List<Thing>();
            else
                stack.list.Clear();
            stack.count = 0;
            stack.max = 0;
            stack.val = -1;
            AddVirtualIngredientToStack(ingredient, stack);
        }
        catch { }
    }
    private static Thing? CreateVirtualCraftIngredientFromThing(Recipe.Ingredient? ingredient, Thing? thing)
    {
        if (!ShouldNoCraftMaterials() || ingredient == null || thing == null || IsCraftVirtualThing(thing))
            return thing;
        try
        {
            var id = thing.id;
            if (string.IsNullOrEmpty(id))
                id = GetIngredientThingId(ingredient);
            if (string.IsNullOrEmpty(id))
                return null;

            var option = new CraftIngredientOption(id, false, GetThingMaterialAlias(thing));
            var virtualThing = CreateVirtualCraftIngredient(null, ingredient, GetIngredientRequiredCount(ingredient, 1), option);
            if (virtualThing == null)
                virtualThing = CreateVirtualCraftIngredient(null, ingredient, GetIngredientRequiredCount(ingredient, 1));
            if (virtualThing == null)
                return null;
            return virtualThing;
        }
        catch
        {
            return null;
        }
    }
    private struct CraftLastIngredientsState
    {
        public bool Removed;
        public string RecipeId;
        public List<int>? Values;
    }
    private static CraftLastIngredientsState SuppressCraftLastIngredients(Recipe? recipe)
    {
        var state = new CraftLastIngredientsState();
        if (!ShouldNoCraftMaterials() || recipe == null || string.IsNullOrEmpty(recipe.id))
            return state;
        try
        {
            var dict = GameAccess.Runtime.Player?.recipes?.lastIngredients;
            if (dict == null || !dict.TryGetValue(recipe.id, out var values))
                return state;
            state.Removed = true;
            state.RecipeId = recipe.id;
            state.Values = values;
            dict.Remove(recipe.id);
        }
        catch { }
        return state;
    }
    private static void RestoreCraftLastIngredients(CraftLastIngredientsState state)
    {
        if (!state.Removed || string.IsNullOrEmpty(state.RecipeId)) return;
        try
        {
            var dict = GameAccess.Runtime.Player?.recipes?.lastIngredients;
            if (dict == null || dict.ContainsKey(state.RecipeId)) return;
            dict[state.RecipeId] = state.Values ?? new List<int>();
        }
        catch { }
    }
    private static void ClearNonVirtualRecipeIngredients(Recipe? recipe)
    {
        if (!ShouldNoCraftMaterials() || recipe == null || recipe.ingredients == null) return;
        try
        {
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient == null || ingredient.thing == null || IsCraftVirtualThing(ingredient.thing))
                    continue;
                ingredient.SetThing(null);
            }
        }
        catch { }
    }
    private static bool IsCraftVirtualThing(Card? card)
    {
        try { return card != null && Instance != null && Instance._virtualCraftThingUids.Contains(card.uid); }
        catch { return false; }
    }
    private static void MarkCraftVirtualThing(Card? card)
    {
        try
        {
            if (card != null && Instance != null)
                Instance._virtualCraftThingUids.Add(card.uid);
        }
        catch { }
    }
    private static void DestroyCraftVirtualThing(Card? card)
    {
        if (!IsCraftVirtualThing(card)) return;
        var uid = 0;
        try { uid = card == null ? 0 : card.uid; } catch { }
        try
        {
            if (card != null && !card.isDestroyed)
                card.Destroy();
        }
        catch { }
        try
        {
            if (Instance != null)
                Instance._virtualCraftThingUids.Remove(uid);
        }
        catch { }
    }
    private static void DestroyCraftVirtualThings(List<Thing>? things)
    {
        if (things == null) return;
        try
        {
            foreach (var thing in things)
                DestroyCraftVirtualThing(thing);
        }
        catch { }
    }
    private static void ClearRecipeVirtualIngredients(Recipe? recipe)
    {
        if (recipe == null || recipe.ingredients == null) return;
        try
        {
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient == null || !IsCraftVirtualThing(ingredient.thing)) continue;
                DestroyCraftVirtualThing(ingredient.thing);
                ingredient.SetThing(null);
            }
            try { recipe.OnChangeIngredient(); } catch { }
        }
        catch { }
    }
    private static void ClearCraftVirtualIngredients()
    {
        try
        {
            if (LayerCraft.Instance != null)
                ClearRecipeVirtualIngredients(LayerCraft.Instance.recipe);
        }
        catch { }
        try
        {
            if (LayerCraftFloat.Instance != null)
                ClearRecipeVirtualIngredients(LayerCraftFloat.Instance.recipe);
        }
        catch { }
    }
    private static void RefreshCraftingUi()
    {
        try
        {
            if (LayerCraft.Instance != null)
            {
                LayerCraft.Instance.RefreshCategory("all", true);
                LayerCraft.Instance.RefreshCurrentGrid();
                LayerCraft.Instance.RefreshInfo();
            }
        }
        catch { }
        try
        {
            if (LayerCraftFloat.Instance != null)
                LayerCraftFloat.Instance.RefreshCraft();
        }
        catch { }
        try
        {
            if (DropdownGrid.Instance != null)
                DropdownGrid.Instance.Redraw();
        }
        catch { }
    }
    private static bool IsRecipeSourceVisibleForFactory(RecipeSource? source, Thing? factory)
    {
        if (source == null) return false;
        try
        {
            if (source.isBridgePillar || source.isChara || source.noListing)
                return false;
            if (factory == null)
                return string.Equals(source.idFactory, "self", StringComparison.Ordinal);
            return factory.trait != null && factory.trait.Contains(source);
        }
        catch
        {
            return false;
        }
    }
    private static bool IsRecipeSourceLearnable(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        try
        {
            var source = RecipeManager.Get(id);
            if (source == null || source.isBridgePillar || source.isChara || source.noListing)
                return false;
            return source.NeedFactory || source.IsQuickCraft;
        }
        catch
        {
            return false;
        }
    }
    internal static string Tr(string zh, string en)
    {
        return Instance == null ? zh : Instance.T(zh, en);
    }
}
