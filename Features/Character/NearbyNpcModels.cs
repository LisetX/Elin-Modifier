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

internal sealed class NearbyNpcEntry
{
    public readonly Chara Chara;
    public readonly int Uid;
    public readonly string Name;
    public readonly string Id;
    public readonly string HostilityLabel;
    public readonly int Affinity;
    public readonly int FollowRank;
    public readonly int RelationshipRank;
    public readonly string Label;

    public NearbyNpcEntry(Chara chara, int uid, string name, string id, string hostilityLabel, int affinity, int followRank, int relationshipRank, string label)
    {
        Chara = chara;
        Uid = uid;
        Name = name ?? "";
        Id = id ?? "";
        HostilityLabel = hostilityLabel ?? "";
        Affinity = affinity;
        FollowRank = followRank;
        RelationshipRank = relationshipRank;
        Label = label ?? "";
    }
}

