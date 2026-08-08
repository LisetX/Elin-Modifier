using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    internal string ModuleLanguage => _language;
    internal bool ModuleInfinitePlayerSight => _infinitePlayerSight;
    internal bool ModuleLGuiDataDirty
    {
        set => _lGuiDataDirty = value;
    }

    internal static Chara? GetModuleSafePc() => GetSafePc();
    internal void SetModuleInfinitePlayerSight(bool enabled) => SetInfinitePlayerSight(enabled);
    internal void SetModuleHostileThreatMarker(bool enabled) => SetHostileThreatMarker(enabled);
    internal AbilityDef? FindModuleAiAbility(string text) => FindAiAbility(text);
    internal static bool IsModuleToolThing(Thing thing) => IsToolThing(thing);

    internal Image CreateModuleLGuiImage(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height) =>
        CreateLGuiImage(parent, name, x, y, width, height);

    internal Toggle CreateModuleLGuiToggle(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height,
        out Text label) =>
        CreateLGuiToggle(parent, name, x, y, width, height, out label);

    internal Color GetModuleLGuiRowColor(int index, bool header) => GetLGuiRowColor(index, header);

    internal void RegisterModuleLGuiRoundedImage(Image image, bool capsule) =>
        RegisterLGuiRoundedImage(
            image,
            capsule ? LGuiRoundedImageStyle.Capsule : LGuiRoundedImageStyle.Standard);

    internal string GetModuleKeyLabel(KeyCode key) => GetKeyLabel(key);

    internal KeyCode GetAdjacentModuleKey(KeyCode current, int direction)
    {
        var index = 0;
        for (var i = 0; i < KeyOptions.Length; i++)
        {
            if (KeyOptions[i].Key != current)
                continue;
            index = i;
            break;
        }

        index = (index + direction) % KeyOptions.Length;
        if (index < 0)
            index += KeyOptions.Length;
        return KeyOptions[index].Key;
    }

    internal bool TryParseModuleKeyCode(string text, out KeyCode key) => TryParseKeyCode(text, out key);
    internal static string EscapeModuleJson(string value) => EscapeJson(value);
    internal static string GetModulePluginDirectory() => GetPluginDirectory();
    internal bool IsModuleAutomationPageActive() => _lGuiPage == LGuiPage.Automation;
    internal void RefreshModuleAutomationPage() => SwitchLGuiPage(LGuiPage.Automation);
    internal void SaveModuleConfig(bool updateLog) => SaveConfig(updateLog);

    private string AutomationLog => _modules.Automation.Log;
    private void LoadAutomationConfig(string json) => _modules.Automation.LoadAutomationConfig(json);
    private bool HasLegacyAutomationConfigFields(string json) =>
        AutomationModule.HasLegacyAutomationConfigFields(json);
    private void ResetAutomationConfig() => _modules.Automation.ResetAutomationConfig();
    private void SaveAutomationScriptFiles() => _modules.Automation.SaveAutomationScriptFiles();
    private bool GetAutomationPersistedHostileThreatMarker() =>
        _modules.Automation.GetAutomationPersistedHostileThreatMarker();
    private bool GetAutomationPersistedInfinitePlayerSight() =>
        _modules.Automation.GetAutomationPersistedInfinitePlayerSight();
    private void AppendAutomationConfigJson(System.Text.StringBuilder builder) =>
        _modules.Automation.AppendAutomationConfigJson(builder);
    private void TickAutomation() => _modules.Automation.TickAutomation();
    private void BuildAutomationPage() => _modules.Automation.BuildAutomationPage();
    private string AutomationText(string zh, string en, string ja, string ru) =>
        _modules.Automation.AutomationText(zh, en, ja, ru);

    private Dropdown CreateAutomationDropdown(
        Transform parent,
        string name,
        IReadOnlyList<string> options,
        int selectedIndex,
        float x,
        float y,
        float width,
        float height,
        Action<int> onValueChanged) =>
        _modules.Automation.CreateAutomationDropdown(
            parent,
            name,
            options,
            selectedIndex,
            x,
            y,
            width,
            height,
            onValueChanged);
}
