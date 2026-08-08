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
    private void SpawnNpc()
    {
        try
        {
            var npcId = (_npcSpawnId ?? "").Trim();
            if (string.IsNullOrEmpty(npcId))
            {
                _npcLog = T("NPC ID 不能为空", "NPC ID cannot be empty");
                return;
            }

            var requestedLv = ParseInt(_npcSpawnLv, -1);
            var lv = requestedLv < 1 ? GetNpcTemplateLevel(npcId) : requestedLv;
            var affinity = ParseInt(_npcSpawnAffinity, 0);
            _npcSpawnHostilityIndex = Clamp(_npcSpawnHostilityIndex, 0, RelationshipOptions.Length - 1);
            var hostility = RelationshipOptions[_npcSpawnHostilityIndex].Value;

            var chara = GameAccess.Spawn.CreateCharacter(npcId, lv);
            if (chara == null)
            {
                _npcLog = T("NPC生成失败：", "NPC spawn failed: ") + npcId;
                return;
            }

            chara.genLv = lv;
            chara.SetLv(lv);
            GameAccess.World.AddCard(GameAccess.World.CurrentZone, chara, GameAccess.Characters.PlayerCharacter.pos);
            chara._affinity = affinity;
            chara.SetHostility(hostility);
            chara.Refresh(false);
            _npcLog = T("已生成NPC：", "Spawned NPC: ") + SafeName(chara) + " (" + npcId + "), " +
                      T("好感度 ", "affinity ") + affinity + ", " + GetHostilityLabel(hostility);
        }
        catch (Exception ex)
        {
            _npcLog = T("NPC生成失败：", "NPC spawn failed: ") + ex.Message;
        }
    }
    private static int GetNpcTemplateLevel(string npcId)
    {
        try
        {
            foreach (var row in EnumerateSourceCharaRows())
            {
                if (!string.Equals(GetString(row, "id"), npcId, StringComparison.OrdinalIgnoreCase))
                    continue;
                return Math.Max(1, GetInt(row, "LV"));
            }
        }
        catch { }
        return 1;
    }
    private void TeleportPlayerBesideNpc(Chara target)
    {
        try
        {
            var pc = GameAccess.Characters.PlayerCharacter;
            if (pc == null || target == null || target.pos == null)
            {
                _log = T("传送失败: ", "Teleport failed: ") + T("未获取到人物数据", "No character data");
                return;
            }

            var point = FindTeleportPointBesideNpc(pc, target);
            if (point == null)
            {
                _log = T("传送失败: ", "Teleport failed: ") + T("找不到可用位置", "No usable position found");
                return;
            }

            pc.Teleport(point, false, true);
            pc.Refresh(false);
            _log = T("已传送至NPC: ", "Teleported to NPC: ") + SafeName(target);
        }
        catch (Exception ex)
        {
            _log = T("传送失败: ", "Teleport failed: ") + ex.Message;
        }
    }
    private static Point? FindTeleportPointBesideNpc(Chara pc, Chara target)
    {
        try
        {
            var origin = target.pos;
            if (origin == null)
                return null;

            var offsets = new[]
            {
                new[] { 0, 1 },
                new[] { 1, 0 },
                new[] { 0, -1 },
                new[] { -1, 0 },
                new[] { 1, 1 },
                new[] { 1, -1 },
                new[] { -1, 1 },
                new[] { -1, -1 }
            };

            for (var i = 0; i < offsets.Length; i++)
            {
                var point = new Point(origin.x + offsets[i][0], origin.z + offsets[i][1]);
                if (!IsUsableTeleportPoint(pc, point))
                    continue;
                return point;
            }

            return IsUsableTeleportPoint(pc, origin) ? origin.Copy() : null;
        }
        catch
        {
            return null;
        }
    }
    private static bool IsUsableTeleportPoint(Chara pc, Point point)
    {
        try
        {
            if (point == null || !point.IsValid || !point.IsInBounds || point.HasChara)
                return false;
            return pc.CanMoveTo(point, false);
        }
        catch
        {
            return false;
        }
    }
    private void SpawnItem(ItemDef item)
    {
        try
        {
            var count = Math.Max(1, ParseInt(_itemCount, 1));
            var lv = Math.Max(1, ParseInt(_itemLv, 1));
            var mat = ParseInt(_itemMat, -1);
            var thing = GameAccess.Spawn.CreateThing(item.Id, mat, lv);
            if (thing == null)
            {
                _itemLog = T("生成失败：", "Spawn failed: ") + item.DisplayName;
                return;
            }
            SetCardNum(thing, count);
            if (item.SeedRefVal >= 0 && thing.trait is TraitSeed)
                TraitSeed.ApplySeed(thing, item.SeedRefVal);
            else if (item.VariantIndex >= 0)
                SetCardIntProperty(thing, "idSkin", item.SkinId);
            var generatedAtFeet = false;
            var pc = GameAccess.Characters.PlayerCharacter;
            if (pc != null && pc.things != null && pc.things.IsFull(thing, true, true))
            {
                var zone = GameAccess.World.CurrentZone;
                if (zone != null && pc.pos != null)
                    generatedAtFeet = zone.TryAddThing(thing, pc.pos, false);
            }

            if (!generatedAtFeet)
                GameAccess.Runtime.Player.AddInventory(thing);

            _itemLog = (generatedAtFeet
                    ? T("已生成到脚下：", "Spawned at feet: ")
                    : T("已生成：", "Spawned: "))
                + SafeThingName(thing) + " x" + count;
        }
        catch (Exception ex)
        {
            _itemLog = T("生成失败：", "Spawn failed: ") + ex.Message;
        }
    }
    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, out var value) ? value : fallback;
    }
    private static void SetCardNum(Card card, int count)
    {
        var method = typeof(Card).GetMethod("SetNum", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method != null)
        {
            method.Invoke(card, new object[] { count });
            return;
        }
        typeof(Card).GetProperty("Num", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(card, count, null);
    }
    private static void SetCardIntProperty(Card card, string name, int value)
    {
        typeof(Card).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(card, value, null);
    }
}
