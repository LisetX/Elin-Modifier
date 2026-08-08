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
    private void ExecuteAutomationAbility(AutomationActionConfig action)
    {
        var pc = GetSafePc();
        if (pc == null)
        {
            FinishAutomationAction(false, AutomationText("未获取到玩家", "Player unavailable", "プレイヤーを取得できません", "Игрок недоступен"));
            return;
        }

        var query = (action.Param1 ?? "").Trim();
        if (query.Length == 0)
        {
            FinishAutomationAction(false, AutomationText("未填写能力或咒语", "Ability or spell is empty", "能力または呪文が入力されていません", "Способность или заклинание не указано"));
            return;
        }

        var ability = FindAiAbility(query);
        if (ability == null)
        {
            FinishAutomationAction(false, AutomationText("找不到能力或咒语: ", "Ability or spell not found: ", "能力または呪文が見つかりません: ", "Способность или заклинание не найдено: ") + query);
            return;
        }

        var act = pc.elements.GetElement(ability.Id)?.act ?? ACT.Create(ability.Id);
        if (act == null)
        {
            FinishAutomationAction(false, AutomationText("无法创建能力实例", "Unable to create ability instance", "能力インスタンスを作成できません", "Не удалось создать экземпляр способности"));
            return;
        }

        var mode = NormalizeAutomationAbilityTarget(action.Param2);
        Card? target = null;
        Point? point = null;
        if (mode == "self" || (mode == "auto" && act.TargetType.Range == TargetRange.Self))
        {
            target = pc;
            point = pc.pos;
        }
        else
        {
            var enemy = FindNearestAutomationEnemy(pc, ParseAutomationInt(action.Param3, 30, 1, 200));
            if (enemy == null)
            {
                FinishAutomationAction(false, AutomationText("范围内没有能力目标", "No ability target in range", "範囲内に能力対象がありません", "В радиусе нет цели для способности"));
                return;
            }
            target = enemy;
            point = enemy.pos;
        }

        var success = pc.UseAbility(act, target, point, false);
        if (success)
        {
            try { GameAccess.Runtime.Player.EndTurn(); }
            catch { }
        }
        FinishAutomationAction(success, success
            ? AutomationText("已使用: ", "Used: ", "使用しました: ", "Использовано: ") + ability.DisplayName
            : AutomationText("能力使用失败: ", "Failed to use ability: ", "能力の使用に失敗: ", "Не удалось использовать способность: ") + ability.DisplayName);
    }
}
