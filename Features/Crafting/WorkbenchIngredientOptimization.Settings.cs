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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private void SetWorkbenchIngredientReadingOptimization(bool enabled)
    {
        _workbenchIngredientReadingOptimization = enabled;
        NonStandardCrafterIngredientOptimizer.Clear();
        if (enabled)
        {
            NonStandardCrafterIngredientPager.RefreshActive();
        }
        else
        {
            CraftIngredientPickerPager.CloseActivePickers();
            NonStandardCrafterIngredientPager.DisableAndRestoreActive();
        }
        try
        {
            var layer = LayerDragGrid.Instance;
            if (layer != null)
            {
                layer.RefreshCurrentGrid();
                layer.uiIngredients?.Refresh();
            }
        }
        catch { }
        _log = enabled
            ? T("工作台素材读取优化已开启", "Workbench ingredient loading optimization enabled")
            : T("工作台素材读取优化已关闭", "Workbench ingredient loading optimization disabled");
    }
}
