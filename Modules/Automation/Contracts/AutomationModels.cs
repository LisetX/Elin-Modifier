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
    private const string AutomationTypeAutoMine = "auto_mine";
    private const string AutomationTypeAutoChop = "auto_chop";
    private const string AutomationTypeAutoHarvest = "auto_harvest";
    private const string AutomationTypeAutoFertilize = "auto_fertilize";
    private const string AutomationTypeSearchContainers = "search_containers";
    private const string AutomationTypeAutoInteract = "auto_interact";
    private const string AutomationTypeAutoKill = "auto_kill";
    private const string AutomationTypeMoveTo = "move_to";
    private const string AutomationTypeUseAbility = "use_ability";
    private const string AutomationTypeNextFloor = "next_floor";
    private const string AutomationTypePickupByValue = "pickup_by_value";
    private const string AutomationTypeWait = "wait";
    private const string AutomationTypeSaveGame = "save_game";
    private const string AutomationTypeLoadGame = "load_game";
    private const float AutomationActionTimeoutSeconds = 300f;
    private const int AutomationKillPathRetryLimit = 8;
    private const int AutomationKillTeleportRetryLimit = 4;
    private const int AutomationKillEmptyRecheckLimit = 3;
    private const float AutomationKillEmptyRecheckDelaySeconds = 0.12f;
    private const int AutomationAutoEatHungerThreshold = 50;
    private const int AutomationAutoSleepSleepinessPhase = 2;
    private const int AutomationAutoSleepStaminaPhase = 1;
    private const int AutomationAutoSleepStaminaPercent = 20;
    private const int AutomationAutoSleepMoveAttemptLimit = 6;
    private const float AutomationPostSleepEatDelaySeconds = 2f;
    private static readonly string[] AutomationActionTypes =
    {
        AutomationTypeAutoMine,
        AutomationTypeAutoChop,
        AutomationTypeAutoHarvest,
        AutomationTypeAutoFertilize,
        AutomationTypeSearchContainers,
        AutomationTypeAutoInteract,
        AutomationTypeAutoKill,
        AutomationTypeMoveTo,
        AutomationTypeUseAbility,
        AutomationTypeNextFloor,
        AutomationTypePickupByValue,
        AutomationTypeWait,
        AutomationTypeSaveGame,
        AutomationTypeLoadGame
    };
    private static readonly Dictionary<Type, bool> AutomationInteractableTraitTypeCache =
        new Dictionary<Type, bool>();
    private static readonly int[,] AutomationSleepMoveOffsets =
    {
        { 0, 1 },
        { 1, 0 },
        { 0, -1 },
        { -1, 0 },
        { 1, 1 },
        { 1, -1 },
        { -1, -1 },
        { -1, 1 }
    };
    private sealed class AutomationActionConfig
    {
        public bool Enabled = true;
        public string Type = AutomationTypeAutoMine;
        public string Param1 = "";
        public string Param2 = "";
        public string Param3 = "";
        public string Param4 = "";
        public float DelaySeconds;

        public AutomationActionConfig Clone()
        {
            return new AutomationActionConfig
            {
                Enabled = Enabled,
                Type = Type,
                Param1 = Param1,
                Param2 = Param2,
                Param3 = Param3,
                Param4 = Param4,
                DelaySeconds = DelaySeconds
            };
        }
    }
    private sealed class AutomationCombatGoal : GoalAutoCombat
    {
        public AutomationCombatGoal(Chara enemy) : base(enemy)
        {
        }

        public override bool TryAbortCombat()
        {
            var condition = owner?.GetCondition<ConDeathSentense>();
            if (condition != null && condition.value <= 3)
            {
                Msg.Say("abort_sentense");
                return true;
            }
            return false;
        }
    }
    private readonly struct AutomationDamageState
    {
        public readonly int Hp;
        public readonly int Mana;

        public AutomationDamageState(Chara? target)
        {
            try { Hp = target?.hp ?? int.MinValue; }
            catch { Hp = int.MinValue; }
            try { Mana = target?.mana?.value ?? int.MinValue; }
            catch { Mana = int.MinValue; }
        }

        public bool WasDamaged(Chara target)
        {
            try
            {
                if (Hp != int.MinValue && target.hp < Hp)
                    return true;
            }
            catch { }
            try
            {
                if (Mana != int.MinValue && target.mana != null && target.mana.value < Mana)
                    return true;
            }
            catch { }
            return false;
        }
    }
    private sealed class AutomationProfile
    {
        public string Name = "配置 1";
        public string FileName = "";
        public bool Loop = true;
        public readonly List<AutomationActionConfig> Actions = new List<AutomationActionConfig>();

        public AutomationProfile Clone(string name)
        {
            var result = new AutomationProfile { Name = name, Loop = Loop };
            for (var i = 0; i < Actions.Count; i++)
                result.Actions.Add(Actions[i].Clone());
            return result;
        }
    }
}
