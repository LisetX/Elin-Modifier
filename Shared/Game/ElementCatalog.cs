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
    internal static IEnumerable<object> EnumerateSourceElementRows()
    {
        var source = GameAccess.Sources.Elements;
        if (source == null) yield break;
        for (var t = source.GetType(); t != null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!(field.GetValue(source) is IEnumerable items)) continue;
                foreach (var item in items)
                    if (item != null && item.GetType().FullName == "SourceElement+Row")
                        yield return item;
            }
        }
    }
    private static string GetDisplayName(object row)
    {
        foreach (var name in new[] { "name_L", "name", "altname_L", "altname", "name_JP" })
        {
            var value = GetString(row, name);
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return GetString(row, "alias");
    }
    private static string GetElementDisplayName(object row)
    {
        var moldedName = GetMoldedElementDisplayName(row);
        if (!string.IsNullOrEmpty(moldedName))
            return moldedName;

        return GetNativeElementDisplayName(row);
    }
    internal static string GetNativeElementDisplayName(object row)
    {
        try
        {
            var method = row.GetType().GetMethod("GetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method != null && method.ReturnType == typeof(string))
            {
                var value = CleanDisplayName(method.Invoke(row, null) as string ?? "");
                if (!string.IsNullOrEmpty(value) && !value.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    return value;
            }
        }
        catch { }

        return GetDisplayName(row);
    }
    private static string GetMoldedElementDisplayName(object row)
    {
        var idMold = GetInt(row, "idMold");
        var aliasRef = GetString(row, "aliasRef");
        if (idMold <= 0 || string.IsNullOrEmpty(aliasRef))
            return "";

        var moldRow = FindSourceElementRowById(idMold);
        var refRow = FindSourceElementRowByAlias(aliasRef);
        var moldName = moldRow == null ? GetNativeElementDisplayName(row) : GetNativeElementDisplayName(moldRow);
        var refName = refRow == null ? aliasRef : GetElementReferenceDisplayName(refRow);
        return CombineElementAbilityName(refName, moldName);
    }
    private static string GetAbilityNameQualifier(AbilityDef ability)
    {
        var aliasRef = GetString(ability.Source, "aliasRef");
        if (!string.IsNullOrEmpty(aliasRef) && !string.Equals(aliasRef, "mold", StringComparison.OrdinalIgnoreCase))
        {
            var refRow = FindSourceElementRowByAlias(aliasRef);
            var refName = refRow == null ? aliasRef : GetElementReferenceDisplayName(refRow);
            if (!string.IsNullOrEmpty(refName))
                return refName;
        }

        var abilityTypes = GetStringArray(ability.Source, "abilityType");
        if (abilityTypes.Length > 0 && !string.IsNullOrEmpty(abilityTypes[0]))
            return abilityTypes[0];

        if (!string.IsNullOrEmpty(ability.Category))
            return ability.Category;

        return ability.Alias;
    }
    private static string GetElementReferenceDisplayName(object row)
    {
        var altName = GetElementAltName(row);
        if (!string.IsNullOrEmpty(altName) && !altName.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            return altName;

        return GetNativeElementDisplayName(row);
    }
    private static string GetElementAltName(object row)
    {
        try
        {
            var method = row.GetType().GetMethod("GetAltname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            if (method != null && method.ReturnType == typeof(string))
                return CleanDisplayName(method.Invoke(row, new object[] { 0 }) as string ?? "");
        }
        catch { }

        foreach (var name in new[] { "altname_L", "altname", "altname_JP" })
        {
            var value = CleanDisplayName(GetString(row, name));
            if (!string.IsNullOrEmpty(value))
                return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        }

        return "";
    }
    private static string CombineElementAbilityName(string elementName, string abilityName)
    {
        elementName = CleanDisplayName(elementName);
        abilityName = CleanDisplayName(abilityName);
        if (string.IsNullOrEmpty(elementName)) return abilityName;
        if (string.IsNullOrEmpty(abilityName)) return elementName;
        if (abilityName.IndexOf(elementName, StringComparison.OrdinalIgnoreCase) >= 0)
            return abilityName;

        return IsAsciiText(elementName) && IsAsciiText(abilityName)
            ? elementName + " " + abilityName
            : elementName + abilityName;
    }
    private static bool IsAsciiText(string text)
    {
        foreach (var ch in text)
            if (ch > 127)
                return false;
        return true;
    }
    internal static object FindSourceElementRowById(int id)
    {
        foreach (var row in EnumerateSourceElementRows())
            if (GetInt(row, "id") == id)
                return row;
        return null;
    }
    internal static object FindSourceElementRowByAlias(string alias)
    {
        if (string.IsNullOrEmpty(alias)) return null;
        foreach (var row in EnumerateSourceElementRows())
            if (string.Equals(GetString(row, "alias"), alias, StringComparison.OrdinalIgnoreCase))
                return row;
        return null;
    }
    internal static bool TextHas(string text, string value)
    {
        return !string.IsNullOrEmpty(text) && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    internal static int GetInt(object row, string name)
    {
        return GetField(row, name) is int i ? i : 0;
    }
    private static bool GetBool(object row, string name)
    {
        return GetMemberValue(row, name) is bool b && b;
    }
    internal static string GetString(object row, string name)
    {
        return GetMemberValue(row, name) as string ?? "";
    }
    internal static string[] GetStringArray(object row, string name)
    {
        return GetMemberValue(row, name) as string[] ?? Array.Empty<string>();
    }
    private static int[] GetIntArray(object row, string name)
    {
        return GetMemberValue(row, name) as int[] ?? Array.Empty<int>();
    }
    internal static object GetMemberValue(object row, string name)
    {
        var value = GetField(row, name);
        if (value != null)
            return value;
        if (row == null) return null;
        for (var t = row.GetType(); t != null; t = t.BaseType)
        {
            var property = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try { return property.GetValue(row, null); }
                catch { return null; }
            }
        }
        return null;
    }
    private static object GetField(object row, string name)
    {
        if (row == null) return null;
        for (var t = row.GetType(); t != null; t = t.BaseType)
        {
            var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(row);
        }
        return null;
    }
    private static void SetField(object row, string name, object value)
    {
        if (row == null) return;
        for (var t = row.GetType(); t != null; t = t.BaseType)
        {
            var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(row, value);
            return;
        }
    }
}
