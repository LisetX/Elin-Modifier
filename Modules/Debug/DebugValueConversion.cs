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
    private static bool TryParseDebugValue(string text, Type type, out object value)
    {
        value = null;
        if (type == null)
            return false;
        var raw = text ?? "";
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
            type = nullable;
        try
        {
            if (type == typeof(string))
            {
                value = raw;
                return true;
            }
            if (type == typeof(bool))
            {
                bool b;
                if (bool.TryParse(raw, out b))
                {
                    value = b;
                    return true;
                }
                int i;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                {
                    value = i != 0;
                    return true;
                }
                return false;
            }
            if (type.IsEnum)
            {
                value = Enum.Parse(type, raw, true);
                return true;
            }
            if (type == typeof(int))
            {
                int v;
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(long))
            {
                long v;
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(short))
            {
                short v;
                if (!short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(byte))
            {
                byte v;
                if (!byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(sbyte))
            {
                sbyte v;
                if (!sbyte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(ushort))
            {
                ushort v;
                if (!ushort.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(uint))
            {
                uint v;
                if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(ulong))
            {
                ulong v;
                if (!ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(float))
            {
                float v;
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(double))
            {
                double v;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(decimal))
            {
                decimal v;
                if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                value = v;
                return true;
            }
            if (type == typeof(char))
            {
                if (raw.Length != 1) return false;
                value = raw[0];
                return true;
            }
            float[] parts;
            if (type == typeof(Vector2))
            {
                if (!TryParseDebugFloatList(raw, 2, out parts)) return false;
                value = new Vector2(parts[0], parts[1]);
                return true;
            }
            if (type == typeof(Vector3))
            {
                if (!TryParseDebugFloatList(raw, 3, out parts)) return false;
                value = new Vector3(parts[0], parts[1], parts[2]);
                return true;
            }
            if (type == typeof(Vector4))
            {
                if (!TryParseDebugFloatList(raw, 4, out parts)) return false;
                value = new Vector4(parts[0], parts[1], parts[2], parts[3]);
                return true;
            }
            int[] intParts;
            if (type == typeof(Vector2Int))
            {
                if (!TryParseDebugIntList(raw, 2, out intParts)) return false;
                value = new Vector2Int(intParts[0], intParts[1]);
                return true;
            }
            if (type == typeof(Vector3Int))
            {
                if (!TryParseDebugIntList(raw, 3, out intParts)) return false;
                value = new Vector3Int(intParts[0], intParts[1], intParts[2]);
                return true;
            }
            if (type == typeof(Quaternion))
            {
                if (!TryParseDebugFloatList(raw, 4, out parts)) return false;
                value = new Quaternion(parts[0], parts[1], parts[2], parts[3]);
                return true;
            }
            if (type == typeof(Rect))
            {
                if (!TryParseDebugFloatList(raw, 4, out parts)) return false;
                value = new Rect(parts[0], parts[1], parts[2], parts[3]);
                return true;
            }
            if (type == typeof(RectInt))
            {
                if (!TryParseDebugIntList(raw, 4, out intParts)) return false;
                value = new RectInt(intParts[0], intParts[1], intParts[2], intParts[3]);
                return true;
            }
            if (type == typeof(Bounds))
            {
                if (!TryParseDebugFloatList(raw, 6, out parts)) return false;
                value = new Bounds(new Vector3(parts[0], parts[1], parts[2]), new Vector3(parts[3], parts[4], parts[5]));
                return true;
            }
            if (type == typeof(BoundsInt))
            {
                if (!TryParseDebugIntList(raw, 6, out intParts)) return false;
                value = new BoundsInt(new Vector3Int(intParts[0], intParts[1], intParts[2]), new Vector3Int(intParts[3], intParts[4], intParts[5]));
                return true;
            }
            if (type == typeof(Color))
            {
                if (!TryParseDebugFloatList(raw, 4, out parts)) return false;
                value = new Color(parts[0], parts[1], parts[2], parts[3]);
                return true;
            }
            if (type == typeof(Color32))
            {
                if (!TryParseDebugIntList(raw, 4, out intParts)) return false;
                value = new Color32((byte)Math.Max(0, Math.Min(255, intParts[0])), (byte)Math.Max(0, Math.Min(255, intParts[1])), (byte)Math.Max(0, Math.Min(255, intParts[2])), (byte)Math.Max(0, Math.Min(255, intParts[3])));
                return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }
    private static bool TryParseDebugFloatList(string text, int expectedCount, out float[] values)
    {
        values = null;
        var parts = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expectedCount)
            return false;
        values = new float[expectedCount];
        for (var i = 0; i < parts.Length; i++)
        {
            float value;
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;
            values[i] = value;
        }
        return true;
    }
    private static bool TryParseDebugIntList(string text, int expectedCount, out int[] values)
    {
        values = null;
        var parts = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expectedCount)
            return false;
        values = new int[expectedCount];
        for (var i = 0; i < parts.Length; i++)
        {
            int value;
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return false;
            values[i] = value;
        }
        return true;
    }
    private static bool IsDebugEditableType(Type type)
    {
        if (type == null)
            return false;
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
            type = nullable;
        return type == typeof(string) ||
               type == typeof(bool) ||
               type == typeof(int) ||
               type == typeof(long) ||
               type == typeof(short) ||
               type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(ushort) ||
               type == typeof(uint) ||
               type == typeof(ulong) ||
               type == typeof(char) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal) ||
               type == typeof(Vector2) ||
               type == typeof(Vector3) ||
               type == typeof(Vector4) ||
               type == typeof(Vector2Int) ||
               type == typeof(Vector3Int) ||
               type == typeof(Quaternion) ||
               type == typeof(Rect) ||
               type == typeof(RectInt) ||
               type == typeof(Bounds) ||
               type == typeof(BoundsInt) ||
               type == typeof(Color) ||
               type == typeof(Color32) ||
               type.IsEnum;
    }
    internal static bool IsDebugLeafType(Type type)
    {
        if (type == null)
            return true;
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
            type = nullable;
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(Vector2) ||
               type == typeof(Vector3) ||
               type == typeof(Vector4) ||
               type == typeof(Vector2Int) ||
               type == typeof(Vector3Int) ||
               type == typeof(Quaternion) ||
               type == typeof(Rect) ||
               type == typeof(RectInt) ||
               type == typeof(Bounds) ||
               type == typeof(BoundsInt) ||
               type == typeof(Color32) ||
               type == typeof(Color);
    }
    internal static string DebugValueToString(object value)
    {
        if (value == null)
            return "null";
        try
        {
            if (value is Vector2 v2) return v2.x.ToString("R", CultureInfo.InvariantCulture) + "," + v2.y.ToString("R", CultureInfo.InvariantCulture);
            if (value is Vector3 v3) return v3.x.ToString("R", CultureInfo.InvariantCulture) + "," + v3.y.ToString("R", CultureInfo.InvariantCulture) + "," + v3.z.ToString("R", CultureInfo.InvariantCulture);
            if (value is Vector4 v4) return v4.x.ToString("R", CultureInfo.InvariantCulture) + "," + v4.y.ToString("R", CultureInfo.InvariantCulture) + "," + v4.z.ToString("R", CultureInfo.InvariantCulture) + "," + v4.w.ToString("R", CultureInfo.InvariantCulture);
            if (value is Vector2Int v2i) return v2i.x.ToString(CultureInfo.InvariantCulture) + "," + v2i.y.ToString(CultureInfo.InvariantCulture);
            if (value is Vector3Int v3i) return v3i.x.ToString(CultureInfo.InvariantCulture) + "," + v3i.y.ToString(CultureInfo.InvariantCulture) + "," + v3i.z.ToString(CultureInfo.InvariantCulture);
            if (value is Quaternion q) return q.x.ToString("R", CultureInfo.InvariantCulture) + "," + q.y.ToString("R", CultureInfo.InvariantCulture) + "," + q.z.ToString("R", CultureInfo.InvariantCulture) + "," + q.w.ToString("R", CultureInfo.InvariantCulture);
            if (value is Rect rect) return rect.x.ToString("R", CultureInfo.InvariantCulture) + "," + rect.y.ToString("R", CultureInfo.InvariantCulture) + "," + rect.width.ToString("R", CultureInfo.InvariantCulture) + "," + rect.height.ToString("R", CultureInfo.InvariantCulture);
            if (value is RectInt rectInt) return rectInt.x.ToString(CultureInfo.InvariantCulture) + "," + rectInt.y.ToString(CultureInfo.InvariantCulture) + "," + rectInt.width.ToString(CultureInfo.InvariantCulture) + "," + rectInt.height.ToString(CultureInfo.InvariantCulture);
            if (value is Bounds bounds) return bounds.center.x.ToString("R", CultureInfo.InvariantCulture) + "," + bounds.center.y.ToString("R", CultureInfo.InvariantCulture) + "," + bounds.center.z.ToString("R", CultureInfo.InvariantCulture) + "," + bounds.size.x.ToString("R", CultureInfo.InvariantCulture) + "," + bounds.size.y.ToString("R", CultureInfo.InvariantCulture) + "," + bounds.size.z.ToString("R", CultureInfo.InvariantCulture);
            if (value is BoundsInt boundsInt) return boundsInt.position.x.ToString(CultureInfo.InvariantCulture) + "," + boundsInt.position.y.ToString(CultureInfo.InvariantCulture) + "," + boundsInt.position.z.ToString(CultureInfo.InvariantCulture) + "," + boundsInt.size.x.ToString(CultureInfo.InvariantCulture) + "," + boundsInt.size.y.ToString(CultureInfo.InvariantCulture) + "," + boundsInt.size.z.ToString(CultureInfo.InvariantCulture);
            if (value is Color color) return color.r.ToString("R", CultureInfo.InvariantCulture) + "," + color.g.ToString("R", CultureInfo.InvariantCulture) + "," + color.b.ToString("R", CultureInfo.InvariantCulture) + "," + color.a.ToString("R", CultureInfo.InvariantCulture);
            if (value is Color32 color32) return color32.r.ToString(CultureInfo.InvariantCulture) + "," + color32.g.ToString(CultureInfo.InvariantCulture) + "," + color32.b.ToString(CultureInfo.InvariantCulture) + "," + color32.a.ToString(CultureInfo.InvariantCulture);
            if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (value is decimal m) return m.ToString(CultureInfo.InvariantCulture);
            if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }
        catch { return "?"; }
    }
    internal static string GetDebugTypeName(Type type)
    {
        if (type == null)
            return "null";
        return string.IsNullOrEmpty(type.FullName) ? type.Name : type.FullName;
    }
    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }
        return count;
    }
}
