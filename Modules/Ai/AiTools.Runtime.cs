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
    private bool TryResolveEmpFunction(EmpPluginDefinition plugin, string text, out EmpFunctionDefinition function, out string error)
    {
        function = null;
        error = "";
        if (plugin == null)
        {
            error = "plugin is null";
            return false;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            if (plugin.Functions.Count == 1)
            {
                function = plugin.Functions[0];
                return true;
            }
            error = "function is empty";
            return false;
        }

        var key = NormalizeAiKey(text);
        var exact = new List<EmpFunctionDefinition>();
        var partial = new List<EmpFunctionDefinition>();
        foreach (var candidate in plugin.Functions)
        {
            if (candidate == null)
                continue;
            var score = GetEmpFunctionMatchScore(candidate, key);
            if (score >= 2)
                exact.Add(candidate);
            else if (score == 1)
                partial.Add(candidate);
        }

        if (exact.Count == 1)
        {
            function = exact[0];
            return true;
        }
        if (exact.Count > 1)
        {
            error = "ambiguous function: " + string.Join(", ", exact.Take(5).Select(DescribeEmpFunction));
            return false;
        }
        if (partial.Count == 1)
        {
            function = partial[0];
            return true;
        }
        if (partial.Count > 1)
        {
            error = "ambiguous function: " + string.Join(", ", partial.Take(5).Select(DescribeEmpFunction));
            return false;
        }

        error = "function not found: " + text;
        return false;
    }
    private static string DescribeEmpPlugin(EmpPluginDefinition plugin)
    {
        if (plugin == null)
            return "<null>";
        return SafeEmpText(plugin.Name, SafeEmpText(plugin.Id, "<empty>")) + " [" + SafeEmpText(plugin.Id, "<empty>") + "]";
    }
    private static string DescribeEmpFunction(EmpFunctionDefinition function)
    {
        if (function == null)
            return "<null>";
        return SafeEmpText(function.Name, SafeEmpText(function.Id, "<empty>")) + " [" + SafeEmpText(function.Id, "<empty>") + "]";
    }
    private static bool EmpPluginMatchesFilter(EmpPluginDefinition plugin, string filter, bool includeFunctions)
    {
        if (plugin == null)
            return false;
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        var haystack = new StringBuilder();
        haystack.Append(plugin.Id ?? "").Append(' ')
            .Append(plugin.Name ?? "").Append(' ')
            .Append(plugin.Description ?? "").Append(' ')
            .Append(plugin.RelativePath ?? "").Append(' ')
            .Append(plugin.SourcePath ?? "");
        if (EmpTextMatches(haystack.ToString(), filter))
            return true;

        if (!includeFunctions)
            return false;

        for (var i = 0; i < plugin.Functions.Count; i++)
        {
            if (EmpFunctionMatchesFilter(plugin.Functions[i], filter))
                return true;
        }
        return false;
    }
    private static bool EmpFunctionMatchesFilter(EmpFunctionDefinition function, string filter)
    {
        if (function == null)
            return false;
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        var haystack = new StringBuilder();
        haystack.Append(function.Id ?? "").Append(' ')
            .Append(function.Name ?? "").Append(' ')
            .Append(function.Description ?? "").Append(' ')
            .Append(function.SourcePath ?? "").Append(' ')
            .Append(function.Error ?? "").Append(' ')
            .Append(GetEmpFunctionKindToken(function.Kind)).Append(' ')
            .Append(GetEmpValueKindToken(function.ValueKind)).Append(' ')
            .Append(function.DefaultValue ?? "");
        for (var i = 0; i < function.ValueOptions.Count; i++)
            haystack.Append(' ').Append(function.ValueOptions[i] ?? "");
        for (var i = 0; i < function.Operations.Count; i++)
            AppendEmpOperationSearchText(haystack, function.Operations[i]);
        for (var i = 0; i < function.OnEnableOperations.Count; i++)
            AppendEmpOperationSearchText(haystack, function.OnEnableOperations[i]);
        for (var i = 0; i < function.OnDisableOperations.Count; i++)
            AppendEmpOperationSearchText(haystack, function.OnDisableOperations[i]);
        return EmpTextMatches(haystack.ToString(), filter);
    }
    private static void AppendEmpOperationSearchText(StringBuilder sb, EmpOperationDefinition op)
    {
        if (sb == null || op == null)
            return;
        sb.Append(' ').Append(op.Tool ?? "").Append(' ').Append(op.Summary ?? "");
        foreach (var pair in op.Arguments)
        {
            sb.Append(' ').Append(pair.Key ?? "").Append(' ').Append(pair.Value ?? "");
        }
    }
    private static bool EmpTextMatches(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return true;
        if (string.IsNullOrWhiteSpace(haystack))
            return false;
        var hay = NormalizeAiKey(haystack);
        var key = NormalizeAiKey(needle);
        if (string.IsNullOrWhiteSpace(hay) || string.IsNullOrWhiteSpace(key))
            return false;
        return hay.Contains(key) || key.Contains(hay);
    }
    private static int GetEmpPluginMatchScore(EmpPluginDefinition plugin, string needle)
    {
        if (plugin == null)
            return 0;
        var score = 0;
        score = Math.Max(score, GetEmpMatchScore(plugin.Id, needle));
        score = Math.Max(score, GetEmpMatchScore(plugin.Name, needle));
        score = Math.Max(score, GetEmpMatchScore(plugin.Description, needle));
        score = Math.Max(score, GetEmpMatchScore(plugin.RelativePath, needle));
        score = Math.Max(score, GetEmpMatchScore(plugin.SourcePath, needle));
        if (score < 2)
        {
            foreach (var function in plugin.Functions)
            {
                score = Math.Max(score, GetEmpFunctionMatchScore(function, needle));
                if (score >= 2)
                    break;
            }
        }
        return score;
    }
    private static int GetEmpFunctionMatchScore(EmpFunctionDefinition function, string needle)
    {
        if (function == null)
            return 0;
        var score = 0;
        score = Math.Max(score, GetEmpMatchScore(function.Id, needle));
        score = Math.Max(score, GetEmpMatchScore(function.Name, needle));
        score = Math.Max(score, GetEmpMatchScore(function.Description, needle));
        score = Math.Max(score, GetEmpMatchScore(function.SourcePath, needle));
        score = Math.Max(score, GetEmpMatchScore(function.Error, needle));
        score = Math.Max(score, GetEmpMatchScore(GetEmpFunctionKindToken(function.Kind), needle));
        score = Math.Max(score, GetEmpMatchScore(GetEmpValueKindToken(function.ValueKind), needle));
        score = Math.Max(score, GetEmpMatchScore(function.DefaultValue, needle));
        for (var i = 0; i < function.ValueOptions.Count; i++)
            score = Math.Max(score, GetEmpMatchScore(function.ValueOptions[i], needle));
        for (var i = 0; i < function.Operations.Count; i++)
        {
            var op = function.Operations[i];
            if (op == null)
                continue;
            score = Math.Max(score, GetEmpMatchScore(op.Tool, needle));
            score = Math.Max(score, GetEmpMatchScore(op.Summary, needle));
            foreach (var pair in op.Arguments)
            {
                score = Math.Max(score, GetEmpMatchScore(pair.Key, needle));
                score = Math.Max(score, GetEmpMatchScore(pair.Value, needle));
            }
        }
        for (var i = 0; i < function.OnEnableOperations.Count; i++)
        {
            var op = function.OnEnableOperations[i];
            if (op == null)
                continue;
            score = Math.Max(score, GetEmpMatchScore(op.Tool, needle));
            score = Math.Max(score, GetEmpMatchScore(op.Summary, needle));
            foreach (var pair in op.Arguments)
            {
                score = Math.Max(score, GetEmpMatchScore(pair.Key, needle));
                score = Math.Max(score, GetEmpMatchScore(pair.Value, needle));
            }
        }
        for (var i = 0; i < function.OnDisableOperations.Count; i++)
        {
            var op = function.OnDisableOperations[i];
            if (op == null)
                continue;
            score = Math.Max(score, GetEmpMatchScore(op.Tool, needle));
            score = Math.Max(score, GetEmpMatchScore(op.Summary, needle));
            foreach (var pair in op.Arguments)
            {
                score = Math.Max(score, GetEmpMatchScore(pair.Key, needle));
                score = Math.Max(score, GetEmpMatchScore(pair.Value, needle));
            }
        }
        return score;
    }
    private static int GetEmpMatchScore(string candidate, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle) || string.IsNullOrWhiteSpace(candidate))
            return 0;
        var candidateKey = NormalizeAiKey(candidate);
        var needleKey = NormalizeAiKey(needle);
        if (string.IsNullOrWhiteSpace(candidateKey) || string.IsNullOrWhiteSpace(needleKey))
            return 0;
        if (string.Equals(candidateKey, needleKey, StringComparison.Ordinal))
            return 2;
        if (candidateKey.Contains(needleKey) || needleKey.Contains(candidateKey))
            return 1;
        return 0;
    }
}
