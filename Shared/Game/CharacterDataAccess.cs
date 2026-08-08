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
    internal static Chara GetTalkingNpc()
    {
        try
        {
            var layer = LayerDrama.Instance;
            var drama = layer?.drama;
            var field = typeof(DramaManager).GetField("TG", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(field.IsStatic ? null : drama) as Chara;
        }
        catch { return null; }
    }
    internal static string SafeName(Chara c)
    {
        try { return c.GetName(NameStyle.Full, 1); }
        catch { return c.ToString(); }
    }
    private static string SafeCharaId(Chara c)
    {
        if (c == null)
            return "";
        try
        {
            var id = c.id;
            if (!string.IsNullOrEmpty(id))
                return id;
        }
        catch { }
        try
        {
            var id = GetField(c, "id") as string;
            if (!string.IsNullOrEmpty(id))
                return id;
        }
        catch { }
        try
        {
            var id = c.c_idRace;
            if (!string.IsNullOrEmpty(id))
                return id;
        }
        catch { }
        return "?";
    }
    private void SetElement(Chara target, string idText, int value, int potential)
    {
        try
        {
            var elements = GetElements(target);
            var e = int.TryParse(idText, out var id) ? elements.SetBase(id, value, potential) : elements.SetBase(idText, value, potential);
            target.Refresh(false);
            _log = $"已设置 {SafeName(target)} {idText} = {value} ({e?.Name ?? "ok"})";
        }
        catch (Exception ex) { _log = $"设置失败 {idText}: {ex.Message}"; }
    }
    private int GetElementBasePotential(Chara target, string idText)
    {
        try
        {
            var element = GetElement(target, idText);
            return element == null ? 0 : element.vPotential;
        }
        catch
        {
            return 0;
        }
    }
    private void SetElementPotential(Chara target, string idText, int displayPotential)
    {
        try
        {
            var element = GetOrCreateElement(target, idText);
            if (element == null)
            {
                _log = "找不到: " + idText;
                return;
            }

            var minPotential = 100;
            try { minPotential = element.MinPotential; } catch { }
            var tempPotential = 0;
            try { tempPotential = element.vTempPotential; } catch { }
            var sourcePotential = 0;
            try { sourcePotential = element.vSourcePotential; } catch { }
            element.vPotential = displayPotential - minPotential - tempPotential - sourcePotential;
            element.OnChangeValue();
            target.Refresh(false);
            _log = $"已设置 {SafeName(target)} {idText} 潜力 = {displayPotential}";
        }
        catch (Exception ex)
        {
            _log = $"设置潜力失败 {idText}: {ex.Message}";
        }
    }
    private void SetFeatElement(Chara target, string idText, int value)
    {
        try
        {
            var elements = GetElements(target);
            var id = int.TryParse(idText, out var n) ? n : elements.GetElement(idText)?.id ?? -1;
            if (id < 0) { _log = "找不到: " + idText; return; }

            if (!CanCreateFeatElement(id))
            {
                SetElement(target, idText, value, GetElementBasePotential(target, idText));
                return;
            }

            var existing = elements.GetElement(id);
            if (existing != null && !(existing is Feat))
            {
                elements.Remove(id);
                existing = null;
            }

            target.SetFeat(id, Math.Max(0, value), false);
            _log = $"已设置 {SafeName(target)} {idText} = {Math.Max(0, value)}";
        }
        catch (Exception ex)
        {
            _log = $"设置专长失败 {idText}: {ex.Message}";
        }
    }
    private static bool CanCreateFeatElement(int id)
    {
        try
        {
            return Element.Create(id, 0) is Feat;
        }
        catch
        {
            return false;
        }
    }
    private void RemoveElement(Chara target, string idText)
    {
        try
        {
            var elements = GetElements(target);
            var id = int.TryParse(idText, out var n) ? n : elements.GetElement(idText)?.id ?? -1;
            if (id < 0) { _log = "找不到: " + idText; return; }
            elements.Remove(id);
            target.Refresh(false);
            _log = "已删除 " + SafeName(target) + " 的 " + idText;
        }
        catch (Exception ex) { _log = ex.Message; }
    }
    private void RemoveRowValue(Chara target, RowDef row)
    {
        if (row.Kind == RowKind.Feat)
            SetFeatElement(target, row.Key, 0);
        else
            RemoveElement(target, row.Key);
    }
    internal static ElementContainer GetElements(Chara target)
    {
        var field = typeof(Card).GetField("elements", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(target) is ElementContainer c) return c;
        throw new MissingFieldException("Card.elements");
    }
    private static int GetElementValue(Chara target, string idText)
    {
        var elements = GetElements(target);
        if (int.TryParse(idText, out var id)) return elements.Value(id);
        var element = elements.GetElement(idText);
        return element?.Value ?? 0;
    }
    private static Element GetElement(Chara target, string idText)
    {
        var elements = GetElements(target);
        return int.TryParse(idText, out var id) ? elements.GetElement(id) : elements.GetElement(idText);
    }
    private static Element GetOrCreateElement(Chara target, string idText)
    {
        var elements = GetElements(target);
        return int.TryParse(idText, out var id) ? elements.GetOrCreateElement(id) : elements.GetOrCreateElement(idText);
    }
    private static int GetCardInt(Chara target, string key)
    {
        if (key == "HP")
            return (int)typeof(Card).GetProperty("hp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target, null);
        return 0;
    }
    private void SetCardInt(Chara target, string key, int value)
    {
        try
        {
            SetCardIntRaw(target, key, value);
            target.Refresh(false);
            _log = $"已设置 {SafeName(target)} {key} = {value}";
        }
        catch (Exception ex) { _log = $"{key} 失败: {ex.Message}"; }
    }
    private static void SetCardIntRaw(Chara target, string key, int value)
    {
        if (key == "HP")
            typeof(Card).GetProperty("hp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value, null);
    }
    private string GetStatObjectValue(Chara target, string name, bool isPc)
    {
        var stats = FindStats(target) ?? (isPc ? FindStats(GameAccess.Runtime.Player) : null);
        var obj = GetFieldObject(stats, name);
        return ReadStatsValue(obj);
    }
    private void SetStatObject(Chara target, string name, int value, bool isPc)
    {
        try
        {
            var stats = FindStats(target) ?? (isPc ? FindStats(GameAccess.Runtime.Player) : null);
            var obj = GetFieldObject(stats, name);
            obj?.GetType().GetMethod("Set", new[] { typeof(int) })?.Invoke(obj, new object[] { value });
            target.Refresh(false);
            _log = $"已设置 {SafeName(target)} {name} = {value}";
        }
        catch (Exception ex) { _log = $"{name} 失败: {ex.Message}"; }
    }
    private string GetCharaStatPropertyValue(Chara target, string name)
    {
        var obj = typeof(Chara).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target, null);
        return ReadStatsValue(obj);
    }
    private void SetCharaStatProperty(Chara target, string name, int value)
    {
        try
        {
            SetCharaStatPropertyRaw(target, name, value);
            target.Refresh(false);
            _log = $"已设置 {SafeName(target)} {name} = {value}";
        }
        catch (Exception ex) { _log = $"{name} 失败: {ex.Message}"; }
    }
    private static void SetCharaStatPropertyRaw(Chara target, string name, int value)
    {
        var obj = typeof(Chara).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target, null);
        obj?.GetType().GetMethod("Set", new[] { typeof(int) })?.Invoke(obj, new object[] { value });
    }
    private static string GetCharaIntPropertyValue(Chara target, string name)
    {
        var prop = typeof(Chara).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var value = prop?.GetValue(target, null);
        return value == null ? "?" : Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }
    private void SetCharaIntProperty(Chara target, string name, int value)
    {
        try
        {
            var prop = typeof(Chara).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null || !prop.CanWrite)
                throw new MissingMemberException(typeof(Chara).Name, name);
            prop.SetValue(target, value, null);
            target.Refresh(false);
            _log = $"已设置 {SafeName(target)} {name} = {value}";
        }
        catch (Exception ex) { _log = $"{name} 失败: {ex.Message}"; }
    }
    private static int GetGeneSlotCount(Chara target)
    {
        try { return target.MaxGeneSlot; }
        catch { return 0; }
    }
    private void SetGeneSlotCount(Chara target, int value)
    {
        try
        {
            var current = target.MaxGeneSlot;
            var adjust = target.Evalue(1242);
            var nextAdjust = adjust + value - current;
            var elements = GetElements(target);
            elements.SetBase(1242, nextAdjust, 100);
            target.Refresh(false);
            _log = T("已设置 ", "Set ") + SafeName(target) + " " + T("基因槽数量", "Gene slots") + " = " + value.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { _log = T("基因槽数量", "Gene slots") + T(" 设置失败: ", " failed: ") + ex.Message; }
    }
    private static string ReadStatsValue(object obj)
    {
        var method = obj?.GetType().GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method != null) return method.Invoke(obj, null)?.ToString() ?? "?";
        var prop = obj?.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return prop?.GetValue(obj, null)?.ToString() ?? "?";
    }
    private static object GetFieldObject(object owner, string name)
    {
        return owner?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(owner);
    }
    private static string GetPlayerField(string name)
    {
        return typeof(Player).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(GameAccess.Runtime.Player)?.ToString() ?? "?";
    }
    private void SetPlayerField(string name, int value)
    {
        try
        {
            typeof(Player).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(GameAccess.Runtime.Player, value);
            _log = $"已设置 {name} = {value}";
        }
        catch (Exception ex) { _log = $"{name} 失败: {ex.Message}"; }
    }
    private static string GetCurrentZoneInfluence()
    {
        try
        {
            var zone = GameAccess.World.CurrentZone;
            return zone != null ? zone.influence.ToString(CultureInfo.InvariantCulture) : "?";
        }
        catch { return "?"; }
    }
    private void SetCurrentZoneInfluence(int value)
    {
        try
        {
            var zone = GameAccess.World.CurrentZone;
            if (zone == null)
            {
                _log = T("影响力设置失败: 未获取到当前区域", "Set influence failed: no current zone");
                return;
            }

            zone.influence = value;
            _log = T("已设置影响力: ", "Set influence: ") + value;
        }
        catch (Exception ex) { _log = T("影响力设置失败: ", "Set influence failed: ") + ex.Message; }
    }
}
