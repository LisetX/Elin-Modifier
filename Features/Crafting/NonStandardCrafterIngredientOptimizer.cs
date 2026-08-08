using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal static class NonStandardCrafterIngredientOptimizer
{
    private static InvOwnerCraft? _owner;
    private static TraitCrafter? _crafter;
    private static Card? _firstIngredient;
    private static int _ingredientIndex = -1;
    private static int _firstIngredientNum;
    private static int _firstIngredientEncLv;
    private static int _firstIngredientMaterialId;
    private static int _firstIngredientSocketsHash;
    private static int _sourceRecipeCount = -1;
    private static string _factory = "";
    private static readonly List<SourceRecipe.Row> CandidateRows = new List<SourceRecipe.Row>();

    internal static void Clear()
    {
        _owner = null;
        _crafter = null;
        _firstIngredient = null;
        _ingredientIndex = -1;
        _firstIngredientNum = 0;
        _firstIngredientEncLv = 0;
        _firstIngredientMaterialId = 0;
        _firstIngredientSocketsHash = 0;
        _sourceRecipeCount = -1;
        _factory = "";
        CandidateRows.Clear();
    }

    internal static bool TryEvaluate(
        TraitCrafter crafter,
        Card card,
        int ingredientIndex,
        out bool result)
    {
        result = false;
        if (!ElinModifierPlugin.IsWorkbenchIngredientReadingOptimizationEnabled() ||
            crafter == null || card == null)
            return false;

        try
        {
            var layer = LayerDragGrid.Instance;
            if (layer == null || layer.owner is not InvOwnerCraft owner ||
                owner.crafter != crafter || ingredientIndex < 0 ||
                ingredientIndex >= crafter.numIng)
                return false;

            Card? firstIngredient = null;
            if (ingredientIndex == 1 && layer.buttons != null && layer.buttons.Count > 0)
                firstIngredient = layer.buttons[0].Card;

            var sourceRows = GameAccess.Sources.Recipes?.rows;
            if (sourceRows == null)
                return false;

            var firstNum = firstIngredient?.Num ?? 0;
            var firstEncLv = firstIngredient?.encLV ?? 0;
            var firstMaterialId = GetMaterialId(firstIngredient);
            var firstSocketsHash = GetSocketsHash(firstIngredient);
            var factory = crafter.IdSource ?? "";
            if (_owner != owner || _crafter != crafter ||
                _ingredientIndex != ingredientIndex ||
                _firstIngredient != firstIngredient ||
                _firstIngredientNum != firstNum ||
                _firstIngredientEncLv != firstEncLv ||
                _firstIngredientMaterialId != firstMaterialId ||
                _firstIngredientSocketsHash != firstSocketsHash ||
                _sourceRecipeCount != sourceRows.Count ||
                !string.Equals(_factory, factory, StringComparison.Ordinal))
            {
                RebuildRows(
                    owner,
                    crafter,
                    ingredientIndex,
                    firstIngredient,
                    firstNum,
                    firstEncLv,
                    firstMaterialId,
                    firstSocketsHash,
                    factory,
                    sourceRows);
            }

            if (ingredientIndex == 1 && firstIngredient == card && card.Num < 2)
            {
                result = false;
                return true;
            }

            for (var i = 0; i < CandidateRows.Count; i++)
            {
                if (!crafter.IsIngredient(ingredientIndex, CandidateRows[i], card))
                    continue;
                result = true;
                return true;
            }

            result = false;
            return true;
        }
        catch
        {
            Clear();
            return false;
        }
    }

    private static void RebuildRows(
        InvOwnerCraft owner,
        TraitCrafter crafter,
        int ingredientIndex,
        Card? firstIngredient,
        int firstNum,
        int firstEncLv,
        int firstMaterialId,
        int firstSocketsHash,
        string factory,
        List<SourceRecipe.Row> sourceRows)
    {
        CandidateRows.Clear();
        for (var i = 0; i < sourceRows.Count; i++)
        {
            var row = sourceRows[i];
            if (row == null || !string.Equals(row.factory, factory, StringComparison.Ordinal))
                continue;
            if (ingredientIndex == 1 && !crafter.IsIngredient(0, row, firstIngredient))
                continue;
            CandidateRows.Add(row);
        }

        _owner = owner;
        _crafter = crafter;
        _ingredientIndex = ingredientIndex;
        _firstIngredient = firstIngredient;
        _firstIngredientNum = firstNum;
        _firstIngredientEncLv = firstEncLv;
        _firstIngredientMaterialId = firstMaterialId;
        _firstIngredientSocketsHash = firstSocketsHash;
        _sourceRecipeCount = sourceRows.Count;
        _factory = factory;
    }

    private static int GetMaterialId(Card? card)
    {
        try
        {
            return card?.material?.id ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int GetSocketsHash(Card? card)
    {
        try
        {
            if (card?.sockets == null)
                return 0;
            var hash = 17;
            for (var i = 0; i < card.sockets.Count; i++)
                hash = unchecked(hash * 31 + card.sockets[i]);
            return hash;
        }
        catch
        {
            return 0;
        }
    }
}

