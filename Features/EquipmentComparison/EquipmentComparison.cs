using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed class EquipmentComparisonTooltipFollower : MonoBehaviour
{
    internal UITooltip? Source;
    internal RectTransform[] Targets = Array.Empty<RectTransform>();

    internal void RepositionNow()
    {
        Reposition();
    }

    private void LateUpdate()
    {
        if (Source == null || Targets.Length == 0 ||
            !Source.gameObject.activeInHierarchy ||
            Source.cg == null || Source.cg.alpha <= 0.01f)
        {
            for (var i = 0; i < Targets.Length; i++)
            {
                var target = Targets[i];
                if (target == null)
                    continue;
                target.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(target.gameObject);
            }
            enabled = false;
            return;
        }

        Reposition();
    }

    private void Reposition()
    {
        if (Source == null || Targets.Length == 0)
            return;

        var sourceRect = Source.transform as RectTransform;
        if (sourceRect == null)
            return;

        const float gap = 12f;
        var sourcePosition = sourceRect.localPosition;
        var sourceWidth = sourceRect.rect.width;
        var sourceHeight = sourceRect.rect.height;
        var sourceLeft = sourcePosition.x - sourceRect.pivot.x * sourceWidth;
        var sourceRight = sourcePosition.x + (1f - sourceRect.pivot.x) * sourceWidth;
        var sourceTop = sourcePosition.y + (1f - sourceRect.pivot.y) * sourceHeight;

        var activeCount = 0;
        for (var i = 0; i < Targets.Length; i++)
        {
            var target = Targets[i];
            if (target == null)
                continue;
            activeCount++;
        }
        if (activeCount == 0)
            return;

        var direction = GetPreferredDirection();
        var cursorX = direction == 0 ? sourceLeft - gap : sourceRight + gap;
        for (var i = 0; i < Targets.Length; i++)
        {
            var target = Targets[i];
            if (target == null)
                continue;
            var targetLeft = direction == 0 ? cursorX - target.rect.width : cursorX;
            target.localPosition = new Vector3(
                targetLeft + target.pivot.x * target.rect.width,
                sourceTop - (1f - target.pivot.y) * target.rect.height,
                sourcePosition.z);
            cursorX = direction == 0
                ? targetLeft - gap
                : targetLeft + target.rect.width + gap;
        }

        ClampGroupToScreen(Targets, 20f);
    }

    private static int GetPreferredDirection()
    {
        return Input.mousePosition.x < Screen.width * 0.5f ? 1 : 0;
    }

    private static void ClampGroupToScreen(RectTransform[] targets, float margin)
    {
        var hasBounds = false;
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var corners = new Vector3[4];
        RectTransform? first = null;
        for (var i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (target == null)
                continue;
            first ??= target;
            target.GetWorldCorners(corners);
            for (var cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                minX = Mathf.Min(minX, corners[cornerIndex].x);
                minY = Mathf.Min(minY, corners[cornerIndex].y);
                maxX = Mathf.Max(maxX, corners[cornerIndex].x);
                maxY = Mathf.Max(maxY, corners[cornerIndex].y);
            }
            hasBounds = true;
        }
        if (!hasBounds || first == null)
            return;

        var deltaX = minX < margin
            ? margin - minX
            : maxX > Screen.width - margin ? Screen.width - margin - maxX : 0f;
        var deltaY = minY < margin
            ? margin - minY
            : maxY > Screen.height - margin ? Screen.height - margin - maxY : 0f;
        if (Mathf.Abs(deltaX) < 0.01f && Mathf.Abs(deltaY) < 0.01f)
            return;

        var parent = first.parent;
        var localDelta = parent == null
            ? new Vector3(deltaX, deltaY, 0f)
            : parent.InverseTransformVector(new Vector3(deltaX, deltaY, 0f));
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
                targets[i].localPosition += localDelta;
        }
    }
}

public sealed partial class ElinModifierPlugin
{
    private static readonly FieldInfo? EquipmentComparisonTooltipDataField =
        AccessTools.Field(typeof(UIButton), "tooltip");

    [HarmonyPatch]
    private static class ButtonGridSetCardEquipmentComparisonPatch
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var stable = typeof(ButtonGrid).GetMethod(
                "SetCard", flags, null,
                new[] { typeof(Card), typeof(ButtonGrid.Mode), typeof(Action<UINote>) }, null);
            if (stable != null)
                return stable;

            var methods = typeof(ButtonGrid).GetMethods(flags);
            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                var parameters = method.GetParameters();
                if (string.Equals(method.Name, "SetCard", StringComparison.Ordinal) &&
                    parameters.Length > 0 && parameters[0].ParameterType == typeof(Card))
                    return method;
            }

            throw new MissingMethodException("ButtonGrid.SetCard compatible signature was not found.");
        }

        private static void Postfix(ButtonGrid __instance, Card __0)
        {
            try
            {
                if (EquipmentComparisonTooltipDataField?.GetValue(__instance) is not TooltipData data ||
                    data.onShowTooltip == null)
                    return;

                var original = data.onShowTooltip;
                var candidate = __0 as Thing;
                data.onShowTooltip = tooltip =>
                {
                    original(tooltip);
                    try { RefreshEquipmentComparisonTooltip(tooltip, candidate); }
                    catch { DestroyEquipmentComparisonTooltip(); }
                };
            }
            catch
            {
                DestroyEquipmentComparisonTooltip();
            }
        }
    }

    [HarmonyPatch(typeof(TooltipManager), "HideTooltips")]
    private static class TooltipManagerHideEquipmentComparisonPatch
    {
        private static void Postfix()
        {
            DestroyEquipmentComparisonTooltip();
        }
    }

    private static void RefreshEquipmentComparisonTooltip(UITooltip source, Thing? candidate)
    {
        DestroyEquipmentComparisonTooltip();

        if (Instance == null || !Instance._equipmentComparison || source == null ||
            candidate == null || candidate.isDestroyed || candidate.isEquipped ||
            candidate.c_equippedSlot != 0 || !candidate.IsEquipmentOrRangedOrAmmo)
            return;

        var equippedThings = FindEquipmentComparisonThings(candidate);
        if (equippedThings.Count == 0)
            return;

        try
        {
            var parent = source.transform.parent;
            if (parent == null)
                return;

            var targets = new List<RectTransform>(equippedThings.Count);
            for (var i = 0; i < equippedThings.Count; i++)
            {
                var comparisonObject = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
                comparisonObject.name = "ElinModifier.EquipmentComparisonTooltip." + i;
                comparisonObject.transform.SetAsLastSibling();
                _equipmentComparisonTooltips.Add(comparisonObject);

                var comparison = comparisonObject.GetComponent<UITooltip>();
                var target = comparisonObject.transform as RectTransform;
                if (comparison == null || comparison.note == null || target == null)
                {
                    DestroyEquipmentComparisonTooltip();
                    return;
                }

                comparison.data = null;
                comparison.hideFunc = null;
                comparison.follow = false;
                comparison.followType = UITooltip.FollowType.None;
                comparison.delayHide = false;
                if (comparison.cg != null)
                {
                    comparison.cg.alpha = 1f;
                    comparison.cg.interactable = false;
                    comparison.cg.blocksRaycasts = false;
                }

                equippedThings[i].WriteNote(comparison.note, null, default(IInspect.NoteMode), null);
                comparison.note.Space(0, 1);
                comparison.note.AddText(
                    Instance?.T("（装备中）", "(Equipped)") ?? "（装备中）",
                    FontColor.Flavor);

                LayoutRebuilder.ForceRebuildLayoutImmediate(target);
                comparison.enabled = false;
                targets.Add(target);
            }

            Canvas.ForceUpdateCanvases();
            for (var i = 0; i < targets.Count; i++)
                LayoutRebuilder.ForceRebuildLayoutImmediate(targets[i]);
            Canvas.ForceUpdateCanvases();

            var follower = targets[0].gameObject.AddComponent<EquipmentComparisonTooltipFollower>();
            follower.Source = source;
            follower.Targets = targets.ToArray();
            follower.RepositionNow();
        }
        catch
        {
            DestroyEquipmentComparisonTooltip();
        }
    }

    private static List<Thing> FindEquipmentComparisonThings(Thing candidate)
    {
        var matches = new List<Thing>();
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            var body = pc?.body;
            var slots = body?.slots;
            var category = candidate.category;
            var slotId = category?.slot ?? 0;
            if (slots == null || slotId <= 0)
                return matches;

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var equipped = slot?.thing;
                if (slot == null || slot.elementId != slotId || equipped == null ||
                    equipped == candidate || equipped.isDestroyed ||
                    !IsSameEquipmentComparisonType(candidate, equipped, slotId))
                    continue;

                var duplicate = false;
                for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
                {
                    if (ReferenceEquals(matches[matchIndex], equipped))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate)
                    continue;

                if (slot == body.slotMainHand || slot == body.slotRange)
                    matches.Insert(0, equipped);
                else
                    matches.Add(equipped);
            }
        }
        catch
        {
            matches.Clear();
        }
        return matches;
    }

    private static bool IsSameEquipmentComparisonType(Thing candidate, Thing equipped, int slotId)
    {
        try
        {
            if (equipped.category == null || equipped.category.slot != slotId)
                return false;

            if (slotId != 35)
                return true;

            var candidateShield = candidate.category.IsChildOf("shield");
            var equippedShield = equipped.category.IsChildOf("shield");
            if (candidateShield || equippedShield)
                return candidateShield == equippedShield;

            if (candidate.IsWeapon || equipped.IsWeapon)
                return candidate.IsWeapon == equipped.IsWeapon &&
                       candidate.IsRangedWeapon == equipped.IsRangedWeapon;

            return string.Equals(candidate.category.id, equipped.category.id, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void DestroyEquipmentComparisonTooltip()
    {
        try
        {
            for (var i = 0; i < _equipmentComparisonTooltips.Count; i++)
            {
                var comparisonObject = _equipmentComparisonTooltips[i];
                if (comparisonObject == null)
                    continue;
                comparisonObject.SetActive(false);
                UnityEngine.Object.Destroy(comparisonObject);
            }
        }
        catch { }
        finally
        {
            _equipmentComparisonTooltips.Clear();
        }
    }
}
