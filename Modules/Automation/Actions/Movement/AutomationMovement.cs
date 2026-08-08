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
    private void StartAutomationMove(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        if (pc == null)
        {
            FinishAutomationAction(false, AutomationText("未获取到玩家", "Player unavailable", "プレイヤーを取得できません", "Игрок недоступен"));
            return;
        }

        if (!int.TryParse((action.Param1 ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse((action.Param2 ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var z))
        {
            FinishAutomationAction(false, AutomationText("XZ坐标无效", "Invalid XZ coordinates", "XZ座標が無効です", "Некорректные координаты XZ"));
            return;
        }

        var point = new Point(x, z);
        if (!point.IsValid || !point.IsInBounds)
        {
            FinishAutomationAction(false, AutomationText("目标坐标不在当前地图范围内", "Target is outside the current map", "目標座標は現在のマップ範囲外です", "Цель находится за пределами текущей карты"));
            return;
        }

        if (pc.pos.Equals(point))
        {
            FinishAutomationAction(true, AutomationText("已经位于目标坐标", "Already at the destination", "すでに目標座標にいます", "Игрок уже находится в точке назначения"));
            return;
        }

        var goal = new AI_Goto(point, 0);
        pc.SetAIImmediate(goal);
        _automationActionAi = goal;
    }
}
