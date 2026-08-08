using System;
using System.IO;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    internal RectTransform? ModuleLGuiPageHost => _lGuiPageHost;
    internal Harmony? ModuleHarmony => _modules.Harmony.GetGroupHarmony("probability");
    internal bool IsModuleProbabilityPageActive() => _lGuiPage == LGuiPage.Probability;
    internal static bool HasModuleCharacterData() => HasCharacterData();

    internal InputField CreateModuleLGuiInput(
        Transform parent,
        string name,
        string placeholder,
        float x,
        float y,
        float width,
        float height) =>
        CreateLGuiInput(parent, name, placeholder, x, y, width, height);

    internal ScrollRect CreateModuleLGuiScroll(RectTransform parent, string name, float top) =>
        CreateLGuiScroll(parent, name, top);

    internal RectTransform CreateModuleLGuiVirtualRow(RectTransform parent) => CreateLGuiVirtualRow(parent);
    internal void ApplyModuleLGuiRowVisual(LGuiRowView view, int index, bool header) =>
        ApplyLGuiRowVisual(view, index, header);

    internal static void AnchorModuleLGuiTop(
        RectTransform rect,
        float top,
        float height,
        float left,
        float right) =>
        AnchorLGuiTop(rect, top, height, left, right);

    internal static void PlaceModuleLGuiRect(
        RectTransform rect,
        float x,
        float y,
        float width,
        float height) =>
        PlaceLGuiRect(rect, x, y, width, height);

    internal static string IndentModuleLGuiText(string text, int depth) => IndentLGuiText(text, depth);
    internal static bool ModuleLGuiFilterMatches(string first, string second, string third, string filter) =>
        LGuiFilterMatches(first, second, third, filter);

    internal bool TryReadProbabilityModuleConfiguration(out string json, out string error)
    {
        json = "";
        error = "";
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path))
            {
                error = TranslateModuleText("配置文件不存在", "Config file does not exist");
                return false;
            }

            var root = JObject.Parse(_modules.ConfigurationStorage.ReadAllText(path, Encoding.UTF8));
            var token = root["probabilityModule"];
            json = token?.Type == JTokenType.Object
                ? token.ToString(Formatting.None)
                : ProbabilityModule.EmptyStoredConfigurationJson;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryWriteProbabilityModuleConfiguration(string json, out string error)
    {
        error = "";
        try
        {
            var path = GetConfigPath();
            JObject root;
            if (File.Exists(path))
                root = JObject.Parse(_modules.ConfigurationStorage.ReadAllText(path, Encoding.UTF8));
            else
                root = new JObject();

            JToken token;
            try
            {
                token = JToken.Parse(json);
            }
            catch (JsonException)
            {
                token = JObject.Parse(ProbabilityModule.EmptyStoredConfigurationJson);
            }

            if (token.Type != JTokenType.Object)
                token = JObject.Parse(ProbabilityModule.EmptyStoredConfigurationJson);
            root["probabilityModule"] = token;
            _modules.ConfigurationStorage.WriteAllTextAtomic(
                path,
                root.ToString(Formatting.Indented),
                Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void BuildProbabilityPage() => _modules.Probability.BuildPage();
    private void TickProbabilitySession() => _modules.Probability.Tick();
    private void RestoreAllProbabilityValues(bool updateLog) => _modules.Probability.RestoreAll(updateLog);
}
