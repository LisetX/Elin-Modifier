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
    private void ExecuteSimulatedAdvance()
    {
        if (!int.TryParse(_simulateAdvanceMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            _log = T("推进时间输入不是数字", "Advance time is not a number");
            return;
        }
        if (minutes <= 0)
        {
            _log = T("推进时间必须大于0", "Advance time must be greater than 0");
            return;
        }

        try
        {
            if (!HasCharacterData() || GameAccess.World.CurrentWorld?.date == null || GameAccess.World.CurrentMap == null || GameAccess.World.CurrentZone == null)
            {
                _log = T("模拟推进只能在存档内执行", "Simulated advance can only run inside a loaded save");
                return;
            }

            _simulatedAdvanceRunning = true;
            GameAccess.World.CurrentWorld.date.AdvanceMin(minutes);
            _log = T("模拟推进完成: ", "Simulated advance completed: ") + minutes.ToString(CultureInfo.InvariantCulture) + T("分钟", " minutes");
        }
        catch (Exception ex)
        {
            _log = T("模拟推进失败: ", "Simulated advance failed: ") + ex.Message;
        }
        finally
        {
            _simulatedAdvanceRunning = false;
        }
    }
    private void ExecuteGenerateDungeon()
    {
        if (!int.TryParse(_generateDungeonDangerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedDanger))
        {
            _log = T("危险度输入不是数字", "Danger level is not a number");
            return;
        }

        try
        {
            var currentZone = GameAccess.World.CurrentZone;
            var region = GameAccess.World.CurrentRegion;
            if (!HasCharacterData() || GameAccess.World.CurrentWorld?.date == null || currentZone == null || region == null)
            {
                _log = T("生成地牢只能在存档内执行", "Dungeon generation can only run inside a loaded save");
                return;
            }

            var topZone = currentZone.GetTopZone();
            if (!DungeonGenerationPolicy.CanGenerateAtCurrentArea(
                    currentZone.IsRegion,
                    _modules.Moongate.IsInsideMoongateWorld,
                    topZone != null,
                    currentZone.instance != null,
                    topZone?.instance != null,
                    currentZone.isExternalZone,
                    topZone?.isExternalZone == true))
            {
                _log = T("当前区域无法生成地牢", "A dungeon cannot be generated in the current area");
                return;
            }

            var creationDanger = DungeonGenerationPolicy.ResolveCreationDanger(requestedDanger);
            var dungeon = region.CreateRandomSite(
                currentZone,
                DungeonGenerationPolicy.SearchRadius,
                null,
                true,
                creationDanger);
            if (dungeon == null)
            {
                _log = T("没有找到可生成地牢的位置", "No valid dungeon location was found");
                return;
            }

            dungeon.isKnown = true;
            _log = T("已生成地牢: ", "Generated dungeon: ") + dungeon.Name +
                   " / " + T("危险度", "Danger level") + ": " + dungeon.DangerLv.ToString(CultureInfo.InvariantCulture) +
                   " / " + T("坐标", "Coordinates") + ": " +
                   dungeon.x.ToString(CultureInfo.InvariantCulture) + ", " + dungeon.y.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            _log = T("生成地牢失败: ", "Dungeon generation failed: ") + ex.Message;
        }
    }
}
