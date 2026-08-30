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
    private readonly Dictionary<string, string> _inputs = new Dictionary<string, string>();
    private readonly Dictionary<string, bool> _locks = new Dictionary<string, bool>();
    private readonly Dictionary<int, int> _abilityChanceOverrides = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _abilityPowerOverrides = new Dictionary<int, int>();
    private readonly Dictionary<int, AbilityCostOverride> _abilityCostOverrides = new Dictionary<int, AbilityCostOverride>();
}
