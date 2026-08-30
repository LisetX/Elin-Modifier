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
    private static void RefreshFoodRotOverlays()
    {
        try
        {
            foreach (var button in UnityEngine.Object.FindObjectsOfType<ButtonGrid>())
                ApplyFoodRotOverlay(button, button == null ? null : button.Card);
        }
        catch { }
    }
    private static void RefreshFoodRotOverlayForCard(Card? card)
    {
        if (Instance == null || !Instance._showFoodRot || card == null) return;
        try
        {
            foreach (var button in UnityEngine.Object.FindObjectsOfType<ButtonGrid>())
            {
                if (button != null && IsFoodRotOverlayBoundToCard(button, card))
                    ApplyFoodRotOverlay(button, card);
            }
        }
        catch { }
    }
    private static bool IsFoodRotOverlayBoundToCard(ButtonGrid button, Card card)
    {
        if (button == null || card == null) return false;
        try
        {
            if (ReferenceEquals(button.card, card))
                return true;
        }
        catch { }

        if (!(button is ButtonHotItem hotbarButton))
            return false;

        try
        {
            var hotItem = ButtonHotItemBaseItemField?.GetValue(hotbarButton) as HotItem;
            return ReferenceEquals(hotItem?.Thing, card);
        }
        catch
        {
            return false;
        }
    }
    private static void ClearFoodRotOverlays()
    {
        try
        {
            foreach (var button in UnityEngine.Object.FindObjectsOfType<ButtonGrid>())
                SetFoodRotOverlayVisible(button, false);
        }
        catch { }
    }
    private static void ApplyFoodRotOverlay(ButtonGrid button, Card? card)
    {
        if (button == null) return;
        card = ResolveFoodRotOverlayCard(button, card);
        var show = Instance != null && Instance._showFoodRot && ShouldShowFoodRot(card);
        if (!show)
        {
            SetFoodRotOverlayVisible(button, false);
            return;
        }

        var overlay = GetFoodRotOverlay(button, true);
        if (overlay == null) return;

        overlay.raycastTarget = false;
        overlay.color = GetFoodRotColor(card);
        overlay.gameObject.SetActive(true);
        SetFoodRotOverlaySiblingIndex(button, overlay.transform);
    }
    private static Card? ResolveFoodRotOverlayCard(ButtonGrid button, Card? fallback)
    {
        if (button is ButtonHotItem hotbarButton)
        {
            try
            {
                var hotItem = ButtonHotItemBaseItemField?.GetValue(hotbarButton) as HotItem;
                var thing = hotItem?.Thing;
                return thing == null || thing.isDestroyed ? null : thing;
            }
            catch
            {
                return null;
            }
        }

        try
        {
            if (fallback != null && !fallback.isDestroyed)
                return fallback;
            return button.Card;
        }
        catch
        {
            return null;
        }
    }
    private static void SetFoodRotOverlayVisible(ButtonGrid button, bool visible)
    {
        if (button == null) return;
        var child = button.transform.Find(FoodRotOverlayName);
        if (child != null)
            child.gameObject.SetActive(visible);
    }
    private static UnityEngine.UI.Image? GetFoodRotOverlay(ButtonGrid button, bool create)
    {
        var child = button.transform.Find(FoodRotOverlayName);
        if (child != null)
        {
            var existing = child.GetComponent<UnityEngine.UI.Image>();
            if (existing != null)
                ConfigureFoodRotOverlayRect(child);
            return existing;
        }
        if (!create) return null;

        var go = new GameObject(FoodRotOverlayName, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(button.transform, false);
        ConfigureFoodRotOverlayRect(go.transform);
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        SetFoodRotOverlaySiblingIndex(button, image.transform);
        return image;
    }
    private static void SetFoodRotOverlaySiblingIndex(ButtonGrid button, Transform overlay)
    {
        var index = int.MaxValue;
        if (button.icon != null) index = Math.Min(index, button.icon.transform.GetSiblingIndex());
        if (button.mainText != null) index = Math.Min(index, button.mainText.transform.GetSiblingIndex());
        if (button.subText != null) index = Math.Min(index, button.subText.transform.GetSiblingIndex());
        if (button.subText2 != null) index = Math.Min(index, button.subText2.transform.GetSiblingIndex());
        if (button.keyText != null) index = Math.Min(index, button.keyText.transform.GetSiblingIndex());
        if (index == int.MaxValue)
            index = Math.Max(0, button.transform.childCount - 1);
        overlay.SetSiblingIndex(Math.Max(0, index));
    }
    private static void ConfigureFoodRotOverlayRect(Transform transform)
    {
        if (!(transform is RectTransform rect)) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
    private static Color GetFoodRotColor(Card? card)
    {
        var ratio = GetFoodRotRatio(card);
        return Color.Lerp(FoodRotFreshColor, FoodRotSpoiledColor, ratio);
    }
    private static bool IsFoodCard(Card? card)
    {
        try { return card != null && !card.isDestroyed && card.IsFood; }
        catch { return false; }
    }
    private static bool ShouldKeepFoodFresh(Card? card)
    {
        return Instance != null && Instance._ignoreFoodDecay && IsFoodCard(card);
    }
    private static bool ShouldShowFoodRot(Card? card)
    {
        return IsFoodCard(card);
    }
    private static int GetFoodDecayRate(Card? card)
    {
        if (card == null) return 0;
        try { return card.trait == null ? 0 : card.trait.Decay; }
        catch { }

        try
        {
            var material = card.material;
            return material == null ? 0 : material.decay;
        }
        catch { return 0; }
    }
    private static int GetRawFoodDecay(Card? card)
    {
        if (card == null) return 0;
        try { return card.decay; }
        catch { }

        try
        {
            var values = CardIntsField?.GetValue(card) as int[];
            if (values != null && values.Length > CardDecayIntIndex)
                return values[CardDecayIntIndex];
        }
        catch { }

        return 0;
    }
    private static float GetFoodRotRatio(Card? card)
    {
        try
        {
            if (!IsFoodCard(card) || ShouldKeepFoodFresh(card)) return 0f;
            var max = card!.MaxDecay;
            if (max <= 0) return 0f;
            var decay = Clamp(GetRawFoodDecay(card), 0, max);
            return Clamp(decay / (float)max, 0f, 1f);
        }
        catch
        {
            return 0f;
        }
    }
    private static string GetFoodRotText(Card card)
    {
        var max = 1000;
        try { max = Math.Max(1, card.MaxDecay); }
        catch { }
        var decay = Clamp(GetRawFoodDecay(card), 0, max);
        var percent = Clamp(decay / (float)max * 100f, 0f, 100f);
        var text = Tr("食物腐烂度: ", "Food rot: ") +
                   percent.ToString("0.#", CultureInfo.InvariantCulture) +
                   "% (" + decay.ToString(CultureInfo.InvariantCulture) +
                   "/" + max.ToString(CultureInfo.InvariantCulture) + ")";
        if (GetFoodDecayRate(card) <= 0)
            text += " " + Tr("无腐烂性质", "No decay property");
        return text;
    }
}
