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

internal struct NpcCombatEstimateStats
{
    public int Level;
    public int CurrentHp;
    public int MaxHp;
    public int Speed;
    public int Hit;
    public int DamageBonus;
    public int DV;
    public int PV;
    public int Str;
    public int End;
    public int Dex;
    public int Per;
    public int Wil;
    public int Mag;
    public int MainTotal;
    public int WeaponSkill;
    public int CombatSkill;
    public int ArmorSkill;
    public int Penetration;
    public double WeaponAverageDamage;
    public double EquipmentPower;
}

internal readonly struct NpcCombatEstimate
{
    public readonly double ExpectedDamagePerRound;
    public readonly int TargetHp;
    public readonly double HitChance;
    public readonly int Rounds;

    public NpcCombatEstimate(double expectedDamagePerRound, int targetHp, double hitChance, int rounds)
    {
        ExpectedDamagePerRound = Math.Max(0.0, expectedDamagePerRound);
        TargetHp = Math.Max(1, targetHp);
        HitChance = hitChance;
        Rounds = Math.Max(1, rounds);
    }
}

internal readonly struct NpcMoreInfoResistDefinition
{
    public readonly int Id;
    public readonly string Name;

    public NpcMoreInfoResistDefinition(int id, string name)
    {
        Id = id;
        Name = name ?? id.ToString(CultureInfo.InvariantCulture);
    }
}

