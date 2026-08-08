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
    private static Camera? GetSceneCamera()
    {
        try
        {
            var scene = GameAccess.Ui.Scene;
            if (scene != null && scene.cam != null)
                return scene.cam;
        }
        catch
        {
        }

        try { return Camera.main; }
        catch { return null; }
    }
    private static bool IsHostileThreat(Chara? chara, Chara pc)
    {
        if (chara == null || ReferenceEquals(chara, pc))
            return false;

        try
        {
            if (chara.isDestroyed || chara.IsPC || chara.IsPCParty || chara.IsPCFaction)
                return false;
            if (chara.IsDeadOrSleeping || chara.IsDisabled)
                return false;
            if (chara.hostility == Hostility.Enemy)
                return true;
            if (ReferenceEquals(chara.enemy, pc) || ReferenceEquals(pc.enemy, chara))
                return true;
            return chara.IsHostile() || pc.IsHostile(chara);
        }
        catch
        {
            return false;
        }
    }
    private static bool CanPlayerCurrentlySee(Chara pc, Chara chara)
    {
        try
        {
            return pc.CanSee(chara);
        }
        catch
        {
            return true;
        }
    }
    private static bool TryGetCharaScreenRect(Chara chara, Camera cam, out Rect rect)
    {
        rect = default;
        try
        {
            var cardRenderer = chara.renderer;
            if (cardRenderer == null || cardRenderer.skip)
                return false;

            var actor = cardRenderer.actor;
            Bounds bounds;
            var hasBounds = false;
            if (actor != null)
            {
                var sr = actor.sr;
                if (sr != null && sr.enabled)
                {
                    bounds = sr.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds = default;
                }

                var sr2 = actor.sr2;
                if (sr2 != null && sr2.enabled)
                {
                    if (hasBounds)
                        bounds.Encapsulate(sr2.bounds);
                    else
                    {
                        bounds = sr2.bounds;
                        hasBounds = true;
                    }
                }

                if (hasBounds && TryBoundsToScreenRect(bounds, cam, out rect))
                {
                    TightenMarkerRect(ref rect, 0.78f, 0.96f);
                    return true;
                }
            }

            return TryApproxCharaScreenRect(cardRenderer, cam, out rect);
        }
        catch
        {
            rect = default;
            return false;
        }
    }
    private static bool TryBoundsToScreenRect(Bounds bounds, Camera cam, out Rect rect)
    {
        rect = default;
        var min = bounds.min;
        var max = bounds.max;
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var count = 0;
        AccumulateBoundsScreenPoint(new Vector3(min.x, min.y, min.z), cam, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateBoundsScreenPoint(new Vector3(min.x, min.y, max.z), cam, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateBoundsScreenPoint(new Vector3(min.x, max.y, min.z), cam, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateBoundsScreenPoint(new Vector3(min.x, max.y, max.z), cam, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateBoundsScreenPoint(new Vector3(max.x, min.y, min.z), cam, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateBoundsScreenPoint(new Vector3(max.x, min.y, max.z), cam, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateBoundsScreenPoint(new Vector3(max.x, max.y, min.z), cam, ref minX, ref minY, ref maxX, ref maxY, ref count);
        AccumulateBoundsScreenPoint(new Vector3(max.x, max.y, max.z), cam, ref minX, ref minY, ref maxX, ref maxY, ref count);

        if (count == 0)
            return false;
        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return NormalizeMarkerRect(ref rect);
    }
    private static void AccumulateBoundsScreenPoint(Vector3 point, Camera cam, ref float minX, ref float minY, ref float maxX, ref float maxY, ref int count)
    {
        var screen = cam.WorldToScreenPoint(point);
        if (screen.z <= 0f)
            return;
        var guiY = Screen.height - screen.y;
        minX = Mathf.Min(minX, screen.x);
        maxX = Mathf.Max(maxX, screen.x);
        minY = Mathf.Min(minY, guiY);
        maxY = Mathf.Max(maxY, guiY);
        count++;
    }
    private static bool TryApproxCharaScreenRect(CardRenderer cardRenderer, Camera cam, out Rect rect)
    {
        rect = default;
        var center = cardRenderer.PositionCenter();
        var basePos = cardRenderer.position;
        var top = center + Vector3.up * 0.55f;
        var bottom = basePos + Vector3.down * 0.1f;
        var left = center + Vector3.left * 0.45f;
        var right = center + Vector3.right * 0.45f;

        var sTop = cam.WorldToScreenPoint(top);
        var sBottom = cam.WorldToScreenPoint(bottom);
        var sLeft = cam.WorldToScreenPoint(left);
        var sRight = cam.WorldToScreenPoint(right);
        if (sTop.z <= 0f || sBottom.z <= 0f)
            return false;

        var minX = Mathf.Min(sLeft.x, sRight.x);
        var maxX = Mathf.Max(sLeft.x, sRight.x);
        var minY = Mathf.Min(Screen.height - sTop.y, Screen.height - sBottom.y);
        var maxY = Mathf.Max(Screen.height - sTop.y, Screen.height - sBottom.y);
        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        if (!NormalizeMarkerRect(ref rect))
            return false;
        TightenMarkerRect(ref rect, 0.82f, 0.98f);
        return true;
    }
    private static bool NormalizeMarkerRect(ref Rect rect)
    {
        if (rect.width < 2f || rect.height < 2f)
            return false;

        var center = rect.center;
        var width = Mathf.Clamp(rect.width, 6f, 160f);
        var height = Mathf.Clamp(rect.height, 10f, 190f);
        rect = new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);

        if (rect.xMax < 0f || rect.xMin > Screen.width || rect.yMax < 0f || rect.yMin > Screen.height)
            return false;

        var xMin = Mathf.Clamp(rect.xMin, 0f, Screen.width);
        var yMin = Mathf.Clamp(rect.yMin, 0f, Screen.height);
        var xMax = Mathf.Clamp(rect.xMax, 0f, Screen.width);
        var yMax = Mathf.Clamp(rect.yMax, 0f, Screen.height);
        rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return rect.width >= 8f && rect.height >= 8f;
    }
    private static void TightenMarkerRect(ref Rect rect, float widthScale, float heightScale)
    {
        widthScale = Mathf.Clamp(widthScale, 0.2f, 1f);
        heightScale = Mathf.Clamp(heightScale, 0.2f, 1f);
        var center = rect.center;
        var width = Mathf.Max(8f, rect.width * widthScale);
        var height = Mathf.Max(12f, rect.height * heightScale);
        rect = new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);
    }
    private static Rect ExpandRect(Rect rect, float amount)
    {
        return new Rect(rect.xMin - amount, rect.yMin - amount, rect.width + amount * 2f, rect.height + amount * 2f);
    }
    private static Rect GetThreatHealthBarRect(Rect targetRect)
    {
        var width = Mathf.Clamp(targetRect.width + 8f, 36f, 110f);
        var height = 6f;
        var x = Mathf.Clamp(targetRect.center.x - width * 0.5f, 2f, Mathf.Max(2f, Screen.width - width - 2f));
        var y = Mathf.Clamp(targetRect.yMin - height - 6f, 2f, Mathf.Max(2f, Screen.height - height - 2f));
        return new Rect(x, y, width, height);
    }
    private static float GetThreatHpRatio(Chara chara)
    {
        try
        {
            var maxHp = Mathf.Max(1, chara.MaxHP);
            return Mathf.Clamp01((float)chara.hp / maxHp);
        }
        catch
        {
            return 1f;
        }
    }
    private static string GetThreatLevelText(Chara chara)
    {
        try
        {
            return "Lv. " + Math.Max(1, chara.LV).ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }
}
