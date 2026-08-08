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
    private void SetCustomItemAmount(bool enabled)
    {
        _customItemAmount = enabled;
        if (!enabled)
            _itemAmountWindowVisible = false;
        _log = enabled
            ? T("自定义物品持有数量已开启", "Custom item held amount enabled")
            : T("自定义物品持有数量已关闭", "Custom item held amount disabled");
    }
    private void SetCustomItemEditor(bool enabled)
    {
        _customItemEditor = enabled;
        if (!enabled)
            _itemDataEditorWindowVisible = false;
        _log = enabled
            ? T("自定义物品数据已开启", "Custom item data enabled")
            : T("自定义物品数据已关闭", "Custom item data disabled");
    }
    private void SetCustomFoodEditor(bool enabled)
    {
        _customFoodEditor = enabled;
        if (!enabled)
            _foodEditorWindowVisible = false;
        _log = enabled
            ? T("自定义食物数据已开启", "Custom food data enabled")
            : T("自定义食物数据已关闭", "Custom food data disabled");
    }
    private void SetCustomWeaponEditor(bool enabled)
    {
        _customWeaponEditor = enabled;
        if (!enabled)
            _weaponEditorWindowVisible = false;
        _log = enabled
            ? T("自定义武器数据已开启", "Custom weapon data enabled")
            : T("自定义武器数据已关闭", "Custom weapon data disabled");
    }
    private void SetCustomGeneEditor(bool enabled)
    {
        _customGeneEditor = enabled;
        if (!enabled)
            _geneEditorWindowVisible = false;
        _log = enabled
            ? T("自定义基因编辑已开启", "Custom gene editing enabled")
            : T("自定义基因编辑已关闭", "Custom gene editing disabled");
    }
}
