using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal sealed partial class AiInstructionModule
{
    private const string AutoCombatEnabledActionId = "ElinModifier.AiInstruction.AutoCombat.Enabled";
    private const string AutoCombatDisabledActionId = "ElinModifier.AiInstruction.AutoCombat.Disabled";
    private readonly Dictionary<string, HashSet<int>> _autoCombatNpcUidsBySave =
        new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
    private readonly Dictionary<int, AutoCombatGoal> _autoCombatGoals =
        new Dictionary<int, AutoCombatGoal>();

    internal void LoadAutoCombatConfiguration(string json)
    {
        _autoCombatNpcUidsBySave.Clear();
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            var root = JObject.Parse(json);
            var saves = root["aiInstructionAutoCombatBySave"] as JObject;
            if (saves == null)
                return;

            foreach (var saveProperty in saves.Properties())
            {
                if (string.IsNullOrWhiteSpace(saveProperty.Name) || saveProperty.Value is not JArray values)
                    continue;

                var uids = new HashSet<int>();
                for (var i = 0; i < values.Count; i++)
                {
                    if (int.TryParse(values[i]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) &&
                        uid > 0)
                    {
                        uids.Add(uid);
                    }
                }

                if (uids.Count > 0)
                    _autoCombatNpcUidsBySave[saveProperty.Name] = uids;
            }
        }
        catch
        {
            _autoCombatNpcUidsBySave.Clear();
        }
    }

    internal void AppendAutoCombatConfiguration(StringBuilder sb)
    {
        sb.AppendLine("  \"aiInstructionAutoCombatBySave\": {");
        var saves = _autoCombatNpcUidsBySave
            .Select(pair => new
            {
                SaveId = pair.Key,
                Uids = pair.Value.Where(uid => uid > 0).OrderBy(uid => uid).ToList()
            })
            .Where(pair => pair.Uids.Count > 0)
            .OrderBy(pair => pair.SaveId, StringComparer.Ordinal)
            .ToList();

        for (var saveIndex = 0; saveIndex < saves.Count; saveIndex++)
        {
            var save = saves[saveIndex];
            sb.Append("    ").Append(JsonConvert.ToString(save.SaveId)).Append(": [");
            for (var uidIndex = 0; uidIndex < save.Uids.Count; uidIndex++)
            {
                if (uidIndex > 0)
                    sb.Append(", ");
                sb.Append(save.Uids[uidIndex].ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine(saveIndex == saves.Count - 1 ? "]" : "],");
        }

        sb.AppendLine("  },");
    }

    internal void PrepareAutoCombatTurn(Chara? actor)
    {
        if (!Enabled || !IsValidActor(actor) || !IsAutoCombatEnabled(actor!))
            return;
        if (HasActiveInstruction(actor!))
            return;

        try
        {
            var uid = actor!.uid;
            if (_autoCombatGoals.TryGetValue(uid, out var assignment) &&
                !ReferenceEquals(actor.ai, assignment.Goal))
            {
                _autoCombatGoals.Remove(uid);
            }

            if (actor.ai is GoalCombat currentCombat && currentCombat.IsRunning &&
                IsAutoCombatTarget(actor, actor.enemy))
            {
                return;
            }

            var target = FindNearestAutoCombatTarget(actor);
            if (target == null)
                return;

            actor.enemy = target;
            var goal = new GoalCombat
            {
                destEnemy = target,
                tc = target
            };
            actor.SetAI(goal);
            _autoCombatGoals[uid] = new AutoCombatGoal(actor, goal);
        }
        catch
        {
        }
    }

    private void AddAutoCombatOption(ActPlan plan, Chara actor)
    {
        var actionId = IsAutoCombatEnabled(actor)
            ? AutoCombatEnabledActionId
            : AutoCombatDisabledActionId;
        plan.TrySetAct(
            actionId,
            () => ToggleAutoCombat(actor),
            actor,
            null,
            -1,
            false,
            false,
            false);
    }

    private bool ToggleAutoCombat(Chara actor)
    {
        if (!IsValidActor(actor) || !TryGetAutoCombatSaveState(true, out var saveId, out var enabledUids))
        {
            GameAccess.Messages.SayRaw(T("当前存档不可用。", "The current save is unavailable."));
            return false;
        }

        int uid;
        try { uid = actor.uid; }
        catch { uid = 0; }
        if (uid <= 0)
        {
            GameAccess.Messages.SayRaw(T("该NPC无法保存自动寻敌设置。", "Auto seek enemies cannot be saved for this NPC."));
            return false;
        }

        var enabled = !enabledUids.Contains(uid);
        if (enabled)
        {
            enabledUids.Add(uid);
        }
        else
        {
            enabledUids.Remove(uid);
            CancelAutoCombatGoal(actor);
            if (enabledUids.Count == 0)
                _autoCombatNpcUidsBySave.Remove(saveId);
        }

        _host.SaveConfigFromModule(false);
        GameAccess.Messages.SayRaw(enabled
            ? T("自动寻敌已开启。", "Auto seek enemies enabled.")
            : T("自动寻敌已关闭。", "Auto seek enemies disabled."));
        return false;
    }

    private bool TryGetAutoCombatActionText(string actionId, out string text)
    {
        switch (actionId)
        {
            case AutoCombatEnabledActionId:
                text = T("自动寻敌:开", "Auto seek enemies: On");
                return true;
            case AutoCombatDisabledActionId:
                text = T("自动寻敌:关", "Auto seek enemies: Off");
                return true;
            default:
                text = "";
                return false;
        }
    }

    private bool IsAutoCombatEnabled(Chara actor)
    {
        if (!TryGetAutoCombatSaveState(false, out _, out var enabledUids))
            return false;
        try { return actor.uid > 0 && enabledUids.Contains(actor.uid); }
        catch { return false; }
    }

    private bool TryGetAutoCombatSaveState(
        bool create,
        out string saveId,
        out HashSet<int> enabledUids)
    {
        saveId = "";
        enabledUids = null!;
        try { saveId = GameAccess.Runtime.CurrentSaveId ?? ""; }
        catch { }
        saveId = saveId.Trim();
        if (saveId.Length == 0)
            return false;

        if (_autoCombatNpcUidsBySave.TryGetValue(saveId, out enabledUids!))
            return true;
        if (!create)
            return false;

        enabledUids = new HashSet<int>();
        _autoCombatNpcUidsBySave[saveId] = enabledUids;
        return true;
    }

    private bool HasActiveInstruction(Chara actor)
    {
        for (var i = 0; i < _activeInstructions.Count; i++)
        {
            if (ReferenceEquals(_activeInstructions[i].Actor, actor))
                return true;
        }
        return false;
    }

    private static Chara? FindNearestAutoCombatTarget(Chara actor)
    {
        Chara? nearest = null;
        var nearestDistance = int.MaxValue;
        try
        {
            var candidates = GameAccess.World.CurrentCharacters;
            if (candidates == null)
                return null;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!IsAutoCombatTarget(actor, candidate))
                    continue;

                var distance = actor.Dist(candidate);
                if (distance >= nearestDistance)
                    continue;
                nearest = candidate;
                nearestDistance = distance;
            }
        }
        catch
        {
        }
        return nearest;
    }

    private static bool IsAutoCombatTarget(Chara actor, Chara? candidate)
    {
        if (candidate == null || ReferenceEquals(actor, candidate))
            return false;
        try
        {
            return !candidate.isDead && candidate.ExistsOnMap && candidate.IsAliveInCurrentZone &&
                   !candidate.IsPCFactionOrMinion && !candidate.IsPCParty && actor.IsHostile(candidate);
        }
        catch
        {
            return false;
        }
    }

    private void CancelAutoCombatGoal(Chara actor)
    {
        int uid;
        try { uid = actor.uid; }
        catch { return; }
        if (!_autoCombatGoals.TryGetValue(uid, out var assignment))
            return;
        _autoCombatGoals.Remove(uid);
        CancelAutoCombatGoal(assignment);
    }

    private void CancelAutoCombatGoals()
    {
        foreach (var assignment in _autoCombatGoals.Values)
            CancelAutoCombatGoal(assignment);
        _autoCombatGoals.Clear();
    }

    private static void CancelAutoCombatGoal(AutoCombatGoal assignment)
    {
        try
        {
            if (ReferenceEquals(assignment.Actor.ai, assignment.Goal))
                assignment.Actor.SetEnemy(null);
        }
        catch
        {
        }
    }

    private void ResetAutoCombatConfiguration()
    {
        CancelAutoCombatGoals();
        _autoCombatNpcUidsBySave.Clear();
    }

    private sealed class AutoCombatGoal
    {
        internal AutoCombatGoal(Chara actor, GoalCombat goal)
        {
            Actor = actor;
            Goal = goal;
        }

        internal Chara Actor { get; }
        internal GoalCombat Goal { get; }
    }
}

[HarmonyPatch(typeof(Chara), "Tick")]
internal static class CharaAiInstructionAutoCombatPatch
{
    private static void Prefix(Chara __instance)
    {
        AiInstructionPatchContext.Current?.PrepareAutoCombatTurn(__instance);
    }
}
