using System;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    internal bool ModuleAdaptiveUiScale => _adaptiveUiScale;
    internal float ModuleUiAlpha => _uiAlpha;
    internal bool ModuleUiRoundedCorners => _uiRoundedCorners;
    internal int ModuleUiStyleIndex => _uiStyleIndex;
    internal int ModuleEffectiveUiFontSize => GetEffectiveUiFontSize();
    internal string ModulePluginVersion
    {
        get
        {
            try
            {
                return GetDebugPluginVersion(Info);
            }
            catch
            {
                return ModMetadata.Version;
            }
        }
    }

    internal Font? ModuleLGuiFont
    {
        get => _lGuiFont;
        set => _lGuiFont = value;
    }

    internal GameObject? ModuleLGuiRoot => _lGuiRoot;

    internal bool ModuleLGuiVisible
    {
        get => _lGuiVisible;
        set => _lGuiVisible = value;
    }

    internal bool ModuleLGuiModalRestoreMainOnClose
    {
        get => _lGuiModalRestoreMainOnClose;
        set => _lGuiModalRestoreMainOnClose = value;
    }

    internal void EnsureModuleLGuiEventSystem() => EnsureLGuiEventSystem();
    internal Font FindModuleLGuiFont() => FindLGuiFont();
    internal RectTransform CreateModuleLGuiRect(Transform parent, string name) => CreateLGuiRect(parent, name);
    internal Text CreateModuleLGuiText(
        Transform parent,
        string name,
        string value,
        int size,
        TextAnchor anchor,
        FontStyle style) =>
        CreateLGuiText(parent, name, value, size, anchor, style);

    internal static void StretchModuleLGuiRect(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float top) =>
        StretchLGuiRect(rect, left, bottom, right, top);

    internal float GetModuleCustomUiScaleFactor() => GetCustomUiScaleFactor();
    internal void ApplyModuleLGuiRegisteredCornerStyles(GameObject? root) => ApplyLGuiRegisteredCornerStyles(root);
    internal bool IsModuleLGuiInitialized() => IsLGuiInitialized();
    internal void InitializeModuleLGui() => InitializeLGui();
    internal bool IsModuleLGuiVisible() => IsLGuiVisible();
    internal RectTransform CreateModuleLGuiCompleteModal(
        string name,
        string title,
        out RectTransform content,
        float width,
        float height) =>
        CreateLGuiCompleteModal(name, title, out content, width, height);

    internal void CreateModuleLGuiToggleControl(
        Transform parent,
        string label,
        bool value,
        float y,
        Action<bool> changed) =>
        CreateLGuiToggleControl(parent, label, value, y, changed);

    internal Button CreateModuleLGuiButton(
        Transform parent,
        string name,
        string label,
        float x,
        float y,
        float width,
        float height,
        Action? action) =>
        CreateLGuiButton(parent, name, label, x, y, width, height, action);

    internal void ApplyModuleLGuiVisualSettings() => ApplyLGuiVisualSettings();
    internal string ExtractModuleDebugStackTrace(string text) => ExtractDebugStackTraceFromLogText(text);
    internal Sprite? GetModuleStandardRoundedSprite() => GetLGuiRoundedSprite(LGuiRoundedImageStyle.Standard);

    internal void ShowModuleLGuiRootImmediate()
    {
        _lGuiRootFade?.SetImmediate(1f, true);
    }

    private void InitializeWatermark() => _modules.Watermark.Initialize();
    private void TickWatermark() => _modules.Watermark.Tick();
    private void PersistWatermarkStateIfChanged(bool force) => _modules.Watermark.PersistIfChanged(force);
    private void ShutdownWatermark() => _modules.Watermark.Shutdown();
    private void RefreshWatermarkText() => _modules.Watermark.RefreshText();
    private void ApplyWatermarkVisualSettings() => _modules.Watermark.ApplyVisualSettings();
    private void OpenWatermarkSettings() => _modules.Watermark.OpenSettings();
    private void SetWatermarkEnabled(bool value) => _modules.Watermark.SetEnabled(value);
    private void SetWatermarkGameErrorNotification(bool value) => _modules.Watermark.SetGameErrorNotification(value);
    private void ResetWatermarkPosition() => _modules.Watermark.ResetPosition();
}
