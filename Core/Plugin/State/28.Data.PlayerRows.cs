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
    private readonly RowDef[] _playerRows =
    {
        new RowDef("SAN", "疯狂度", RowKind.CharaStatProperty),
        new RowDef("karma", "善恶值", RowKind.PlayerField),
        new RowDef("fame", "名声", RowKind.PlayerField),
        new RowDef("influence", "影响力", RowKind.ZoneInfluence)
    };
    private readonly RowDef[] _fallbackResistRows =
    {
        new RowDef("961", "火焰抗性", RowKind.Element),
        new RowDef("962", "寒冷抗性", RowKind.Element),
        new RowDef("963", "电击抗性", RowKind.Element),
        new RowDef("964", "暗影抗性", RowKind.Element),
        new RowDef("965", "幻惑抗性", RowKind.Element),
        new RowDef("966", "毒素抗性", RowKind.Element),
        new RowDef("967", "地狱抗性", RowKind.Element),
        new RowDef("968", "音波抗性", RowKind.Element),
        new RowDef("969", "神经抗性", RowKind.Element),
        new RowDef("970", "混沌抗性", RowKind.Element),
        new RowDef("971", "魔法抗性", RowKind.Element),
        new RowDef("972", "以太抗性", RowKind.Element),
        new RowDef("973", "酸抗性", RowKind.Element),
        new RowDef("974", "切割抗性", RowKind.Element),
        new RowDef("975", "腐朽抗性", RowKind.Element)
    };
}
