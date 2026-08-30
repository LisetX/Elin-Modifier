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
    private bool _ignoreTerrainMovement;
    private bool _optimizeDungeonVoidScaling;
    private bool _noTalkInterestLoss;
    private bool _killGrowthEnabled;
    private bool _killGrowthSharedExperience;
    private decimal _killGrowthExpPerLevel = 100m;
    private decimal _killGrowthBaseExp = 10m;
    private string _killGrowthExpPerLevelText = "100";
    private string _killGrowthBaseExpText = "10";
    private string _killGrowthStrBonusText = "1";
    private string _killGrowthEndBonusText = "1";
    private string _killGrowthDexBonusText = "1";
    private string _killGrowthPerBonusText = "1";
    private string _killGrowthLeaBonusText = "1";
    private string _killGrowthWilBonusText = "1";
    private string _killGrowthMagBonusText = "1";
    private string _killGrowthChaBonusText = "1";
    private readonly Dictionary<int, int> _killGrowthAttributeBonus = new Dictionary<int, int>
    {
        { 70, 1 },
        { 71, 1 },
        { 72, 1 },
        { 73, 1 },
        { 74, 1 },
        { 75, 1 },
        { 76, 1 },
        { 77, 1 }
    };
    private Dictionary<int, decimal> _killGrowthExpByUid = new Dictionary<int, decimal>();
    private readonly Dictionary<string, Dictionary<int, decimal>> _killGrowthExpBySaveId =
        new Dictionary<string, Dictionary<int, decimal>>(StringComparer.Ordinal);
    private readonly Dictionary<int, decimal> _killGrowthLegacyExpByUid = new Dictionary<int, decimal>();
    private string _killGrowthActiveSaveId = "";
    private bool _killGrowthLegacyMigrationPending;
    private bool _killGrowthSaveMigrationWritePending;
    private bool _killGrowthSaveMigrationWriteInProgress;
    private float _killGrowthNextSaveContextCheckAt;
    private bool _infinitePlayerSight;
    private bool _showFoodRot;
    private bool _ignoreFoodDecay;
    private bool _noCraftMaterials;
    private bool _unlockAllCraftMaterials;
    private bool _unlockAllCraftRecipes;
    private bool _customItemAmount;
    private bool _customItemEditor;
    private bool _customFoodEditor;
    private bool _customWeaponEditor;
    private bool _customGeneEditor;
    private bool _stethoscopeNoTargetLimit;
}
