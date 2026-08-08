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
    private void OpenItemAmountWindow(Thing thing)
    {
        if (!CanCustomizeItemAmount(thing))
            return;

        _itemAmountTarget = thing;
        _itemAmountName = SafeThingName(thing);
        _itemAmountInput = Math.Max(1, thing.Num).ToString(CultureInfo.InvariantCulture);
        _itemAmountWindowVisible = false;
        if (!IsLGuiInitialized())
            return;
        EnsureLGuiEditorVisible();
        OpenLGuiItemAmountEditor();
    }
    private void ApplyItemAmountChange()
    {
        try
        {
            var target = _itemAmountTarget;
            if (!CanCustomizeItemAmount(target))
            {
                _log = T("目标物品不存在", "Target item does not exist");
                _itemAmountWindowVisible = false;
                return;
            }

            int count;
            if (!int.TryParse((_itemAmountInput ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
            {
                _log = T("数量输入不是数字", "Amount input is not a number");
                return;
            }
            if (count <= 0)
            {
                _log = T("数量必须大于0", "Amount must be greater than 0");
                return;
            }

            SetCardNum(target!, count);
            RefreshInventoryUi();
            RefreshFoodRotOverlayForCard(target);
            _itemAmountName = SafeThingName(target!);
            _log = T("已修改持有数量: ", "Modified held amount: ") + _itemAmountName + " x" + count.ToString(CultureInfo.InvariantCulture);
            _itemAmountWindowVisible = false;
        }
        catch (Exception ex)
        {
            _log = T("修改持有数量失败: ", "Modify held amount failed: ") + ex.Message;
        }
    }
}
