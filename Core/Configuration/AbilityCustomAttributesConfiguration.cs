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
    private void LoadAbilityCustomAttributesConfig(string json)
    {
        _abilityChanceOverrides.Clear();
        _abilityPowerOverrides.Clear();
        _abilityCostOverrides.Clear();

        try
        {
            var root = JObject.Parse(json);
            var entries = root["abilityCustomAttributes"] as JObject;
            if (entries == null)
                return;

            var loaded = 0;
            foreach (var property in entries.Properties())
            {
                if (loaded >= 8192)
                    break;
                if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var abilityId) ||
                    abilityId <= 0 ||
                    !(property.Value is JObject entry) ||
                    !(entry.Value<bool?>("enabled") ?? true) ||
                    !TryReadAbilityCustomAttributeInt(entry, "chance", out var chance) ||
                    !TryReadAbilityCustomAttributeInt(entry, "power", out var power) ||
                    !TryReadAbilityCustomAttributeInt(entry, "hpCost", out var hpCost) ||
                    !TryReadAbilityCustomAttributeInt(entry, "mpCost", out var mpCost) ||
                    !TryReadAbilityCustomAttributeInt(entry, "spCost", out var spCost))
                {
                    continue;
                }

                SetAbilityCustomAttributes(abilityId, chance, power, hpCost, mpCost, spCost);
                loaded++;
            }
        }
        catch
        {
            _abilityChanceOverrides.Clear();
            _abilityPowerOverrides.Clear();
            _abilityCostOverrides.Clear();
        }
    }
    private static bool TryReadAbilityCustomAttributeInt(JObject entry, string name, out int value)
    {
        value = 0;
        var token = entry?[name];
        return token != null &&
               int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
    private void AppendAbilityCustomAttributesConfigJson(StringBuilder sb)
    {
        var abilityIds = new List<int>();
        foreach (var pair in _abilityChanceOverrides)
        {
            if (pair.Key > 0 &&
                _abilityPowerOverrides.ContainsKey(pair.Key) &&
                _abilityCostOverrides.ContainsKey(pair.Key))
            {
                abilityIds.Add(pair.Key);
            }
        }
        abilityIds.Sort();

        sb.AppendLine("  \"abilityCustomAttributes\": {");
        for (var i = 0; i < abilityIds.Count; i++)
        {
            var abilityId = abilityIds[i];
            var chance = _abilityChanceOverrides[abilityId];
            var power = _abilityPowerOverrides[abilityId];
            var cost = _abilityCostOverrides[abilityId];
            sb.Append("    \"").Append(abilityId.ToString(CultureInfo.InvariantCulture)).AppendLine("\": {");
            sb.AppendLine("      \"enabled\": true,");
            sb.Append("      \"chance\": ").Append(chance.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
            sb.Append("      \"power\": ").Append(power.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
            sb.Append("      \"hpCost\": ").Append(cost.Hp.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
            sb.Append("      \"mpCost\": ").Append(cost.Mp.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
            sb.Append("      \"spCost\": ").Append(cost.Sp.ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("    }");
            if (i < abilityIds.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }
        sb.Append("  },");
    }
}
