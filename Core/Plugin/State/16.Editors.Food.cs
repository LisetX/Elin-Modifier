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
    private int _foodEditorRarityValue;
    private int _foodEditorBlessedStateValue;
    private bool _foodEditorFlagStolen;
    private bool _foodEditorFlagCrafted;
    private bool _foodEditorFlagGifted;
    private bool _foodEditorFlagReplica;
    private bool _foodEditorFlagCopy;
    private bool _foodEditorFlagFireproof;
    private bool _foodEditorFlagAcidproof;
    private bool _foodEditorFlagBroken;
    private bool _foodEditorFlagNoSell;
    private bool _foodEditorFlagLostProperty;
    private Thing? _foodEditorTarget;
}
