using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal sealed class TermsConfirmationModule
{
    private const string TermsVersion = "2026-08-08.1";
    private const string TermsChineseResourceName = "ElinModifier.Terms.zh.txt";
    private const string TermsEnglishResourceName = "ElinModifier.Terms.en.txt";

    internal string AcceptedVersion { get; private set; } = "";
    internal bool Active { get; private set; }
    internal bool FinalizePending { get; private set; }
    private bool _termsDisplayEnglish;
    private string _termsChineseText = "";
    private string _termsEnglishText = "";
    private Vector2 _termsScroll;
    private Rect _termsWindow = new Rect(0f, 0f, 1100f, 820f);

    internal void ContinueStartup(ElinModifierPlugin host)
    {
        if (HasAcceptedCurrentTermsFromConfig(host))
        {
            host.InitializePluginFromModule();
            return;
        }

        BeginConfirmation(host);
    }

    private bool HasAcceptedCurrentTermsFromConfig(ElinModifierPlugin host)
    {
        AcceptedVersion = "";
        try
        {
            var path = host.ModuleConfigPath;
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path, Encoding.UTF8);
            var root = JObject.Parse(json);
            AcceptedVersion = (root["acceptedTermsVersion"]?.ToString() ?? "").Trim();
            return string.Equals(AcceptedVersion, TermsVersion, StringComparison.Ordinal);
        }
        catch
        {
            AcceptedVersion = "";
            return false;
        }
    }

    private void BeginConfirmation(ElinModifierPlugin host)
    {
        Active = true;
        FinalizePending = false;
        _termsScroll = Vector2.zero;
        EnsureTermsTextLoaded();
        _termsDisplayEnglish = ShouldDefaultTermsToEnglish(host);
        host.PrepareUiForTermsConfirmation();
    }

    private bool ShouldDefaultTermsToEnglish(ElinModifierPlugin host)
    {
        try
        {
            var path = host.ModuleConfigPath;
            if (File.Exists(path))
            {
                var root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                var language = (root["language"]?.ToString() ?? "").Trim();
                if (string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.IsNullOrEmpty(language))
                    return true;
            }
        }
        catch { }

        try
        {
            return !string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                "zh", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void EnsureTermsTextLoaded()
    {
        if (string.IsNullOrEmpty(_termsChineseText))
            _termsChineseText = ReadEmbeddedTermsText(TermsChineseResourceName,
                "Elin Modifier 使用条款与隐私说明\n\n条款内容加载失败。请重新安装完整版本后重试。");
        if (string.IsNullOrEmpty(_termsEnglishText))
            _termsEnglishText = ReadEmbeddedTermsText(TermsEnglishResourceName,
                "Elin Modifier Terms of Use and Privacy Notice\n\nThe terms content could not be loaded. Please reinstall the complete package and try again.");
    }

    private static string ReadEmbeddedTermsText(string resourceName, string fallback)
    {
        try
        {
            var assembly = typeof(ElinModifierPlugin).Assembly;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return fallback;
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }
        catch
        {
            return fallback;
        }
    }

    internal bool Tick(ElinModifierPlugin host)
    {
        if (FinalizePending)
        {
            FinalizePending = false;
            AcceptedVersion = TermsVersion;
            host.InitializePluginFromModule();
            host.SaveConfigFromModule(false);
            return true;
        }

        return Active;
    }

    private void AcceptCurrentTerms()
    {
        if (!Active)
            return;

        Active = false;
        FinalizePending = true;
    }

    private void ExitFromTermsConfirmation(ElinModifierPlugin host)
    {
        Active = false;
        FinalizePending = false;
        host.enabled = false;
        try { UnityEngine.Object.Destroy(host); }
        catch { }
    }

    internal void Draw(ElinModifierPlugin host)
    {
        EnsureTermsTextLoaded();
        var margin = 24f;
        var width = Mathf.Min(1180f, Mathf.Max(560f, Screen.width - margin * 2f));
        var height = Mathf.Min(900f, Mathf.Max(420f, Screen.height - margin * 2f));
        _termsWindow.width = width;
        _termsWindow.height = height;
        _termsWindow.x = Mathf.Max(margin, (Screen.width - width) * 0.5f);
        _termsWindow.y = Mathf.Max(margin, (Screen.height - height) * 0.5f);

        var oldSkin = GUI.skin;
        var oldColor = GUI.color;
        var oldBackground = GUI.backgroundColor;
        var oldContent = GUI.contentColor;
        try
        {
            GUI.skin = host.GetModuleModifierSkin(oldSkin);
            host.ApplyModuleUiStyle();
            ApplyOpaqueTermsWindowStyle(host);
            DrawOpaqueTermsWindowBackplate(host, _termsWindow);
            _termsWindow = GUI.Window(920699, _termsWindow, id => DrawTermsConfirmationWindow(host, id),
                _termsDisplayEnglish ? "Elin Modifier Terms of Use" : "Elin Modifier 使用条款");
        }
        finally
        {
            GUI.skin = oldSkin;
            GUI.color = oldColor;
            GUI.backgroundColor = oldBackground;
            GUI.contentColor = oldContent;
        }

        var current = Event.current;
        if (current != null)
        {
            switch (current.type)
            {
                case EventType.KeyDown:
                case EventType.KeyUp:
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseDrag:
                case EventType.ScrollWheel:
                    current.Use();
                    break;
            }
        }
    }

    private static void ApplyOpaqueTermsWindowStyle(ElinModifierPlugin host)
    {
        var background = host.ModuleUiStyleColor;
        background.a = 1f;
        var text = host.ModuleActiveUiTextColor;
        text.a = 1f;
        GUI.backgroundColor = background;
        GUI.color = Color.white;
        GUI.contentColor = text;
        host.ApplyModuleSkinTextColor(text);
    }

    private static void DrawOpaqueTermsWindowBackplate(ElinModifierPlugin host, Rect rect)
    {
        var oldColor = GUI.color;
        var background = host.ModuleUiStyleColor;
        background.a = 1f;
        GUI.color = background;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;
    }

    private void DrawTermsConfirmationWindow(ElinModifierPlugin host, int id)
    {
        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("中文", GUILayout.Width(110f), GUILayout.Height(36f)))
        {
            _termsDisplayEnglish = false;
            _termsScroll = Vector2.zero;
        }
        if (GUILayout.Button("English", GUILayout.Width(110f), GUILayout.Height(36f)))
        {
            _termsDisplayEnglish = true;
            _termsScroll = Vector2.zero;
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label((_termsDisplayEnglish ? "Terms version: " : "条款版本：") + TermsVersion,
            GUILayout.Height(36f));
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        var textStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            richText = false,
            fontSize = 16,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(12, 12, 10, 10)
        };
        var termsText = _termsDisplayEnglish ? _termsEnglishText : _termsChineseText;
        var termsTextWidth = Mathf.Max(320f, _termsWindow.width - 58f);
        var termsContent = new GUIContent(termsText);
        var termsTextHeight = Mathf.Max(1f, textStyle.CalcHeight(termsContent, termsTextWidth));
        _termsScroll = GUILayout.BeginScrollView(_termsScroll, false, true,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUILayout.Label(termsContent, textStyle,
            GUILayout.Width(termsTextWidth), GUILayout.Height(termsTextHeight));
        GUILayout.EndScrollView();

        GUILayout.Space(10f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(_termsDisplayEnglish ? "Exit" : "退出",
                GUILayout.Width(140f), GUILayout.Height(46f)))
            ExitFromTermsConfirmation(host);
        GUILayout.Space(12f);
        var acceptButtonWidth = Mathf.Clamp(_termsWindow.width - 210f, 280f, 420f);
        if (GUILayout.Button(
                _termsDisplayEnglish
                    ? "I acknowledge the Privacy Notice and agree to the Terms"
                    : "我已阅读隐私说明并同意使用条款",
                GUILayout.Width(acceptButtonWidth), GUILayout.Height(46f)))
            AcceptCurrentTerms();
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);

        GUI.DragWindow(new Rect(0f, 0f, _termsWindow.width, 34f));
    }
}

public sealed partial class ElinModifierPlugin
{
    internal string ModuleConfigPath => GetConfigPath();

    internal void InitializePluginFromModule()
    {
        InitializePluginAfterTermsConfirmation();
    }

    internal void SaveConfigFromModule(bool updateLog, bool saveAutomationScripts = true)
    {
        SaveConfig(updateLog, saveAutomationScripts);
    }

    internal void PrepareUiForTermsConfirmation()
    {
        CloseLGuiEditorModal();
        if (_lGuiRoot != null)
            _lGuiRoot.SetActive(false);
        _lGuiVisible = false;
        SetLGuiOwnedEventSystemActive(false);
    }

    internal GUISkin GetModuleModifierSkin(GUISkin source)
    {
        return GetModifierSkin(source);
    }

    internal void ApplyModuleUiStyle()
    {
        ApplyUiStyle();
    }

    internal Color ModuleUiStyleColor
    {
        get
        {
            if (_uiStyleIndex < 0 || _uiStyleIndex >= UiStyleColors.Length)
                _uiStyleIndex = 0;
            return UiStyleColors[_uiStyleIndex];
        }
    }

    internal Color ModuleActiveUiTextColor => GetActiveUiTextColor();

    internal void ApplyModuleSkinTextColor(Color color)
    {
        ApplySkinTextColor(color);
    }

    private string AcceptedTermsVersion => _modules.TermsConfirmation.AcceptedVersion;
    private bool TermsConfirmationActive => _modules.TermsConfirmation.Active;
    private bool TermsAcceptanceFinalizePending => _modules.TermsConfirmation.FinalizePending;

    private void BeginTermsConfirmation()
    {
        _modules.TermsConfirmation.ContinueStartup(this);
    }

    private bool TickTermsConfirmation()
    {
        return _modules.TermsConfirmation.Tick(this);
    }

    private void DrawTermsConfirmation()
    {
        _modules.TermsConfirmation.Draw(this);
    }
}
