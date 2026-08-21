using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

internal sealed partial class AiInstructionModule
{
    private const string GoToActionId = "ElinModifier.AiInstruction.GoTo";
    private const string AttackActionId = "ElinModifier.AiInstruction.Attack";
    private const string AbilityActionId = "ElinModifier.AiInstruction.Ability";
    private const int AbilityPageSize = 10;
    private static readonly Color32 MoveFill = new Color32(20, 255, 64, 74);
    private static readonly Color32 MoveBorder = new Color32(32, 255, 72, 235);
    private static readonly Color32 AttackFill = new Color32(255, 20, 20, 74);
    private static readonly Color32 AttackBorder = new Color32(255, 32, 24, 235);

    private readonly ElinModifierPlugin _host;
    private readonly List<ActiveInstruction> _activeInstructions = new List<ActiveInstruction>();
    private readonly List<ProjectedCellOverlay> _targetCells = new List<ProjectedCellOverlay>();
    private readonly Dictionary<long, int> _targetCellIndexes = new Dictionary<long, int>();
    private readonly ProjectedCellOverlayRenderer _targetOverlayRenderer =
        new ProjectedCellOverlayRenderer("ElinModifier.AiInstruction.TargetOverlay", -31990);
    private Chara? _actor;
    private Act? _ability;
    private PendingInstruction _pending;
    private bool _suppressMouseUntilRelease;

    internal AiInstructionModule(ElinModifierPlugin host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    internal bool Enabled { get; private set; }

    internal void Load(bool enabled, string json)
    {
        LoadAutoCombatConfiguration(json);
        SetState(enabled);
    }

    internal void Reset()
    {
        ResetAutoCombatConfiguration();
        SetState(false);
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        SetState(enabled);
        return true;
    }

    internal void Shutdown()
    {
        ClearPending();
        CancelActiveInstructions();
        CancelAutoCombatGoals();
        _targetOverlayRenderer.Dispose();
    }

    internal void Tick()
    {
        for (var i = _activeInstructions.Count - 1; i >= 0; i--)
        {
            var active = _activeInstructions[i];
            if (!Enabled || !IsValidActor(active.Actor) ||
                !ReferenceEquals(active.Actor.ai, active.Action))
            {
                _activeInstructions.RemoveAt(i);
                continue;
            }

            try
            {
                if (active.Action.Tick() != AIAct.Status.Running)
                    _activeInstructions.RemoveAt(i);
            }
            catch
            {
                try { active.Action.Cancel(); }
                catch { }
                _activeInstructions.RemoveAt(i);
            }
        }
    }

    internal void LateTick()
    {
        RefreshTargetOverlays();
    }

    internal void AddInteractionOptions(ActPlan? plan, PointTarget? pointTarget)
    {
        if (!Enabled || plan == null || pointTarget == null || plan.input != ActInput.AllAction)
            return;

        Chara? actor;
        try
        {
            actor = pointTarget.TargetChara;
            if (!IsValidActor(actor))
                return;
        }
        catch
        {
            return;
        }

        var previousIgnoreCondition = plan.ignoreAddCondition;
        try
        {
            plan.ignoreAddCondition = true;
            AddAutoCombatOption(plan, actor);
            plan.TrySetAct(
                GoToActionId,
                () => BeginPointSelection(actor),
                actor,
                null,
                -1,
                false,
                false,
                false);
            plan.TrySetAct(
                AttackActionId,
                () => BeginAttackSelection(actor),
                actor,
                null,
                -1,
                false,
                false,
                false);
            plan.TrySetAct(
                AbilityActionId,
                () => ShowAbilitySelection(actor),
                actor,
                null,
                -1,
                false,
                false,
                false);
        }
        catch
        {
        }
        finally
        {
            plan.ignoreAddCondition = previousIgnoreCondition;
        }
    }

    internal bool ProcessAdventureInput()
    {
        if (_suppressMouseUntilRelease)
        {
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
                _suppressMouseUntilRelease = false;
            return false;
        }

        if (!Enabled || _pending == PendingInstruction.None)
            return true;

        if (Input.GetMouseButtonDown(1))
        {
            ClearPending();
            _suppressMouseUntilRelease = true;
            GameAccess.Messages.SayRaw(T("AI指示已取消。", "AI instruction cancelled."));
            return false;
        }

        if (!Input.GetMouseButtonDown(0))
            return true;

        PointTarget? selected;
        try { selected = EClass.scene?.mouseTarget; }
        catch { selected = null; }
        if (!TryCompleteSelection(selected))
            ShowSelectionPrompt();
        _suppressMouseUntilRelease = true;
        return false;
    }

    internal bool TryGetActionText(DynamicAct? act, out string text)
    {
        text = "";
        if (!Enabled || act == null)
            return false;
        if (TryGetAutoCombatActionText(act.id, out text))
            return true;
        switch (act.id)
        {
            case GoToActionId:
                text = T("前往指定位置", "Go to specified location");
                return true;
            case AttackActionId:
                text = T("攻击指定NPC", "Attack specified NPC");
                return true;
            case AbilityActionId:
                text = T("使用指定能力", "Use specified ability");
                return true;
            default:
                return false;
        }
    }

    private void SetState(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
        {
            ClearPending();
            CancelActiveInstructions();
            CancelAutoCombatGoals();
            _targetOverlayRenderer.Clear();
        }
    }

    private bool BeginPointSelection(Chara actor)
    {
        if (!TrySetPendingActor(actor))
            return false;
        _pending = PendingInstruction.GoTo;
        ShowSelectionPrompt();
        return false;
    }

    private bool BeginAttackSelection(Chara actor)
    {
        if (!TrySetPendingActor(actor))
            return false;
        _pending = PendingInstruction.Attack;
        ShowSelectionPrompt();
        return false;
    }

    private bool ShowAbilitySelection(Chara actor)
    {
        if (!TrySetPendingActor(actor))
            return false;

        var abilities = CollectAbilities(actor);
        if (abilities.Count == 0)
        {
            ClearPending();
            GameAccess.Messages.SayRaw(T("该NPC没有可用能力。", "This NPC has no available abilities."));
            return false;
        }

        try
        {
            OpenAbilitySelection(actor, abilities, 0);
        }
        catch
        {
            ClearPending();
            GameAccess.Messages.SayRaw(T("能力列表打开失败。", "Failed to open the ability list."));
        }
        return false;
    }

    private void OpenAbilitySelection(
        Chara actor,
        IReadOnlyList<AbilityChoice> abilities,
        int pageIndex)
    {
        var pageCount = Math.Max(1, (abilities.Count + AbilityPageSize - 1) / AbilityPageSize);
        pageIndex = Math.Max(0, Math.Min(pageIndex, pageCount - 1));
        var firstIndex = pageIndex * AbilityPageSize;
        var lastIndex = Math.Min(firstIndex + AbilityPageSize, abilities.Count);
        var entries = new List<AbilityDialogEntry>(AbilityPageSize + 2);
        if (pageCount > 1)
        {
            var previousPage = pageIndex == 0 ? pageCount - 1 : pageIndex - 1;
            entries.Add(new AbilityDialogEntry(T("上一页", "Previous page"), previousPage));
        }
        for (var i = firstIndex; i < lastIndex; i++)
            entries.Add(new AbilityDialogEntry(abilities[i]));
        if (pageCount > 1)
        {
            var nextPage = pageIndex + 1 >= pageCount ? 0 : pageIndex + 1;
            entries.Add(new AbilityDialogEntry(T("下一页", "Next page"), nextPage));
        }

        var title = T("选择能力", "Select ability");
        if (pageCount > 1)
            title += " (" + (pageIndex + 1) + "/" + pageCount + ")";
        Dialog.Choice(title, dialog =>
        {
            dialog.option.canClose = true;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                dialog.list.AddButton(
                    entry,
                    entry.Label,
                    () =>
                    {
                        dialog.Close();
                        if (entry.TargetPage >= 0)
                        {
                            OpenAbilitySelection(actor, abilities, entry.TargetPage);
                            return;
                        }
                        if (entry.Choice != null)
                            BeginAbilitySelection(actor, entry.Choice.Ability);
                    },
                    button =>
                    {
                        if (entry.Choice != null)
                            BindAbilitySelectionButton(actor, button, entry.Choice);
                    });
            }
        });
    }

    private void BeginAbilitySelection(Chara actor, Act ability)
    {
        if (!TrySetPendingActor(actor))
            return;
        _ability = ability;
        try
        {
            if (ability.TargetType.Range == TargetRange.Self)
            {
                QueueAbility(actor, ability, actor, actor.pos);
                ClearPending();
                return;
            }
        }
        catch
        {
        }
        _pending = PendingInstruction.Ability;
        ShowSelectionPrompt();
    }

    private bool TryCompleteSelection(PointTarget? selected)
    {
        var actor = _actor;
        if (!IsValidActor(actor) || selected?.pos == null || !selected.pos.IsValid)
        {
            ClearPending();
            return true;
        }

        switch (_pending)
        {
            case PendingInstruction.GoTo:
                QueueMove(actor!, selected.pos);
                ClearPending();
                return true;
            case PendingInstruction.Attack:
                {
                    Chara? target;
                    try { target = selected.TargetChara; }
                    catch { target = null; }
                    if (!IsValidAttackTarget(actor!, target))
                        return false;
                    QueueAttack(actor!, target!);
                    ClearPending();
                    return true;
                }
            case PendingInstruction.Ability:
                {
                    var ability = _ability;
                    if (ability == null)
                    {
                        ClearPending();
                        return true;
                    }
                    Card? target = null;
                    try { target = selected.card ?? selected.TargetChara; }
                    catch { }
                    QueueAbility(actor!, ability, target, selected.pos);
                    ClearPending();
                    return true;
                }
            default:
                return true;
        }
    }

    private List<AbilityChoice> CollectAbilities(Chara actor)
    {
        var result = new List<AbilityChoice>();
        var ids = new HashSet<int>();
        try
        {
            var items = actor.ability?.list?.items;
            if (items == null)
                return result;
            for (var i = 0; i < items.Count; i++)
            {
                var act = items[i]?.act;
                if (act == null)
                    continue;
                var id = act.id;
                if (!ids.Add(id))
                    continue;
                var name = GetAbilityName(act);
                var level = GetAbilityLevel(actor, act);
                result.Add(new AbilityChoice(
                    act,
                    name,
                    level,
                    name + "  " + T("等级", "Level") + ": " + level));
            }
        }
        catch
        {
        }
        result.Sort((left, right) => string.Compare(
            left.Name,
            right.Name,
            StringComparison.CurrentCulture));
        return result;
    }

    private string GetAbilityName(Act ability)
    {
        try
        {
            var name = ability.source?.GetName();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }
        return T("未命名能力", "Unnamed ability");
    }

    private static int GetAbilityLevel(Chara actor, Act ability)
    {
        try
        {
            var element = actor.elements.GetElement(ability.id);
            if (element != null)
            {
                var displayLevel = Math.Max(0, element.DisplayValue);
                if (displayLevel > 0)
                    return displayLevel;
                var baseLevel = Math.Max(0, element.ValueWithoutLink);
                if (baseLevel > 0)
                    return baseLevel;
            }
        }
        catch
        {
        }
        try { return Math.Max(0, ability.source?.LV ?? 0); }
        catch { return 0; }
    }

    private static bool IsValidActor(Chara? actor)
    {
        try
        {
            return actor != null && !ReferenceEquals(actor, EClass.pc) && actor.IsPCFaction &&
                   !actor.isDead && actor.ExistsOnMap;
        }
        catch { return false; }
    }

    private static bool IsValidAttackTarget(Chara actor, Chara? target)
    {
        try
        {
            return target != null && !ReferenceEquals(actor, target) && !ReferenceEquals(target, EClass.pc) &&
                   !target.isDead && target.ExistsOnMap;
        }
        catch
        {
            return false;
        }
    }

    private bool TrySetPendingActor(Chara actor)
    {
        if (!Enabled || !IsValidActor(actor))
        {
            ClearPending();
            return false;
        }
        _actor = actor;
        _ability = null;
        _pending = PendingInstruction.None;
        return true;
    }

    private void QueueMove(Chara actor, Point destination)
    {
        var point = new Point(destination);
        StartInstruction(actor, new AI_Goto(point, 0), TargetOverlayKind.Move, point, null);
    }

    private void QueueAttack(Chara actor, Chara target)
    {
        actor.enemy = target;
        var combat = new GoalCombat
        {
            destEnemy = target,
            tc = target
        };
        StartInstruction(actor, combat, TargetOverlayKind.Attack, null, target);
    }

    private void QueueAbility(Chara actor, Act ability, Card? target, Point destination)
    {
        var point = new Point(destination);
        var action = new DynamicAIAct(
            GetAbilityName(ability),
            () => actor.UseAbility(ability, target, point, false),
            true);
        if (!ReferenceEquals(target, actor))
            action.pos = point;
        StartInstruction(actor, action);
    }

    private void StartInstruction(
        Chara actor,
        AIAct action,
        TargetOverlayKind overlayKind = TargetOverlayKind.None,
        Point? targetPoint = null,
        Chara? targetChara = null)
    {
        for (var i = _activeInstructions.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_activeInstructions[i].Actor, actor))
                _activeInstructions.RemoveAt(i);
        }
        actor.SetAI(action);
        _activeInstructions.Add(new ActiveInstruction(actor, action, overlayKind, targetPoint, targetChara));
    }

    private void CancelActiveInstructions()
    {
        for (var i = 0; i < _activeInstructions.Count; i++)
        {
            try
            {
                var active = _activeInstructions[i];
                if (ReferenceEquals(active.Actor.ai, active.Action))
                    active.Action.Cancel();
            }
            catch
            {
            }
        }
        _activeInstructions.Clear();
    }

    private void RefreshTargetOverlays()
    {
        if (!Enabled)
        {
            _targetOverlayRenderer.Clear();
            return;
        }

        _targetCells.Clear();
        _targetCellIndexes.Clear();
        for (var i = 0; i < _activeInstructions.Count; i++)
        {
            var active = _activeInstructions[i];
            switch (active.OverlayKind)
            {
                case TargetOverlayKind.Move:
                    AddTargetCell(active.TargetPoint, TargetOverlayKind.Move);
                    break;
                case TargetOverlayKind.Attack:
                    if (IsValidAttackTarget(active.Actor, active.TargetChara))
                        AddTargetCell(active.TargetChara!.pos, TargetOverlayKind.Attack);
                    break;
            }
        }

        if (_pending != PendingInstruction.None && !IsValidActor(_actor))
            ClearPending();
        if (_pending == PendingInstruction.GoTo || _pending == PendingInstruction.Attack)
        {
            PointTarget? hovered;
            try { hovered = GameAccess.Ui.Scene?.mouseTarget; }
            catch { hovered = null; }
            if (_pending == PendingInstruction.GoTo)
                AddTargetCell(hovered?.pos, TargetOverlayKind.Move);
            else
            {
                Chara? target;
                try { target = hovered?.TargetChara; }
                catch { target = null; }
                if (IsValidActor(_actor) && IsValidAttackTarget(_actor!, target))
                    AddTargetCell(target!.pos, TargetOverlayKind.Attack);
            }
        }

        _targetOverlayRenderer.Render(_targetCells);
    }

    private void AddTargetCell(Point? point, TargetOverlayKind kind)
    {
        if (point == null || !point.IsValid || kind == TargetOverlayKind.None)
            return;
        var key = ((long)point.x << 32) | (uint)point.z;
        var overlay = kind == TargetOverlayKind.Move
            ? new ProjectedCellOverlay(point, MoveFill, MoveBorder)
            : new ProjectedCellOverlay(point, AttackFill, AttackBorder);
        if (_targetCellIndexes.TryGetValue(key, out var index))
        {
            _targetCells[index] = overlay;
            return;
        }
        _targetCellIndexes[key] = _targetCells.Count;
        _targetCells.Add(overlay);
    }

    private void ShowSelectionPrompt()
    {
        switch (_pending)
        {
            case PendingInstruction.GoTo:
                GameAccess.Messages.SayRaw(T("请左键选择前往位置，右键取消。", "Left-click a destination; right-click to cancel."));
                break;
            case PendingInstruction.Attack:
                GameAccess.Messages.SayRaw(T("请左键选择要攻击的NPC，右键取消。", "Left-click an NPC to attack; right-click to cancel."));
                break;
            case PendingInstruction.Ability:
                GameAccess.Messages.SayRaw(T("请左键选择能力目标，右键取消。", "Left-click an ability target; right-click to cancel."));
                break;
        }
    }

    private void ClearPending()
    {
        _actor = null;
        _ability = null;
        _pending = PendingInstruction.None;
    }

    private string T(string chinese, string english)
    {
        return _host.TranslateModuleText(chinese, english);
    }

    private enum PendingInstruction
    {
        None,
        GoTo,
        Attack,
        Ability
    }

    private enum TargetOverlayKind
    {
        None,
        Move,
        Attack
    }

    private sealed class ActiveInstruction
    {
        internal ActiveInstruction(
            Chara actor,
            AIAct action,
            TargetOverlayKind overlayKind,
            Point? targetPoint,
            Chara? targetChara)
        {
            Actor = actor;
            Action = action;
            OverlayKind = overlayKind;
            TargetPoint = targetPoint == null ? null : new Point(targetPoint);
            TargetChara = targetChara;
        }

        internal Chara Actor { get; }
        internal AIAct Action { get; }
        internal TargetOverlayKind OverlayKind { get; }
        internal Point? TargetPoint { get; }
        internal Chara? TargetChara { get; }
    }

    private sealed class AbilityChoice
    {
        internal AbilityChoice(Act ability, string name, int level, string label)
        {
            Ability = ability;
            Name = name;
            Level = level;
            Label = label;
        }

        internal Act Ability { get; }
        internal string Name { get; }
        internal int Level { get; }
        internal string Label { get; }
    }

    private sealed class AbilityDialogEntry
    {
        internal AbilityDialogEntry(AbilityChoice choice)
        {
            Choice = choice;
            Label = choice.Label;
            TargetPage = -1;
        }

        internal AbilityDialogEntry(string label, int targetPage)
        {
            Label = label;
            TargetPage = targetPage;
        }

        internal AbilityChoice? Choice { get; }
        internal string Label { get; }
        internal int TargetPage { get; }
    }
}

internal static class AiInstructionPatchContext
{
    internal static AiInstructionModule? Current =>
        ElinModifierPlugin.ActiveModules?.AiInstruction;
}

[HarmonyPatch(typeof(ActPlan), "_Update")]
internal static class ActPlanAiInstructionPatch
{
    private static void Postfix(ActPlan __instance, PointTarget __0)
    {
        AiInstructionPatchContext.Current?.AddInteractionOptions(__instance, __0);
    }
}

[HarmonyPatch(typeof(AM_Adv), "_OnUpdateInput")]
internal static class AdventureInputAiInstructionPatch
{
    private static bool Prefix()
    {
        return AiInstructionPatchContext.Current?.ProcessAdventureInput() ?? true;
    }
}

[HarmonyPatch(typeof(DynamicAct), "GetText")]
internal static class DynamicActAiInstructionTextPatch
{
    private static void Postfix(DynamicAct __instance, ref string __result)
    {
        if (AiInstructionPatchContext.Current?.TryGetActionText(__instance, out var text) == true)
            __result = text;
    }
}
