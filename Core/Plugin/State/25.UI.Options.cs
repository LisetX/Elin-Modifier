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
    private static readonly RelationshipOption[] RelationshipOptions =
    {
        new RelationshipOption("Enemy", Hostility.Enemy),
        new RelationshipOption("Neutral", Hostility.Neutral),
        new RelationshipOption("Friend", Hostility.Friend),
        new RelationshipOption("Ally", Hostility.Ally)
    };
    private static readonly KeyOption[] KeyOptions =
    {
        new KeyOption("F1", KeyCode.F1),
        new KeyOption("F2", KeyCode.F2),
        new KeyOption("F3", KeyCode.F3),
        new KeyOption("F4", KeyCode.F4),
        new KeyOption("F5", KeyCode.F5),
        new KeyOption("F6", KeyCode.F6),
        new KeyOption("F7", KeyCode.F7),
        new KeyOption("F8", KeyCode.F8),
        new KeyOption("F9", KeyCode.F9),
        new KeyOption("F10", KeyCode.F10),
        new KeyOption("F11", KeyCode.F11),
        new KeyOption("F12", KeyCode.F12),
        new KeyOption("Insert", KeyCode.Insert),
        new KeyOption("Home", KeyCode.Home),
        new KeyOption("Del", KeyCode.Delete),
        new KeyOption("End", KeyCode.End),
        new KeyOption("PgUP", KeyCode.PageUp),
        new KeyOption("PgDN", KeyCode.PageDown),
        new KeyOption("[", KeyCode.LeftBracket),
        new KeyOption("]", KeyCode.RightBracket),
        new KeyOption("\\", KeyCode.Backslash),
        new KeyOption("=", KeyCode.Equals),
        new KeyOption("-", KeyCode.Minus),
        new KeyOption(";", KeyCode.Semicolon),
        new KeyOption("'", KeyCode.Quote),
        new KeyOption(",", KeyCode.Comma),
        new KeyOption(".", KeyCode.Period),
        new KeyOption("/", KeyCode.Slash)
    };
}
