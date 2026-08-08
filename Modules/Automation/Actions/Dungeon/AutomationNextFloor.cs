using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class AutomationModule
{
    private void StartAutomationNextFloor(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        if (pc == null)
        {
            FinishAutomationAction(false, AutomationText("未获取到玩家", "Player unavailable", "プレイヤーを取得できません", "Игрок недоступен"));
            return;
        }

        Thing? best = null;
        var bestDistance = int.MaxValue;
        try
        {
            foreach (var thing in GameAccess.World.CurrentThings!)
            {
                if (thing == null || !thing.ExistsOnMap || !(thing.trait is TraitStairsDown))
                    continue;
                var distance = pc.Dist(thing);
                if (distance >= bestDistance) continue;
                best = thing;
                bestDistance = distance;
            }
        }
        catch { }

        if (best == null)
        {
            FinishAutomationAction(false, AutomationText("当前地图没有下一层入口", "No next-floor entrance on this map", "現在のマップに次階層への入口がありません", "На текущей карте нет входа на следующий этаж"));
            return;
        }

        _automationTargetThing = best;
        if (pc.Dist(best) == 0)
        {
            _automationZoneMoveRequested = true;
            ((TraitStairsDown)best.trait).MoveZone(true);
            return;
        }

        var goal = new AI_Goto(best, 0);
        pc.SetAIImmediate(goal);
        _automationActionAi = goal;
    }
}
