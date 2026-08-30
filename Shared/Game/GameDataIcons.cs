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
    private Sprite? GetAbilityIcon(AbilityDef ability)
    {
        if (_abilityIconCache.TryGetValue(ability.Id, out var cached))
            return cached;

        var sprite = LoadAbilityIcon(ability);
        _abilityIconCache[ability.Id] = sprite;
        return sprite;
    }
    private static Sprite? LoadAbilityIcon(AbilityDef ability)
    {
        try
        {
            var method = ability.Source.GetType().GetMethod("GetSprite", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            return method == null ? null : method.Invoke(ability.Source, null) as Sprite;
        }
        catch { return null; }
    }
    private Sprite? GetItemIcon(ItemDef item)
    {
        var key = item.Id + "#" + item.SkinId.ToString(CultureInfo.InvariantCulture);
        if (_itemIconCache.TryGetValue(key, out var cached))
            return cached;

        var sprite = LoadItemIcon(item);
        _itemIconCache[key] = sprite;
        return sprite;
    }
    private static Sprite? LoadItemIcon(ItemDef item)
    {
        try
        {
            var thing = GameAccess.Spawn.CreateThing(item.Id, -1, 1);
            if (thing == null) return null;
            if (item.SeedRefVal >= 0 && thing.trait is TraitSeed)
                TraitSeed.ApplySeed(thing, item.SeedRefVal);
            else if (item.VariantIndex >= 0)
                SetCardIntProperty(thing, "idSkin", item.SkinId);
            return thing.GetSprite(0);
        }
        catch { return null; }
    }
    private static Sprite? GetCharaIcon(Chara chara)
    {
        try { return chara == null ? null : chara.GetSprite(0); }
        catch { return null; }
    }
    private Sprite? GetNpcIcon(NpcDef npc)
    {
        if (_npcIconCache.TryGetValue(npc.Id, out var cached))
            return cached;

        var sprite = LoadNpcIcon(npc);
        _npcIconCache[npc.Id] = sprite;
        return sprite;
    }
    private static Sprite? LoadNpcIcon(NpcDef npc)
    {
        try
        {
            var chara = GameAccess.Spawn.CreateCharacter(npc.Id, 1);
            return chara == null ? null : chara.GetSprite(0);
        }
        catch { return null; }
    }
}
