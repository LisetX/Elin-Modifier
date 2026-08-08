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
    internal string AutomationText(string zh, string en, string ja, string ru)
    {
        if (_language == "ja") return ja;
        if (_language == "ru") return ru;
        if (_language == "en") return en;
        return zh;
    }
    internal void ResetAutomationConfig()
    {
        StopAutomation(false, false);
        _automationProfiles.Clear();
        _automationProfiles.Add(new AutomationProfile
        {
            Name = AutomationText("配置 1", "Profile 1", "設定 1", "Профиль 1"),
            Loop = true
        });
        _automationProfileIndex = 0;
        _automationRunKey = KeyCode.F7;
        _automationStopKey = KeyCode.F8;
        _automationIgnoreWeightDuringExecution = true;
        _automationNeedsDetectionDuringExecution = true;
        SetAutomationLog(AutomationText("自动化已就绪", "Automation is ready", "自動化の準備ができました", "Автоматизация готова"));
    }
    private void EnsureAutomationProfiles()
    {
        if (_automationProfiles.Count == 0)
            ResetAutomationConfig();
        _automationProfileIndex = Clamp(_automationProfileIndex, 0, _automationProfiles.Count - 1);
    }
    private AutomationProfile GetCurrentAutomationProfile()
    {
        EnsureAutomationProfiles();
        return _automationProfiles[_automationProfileIndex];
    }
    internal void LoadAutomationConfig(string json)
    {
        StopAutomation(true, false);
        _automationProfiles.Clear();
        _automationKnownScriptFiles.Clear();
        _automationRunKey = KeyCode.F7;
        _automationStopKey = KeyCode.F8;
        _automationIgnoreWeightDuringExecution = true;
        _automationNeedsDetectionDuringExecution = true;
        _automationProfileIndex = 0;
        var legacyProfiles = new List<AutomationProfile>();
        var selectedScript = "";
        var legacySelectedProfile = 0;

        try
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                using (var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    if (doc.RootElement.TryGetProperty("automation", out var root) && root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("runKey", out var runKeyElement) && TryParseKeyCode(runKeyElement.GetString() ?? "", out var runKey))
                            _automationRunKey = runKey;
                        if (root.TryGetProperty("stopKey", out var stopKeyElement) && TryParseKeyCode(stopKeyElement.GetString() ?? "", out var stopKey))
                            _automationStopKey = stopKey;
                        _automationIgnoreWeightDuringExecution = ReadAutomationJsonBool(root, "ignoreWeightDuringExecution", true);
                        _automationNeedsDetectionDuringExecution = ReadAutomationJsonBool(root, "needsDetectionDuringExecution", true);
                        selectedScript = ReadAutomationJsonString(root, "selectedScript", "");
                        if (root.TryGetProperty("selectedProfile", out var selected) && selected.TryGetInt32(out var selectedIndex))
                            legacySelectedProfile = selectedIndex;

                        if (root.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var profileElement in profiles.EnumerateArray())
                            {
                                if (legacyProfiles.Count >= 64 || profileElement.ValueKind != JsonValueKind.Object) break;
                                legacyProfiles.Add(ReadAutomationProfile(profileElement,
                                    AutomationText("配置 ", "Profile ", "設定 ", "Профиль ") + (legacyProfiles.Count + 1).ToString(CultureInfo.InvariantCulture)));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetAutomationLog(AutomationText("自动化配置读取失败: ", "Failed to load automation config: ", "自動化設定の読み込みに失敗: ", "Ошибка загрузки конфигурации автоматизации: ") + ex.Message);
        }

        var scriptProfiles = LoadAutomationScriptFiles();
        if (scriptProfiles.Count > 0)
            _automationProfiles.AddRange(scriptProfiles);
        else if (legacyProfiles.Count > 0)
            _automationProfiles.AddRange(legacyProfiles);
        else
            _automationProfiles.Add(CreateAutomationDefaultProfile());

        var selectedScriptIndex = string.IsNullOrWhiteSpace(selectedScript)
            ? -1
            : _automationProfiles.FindIndex(profile => string.Equals(profile.FileName, Path.GetFileName(selectedScript), StringComparison.OrdinalIgnoreCase));
        _automationProfileIndex = selectedScriptIndex >= 0
            ? selectedScriptIndex
            : Clamp(legacySelectedProfile, 0, _automationProfiles.Count - 1);
    }
    private AutomationProfile CreateAutomationDefaultProfile()
    {
        return new AutomationProfile
        {
            Name = AutomationText("配置 1", "Profile 1", "設定 1", "Профиль 1"),
            Loop = true
        };
    }
    private AutomationProfile ReadAutomationProfile(JsonElement profileElement, string fallbackName)
    {
        var profile = new AutomationProfile
        {
            Name = ReadAutomationJsonString(profileElement, "name", fallbackName),
            Loop = ReadAutomationJsonBool(profileElement, "loop", true)
        };
        if (profileElement.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (var actionElement in actions.EnumerateArray())
            {
                if (profile.Actions.Count >= 256 || actionElement.ValueKind != JsonValueKind.Object) break;
                var type = NormalizeAutomationActionType(ReadAutomationJsonString(actionElement, "type", AutomationTypeAutoMine));
                var action = CreateDefaultAutomationAction(type);
                action.Enabled = ReadAutomationJsonBool(actionElement, "enabled", true);
                action.Param1 = ReadAutomationJsonString(actionElement, "param1", action.Param1);
                action.Param2 = ReadAutomationJsonString(actionElement, "param2", action.Param2);
                action.Param3 = ReadAutomationJsonString(actionElement, "param3", action.Param3);
                action.Param4 = ReadAutomationJsonString(actionElement, "param4", action.Param4);
                action.DelaySeconds = type == AutomationTypeWait
                    ? ReadAutomationJsonFloat(actionElement, "delay", action.DelaySeconds, 0f, 3600f)
                    : 0f;
                profile.Actions.Add(action);
            }
        }
        return profile;
    }
    private List<AutomationProfile> LoadAutomationScriptFiles()
    {
        var result = new List<AutomationProfile>();
        try
        {
            var directory = GetAutomationScriptDirectory();
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (var i = 0; i < files.Length && result.Count < 64; i++)
            {
                var file = files[i];
                try
                {
                    var scriptJson = File.ReadAllText(file, Encoding.UTF8);
                    using (var doc = JsonDocument.Parse(scriptJson, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                    {
                        if (doc.RootElement.ValueKind != JsonValueKind.Object)
                            continue;
                        var fileName = Path.GetFileName(file);
                        var profile = ReadAutomationProfile(doc.RootElement, Path.GetFileNameWithoutExtension(fileName));
                        profile.FileName = fileName;
                        result.Add(profile);
                        _automationKnownScriptFiles.Add(fileName);
                    }
                }
                catch (Exception ex)
                {
                    SetAutomationLog(AutomationText("自动化脚本读取失败: ", "Failed to load automation script: ", "自動化スクリプトの読み込みに失敗: ", "Ошибка загрузки сценария автоматизации: ") + Path.GetFileName(file) + " | " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            SetAutomationLog(AutomationText("自动化脚本目录读取失败: ", "Failed to read automation script directory: ", "自動化スクリプトフォルダーの読み込みに失敗: ", "Ошибка чтения папки сценариев автоматизации: ") + ex.Message);
        }
        return result;
    }
    internal void SaveAutomationScriptFiles()
    {
        EnsureAutomationProfiles();
        var directory = GetAutomationScriptDirectory();
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var desiredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unavailableFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (!_automationKnownScriptFiles.Contains(fileName))
                unavailableFiles.Add(fileName);
        }

        for (var i = 0; i < _automationProfiles.Count; i++)
        {
            var profile = _automationProfiles[i];
            var fileName = CreateAutomationScriptFileName(profile.Name, i + 1, desiredFiles, unavailableFiles);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, BuildAutomationScriptJson(profile), Encoding.UTF8);
            profile.FileName = fileName;
            desiredFiles.Add(fileName);
        }

        foreach (var staleFile in _automationKnownScriptFiles.ToArray())
        {
            if (desiredFiles.Contains(staleFile))
                continue;
            var safeName = Path.GetFileName(staleFile);
            if (!string.Equals(safeName, staleFile, StringComparison.Ordinal))
                continue;
            var stalePath = Path.Combine(directory, safeName);
            if (File.Exists(stalePath))
                File.Delete(stalePath);
        }

        _automationKnownScriptFiles.Clear();
        foreach (var fileName in desiredFiles)
            _automationKnownScriptFiles.Add(fileName);
    }
    private string CreateAutomationScriptFileName(string name, int index, HashSet<string> desiredFiles, HashSet<string> unavailableFiles)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var source = (name ?? "").Trim();
        var sanitized = new string(source.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = AutomationText("配置 ", "Profile ", "設定 ", "Профиль ") + index.ToString(CultureInfo.InvariantCulture);
        if (sanitized.Length > 96)
            sanitized = sanitized.Substring(0, 96).TrimEnd(' ', '.');
        if (IsAutomationReservedFileName(sanitized))
            sanitized = "_" + sanitized;

        var candidate = sanitized + ".json";
        var suffix = 2;
        while (desiredFiles.Contains(candidate) || unavailableFiles.Contains(candidate))
        {
            candidate = sanitized + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ").json";
            suffix++;
        }
        return candidate;
    }
    private static bool IsAutomationReservedFileName(string name)
    {
        var value = (name ?? "").Trim().TrimEnd('.').ToUpperInvariant();
        if (value == "CON" || value == "PRN" || value == "AUX" || value == "NUL")
            return true;
        if (value.Length == 4 && (value.StartsWith("COM", StringComparison.Ordinal) || value.StartsWith("LPT", StringComparison.Ordinal)))
            return value[3] >= '1' && value[3] <= '9';
        return false;
    }
    private string BuildAutomationScriptJson(AutomationProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"name\": \"" + EscapeJson(profile.Name ?? "") + "\",");
        sb.AppendLine("  \"loop\": " + (profile.Loop ? "true" : "false") + ",");
        sb.AppendLine("  \"actions\": [");
        for (var i = 0; i < profile.Actions.Count; i++)
        {
            var fields = BuildAutomationActionJsonFields(profile.Actions[i]);
            sb.AppendLine("    {");
            for (var j = 0; j < fields.Count; j++)
            {
                sb.Append("      ").Append(fields[j]);
                if (j < fields.Count - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.Append("    }");
            if (i < profile.Actions.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }
    private static List<string> BuildAutomationActionJsonFields(AutomationActionConfig action)
    {
        var type = NormalizeAutomationActionType(action.Type);
        var fields = new List<string>
        {
            "\"enabled\": " + (action.Enabled ? "true" : "false"),
            "\"type\": \"" + EscapeJson(type) + "\""
        };
        switch (type)
        {
            case AutomationTypeWait:
                fields.Add("\"delay\": " + Mathf.Clamp(action.DelaySeconds, 0f, 3600f).ToString("0.###", CultureInfo.InvariantCulture));
                break;
            case AutomationTypeMoveTo:
                fields.Add("\"param1\": \"" + EscapeJson(action.Param1 ?? "") + "\"");
                fields.Add("\"param2\": \"" + EscapeJson(action.Param2 ?? "") + "\"");
                break;
            case AutomationTypeUseAbility:
                fields.Add("\"param1\": \"" + EscapeJson(action.Param1 ?? "") + "\"");
                fields.Add("\"param2\": \"" + EscapeJson(action.Param2 ?? "") + "\"");
                fields.Add("\"param3\": \"" + EscapeJson(action.Param3 ?? "") + "\"");
                break;
            case AutomationTypePickupByValue:
                fields.Add("\"param1\": \"" + EscapeJson(action.Param1 ?? "") + "\"");
                fields.Add("\"param2\": \"" + EscapeJson(action.Param2 ?? "") + "\"");
                fields.Add("\"param3\": \"" + EscapeJson(action.Param3 ?? "") + "\"");
                fields.Add("\"param4\": \"" + EscapeJson(action.Param4 ?? "") + "\"");
                break;
        }
        return fields;
    }
    private static string GetAutomationScriptDirectory()
    {
        return Path.Combine(GetPluginDirectory(), "script");
    }
    internal static bool HasLegacyAutomationConfigFields(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using (var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
            {
                if (!doc.RootElement.TryGetProperty("automation", out var root) || root.ValueKind != JsonValueKind.Object)
                    return false;
                return root.TryGetProperty("profiles", out _) || root.TryGetProperty("selectedProfile", out _) || root.TryGetProperty("hotkeyDefaultsVersion", out _);
            }
        }
        catch
        {
            return false;
        }
    }
    private static string ReadAutomationJsonString(JsonElement element, string name, string fallback)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
    private static bool ReadAutomationJsonBool(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.TryGetInt32(out var number)) return number != 0;
        if (bool.TryParse(value.GetString(), out var parsed)) return parsed;
        return fallback;
    }
    private static float ReadAutomationJsonFloat(JsonElement element, string name, float fallback, float minimum, float maximum)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (!float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return fallback;
        return Mathf.Clamp(parsed, minimum, maximum);
    }
    internal void AppendAutomationConfigJson(StringBuilder sb)
    {
        EnsureAutomationProfiles();
        var selectedProfile = _automationProfiles[Clamp(_automationProfileIndex, 0, _automationProfiles.Count - 1)];
        sb.AppendLine("  \"automation\": {");
        sb.AppendLine("    \"runKey\": \"" + EscapeJson(GetKeyLabel(_automationRunKey)) + "\",");
        sb.AppendLine("    \"stopKey\": \"" + EscapeJson(GetKeyLabel(_automationStopKey)) + "\",");
        sb.AppendLine("    \"ignoreWeightDuringExecution\": " + (_automationIgnoreWeightDuringExecution ? "true" : "false") + ",");
        sb.AppendLine("    \"needsDetectionDuringExecution\": " + (_automationNeedsDetectionDuringExecution ? "true" : "false") + ",");
        sb.AppendLine("    \"selectedScript\": \"" + EscapeJson(selectedProfile.FileName ?? "") + "\"");
        sb.AppendLine("  },");
    }
}
