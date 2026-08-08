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
    private readonly ElinModifierPlugin _host;
    private ScrollRect? _automationActionsScroll;
    internal AutomationModule(ElinModifierPlugin host)
    {
        _host = host;
    }
    internal string Log => _automationLog;
    internal void Shutdown()
    {
        StopAutomation(true, false);
        _automationStatusText = null;
        _automationActionsScroll = null;
    }
    private string _language => _host.ModuleLanguage;
    private bool _infinitePlayerSight => _host.ModuleInfinitePlayerSight;
    private bool _hostileThreatMarker => _host.ModuleHostileThreatMarker;
    private string _log
    {
        get => _host.ModuleLog;
        set => _host.ModuleLog = value;
    }
    private bool _lGuiDataDirty
    {
        set => _host.ModuleLGuiDataDirty = value;
    }
    private RectTransform? _lGuiPageHost => _host.ModuleLGuiPageHost;
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    private static bool HasCharacterData() => ElinModifierPlugin.HasModuleCharacterData();
    private static Chara? GetSafePc() => ElinModifierPlugin.GetModuleSafePc();
    private void SetInfinitePlayerSight(bool enabled) => _host.SetModuleInfinitePlayerSight(enabled);
    private void SetHostileThreatMarker(bool enabled) => _host.SetModuleHostileThreatMarker(enabled);
    private AbilityDef? FindAiAbility(string text) => _host.FindModuleAiAbility(text);
    private static bool IsToolThing(Thing thing) => ElinModifierPlugin.IsModuleToolThing(thing);
    private RectTransform CreateLGuiRect(Transform parent, string name) => _host.CreateModuleLGuiRect(parent, name);
    private Text CreateLGuiText(
        Transform parent,
        string name,
        string value,
        int size,
        TextAnchor anchor,
        FontStyle style) =>
        _host.CreateModuleLGuiText(parent, name, value, size, anchor, style);
    private Image CreateLGuiImage(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height) =>
        _host.CreateModuleLGuiImage(parent, name, x, y, width, height);
    private Button CreateLGuiButton(
        Transform parent,
        string name,
        string label,
        float x,
        float y,
        float width,
        float height,
        Action? action) =>
        _host.CreateModuleLGuiButton(parent, name, label, x, y, width, height, action);
    private InputField CreateLGuiInput(
        Transform parent,
        string name,
        string placeholder,
        float x,
        float y,
        float width,
        float height) =>
        _host.CreateModuleLGuiInput(parent, name, placeholder, x, y, width, height);
    private Toggle CreateLGuiToggle(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height,
        out Text label) =>
        _host.CreateModuleLGuiToggle(parent, name, x, y, width, height, out label);
    private ScrollRect CreateLGuiScroll(RectTransform parent, string name, float top) =>
        _host.CreateModuleLGuiScroll(parent, name, top);
    private static void PlaceLGuiRect(
        RectTransform rect,
        float x,
        float y,
        float width,
        float height) =>
        ElinModifierPlugin.PlaceModuleLGuiRect(rect, x, y, width, height);
    private static void AnchorLGuiTop(
        RectTransform rect,
        float top,
        float height,
        float left,
        float right) =>
        ElinModifierPlugin.AnchorModuleLGuiTop(rect, top, height, left, right);
    private static void StretchLGuiRect(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float top) =>
        ElinModifierPlugin.StretchModuleLGuiRect(rect, left, bottom, right, top);
    private Color GetLGuiRowColor(int index, bool header = false) => _host.GetModuleLGuiRowColor(index, header);
    private void RegisterLGuiRoundedImage(Image image, bool capsule = false) =>
        _host.RegisterModuleLGuiRoundedImage(image, capsule);
    private string GetKeyLabel(KeyCode key) => _host.GetModuleKeyLabel(key);
    private KeyCode GetAdjacentKey(KeyCode key, int direction) => _host.GetAdjacentModuleKey(key, direction);
    private bool TryParseKeyCode(string text, out KeyCode key) => _host.TryParseModuleKeyCode(text, out key);
    private static string EscapeJson(string value) => ElinModifierPlugin.EscapeModuleJson(value);
    private static string GetPluginDirectory() => ElinModifierPlugin.GetModulePluginDirectory();
    private bool IsLGuiInitialized() => _host.IsModuleLGuiInitialized();
    private void SaveConfig(bool updateLog = true) => _host.SaveModuleConfig(updateLog);
}
