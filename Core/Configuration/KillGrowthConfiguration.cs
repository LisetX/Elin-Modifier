using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private void LoadKillGrowthConfig(string json)
    {
        try
        {
            var root = JObject.Parse(json);
            var growth = root["killGrowth"] as JObject;
            if (growth == null)
            {
                SyncKillGrowthTextFields();
                return;
            }

            _killGrowthEnabled = growth.Value<bool?>("enabled") ?? _killGrowthEnabled;
            _killGrowthSharedExperience = growth.Value<bool?>("sharedExperience") ?? _killGrowthSharedExperience;
            decimal decimalValue;
            if (TryReadKillGrowthDecimal(growth, "expPerLevel", out decimalValue))
                _killGrowthExpPerLevel = ClampKillGrowthDecimal(decimalValue, 0.01m, 100000000m);
            if (TryReadKillGrowthDecimal(growth, "baseExp", out decimalValue))
                _killGrowthBaseExp = ClampKillGrowthDecimal(decimalValue, 0m, 100000000m);

            var attr = growth["attributeBonus"] as JObject;
            if (attr != null)
            {
                foreach (var id in KillGrowthAttributeIds)
                {
                    var token = attr[id.ToString(CultureInfo.InvariantCulture)];
                    if (token == null) continue;
                    int value;
                    if (int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                        _killGrowthAttributeBonus[id] = Clamp(value, 0, 1000000);
                }
            }

            _killGrowthExpByUid = new Dictionary<int, decimal>();
            _killGrowthExpBySaveId.Clear();
            _killGrowthLegacyExpByUid.Clear();
            _killGrowthActiveSaveId = "";
            _killGrowthLegacyMigrationPending = false;
            _killGrowthSaveMigrationWritePending = false;
            var expBySave = growth["expBySave"] as JObject;
            if (expBySave != null)
            {
                foreach (var saveProperty in expBySave.Properties())
                {
                    var saveObject = saveProperty.Value as JObject;
                    if (saveObject == null || string.IsNullOrWhiteSpace(saveProperty.Name))
                        continue;

                    var saveValues = new Dictionary<int, decimal>();
                    foreach (var prop in saveObject.Properties())
                    {
                        int uid;
                        decimal value;
                        if (int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out uid) &&
                            decimal.TryParse(prop.Value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) &&
                            value > 0m)
                        {
                            saveValues[uid] = NormalizeKillGrowthExperience(value);
                        }
                    }

                    _killGrowthExpBySaveId[saveProperty.Name] = saveValues;
                }
            }

            var exp = growth["exp"] as JObject;
            if (exp != null)
            {
                foreach (var prop in exp.Properties())
                {
                    int uid;
                    decimal value;
                    if (int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out uid) &&
                        decimal.TryParse(prop.Value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) &&
                        value > 0m)
                    {
                        _killGrowthLegacyExpByUid[uid] = NormalizeKillGrowthExperience(value);
                    }
                }
            }
            _killGrowthLegacyMigrationPending = _killGrowthLegacyExpByUid.Count > 0;

            SyncKillGrowthTextFields();
            RefreshKillGrowthAffectedCharacters();
        }
        catch
        {
            SyncKillGrowthTextFields();
        }
    }
    private static bool TryReadKillGrowthDecimal(JObject obj, string name, out decimal value)
    {
        value = 0m;
        if (obj == null || string.IsNullOrEmpty(name))
            return false;

        var token = obj[name];
        return token != null &&
               decimal.TryParse(token.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
    private void ResetKillGrowthConfig()
    {
        _killGrowthEnabled = false;
        _killGrowthSharedExperience = false;
        _killGrowthExpPerLevel = 100m;
        _killGrowthBaseExp = 10m;
        _killGrowthAttributeBonus[70] = 1;
        _killGrowthAttributeBonus[71] = 1;
        _killGrowthAttributeBonus[72] = 1;
        _killGrowthAttributeBonus[73] = 1;
        _killGrowthAttributeBonus[74] = 1;
        _killGrowthAttributeBonus[75] = 1;
        _killGrowthAttributeBonus[76] = 1;
        _killGrowthAttributeBonus[77] = 1;
        _killGrowthExpByUid = new Dictionary<int, decimal>();
        _killGrowthExpBySaveId.Clear();
        _killGrowthLegacyExpByUid.Clear();
        _killGrowthActiveSaveId = "";
        _killGrowthLegacyMigrationPending = false;
        _killGrowthSaveMigrationWritePending = false;
        SyncKillGrowthTextFields();
        RefreshKillGrowthAffectedCharacters();
    }
    private void ApplyKillGrowthConfigTexts()
    {
        decimal value;
        if (decimal.TryParse((_killGrowthExpPerLevelText ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            _killGrowthExpPerLevel = ClampKillGrowthDecimal(value, 0.01m, 100000000m);
        if (decimal.TryParse((_killGrowthBaseExpText ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            _killGrowthBaseExp = ClampKillGrowthDecimal(value, 0m, 100000000m);

        ApplyKillGrowthAttributeText(70, _killGrowthStrBonusText);
        ApplyKillGrowthAttributeText(71, _killGrowthEndBonusText);
        ApplyKillGrowthAttributeText(72, _killGrowthDexBonusText);
        ApplyKillGrowthAttributeText(73, _killGrowthPerBonusText);
        ApplyKillGrowthAttributeText(74, _killGrowthLeaBonusText);
        ApplyKillGrowthAttributeText(75, _killGrowthWilBonusText);
        ApplyKillGrowthAttributeText(76, _killGrowthMagBonusText);
        ApplyKillGrowthAttributeText(77, _killGrowthChaBonusText);
        SyncKillGrowthTextFields();
    }
    private void ApplyKillGrowthAttributeText(int elementId, string text)
    {
        int value;
        if (int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            _killGrowthAttributeBonus[elementId] = Clamp(value, 0, 1000000);
    }
    private void SyncKillGrowthTextFields()
    {
        _killGrowthExpPerLevelText = FormatKillGrowthDecimal(_killGrowthExpPerLevel);
        _killGrowthBaseExpText = FormatKillGrowthDecimal(_killGrowthBaseExp);
        _killGrowthStrBonusText = GetKillGrowthConfiguredAttributeBonus(70).ToString(CultureInfo.InvariantCulture);
        _killGrowthEndBonusText = GetKillGrowthConfiguredAttributeBonus(71).ToString(CultureInfo.InvariantCulture);
        _killGrowthDexBonusText = GetKillGrowthConfiguredAttributeBonus(72).ToString(CultureInfo.InvariantCulture);
        _killGrowthPerBonusText = GetKillGrowthConfiguredAttributeBonus(73).ToString(CultureInfo.InvariantCulture);
        _killGrowthLeaBonusText = GetKillGrowthConfiguredAttributeBonus(74).ToString(CultureInfo.InvariantCulture);
        _killGrowthWilBonusText = GetKillGrowthConfiguredAttributeBonus(75).ToString(CultureInfo.InvariantCulture);
        _killGrowthMagBonusText = GetKillGrowthConfiguredAttributeBonus(76).ToString(CultureInfo.InvariantCulture);
        _killGrowthChaBonusText = GetKillGrowthConfiguredAttributeBonus(77).ToString(CultureInfo.InvariantCulture);
    }
    private void AppendKillGrowthConfigJson(StringBuilder sb)
    {
        sb.AppendLine("  \"killGrowth\": {");
        sb.AppendLine("    \"enabled\": " + (_killGrowthEnabled ? "true" : "false") + ",");
        sb.AppendLine("    \"sharedExperience\": " + (_killGrowthSharedExperience ? "true" : "false") + ",");
        sb.AppendLine("    \"expPerLevel\": " + FormatKillGrowthDecimal(_killGrowthExpPerLevel) + ",");
        sb.AppendLine("    \"baseExp\": " + FormatKillGrowthDecimal(_killGrowthBaseExp) + ",");
        sb.AppendLine("    \"attributeBonus\": {");
        for (var i = 0; i < KillGrowthAttributeIds.Length; i++)
        {
            var id = KillGrowthAttributeIds[i];
            sb.Append("      \"").Append(id.ToString(CultureInfo.InvariantCulture)).Append("\": ")
              .Append(GetKillGrowthConfiguredAttributeBonus(id).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(i == KillGrowthAttributeIds.Length - 1 ? "" : ",");
        }
        sb.AppendLine("    },");
        var entries = _killGrowthLegacyExpByUid.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key).ToList();
        if (entries.Count > 0)
        {
            sb.AppendLine("    \"exp\": {");
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                sb.Append("      \"").Append(entry.Key.ToString(CultureInfo.InvariantCulture)).Append("\": ")
                  .Append(FormatKillGrowthDecimal(entry.Value));
                sb.AppendLine(i == entries.Count - 1 ? "" : ",");
            }
            sb.AppendLine("    },");
        }
        sb.AppendLine("    \"expBySave\": {");
        var saveEntries = _killGrowthExpBySaveId
            .Select(pair => new
            {
                SaveId = pair.Key,
                Entries = pair.Value.Where(value => value.Value > 0m).OrderBy(value => value.Key).ToList()
            })
            .Where(pair => pair.Entries.Count > 0)
            .OrderBy(pair => pair.SaveId, StringComparer.Ordinal)
            .ToList();
        for (var saveIndex = 0; saveIndex < saveEntries.Count; saveIndex++)
        {
            var saveEntry = saveEntries[saveIndex];
            sb.Append("      \"").Append(EscapeJson(saveEntry.SaveId)).AppendLine("\": {");
            for (var entryIndex = 0; entryIndex < saveEntry.Entries.Count; entryIndex++)
            {
                var entry = saveEntry.Entries[entryIndex];
                sb.Append("        \"").Append(entry.Key.ToString(CultureInfo.InvariantCulture)).Append("\": ")
                  .Append(FormatKillGrowthDecimal(entry.Value));
                sb.AppendLine(entryIndex == saveEntry.Entries.Count - 1 ? "" : ",");
            }
            sb.AppendLine(saveIndex == saveEntries.Count - 1 ? "      }" : "      },");
        }
        sb.AppendLine("    }");
        sb.AppendLine("  },");
    }
}
