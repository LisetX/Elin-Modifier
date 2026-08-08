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
    private static bool ShouldShowNpcMoreInfo()
    {
        return Instance != null && Instance._showNpcMoreInfo;
    }
    private static bool ShouldShowItemMoreInfo()
    {
        return Instance != null && Instance._showItemMoreInfo;
    }
    internal static bool ShouldShowNpcMoreInfoLevel()
    {
        return Instance != null && Instance._showNpcMoreInfoLevel;
    }
    internal static bool ShouldShowNpcMoreInfoIdentity()
    {
        return Instance != null && Instance._showNpcMoreInfoIdentity;
    }
    internal static bool ShouldShowNpcMoreInfoRelationFaith()
    {
        return Instance != null && Instance._showNpcMoreInfoRelationFaith;
    }
    internal static bool ShouldShowNpcMoreInfoVitals()
    {
        return Instance != null && Instance._showNpcMoreInfoVitals;
    }
    internal static bool ShouldShowNpcMoreInfoAttributes()
    {
        return Instance != null && Instance._showNpcMoreInfoAttributes;
    }
    internal static bool ShouldShowNpcMoreInfoBuffs()
    {
        return Instance != null && Instance._showNpcMoreInfoBuffs;
    }
    internal static bool ShouldShowNpcMoreInfoResists()
    {
        return Instance != null && Instance._showNpcMoreInfoResists;
    }
    internal static bool ShouldShowNpcMoreInfoSkills()
    {
        return Instance != null && Instance._showNpcMoreInfoSkills;
    }
    internal static bool ShouldShowNpcMoreInfoAbilities()
    {
        return Instance != null && Instance._showNpcMoreInfoAbilities;
    }
    internal static bool ShouldShowNpcMoreInfoFeats()
    {
        return Instance != null && Instance._showNpcMoreInfoFeats;
    }
    internal static bool ShouldShowNpcMoreInfoCombatSimulation()
    {
        return Instance != null && Instance._showNpcMoreInfoCombatSimulation;
    }
}
