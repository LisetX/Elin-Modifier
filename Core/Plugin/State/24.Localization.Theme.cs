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
    private static readonly Color[] UiStyleColors =
    {
        new Color(1f, 1f, 1f, 1f),
        new Color(0.55f, 0.55f, 0.55f, 1f),
        new Color(0.55f, 0.72f, 1f, 1f),
        new Color(0.56f, 0.9f, 0.66f, 1f),
        new Color(0.08f, 0.08f, 0.08f, 1f),
        new Color(0.95f, 0.95f, 0.9f, 1f),
        new Color(0.04f, 0.09f, 0.05f, 1f),
        new Color(0.12f, 0.08f, 0.02f, 1f)
    };
    private static readonly Color[] UiTextColors =
    {
        Color.white,
        Color.white,
        Color.white,
        Color.white,
        Color.white,
        Color.black,
        new Color(0.65f, 1f, 0.65f, 1f),
        new Color(1f, 0.82f, 0.35f, 1f)
    };
    private static readonly Color[] UiTextColorPalette =
    {
        Color.white,
        Color.black,
        new Color(1f, 0.22f, 0.22f, 1f),
        new Color(1f, 0.75f, 0.25f, 1f),
        new Color(0.35f, 1f, 0.45f, 1f),
        new Color(0.25f, 0.72f, 1f, 1f),
        new Color(0.76f, 0.45f, 1f, 1f),
        new Color(1f, 0.55f, 0.8f, 1f)
    };
}
