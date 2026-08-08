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
    private void ExecuteAutomationSaveGame()
    {
        var game = GameAccess.Runtime.Game;
        if (game == null)
        {
            FinishAutomationAction(false, AutomationText("当前没有可保存的存档", "No active game to save", "保存できるゲームがありません", "Нет активной игры для сохранения"));
            return;
        }

        if (game.principal.disableManualSave && !GameAccess.Runtime.Debug.enable)
        {
            FinishAutomationAction(false, AutomationText("当前游戏状态禁止手动保存", "Manual saving is disabled in the current game state", "現在のゲーム状態では手動保存できません", "В текущем состоянии игры ручное сохранение запрещено"));
            return;
        }

        var success = game.Save();
        FinishAutomationAction(success, success
            ? AutomationText("存档已保存", "Game saved", "ゲームを保存しました", "Игра сохранена")
            : AutomationText("保存存档失败", "Failed to save the game", "ゲームの保存に失敗しました", "Не удалось сохранить игру"));
    }
    private void StartAutomationLoadGame()
    {
        var game = GameAccess.Runtime.Game;
        var action = _automationCurrentAction;
        if (game == null || action == null)
        {
            FinishAutomationAction(false, AutomationText("当前没有可加载的存档", "No active game to load", "読み込めるゲームがありません", "Нет активной игры для загрузки"));
            return;
        }

        if (game.principal.disableManualSave && !GameAccess.Runtime.Debug.enable)
        {
            FinishAutomationAction(false, AutomationText("当前游戏状态禁止手动加载", "Manual loading is disabled in the current game state", "現在のゲーム状態では手動ロードできません", "В текущем состоянии игры ручная загрузка запрещена"));
            return;
        }

        var slot = Game.id;
        var cloud = game.isCloud;
        if (string.IsNullOrWhiteSpace(slot))
        {
            FinishAutomationAction(false, AutomationText("未获取到当前存档槽位", "Current save slot is unavailable", "現在のセーブスロットを取得できません", "Не удалось определить текущий слот сохранения"));
            return;
        }

        _automationGameLoadRequested = true;
        var accepted = Game.TryLoad(slot, cloud, () =>
        {
            GameAccess.Runtime.Core.WaitForEndOfFrame(() =>
            {
                if (!_automationRunning || !ReferenceEquals(_automationCurrentAction, action))
                    return;

                try
                {
                    GameAccess.Ui.Scene.Init(Scene.Mode.None);
                    Game.Load(slot, cloud);
                    _automationGameLoadRequested = false;
                    FinishAutomationAction(true, AutomationText("存档已加载", "Game loaded", "ゲームをロードしました", "Игра загружена"));
                }
                catch (Exception ex)
                {
                    _automationGameLoadRequested = false;
                    FinishAutomationAction(false, AutomationText("加载存档失败: ", "Failed to load the game: ", "ゲームのロードに失敗しました: ", "Не удалось загрузить игру: ") + ex.Message);
                }
            });
        });

        if (!accepted)
        {
            _automationGameLoadRequested = false;
            FinishAutomationAction(false, AutomationText("当前存档无法加载", "The current save cannot be loaded", "現在のセーブデータを読み込めません", "Текущее сохранение невозможно загрузить"));
        }
    }
}
