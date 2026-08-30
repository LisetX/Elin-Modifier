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

internal sealed class KillGrowthKillState
{
    public readonly Chara Killer;
    public readonly Chara Victim;
    public readonly int KillerMainAttributeTotal;
    public readonly int EnemyMainAttributeTotal;

    public KillGrowthKillState(Chara killer, Chara victim, int killerMainAttributeTotal, int enemyMainAttributeTotal)
    {
        Killer = killer;
        Victim = victim;
        KillerMainAttributeTotal = Math.Max(1, killerMainAttributeTotal);
        EnemyMainAttributeTotal = Math.Max(1, enemyMainAttributeTotal);
    }
}

