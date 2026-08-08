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
    internal static bool CanEditWeaponData(Card? card)
    {
        try
        {
            if (!(card is Thing thing) || thing.isDestroyed || IsCraftVirtualThing(thing))
                return false;
            return thing.IsWeapon || IsToolThing(thing);
        }
        catch
        {
            return false;
        }
    }
    private static bool IsToolThing(Thing thing)
    {
        try
        {
            if (thing.trait is TraitTool)
                return true;
        }
        catch { }

        try { return thing.IsToolbelt; }
        catch { return false; }
    }
    private static bool CanEditGene(Card? card)
    {
        try
        {
            return card is Thing thing &&
                   !thing.isDestroyed &&
                   !IsCraftVirtualThing(thing) &&
                   (thing.c_DNA != null || IsGeneThing(thing));
        }
        catch
        {
            return false;
        }
    }
    private static bool IsGeneThing(Thing thing)
    {
        try
        {
            if (thing.trait is TraitGene)
                return true;
        }
        catch { }

        try
        {
            return string.Equals(thing.id, "gene", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(thing.id, "gene_brain", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    private static bool EnsureEditableGeneDna(Thing thing)
    {
        if (thing.c_DNA != null)
            return true;
        if (!IsGeneThing(thing))
            return false;

        try
        {
            var dna = new DNA();
            var pc = GameAccess.Characters.PlayerCharacter;
            dna.id = pc == null ? "" : pc.id;
            dna.type = DNA.Type.Default;
            dna.lv = pc == null ? Math.Max(1, thing.LV) : Math.Max(1, pc.LV);
            try { dna.seed = GameAccess.Random.Next(20000); }
            catch { dna.seed = 0; }
            if (!string.IsNullOrEmpty(dna.id))
            {
                try { thing.MakeRefFrom(dna.id); }
                catch { }
            }
            thing.c_DNA = dna;
            return thing.c_DNA != null;
        }
        catch
        {
            return false;
        }
    }
    private static bool CanCustomizeItemAmount(Card? card)
    {
        try
        {
            return card is Thing thing && !thing.isDestroyed && !IsCraftVirtualThing(thing);
        }
        catch
        {
            return false;
        }
    }
    private static bool CanEditItemData(Card? card)
    {
        try
        {
            if (!(card is Thing thing) || thing.isDestroyed || IsCraftVirtualThing(thing))
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static bool CanEditFoodData(Card? card)
    {
        try
        {
            if (!(card is Thing thing) || thing.isDestroyed || IsCraftVirtualThing(thing))
                return false;
            return IsFoodCard(thing);
        }
        catch
        {
            return false;
        }
    }
    internal static int GetThingElementBase(Thing thing, int elementId)
    {
        try { return thing.elements.Base(elementId); }
        catch { }
        try { return thing.elements.Value(elementId); }
        catch { return 0; }
    }
    internal static int GetThingElementEditorValue(Thing thing, Element element)
    {
        if (thing == null || element == null)
            return 0;
        try { return element.Value; }
        catch { }
        try { return thing.elements.Value(element.id); }
        catch { }
        try { return element.ValueWithoutLink; }
        catch { }
        try { return element.vBase; }
        catch { return 0; }
    }
    private static bool CanRestoreThingElementOriginalValue(Thing? thing, string elementIdText)
    {
        if (!TryParseElementId(elementIdText, out var elementId))
            return false;
        return TryGetThingElementOriginalValue(thing, elementId, out var originalValue) && originalValue != 0;
    }
    private static void RestoreThingElementInputToOriginal(Thing? thing, GeneValueInput row)
    {
        if (row == null || !TryParseElementId(row.ElementId, out var elementId))
            return;
        if (TryGetThingElementOriginalValue(thing, elementId, out var originalValue))
            row.Value = originalValue.ToString(CultureInfo.InvariantCulture);
    }
    private static bool TryGetThingElementOriginalValue(Thing? thing, int elementId, out int originalValue)
    {
        originalValue = 0;
        if (thing == null || elementId <= 0)
            return false;

        try
        {
            var element = thing.elements.GetElement(elementId);
            if (element == null)
                return false;
            originalValue = element.Value - element.vBase;
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static bool TryParseElementId(string text, out int elementId)
    {
        return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out elementId) && elementId > 0;
    }
    private static void SetThingElementBase(Thing thing, int elementId, int value)
    {
        try { thing.elements.SetBase(elementId, value, 0); }
        catch { }
    }
    private static void SetThingElementEditorValue(Thing thing, int elementId, int targetValue)
    {
        if (thing == null || elementId <= 0)
            return;

        var sourceAndBonus = 0;
        try
        {
            var element = thing.elements.GetOrCreateElement(elementId);
            var currentValue = element.Value;
            var currentBase = element.vBase;
            sourceAndBonus = currentValue - currentBase;
        }
        catch { }

        SetThingElementBase(thing, elementId, targetValue - sourceAndBonus);
    }
    private static string SafeIntText(Func<int> getter, string fallback = "?")
    {
        try { return getter().ToString(CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }
    private static string SafeThingName(Thing thing)
    {
        try
        {
            var name = thing.GetName(NameStyle.FullNoArticle, Math.Max(1, thing.Num));
            name = CleanDisplayName(name);
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        catch { }

        try
        {
            var name = thing.GetName(NameStyle.Full, Math.Max(1, thing.Num));
            name = CleanDisplayName(name);
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        catch { }

        try { return string.IsNullOrEmpty(thing.id) ? thing.ToString() : thing.id; }
        catch { return thing.ToString(); }
    }
    private static void RefreshInventoryUi()
    {
        ActiveModules?.InteractionReflection.MarkInventoryDirty();

        try
        {
            foreach (var button in UnityEngine.Object.FindObjectsOfType<ButtonGrid>())
                ApplyFoodRotOverlay(button, button == null ? null : button.Card);
        }
        catch { }
    }
    private static bool IsPlayerInventoryOwner(InvOwner? invOwner)
    {
        if (invOwner == null)
            return false;

        try
        {
            if (ReferenceEquals(invOwner, InvOwner.Main))
                return true;
        }
        catch { }

        try
        {
            if (invOwner.Chara != null && ReferenceEquals(invOwner.Chara, GameAccess.Characters.PlayerCharacter))
                return true;
        }
        catch { }

        return false;
    }
    private static bool AddGeneEditorInteraction(object? interactionList, ButtonGrid? button)
    {
        if (!ShouldCustomGeneEditor() || Instance == null || interactionList == null)
            return false;

        try
        {
            var thing = GetInteractionThing(interactionList) ?? button?.Card as Thing;
            if (!CanEditGene(thing))
                return false;

            return TryAddCachedInteraction(
                interactionList,
                Instance.T("基因编辑", "Gene Editor"),
                9001,
                new Action(() => Instance?.OpenGeneEditorWindow(thing!)),
                "ElinModifierGeneEditor"
            );
        }
        catch
        {
            return false;
        }
    }
    private static bool AddWeaponEditorInteraction(object? interactionList, ButtonGrid? button)
    {
        if (!ShouldCustomWeaponEditor() || Instance == null || interactionList == null)
            return false;

        try
        {
            var thing = GetInteractionThing(interactionList) ?? button?.Card as Thing;
            if (!CanEditWeaponData(thing))
                return false;

            return TryAddCachedInteraction(
                interactionList,
                Instance.T("修改武器数据", "Modify weapon data"),
                9002,
                new Action(() => Instance?.OpenWeaponEditorWindow(thing!)),
                "ElinModifierWeaponEditor"
            );
        }
        catch
        {
            return false;
        }
    }
    private static bool AddCustomItemAmountInteraction(object? interactionList, ButtonGrid? button)
    {
        if (!ShouldCustomItemAmount() || Instance == null || interactionList == null)
            return false;

        try
        {
            var thing = GetInteractionThing(interactionList) ?? button?.Card as Thing;
            if (!CanCustomizeItemAmount(thing))
                return false;

            return TryAddCachedInteraction(
                interactionList,
                Instance.T("修改持有数量", "Modify held amount"),
                9000,
                new Action(() => Instance?.OpenItemAmountWindow(thing!)),
                "ElinModifierCustomItemAmount"
            );
        }
        catch
        {
            return false;
        }
    }
    private static bool AddFoodEditorInteraction(object? interactionList, ButtonGrid? button)
    {
        if (!ShouldCustomFoodEditor() || Instance == null || interactionList == null)
            return false;

        try
        {
            var thing = GetInteractionThing(interactionList) ?? button?.Card as Thing;
            if (!CanEditFoodData(thing))
                return false;

            return TryAddCachedInteraction(
                interactionList,
                Instance.T("修改食品数据", "Modify food data"),
                9004,
                new Action(() => Instance?.OpenFoodEditorWindow(thing!)),
                "ElinModifierFoodEditor"
            );
        }
        catch
        {
            return false;
        }
    }
    private static bool AddItemDataEditorInteraction(object? interactionList, ButtonGrid? button)
    {
        if (!ShouldCustomItemEditor() || Instance == null || interactionList == null)
            return false;

        try
        {
            var thing = GetInteractionThing(interactionList) ?? button?.Card as Thing;
            if (!CanEditItemData(thing))
                return false;

            return TryAddCachedInteraction(
                interactionList,
                Instance.T("修改物品数据", "Modify item data"),
                9003,
                new Action(() => Instance?.OpenItemDataEditorWindow(thing!)),
                "ElinModifierItemDataEditor"
            );
        }
        catch
        {
            return false;
        }
    }
    private static Thing? GetInteractionThing(object interactionList)
    {
        return ActiveModules?.InteractionReflection.GetThing(interactionList);
    }
    private static bool TryAddCachedInteraction(
        object interactionList,
        string label,
        int priority,
        Action action,
        string id)
    {
        var module = ActiveModules?.InteractionReflection;
        return module != null && module.TryAdd(interactionList, label, priority, action, id);
    }
}
