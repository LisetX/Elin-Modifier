using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed class ThreatMoveCellGraphic : MaskableGraphic
{
    private readonly Vector2[] _points = new Vector2[4];
    private bool _hasQuad;

    internal void SetQuad(Vector2 left, Vector2 top, Vector2 right, Vector2 bottom)
    {
        var changed = !_hasQuad ||
                      !Approximately(_points[0], left) ||
                      !Approximately(_points[1], top) ||
                      !Approximately(_points[2], right) ||
                      !Approximately(_points[3], bottom);
        if (!changed)
            return;

        _points[0] = left;
        _points[1] = top;
        _points[2] = right;
        _points[3] = bottom;
        _hasQuad = true;
        SetVerticesDirty();
    }

    internal void ClearQuad()
    {
        if (!_hasQuad)
            return;
        _hasQuad = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!_hasQuad)
            return;

        var fill = new Color32(255, 20, 20, 74);
        var border = new Color32(255, 32, 24, 235);
        vh.AddVert(_points[0], fill, Vector2.zero);
        vh.AddVert(_points[1], fill, Vector2.zero);
        vh.AddVert(_points[2], fill, Vector2.zero);
        vh.AddVert(_points[3], fill, Vector2.zero);
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);

        AddEdge(vh, _points[0], _points[1], 2.4f, border);
        AddEdge(vh, _points[1], _points[2], 2.4f, border);
        AddEdge(vh, _points[2], _points[3], 2.4f, border);
        AddEdge(vh, _points[3], _points[0], 2.4f, border);
    }

    private static void AddEdge(VertexHelper vh, Vector2 from, Vector2 to, float width, Color32 color)
    {
        var direction = to - from;
        if (direction.sqrMagnitude <= 0.0001f)
            return;
        var normal = new Vector2(-direction.y, direction.x).normalized * (width * 0.5f);
        var start = vh.currentVertCount;
        vh.AddVert(from - normal, color, Vector2.zero);
        vh.AddVert(from + normal, color, Vector2.zero);
        vh.AddVert(to + normal, color, Vector2.zero);
        vh.AddVert(to - normal, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static bool Approximately(Vector2 left, Vector2 right)
    {
        return (left - right).sqrMagnitude <= 0.0001f;
    }
}

[HarmonyPatch(
    typeof(Chara),
    "TryMove",
    new[] { typeof(Point), typeof(bool) })]
internal static class ThreatOverlayConfirmedMovePatch
{
    private static void Prefix(Chara __instance, out Point __state)
    {
        try
        {
            __state = __instance?.pos == null
                ? Point.Invalid
                : new Point(__instance.pos.x, __instance.pos.z);
        }
        catch
        {
            __state = Point.Invalid;
        }
    }

    private static void Postfix(
        Chara __instance,
        Point __0,
        Card.MoveResult __result,
        Point __state)
    {
        try
        {
            var module = ElinModifierPlugin.ActiveModules?.ThreatOverlay;
            module?.RecordResolvedPlayerMove(__instance, __state, __result);
            module?.RecordConfirmedMove(__instance, __0, __result);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(Act),
    "Perform",
    new[] { typeof(Chara), typeof(Card), typeof(Point) })]
internal static class ThreatOverlayConfirmedActPatch
{
    private static void Postfix(Act __instance, Chara __0, bool __result)
    {
        try
        {
            var module = ElinModifierPlugin.ActiveModules?.ThreatOverlay;
            module?.RecordResolvedPlayerAct(__0, __result);
            module?.RecordConfirmedAct(__0, __instance, __result);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(Chara),
    "UseAbility",
    new[] { typeof(Act), typeof(Card), typeof(Point), typeof(bool) })]
internal static class ThreatOverlayConfirmedAbilityPatch
{
    private static void Postfix(Chara __instance, Act __0, bool __result)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordConfirmedAbility(__instance, __0, __result);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(AIAct), "SetChild")]
internal static class ThreatOverlayAiChildSelectionPatch
{
    private static void Postfix(AIAct __instance, AIAct __0)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordSelectedAiAction(__instance, __0);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(Chara),
    "SetAI",
    new[] { typeof(AIAct) })]
internal static class ThreatOverlayAiGoalSelectionPatch
{
    private static void Postfix(Chara __instance, AIAct __0)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordAiGoalChanged(__instance, __0);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(GoalCombat),
    "TryUseAbility",
    new[] { typeof(int), typeof(bool) })]
internal static class ThreatOverlayCombatEvaluationPatch
{
    private static void Postfix(GoalCombat __instance)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordCombatEvaluation(__instance);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(
    typeof(Chara),
    "SetEnemy",
    new[] { typeof(Chara) })]
internal static class ThreatOverlayTargetChangedPatch
{
    private static void Postfix(Chara __instance)
    {
        try
        {
            ElinModifierPlugin.ActiveModules?.ThreatOverlay.RecordThreatTargetChanged(__instance);
        }
        catch
        {
        }
    }
}

