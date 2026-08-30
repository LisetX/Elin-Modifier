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

internal static class EmpSecurityScanner
{
    private static readonly string[] CommandExecutionTerms =
    {
        "systemdiagnosticsprocess", "processstart", "processstartinfo", "cmdexe", "powershellexe",
        "pwsh.exe", "wscript.shell", "shellexecute", "encodedcommand", "start-process",
        "mshta.exe", "rundll32.exe", "regsvr32.exe", "command.com", "bash.exe", "sh.exe",
        "createprocess", "winexec", "systemmanagementautomation"
    };

    private static readonly string[] NetworkTerms =
    {
        "systemnethttp", "httpclient", "webclient", "webrequest", "httpwebrequest", "socket",
        "tcpclient", "udpclient", "downloadfile", "downloadstring", "downloaddata",
        "invokewebrequest", "startbitstransfer", "bitsadmin", "certutil", "urlcache",
        "unitywebrequest", "downloadhandler", "ftpwebrequest"
    };

    private static readonly string[] NativeInjectionTerms =
    {
        "dllimport", "marshalcopy", "marshalgetdelegateforfunctionpointer", "virtualalloc",
        "virtualprotect", "writeprocessmemory", "createremotethread", "ntcreatethreadex",
        "queueuserapc", "openprocess", "loadlibrary", "getprocaddress", "setwindowshookex",
        "createfilemapping", "mapviewoffile", "kernel32", "ntdll", "user32", "shell32"
    };

    private static readonly string[] DynamicCodeTerms =
    {
        "assemblyload", "assemblyloadfile", "assemblyloadfrom", "dynamicmethod", "ilgenerator",
        "reflectionemit", "methodrental", "runtimehelperspreparemethod", "calli",
        "getdelegateforfunctionpointer"
    };

    private static readonly string[] ObfuscationTerms =
    {
        "frombase64string", "fromhexstring", "encodingutf8getstring", "encodingunicodegetstring",
        "encodingasciigetstring", "gzipstream", "deflatestream", "brotlistream", "cryptostream",
        "rijndael", "tripledes", "protecteddata", "securestring", "tobase64string", "xor"
    };

    public static void ApplyDefinitionScan(EmpPluginDefinition def, string rawText)
    {
        if (def == null)
            return;

        var pluginScan = ScanText("plugin file", rawText, false, true);
        if (pluginScan.Blocked)
            AppendError(ref def.Error, "security blocked: " + pluginScan.Reason);

        for (var i = 0; i < def.Functions.Count; i++)
        {
            var function = def.Functions[i];
            if (function == null)
                continue;

            var functionScan = ScanFunction(function);
            if (functionScan.Blocked)
            {
                AppendError(ref function.Error, "security blocked: " + functionScan.Reason);
                AppendError(ref def.Error, "security blocked in " + SafeName(function.Name, function.Id) + ": " + functionScan.Reason);
            }
        }
    }

    public static EmpSecurityScanResult ScanFunction(EmpFunctionDefinition function)
    {
        if (function == null)
            return EmpSecurityScanResult.Allow();

        var meta = ScanText("function metadata", (function.Id ?? "") + "\n" + (function.Name ?? "") + "\n" + (function.Description ?? ""), false, false);
        if (meta.Blocked)
            return meta;

        for (var i = 0; i < function.ValueOptions.Count; i++)
        {
            var option = ScanText("function option", function.ValueOptions[i], false, false);
            if (option.Blocked)
                return option;
        }

        for (var i = 0; i < function.ValueParameters.Count; i++)
        {
            var parameter = function.ValueParameters[i];
            if (parameter == null)
                continue;
            var parameterScan = ScanText("function parameter", (parameter.Key ?? "") + "\n" + (parameter.Label ?? "") + "\n" + (parameter.DefaultValue ?? ""), false, false);
            if (parameterScan.Blocked)
                return parameterScan;
        }

        var result = ScanOperationList(function.Operations, "operation");
        if (result.Blocked)
            return result;
        result = ScanOperationList(function.OnEnableOperations, "on_enable");
        if (result.Blocked)
            return result;
        return ScanOperationList(function.OnDisableOperations, "on_disable");
    }

    public static EmpSecurityScanResult ScanOperation(EmpOperationDefinition operation, string expandedArgumentsJson)
    {
        if (operation == null)
            return EmpSecurityScanResult.Allow();

        var tool = operation.Tool ?? "";
        var normalizedTool = NormalizeSecurityText(tool);
        var toolScan = ScanText("tool", tool, false, false);
        if (toolScan.Blocked)
            return toolScan;

        if (normalizedTool == "runtimeharmonypatch")
        {
            var patchScan = ScanHarmonyPatchArguments(expandedArgumentsJson, true);
            if (patchScan.Blocked)
                return patchScan;
        }

        if (!string.IsNullOrWhiteSpace(expandedArgumentsJson))
        {
            var argsScan = ScanText("operation arguments", expandedArgumentsJson, normalizedTool == "runtimeharmonypatch", true);
            if (argsScan.Blocked)
                return argsScan;
        }

        foreach (var pair in operation.Arguments)
        {
            var keyScan = ScanText("argument key", pair.Key, false, false);
            if (keyScan.Blocked)
                return keyScan;

            var valueScan = ScanText("argument " + pair.Key, pair.Value, normalizedTool == "runtimeharmonypatch" || IsCodeArgument(pair.Key), true);
            if (valueScan.Blocked)
                return valueScan;
        }

        return EmpSecurityScanResult.Allow();
    }

    public static EmpSecurityScanResult ScanHarmonyPatchArguments(string args, bool strict)
    {
        var all = ScanText("runtime_harmony_patch arguments", args, true, strict);
        if (all.Blocked)
            return all;

        Dictionary<string, string> values;
        if (TryReadJsonObject(args, out values))
        {
            foreach (var pair in values)
            {
                var isCode = IsCodeArgument(pair.Key);
                var scan = ScanText("runtime_harmony_patch." + pair.Key, pair.Value, isCode, strict || isCode);
                if (scan.Blocked)
                    return scan;
            }
        }

        return EmpSecurityScanResult.Allow();
    }

    public static EmpSecurityScanResult ScanText(string label, string text, bool codeContext, bool strictObfuscation)
    {
        if (string.IsNullOrWhiteSpace(text))
            return EmpSecurityScanResult.Allow();

        var decoded = DecodeCommonEscapes(text);
        var lower = decoded.ToLowerInvariant();
        var compact = NormalizeSecurityText(decoded);
        var where = string.IsNullOrWhiteSpace(label) ? "content" : label;

        if (ContainsAny(compact, lower, CommandExecutionTerms) || Regex.IsMatch(lower, @"(^|[^a-z0-9_])(cmd|powershell|pwsh)(\.exe)?([^a-z0-9_]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return EmpSecurityScanResult.Block(where + " contains command or shell execution");

        if (ContainsAny(compact, lower, NetworkTerms) || Regex.IsMatch(lower, @"(^|[^a-z0-9_])(curl|wget)(\.exe)?([^a-z0-9_]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return EmpSecurityScanResult.Block(where + " contains network/download behavior");

        if (ContainsAny(compact, lower, NativeInjectionTerms))
            return EmpSecurityScanResult.Block(where + " contains native DLL/process injection behavior");

        if (ContainsAny(compact, lower, DynamicCodeTerms))
            return EmpSecurityScanResult.Block(where + " contains dynamic code loading or shellcode-like behavior");

        if (codeContext && Regex.IsMatch(lower, @"(^|[^a-z0-9_])(unsafe|fixed)([^a-z0-9_]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return EmpSecurityScanResult.Block(where + " contains unsafe memory code");

        if (strictObfuscation || codeContext)
        {
            if (ContainsAny(compact, lower, ObfuscationTerms) || Regex.IsMatch(decoded, @"\b(Aes|DES|TripleDES|Rijndael)\b", RegexOptions.CultureInvariant))
                return EmpSecurityScanResult.Block(where + " contains encrypted/encoded behavior");

            if (Regex.IsMatch(decoded, @"\bnew\s+byte\s*\[\s*\]\s*\{(?:\s*(?:0x[0-9a-fA-F]{1,2}|\d{1,3})\s*,){24,}", RegexOptions.CultureInvariant))
                return EmpSecurityScanResult.Block(where + " contains a suspicious byte payload");

            if (ContainsSuspiciousEncodedBlob(decoded))
                return EmpSecurityScanResult.Block(where + " contains a suspicious encoded payload");

            if (ContainsSuspiciousEncodedStringLiterals(decoded))
                return EmpSecurityScanResult.Block(where + " contains split encoded payload strings");

            if (ContainsSuspiciousEscapes(decoded))
                return EmpSecurityScanResult.Block(where + " contains heavy escape-based obfuscation");
        }

        return EmpSecurityScanResult.Allow();
    }

    public static EmpSecurityScanResult ScanResolvedReference(string label, string text)
    {
        return ScanText(label, text, true, false);
    }

    private static EmpSecurityScanResult ScanOperationList(List<EmpOperationDefinition> operations, string label)
    {
        if (operations == null)
            return EmpSecurityScanResult.Allow();
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op == null)
                continue;
            var scan = ScanOperation(op, "");
            if (scan.Blocked)
                return EmpSecurityScanResult.Block(label + "[" + i.ToString(CultureInfo.InvariantCulture) + "]: " + scan.Reason);
        }
        return EmpSecurityScanResult.Allow();
    }

    private static bool IsCodeArgument(string key)
    {
        var normalized = NormalizeSecurityText(key);
        return normalized == "code" || normalized == "patchcode" || normalized == "source" || normalized == "script";
    }

    private static bool ContainsAny(string compact, string lower, string[] terms)
    {
        for (var i = 0; i < terms.Length; i++)
        {
            var term = terms[i] ?? "";
            if (term.Length == 0)
                continue;
            if (term.IndexOf(".", StringComparison.Ordinal) >= 0 || term.IndexOf("-", StringComparison.Ordinal) >= 0 || term.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                if (lower.IndexOf(term.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                continue;
            }

            var normalized = NormalizeSecurityText(term);
            if (!string.IsNullOrEmpty(normalized) && compact.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool ContainsSuspiciousEncodedBlob(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (Regex.IsMatch(text, @"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{120,}={0,2}(?![A-Za-z0-9+/])", RegexOptions.CultureInvariant))
            return true;
        if (Regex.IsMatch(text, @"(?<![0-9a-fA-F])[0-9a-fA-F]{160,}(?![0-9a-fA-F])", RegexOptions.CultureInvariant))
            return true;
        if (Regex.IsMatch(text, @"(?:\\x[0-9a-fA-F]{2}){32,}", RegexOptions.CultureInvariant))
            return true;
        return false;
    }

    private static bool ContainsSuspiciousEncodedStringLiterals(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var aggregateBase64 = 0;
        var aggregateHex = 0;
        var matches = Regex.Matches(text, "\"([A-Za-z0-9+/=]{24,})\"|'([A-Za-z0-9+/=]{24,})'|@\"([A-Za-z0-9+/=]{24,})\"", RegexOptions.CultureInvariant);
        foreach (Match match in matches)
        {
            var value = "";
            for (var i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success)
                {
                    value = match.Groups[i].Value;
                    break;
                }
            }
            if (value.Length >= 24 && Regex.IsMatch(value, @"^[A-Za-z0-9+/]+={0,2}$", RegexOptions.CultureInvariant))
                aggregateBase64 += value.Length;
        }

        var hexMatches = Regex.Matches(text, "\"([0-9a-fA-F]{32,})\"|'([0-9a-fA-F]{32,})'|@\"([0-9a-fA-F]{32,})\"", RegexOptions.CultureInvariant);
        foreach (Match match in hexMatches)
        {
            for (var i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success)
                {
                    aggregateHex += match.Groups[i].Value.Length;
                    break;
                }
            }
        }

        return aggregateBase64 >= 160 || aggregateHex >= 192;
    }

    private static bool ContainsSuspiciousEscapes(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 120)
            return false;
        var count = Regex.Matches(text, @"\\u[0-9a-fA-F]{4}|\\x[0-9a-fA-F]{2}|\\[0-7]{3}", RegexOptions.CultureInvariant).Count;
        return count >= 16;
    }

    private static bool TryReadJsonObject(string json, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            var obj = JObject.Parse(json);
            foreach (var prop in obj.Properties())
                values[prop.Name] = prop.Value == null || prop.Value.Type == JTokenType.Null ? "" : prop.Value.Type == JTokenType.String ? prop.Value.Value<string>() ?? "" : prop.Value.ToString(Formatting.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string DecodeCommonEscapes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        try
        {
            var decoded = Regex.Replace(text, @"\\u([0-9a-fA-F]{4})", match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
            decoded = Regex.Replace(decoded, @"\\x([0-9a-fA-F]{2})", match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
            return decoded;
        }
        catch
        {
            return text;
        }
    }

    private static string NormalizeSecurityText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = char.ToLowerInvariant(text[i]);
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static void AppendError(ref string existing, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        if (string.IsNullOrWhiteSpace(existing))
        {
            existing = message;
            return;
        }
        if (existing.IndexOf(message, StringComparison.OrdinalIgnoreCase) < 0)
            existing += "; " + message;
    }

    internal static string SafeName(string text, string fallback)
    {
        return string.IsNullOrWhiteSpace(text) ? (fallback ?? "") : text;
    }
}
