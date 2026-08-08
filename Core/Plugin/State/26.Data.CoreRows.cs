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
    private readonly RowDef[] _statusRows =
    {
        new RowDef("HP", "生命(当前HP)", RowKind.CardInt),
        new RowDef("mana", "玛那(当前MP)", RowKind.CharaStatProperty),
        new RowDef("stamina", "活力(当前SP)", RowKind.CharaStatProperty),
        new RowDef("60", "生命力", RowKind.Element),
        new RowDef("61", "玛那", RowKind.Element),
        new RowDef("62", "活力", RowKind.Element),
        new RowDef("79", "速度", RowKind.Element),
        new RowDef("feat", "专长点", RowKind.CharaIntProperty)
    };
    private readonly RowDef[] _npcStatusRows =
    {
        new RowDef("HP", "生命(当前HP)", RowKind.CardInt),
        new RowDef("mana", "玛那(当前MP)", RowKind.CharaStatProperty),
        new RowDef("stamina", "活力(当前SP)", RowKind.CharaStatProperty),
        new RowDef("60", "生命力", RowKind.Element),
        new RowDef("61", "玛那", RowKind.Element),
        new RowDef("62", "活力", RowKind.Element),
        new RowDef("79", "速度", RowKind.Element),
        new RowDef("MaxGeneSlot", "基因槽数量", RowKind.GeneSlot),
        new RowDef("feat", "FP", RowKind.CharaIntProperty)
    };
    private readonly RowDef[] _attributeRows =
    {
        new RowDef("70", "力量", RowKind.Element),
        new RowDef("71", "体质", RowKind.Element),
        new RowDef("72", "灵巧", RowKind.Element),
        new RowDef("73", "感知", RowKind.Element),
        new RowDef("74", "学习", RowKind.Element),
        new RowDef("75", "意志", RowKind.Element),
        new RowDef("76", "魔力", RowKind.Element),
        new RowDef("77", "魅力", RowKind.Element)
    };
    private static readonly int[] KillGrowthAttributeIds = { 70, 71, 72, 73, 74, 75, 76, 77 };
}
