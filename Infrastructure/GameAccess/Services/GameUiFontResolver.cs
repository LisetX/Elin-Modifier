using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

internal static class GameUiFontResolver
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    internal static Font? ResolveCurrentUiFont()
    {
        try
        {
            var skinManagerType = LoadedAssemblyTypeResolver.ResolveExact("SkinManager", "Plugins.UI") ??
                                  LoadedAssemblyTypeResolver.ResolveExact("SkinManager");
            var manager = ReadStaticValue(skinManagerType, "Instance", "_Instance");
            if (manager != null)
            {
                var fontSet = ReadInstanceValue(manager, "fontSet", "FontSet");
                var uiFontData = ReadInstanceValue(fontSet, "ui", "UI");
                var fontSource = ReadInstanceValue(uiFontData, "source", "Source");
                var selectedFont = ReadInstanceValue(fontSource, "font", "Font") as Font;
                if (selectedFont != null)
                    return selectedFont;

                var indexValue = ReadInstanceValue(uiFontData, "index", "Index");
                var fontList = ReadInstanceValue(manager, "FontList", "fontList") as IList;
                if (TryConvertIndex(indexValue, out var index) &&
                    fontList != null &&
                    index >= 0 &&
                    index < fontList.Count)
                {
                    selectedFont = ReadInstanceValue(fontList[index], "font", "Font") as Font;
                    if (selectedFont != null)
                        return selectedFont;
                }
            }
        }
        catch
        {
        }

        return FindAppliedGameUiFont();
    }

    private static Font? FindAppliedGameUiFont()
    {
        Text[] texts;
        try
        {
            texts = Resources.FindObjectsOfTypeAll<Text>();
        }
        catch
        {
            return null;
        }

        for (var i = 0; i < texts.Length; i++)
        {
            var text = texts[i];
            if (text == null || text.font == null || !IsOriginalGameUiText(text.GetType()))
                continue;

            var fontType = ReadInstanceValue(text, "fontType", "FontType");
            if (!TryConvertIndex(fontType, out var fontTypeValue) || fontTypeValue == 0)
                return text.font;
        }

        return null;
    }

    private static bool IsOriginalGameUiText(Type? type)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (string.Equals(current.FullName, "UIText", StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool TryConvertIndex(object? value, out int index)
    {
        try
        {
            if (value != null)
            {
                index = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch
        {
        }

        index = -1;
        return false;
    }

    private static object? ReadStaticValue(Type? type, params string[] names)
    {
        if (type == null)
            return null;

        for (var nameIndex = 0; nameIndex < names.Length; nameIndex++)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var value = ReadValue(current, null, names[nameIndex], StaticFlags);
                if (value.Found)
                    return value.Value;
            }
        }

        return null;
    }

    private static object? ReadInstanceValue(object? instance, params string[] names)
    {
        if (instance == null)
            return null;

        for (var nameIndex = 0; nameIndex < names.Length; nameIndex++)
        {
            for (var current = instance.GetType(); current != null; current = current.BaseType)
            {
                var value = ReadValue(current, instance, names[nameIndex], InstanceFlags);
                if (value.Found)
                    return value.Value;
            }
        }

        return null;
    }

    private static MemberReadResult ReadValue(Type type, object? instance, string name, BindingFlags flags)
    {
        try
        {
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return new MemberReadResult(true, property.GetValue(instance, null));
        }
        catch
        {
        }

        try
        {
            var field = type.GetField(name, flags);
            if (field != null)
                return new MemberReadResult(true, field.GetValue(instance));
        }
        catch
        {
        }

        return default;
    }

    private readonly struct MemberReadResult
    {
        internal MemberReadResult(bool found, object? value)
        {
            Found = found;
            Value = value;
        }

        internal bool Found { get; }
        internal object? Value { get; }
    }
}
