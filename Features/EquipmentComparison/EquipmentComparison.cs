using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed class EquipmentComparisonTooltipFollower : MonoBehaviour
{
    private static EquipmentComparisonTooltipFollower? _active;

    private enum Placement
    {
        Left,
        Right,
        Below,
        Above
    }

    private static readonly Placement[] LeftPreferredPlacements =
    {
        Placement.Right,
        Placement.Left,
        Placement.Below,
        Placement.Above
    };

    private static readonly Placement[] RightPreferredPlacements =
    {
        Placement.Left,
        Placement.Right,
        Placement.Below,
        Placement.Above
    };

    internal UITooltip? Source;
    internal RectTransform[] Targets = Array.Empty<RectTransform>();
    internal Action<int>? CycleSelection;
    internal int SelectionCount;

    private Vector3[] _bestPositions = Array.Empty<Vector3>();
    private readonly Vector3[] _worldCorners = new Vector3[4];
    private int _lastScrollFrame = -1;

    internal static void HandleActiveScroll()
    {
        _active?.HandleScroll();
    }

    internal void RepositionNow()
    {
        Reposition();
    }

    private void OnEnable()
    {
        _active = this;
    }

    private void OnDisable()
    {
        if (ReferenceEquals(_active, this))
            _active = null;
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_active, this))
            _active = null;
    }

    private void HandleScroll()
    {
        if (_lastScrollFrame == Time.frameCount ||
            SelectionCount <= 1 || CycleSelection == null ||
            Source == null || Targets.Length == 0 ||
            !Source.gameObject.activeInHierarchy ||
            Source.cg == null || Source.cg.alpha <= 0.01f)
            return;

        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) <= 0.01f)
            return;

        _lastScrollFrame = Time.frameCount;
        try { GameAccess.Runtime.Core?.ConsumeInput(); }
        catch { }
        try { CycleSelection(scroll > 0f ? -1 : 1); }
        catch { }
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

        if (!HasActiveTarget())
            return;

        if (_bestPositions.Length != Targets.Length)
            _bestPositions = new Vector3[Targets.Length];

        var camera = GetCanvasCamera(sourceRect);
        var safeArea = GetSafeScreenArea(20f);
        var sourceScreenRect = GetScreenBounds(sourceRect, camera, _worldCorners);
        var placements = Input.mousePosition.x < Screen.width * 0.5f
            ? LeftPreferredPlacements
            : RightPreferredPlacements;
        var bestScore = float.PositiveInfinity;
        var foundLayout = false;

        for (var placementIndex = 0; placementIndex < placements.Length; placementIndex++)
        {
            ArrangeTargets(sourceRect, placements[placementIndex], 12f);
            ClampGroupToScreen(Targets, safeArea, camera, _worldCorners);

            if (!TryGetScreenBounds(Targets, camera, _worldCorners, out var groupBounds))
                continue;

            var score = ScoreLayout(
                groupBounds,
                sourceScreenRect,
                safeArea,
                Input.mousePosition,
                placementIndex);
            if (score >= bestScore)
                continue;

            bestScore = score;
            foundLayout = true;
            for (var i = 0; i < Targets.Length; i++)
            {
                if (Targets[i] != null)
                    _bestPositions[i] = Targets[i].localPosition;
            }
        }

        if (!foundLayout)
            return;

        for (var i = 0; i < Targets.Length; i++)
        {
            if (Targets[i] != null)
                Targets[i].localPosition = _bestPositions[i];
        }
    }

    private bool HasActiveTarget()
    {
        for (var i = 0; i < Targets.Length; i++)
        {
            if (Targets[i] != null)
                return true;
        }
        return false;
    }

    private void ArrangeTargets(RectTransform sourceRect, Placement placement, float gap)
    {
        var sourcePosition = sourceRect.localPosition;
        var sourceWidth = sourceRect.rect.width;
        var sourceHeight = sourceRect.rect.height;
        var sourceLeft = sourcePosition.x - sourceRect.pivot.x * sourceWidth;
        var sourceRight = sourcePosition.x + (1f - sourceRect.pivot.x) * sourceWidth;
        var sourceTop = sourcePosition.y + (1f - sourceRect.pivot.y) * sourceHeight;
        var sourceBottom = sourcePosition.y - sourceRect.pivot.y * sourceHeight;

        if (placement == Placement.Left || placement == Placement.Right)
        {
            var cursorX = placement == Placement.Left ? sourceLeft - gap : sourceRight + gap;
            for (var i = 0; i < Targets.Length; i++)
            {
                var target = Targets[i];
                if (target == null)
                    continue;
                var targetLeft = placement == Placement.Left
                    ? cursorX - target.rect.width
                    : cursorX;
                target.localPosition = new Vector3(
                    targetLeft + target.pivot.x * target.rect.width,
                    sourceTop - (1f - target.pivot.y) * target.rect.height,
                    sourcePosition.z);
                cursorX = placement == Placement.Left
                    ? targetLeft - gap
                    : targetLeft + target.rect.width + gap;
            }
            return;
        }

        var groupWidth = GetLocalGroupWidth(gap);
        var groupLeft = sourcePosition.x - groupWidth * 0.5f;
        for (var i = 0; i < Targets.Length; i++)
        {
            var target = Targets[i];
            if (target == null)
                continue;

            var height = target.rect.height;
            var targetY = placement == Placement.Above
                ? sourceTop + gap + target.pivot.y * height
                : sourceBottom - gap - (1f - target.pivot.y) * height;
            target.localPosition = new Vector3(
                groupLeft + target.pivot.x * target.rect.width,
                targetY,
                sourcePosition.z);
            groupLeft += target.rect.width + gap;
        }
    }

    private float GetLocalGroupWidth(float gap)
    {
        var width = 0f;
        var count = 0;
        for (var i = 0; i < Targets.Length; i++)
        {
            if (Targets[i] == null)
                continue;
            width += Targets[i].rect.width;
            count++;
        }
        return width + Mathf.Max(0, count - 1) * gap;
    }

    private static Camera? GetCanvasCamera(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;
    }

    private static Rect GetSafeScreenArea(float margin)
    {
        var safeArea = Screen.safeArea;
        if (safeArea.width <= 0f || safeArea.height <= 0f)
            safeArea = new Rect(0f, 0f, Screen.width, Screen.height);

        var horizontalMargin = Mathf.Min(margin, safeArea.width * 0.25f);
        var verticalMargin = Mathf.Min(margin, safeArea.height * 0.25f);
        return Rect.MinMaxRect(
            safeArea.xMin + horizontalMargin,
            safeArea.yMin + verticalMargin,
            safeArea.xMax - horizontalMargin,
            safeArea.yMax - verticalMargin);
    }

    private static Rect GetScreenBounds(
        RectTransform target,
        Camera? camera,
        Vector3[] corners)
    {
        target.GetWorldCorners(corners);
        var first = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var minX = first.x;
        var minY = first.y;
        var maxX = first.x;
        var maxY = first.y;
        for (var i = 1; i < corners.Length; i++)
        {
            var point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            minX = Mathf.Min(minX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxX = Mathf.Max(maxX, point.x);
            maxY = Mathf.Max(maxY, point.y);
        }
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static bool TryGetScreenBounds(
        RectTransform[] targets,
        Camera? camera,
        Vector3[] corners,
        out Rect bounds)
    {
        var hasBounds = false;
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        for (var i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (target == null)
                continue;
            var targetBounds = GetScreenBounds(target, camera, corners);
            minX = Mathf.Min(minX, targetBounds.xMin);
            minY = Mathf.Min(minY, targetBounds.yMin);
            maxX = Mathf.Max(maxX, targetBounds.xMax);
            maxY = Mathf.Max(maxY, targetBounds.yMax);
            hasBounds = true;
        }

        bounds = hasBounds
            ? Rect.MinMaxRect(minX, minY, maxX, maxY)
            : default;
        return hasBounds;
    }

    private static void ClampGroupToScreen(
        RectTransform[] targets,
        Rect safeArea,
        Camera? camera,
        Vector3[] corners)
    {
        if (!TryGetScreenBounds(targets, camera, corners, out var bounds))
            return;

        var deltaX = bounds.width > safeArea.width
            ? safeArea.center.x - bounds.center.x
            : bounds.xMin < safeArea.xMin
                ? safeArea.xMin - bounds.xMin
                : bounds.xMax > safeArea.xMax
                    ? safeArea.xMax - bounds.xMax
                    : 0f;
        var deltaY = bounds.height > safeArea.height
            ? safeArea.center.y - bounds.center.y
            : bounds.yMin < safeArea.yMin
                ? safeArea.yMin - bounds.yMin
                : bounds.yMax > safeArea.yMax
                    ? safeArea.yMax - bounds.yMax
                    : 0f;
        if (Mathf.Abs(deltaX) < 0.01f && Mathf.Abs(deltaY) < 0.01f)
            return;

        RectTransform? first = null;
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
            {
                first = targets[i];
                break;
            }
        }
        if (first == null)
            return;

        var localDelta = ScreenDeltaToLocal(first, new Vector2(deltaX, deltaY), camera);
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
                targets[i].localPosition += localDelta;
        }
    }

    private static Vector3 ScreenDeltaToLocal(
        RectTransform target,
        Vector2 screenDelta,
        Camera? camera)
    {
        var parent = target.parent as RectTransform;
        if (parent == null)
            return new Vector3(screenDelta.x, screenDelta.y, 0f);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, Vector2.zero, camera, out var origin) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, screenDelta, camera, out var destination))
            return parent.InverseTransformVector(new Vector3(screenDelta.x, screenDelta.y, 0f));

        return new Vector3(destination.x - origin.x, destination.y - origin.y, 0f);
    }

    private static float ScoreLayout(
        Rect groupBounds,
        Rect sourceBounds,
        Rect safeArea,
        Vector2 pointer,
        int preferenceIndex)
    {
        var pointerBounds = new Rect(pointer.x - 18f, pointer.y - 18f, 36f, 36f);
        var outsideArea = Mathf.Max(0f, Area(groupBounds) - IntersectionArea(groupBounds, safeArea));
        var sourceOverlap = IntersectionArea(groupBounds, sourceBounds);
        var pointerOverlap = IntersectionArea(groupBounds, pointerBounds);
        var distance = Vector2.Distance(groupBounds.center, sourceBounds.center);
        return outsideArea * 10000f +
               sourceOverlap * 12000f +
               pointerOverlap * 4000f +
               preferenceIndex * 10f +
               distance * 0.001f;
    }

    private static float Area(Rect rect)
    {
        return Mathf.Max(0f, rect.width) * Mathf.Max(0f, rect.height);
    }

    private static float IntersectionArea(Rect a, Rect b)
    {
        var width = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
        var height = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
        return width * height;
    }
}

public sealed partial class ElinModifierPlugin
{
    private static readonly FieldInfo? EquipmentComparisonTooltipDataField =
        AccessTools.Field(typeof(UIButton), "tooltip");

    [HarmonyPatch(typeof(Core), "Update")]
    private static class CoreUpdateEquipmentComparisonInputPatch
    {
        private static void Prefix()
        {
            EquipmentComparisonTooltipFollower.HandleActiveScroll();
        }
    }

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

            var comparisonObject = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
            comparisonObject.name = "ElinModifier.EquipmentComparisonTooltip";
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
            comparison.enabled = false;
            if (comparison.cg != null)
            {
                comparison.cg.alpha = 1f;
                comparison.cg.interactable = false;
                comparison.cg.blocksRaycasts = false;
            }

            var selectedIndex = 0;
            RenderEquipmentComparisonTooltip(
                comparison,
                target,
                equippedThings[selectedIndex],
                selectedIndex,
                equippedThings.Count);

            var follower = target.gameObject.AddComponent<EquipmentComparisonTooltipFollower>();
            follower.Source = source;
            follower.Targets = new[] { target };
            follower.SelectionCount = equippedThings.Count;
            follower.CycleSelection = step =>
            {
                selectedIndex = (selectedIndex + step) % equippedThings.Count;
                if (selectedIndex < 0)
                    selectedIndex += equippedThings.Count;
                RenderEquipmentComparisonTooltip(
                    comparison,
                    target,
                    equippedThings[selectedIndex],
                    selectedIndex,
                    equippedThings.Count);
            };
            follower.RepositionNow();
        }
        catch
        {
            DestroyEquipmentComparisonTooltip();
        }
    }

    private static void RenderEquipmentComparisonTooltip(
        UITooltip comparison,
        RectTransform target,
        Thing equipped,
        int selectedIndex,
        int totalCount)
    {
        equipped.WriteNote(comparison.note, null, default(IInspect.NoteMode), null);
        comparison.note.Space(0, 1);
        var equippedLabel = Instance?.T("（装备中）", "(Equipped)") ?? "（装备中）";
        if (totalCount > 1)
        {
            equippedLabel += " [" + (selectedIndex + 1) + "/" + totalCount + "]";
            equippedLabel += " " + (Instance?.T("（滚轮切换）", "(Scroll to switch)") ?? "（滚轮切换）");
        }

        comparison.note.AddText(equippedLabel, FontColor.Flavor);

        LayoutRebuilder.ForceRebuildLayoutImmediate(target);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(target);
        Canvas.ForceUpdateCanvases();
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
