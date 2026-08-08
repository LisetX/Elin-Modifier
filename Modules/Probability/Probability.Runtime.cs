using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class ProbabilityModule
{
    internal void Tick()
    {
        if (_probabilityScanned && (!_slotProbabilityPatchInstalled || !_gambleChestProbabilityPatchInstalled) &&
            Time.frameCount >= _slotProbabilityPatchRetryFrame)
        {
            _slotProbabilityPatchRetryFrame = Time.frameCount + 300;
            EnsureSlotProbabilityPatch();
        }

        if (_probabilityFilterDirty && IsProbabilityPageActive() && Time.unscaledTime >= _probabilityFilterDueAt)
        {
            _probabilityFilterDirty = false;
            RebuildProbabilityRows();
        }

        if (_probabilityModifiedCount <= 0)
            return;

        var currentSources = GetCurrentProbabilitySourceManager();
        if (!HasCharacterData() || currentSources == null || !ReferenceEquals(currentSources, _probabilitySourceManager))
        {
            RestoreAll(false);
            _probabilityLog = T("已离开当前游戏，概率修改已自动恢复", "Left the current game; probability changes were restored automatically");
        }
    }
    private static string FormatProbabilityValue(object? value)
    {
        if (value == null)
            return "0";
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "0";
        return value.ToString() ?? "0";
    }
    private bool TryParseProbabilityValue(string text, Type type, out object value, out string error)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        text = (text ?? "").Trim();
        value = 0;
        error = "";
        if (text.Length == 0)
        {
            error = T("数值不能为空", "Value cannot be empty");
            return false;
        }

        try
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.SByte: value = sbyte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case TypeCode.Byte: value = byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case TypeCode.Int16: value = short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case TypeCode.UInt16: value = ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case TypeCode.Int32: value = int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case TypeCode.UInt32: value = uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case TypeCode.Int64: value = long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case TypeCode.UInt64: value = ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case TypeCode.Single:
                    {
                        var parsed = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (float.IsNaN(parsed) || float.IsInfinity(parsed)) throw new FormatException();
                        value = parsed;
                        break;
                    }
                case TypeCode.Double:
                    {
                        var parsed = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (double.IsNaN(parsed) || double.IsInfinity(parsed)) throw new FormatException();
                        value = parsed;
                        break;
                    }
                case TypeCode.Decimal: value = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture); break;
                default:
                    error = T("不支持该数值类型", "Unsupported numeric type");
                    return false;
            }
            return true;
        }
        catch
        {
            error = T("请输入该字段支持的有效数字", "Enter a valid number supported by this field");
            return false;
        }
    }
}
