using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void BuildLGuiDebugRoots()
    {
        _lGuiDebugRoots.Clear();
        // Keep the default debug surface focused on the game and loaded mods. The
        // modifier itself remains searchable from the root selector, but no longer
        // occupies the first/default root or the normal root cycle.
        AddLGuiDebugRoot("[Game runtime] EClass", typeof(EClass));
        AddLGuiDebugRoot("[Game runtime] EClass.game", ReadLGuiDebugTarget(() => GameAccess.Runtime.Game));
        AddLGuiDebugRoot("[Game runtime] EClass.pc", ReadLGuiDebugTarget(() => GameAccess.Characters.PlayerCharacter));
        AddLGuiDebugRoot("[Game runtime] EClass.player", ReadLGuiDebugTarget(() => GameAccess.Runtime.Player));
        AddLGuiDebugRoot("[Game runtime] EClass.world", ReadLGuiDebugTarget(() => GameAccess.World.CurrentWorld));
        AddLGuiDebugRoot("[Game runtime] EClass.world.date", ReadLGuiDebugTarget(() => GameAccess.World.CurrentWorld?.date));
        AddLGuiDebugRoot("[Game runtime] EClass.scene", ReadLGuiDebugTarget(() => GameAccess.Ui.Scene));
        AddLGuiDebugRoot("[Game runtime] EClass._map", ReadLGuiDebugTarget(() => GameAccess.World.CurrentMap));
        AddLGuiDebugRoot("[Game runtime] Map.charas", ReadLGuiDebugTarget(() => GameAccess.World.CurrentCharacters));
        AddLGuiDebugRoot("[Game runtime] Map.things", ReadLGuiDebugTarget(() => GameAccess.World.CurrentThings));
        AddLGuiDebugRoot("[Game runtime] Map.cells", ReadLGuiDebugTarget(() => GameAccess.World.CurrentMap?.cells));
        AddLGuiDebugRoot("[Game runtime] EClass._zone", ReadLGuiDebugTarget(() => GameAccess.World.CurrentZone));
        AddLGuiDebugRoot("[Game runtime] Zone.branches", ReadLGuiDebugTarget(() => GetDebugMemberValue(GameAccess.World.CurrentZone, "branches")));
        AddLGuiDebugRoot("[Game runtime] BranchOrHomeBranch", ReadLGuiDebugTarget(() => GameAccess.World.BranchOrHomeBranch));
        AddLGuiDebugRoot("[Game runtime] Talking NPC", ReadLGuiDebugTarget(() => GetTalkingNpc()));
        AddLGuiDebugRoot("[Game runtime] Player party", ReadLGuiDebugTarget(() => GameAccess.Characters.PlayerCharacter?.party));
        AddLGuiDebugRoot("[Game runtime] Player inventory", ReadLGuiDebugTarget(() => GameAccess.Characters.PlayerCharacter?.things));
        AddLGuiDebugRoot("[Game runtime] Player elements", ReadLGuiDebugTarget(() => GameAccess.Characters.PlayerElements));
        AddLGuiDebugRoot("[Game runtime] Player abilities", ReadLGuiDebugTarget(() => GetDebugMemberValue(GameAccess.Characters.PlayerCharacter, "ability")));
        AddLGuiDebugRoot("[Game database] EClass.sources", ReadLGuiDebugTarget(() => GameAccess.Sources.Manager));
        AddLGuiDebugRoot("[Game database] SourceThing", ReadLGuiDebugTarget(() => GameAccess.Sources.Things));
        AddLGuiDebugRoot("[Game database] SourceChara", ReadLGuiDebugTarget(() => GameAccess.Sources.Characters));
        AddLGuiDebugRoot("[Game database] SourceElement", ReadLGuiDebugTarget(() => GameAccess.Sources.Elements));
        AddLGuiDebugRoot("[Game database] SourceMaterial", ReadLGuiDebugTarget(() => GameAccess.Sources.Materials));
        AddLGuiDebugRoot("[Game database] SourceRecipe", ReadLGuiDebugTarget(() => GetDebugMemberValue(GameAccess.Sources.Manager, "recipes")));
        AddLGuiDebugRoot("[Game database] SourceZone", ReadLGuiDebugTarget(() => GetDebugMemberValue(GameAccess.Sources.Manager, "zones")));
        AddLGuiDebugRoot("[Game layer] LayerCraft.Instance", ReadLGuiDebugTarget(() => LayerCraft.Instance));
        AddLGuiDebugRoot("[Game layer] LayerDrama.Instance", ReadLGuiDebugTarget(() => LayerDrama.Instance));
        AddLGuiDebugRoot("[Game layer] DropdownGrid.Instance", ReadLGuiDebugTarget(() => DropdownGrid.Instance));
        AddLGuiDebugRoot("[Game module] ModManager", typeof(ModManager));

        foreach (var plugin in GetOtherLoadedBepInExPluginsCached())
        {
            if (plugin?.Instance == null)
                continue;
            var name = GetDebugBepInExPluginDisplayName(plugin);
            AddLGuiDebugRoot("[Plugin/Mod runtime] " + name, plugin.Instance);
        }
    }
    private static object? ReadLGuiDebugTarget(Func<object?> getter)
    {
        try { return getter(); }
        catch { return null; }
    }
    private void AddLGuiDebugRoot(string label, object? target)
    {
        if (target != null)
            _lGuiDebugRoots.Add(new LGuiDebugRoot(label, target));
    }
    private void CycleLGuiDebugRoot(int direction)
    {
        BuildLGuiDebugRoots();
        if (_lGuiDebugRoots.Count == 0)
            return;
        _lGuiDebugObjectStack.Clear();
        _lGuiDebugPathStack.Clear();
        _lGuiDebugRootIndex = (_lGuiDebugRootIndex + direction) % _lGuiDebugRoots.Count;
        if (_lGuiDebugRootIndex < 0)
            _lGuiDebugRootIndex += _lGuiDebugRoots.Count;
        _lGuiDebugTarget = _lGuiDebugRoots[_lGuiDebugRootIndex].Target;
        _lGuiDebugTargetLabel = _lGuiDebugRoots[_lGuiDebugRootIndex].Label;
        _lGuiDebugTargetPath = "debug:" + _lGuiDebugRootIndex.ToString(CultureInfo.InvariantCulture);
        RebuildLGuiDebugRows();
        if (_lGuiDebugTargetText != null)
            _lGuiDebugTargetText.text = _lGuiDebugTargetLabel;
    }
    private void NavigateLGuiDebugBack()
    {
        if (_lGuiDebugObjectStack.Count == 0)
            return;
        var last = _lGuiDebugObjectStack.Count - 1;
        _lGuiDebugTarget = _lGuiDebugObjectStack[last];
        _lGuiDebugTargetLabel = _lGuiDebugPathStack[last];
        _lGuiDebugObjectStack.RemoveAt(last);
        _lGuiDebugPathStack.RemoveAt(last);
        _lGuiDebugTargetPath = "debug:nested:" + _lGuiDebugObjectStack.Count.ToString(CultureInfo.InvariantCulture);
        RebuildLGuiDebugRows();
        if (_lGuiDebugTargetText != null)
            _lGuiDebugTargetText.text = _lGuiDebugTargetLabel;
    }
    private void RebuildLGuiDebugRows()
    {
        if (_lGuiDebugList == null || _lGuiDebugTarget == null)
            return;
        _lGuiDebugRows.Clear();
        var targetType = _lGuiDebugTarget as Type;
        var type = targetType ?? _lGuiDebugTarget.GetType();
        var members = GetDebugMembers(type, targetType != null);
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var key = _lGuiDebugTargetPath + "." + member.Name;
            if (!LGuiFilterMatches(member.Name, member.Kind, GetDebugTypeName(member.ValueType), _lGuiDebugFilter))
                continue;
            object? value = null;
            var error = "";
            try { value = member.GetValue(targetType == null ? _lGuiDebugTarget! : null!); }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
            _lGuiDebugRows.Add(new LGuiDebugRow(key, targetType == null ? _lGuiDebugTarget : null!, member, value, error));
        }
        _lGuiDebugList.SetItems(_lGuiDebugRows);
    }
    private void BindLGuiDebugRow(RectTransform rect, LGuiDebugRow model, int index)
    {
        RefreshLGuiDebugRowValue(model);
        var view = rect.GetComponent<LGuiRowView>();
        view.BeginBind();
        view.BoundData = model;
        view.BoundIndex = index;
        ApplyLGuiRowVisual(view, index);
        view.Icon.gameObject.SetActive(false);
        view.Label.gameObject.SetActive(true);
        view.Label.text = model.Member.Kind + " " + model.Member.Name;
        view.Secondary.gameObject.SetActive(true);
        view.Secondary.text = model.Error.Length > 0 ? model.Error : GetDebugTypeName(model.ValueType) + " = " + TruncateForLog(DebugValueToString(model.Value!), 56);
        var editable = model.Error.Length == 0 && model.Member.CanWrite && IsDebugEditableType(model.ValueType);
        var isBool = editable && model.ValueType == typeof(bool);
        view.Input.gameObject.SetActive(editable && !isBool);
        if (editable && !isBool && (EventSystem.current == null || EventSystem.current.currentSelectedGameObject != view.Input.gameObject))
        {
            if (!_debugInputs.ContainsKey(model.Key) || !_debugLocks.TryGetValue(model.Key, out var locked) || !locked)
                _debugInputs[model.Key] = DebugValueToString(model.Value!);
            view.SetInputWithoutNotify(_debugInputs[model.Key]);
        }
        view.Toggle.gameObject.SetActive(isBool);
        if (isBool)
        {
            view.ToggleLabel.text = T("值", "Value");
            view.SetToggleWithoutNotify(model.Value is bool b && b);
        }
        view.Primary.gameObject.SetActive(editable || (model.Value != null && !IsDebugLeafType(model.ValueType)));
        view.PrimaryText.text = editable && !isBool ? T("应用", "Apply") : T("打开", "Open");
        view.Auxiliary.gameObject.SetActive(editable);
        view.AuxiliaryText.text = _debugLocks.TryGetValue(model.Key, out var isLocked) && isLocked ? T("已锁定", "Locked") : T("锁定", "Lock");
        view.EndBind();
    }
    private static void RefreshLGuiDebugRowValue(LGuiDebugRow model)
    {
        try
        {
            model.Value = model.Member.GetValue(model.Instance);
            model.Error = "";
            model.ValueType = model.Value == null ? model.Member.ValueType : model.Value.GetType();
        }
        catch (Exception ex)
        {
            model.Value = null;
            model.Error = ex.GetType().Name + ": " + ex.Message;
            model.ValueType = model.Member.ValueType;
        }
    }
}
