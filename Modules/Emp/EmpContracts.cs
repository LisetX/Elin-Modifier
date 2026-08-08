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
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

internal enum EmpFunctionKind
{
    Toggle,
    Value,
    Patch,
    Button
}

internal enum EmpValueKind
{
    String,
    Int,
    Float,
    Bool,
    Enum
}

internal sealed class EmpPluginDefinition
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public string SourcePath = "";
    public string RelativePath = "";
    public string Error = "";
    public readonly List<EmpFunctionDefinition> Functions = new List<EmpFunctionDefinition>();

    public bool IsValid => string.IsNullOrEmpty(Error);
}

internal sealed class EmpFunctionDefinition
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public string SourcePath = "";
    public string Error = "";
    public EmpFunctionKind Kind = EmpFunctionKind.Toggle;
    public EmpValueKind ValueKind = EmpValueKind.String;
    public bool DefaultEnabled;
    public string DefaultValue = "";
    public readonly List<string> ValueOptions = new List<string>();
    public readonly List<EmpValueParameterDefinition> ValueParameters = new List<EmpValueParameterDefinition>();
    public readonly List<EmpOperationDefinition> Operations = new List<EmpOperationDefinition>();
    public readonly List<EmpOperationDefinition> OnEnableOperations = new List<EmpOperationDefinition>();
    public readonly List<EmpOperationDefinition> OnDisableOperations = new List<EmpOperationDefinition>();

    public bool IsValid => string.IsNullOrEmpty(Error);
}

internal sealed class EmpValueParameterDefinition
{
    public string Key = "";
    public string Label = "";
    public string DefaultValue = "";
    public EmpValueKind ValueKind = EmpValueKind.String;
}

internal sealed class EmpOperationDefinition
{
    public string Tool = "";
    public string Summary = "";
    public readonly Dictionary<string, string> Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

internal sealed class EmpFunctionState
{
    public bool Enabled;
    public string Value = "";
    public string LastAppliedSignature = "";
    public bool LastApplySucceeded;
    public bool Initialized;
    public bool PendingApply;
}

internal sealed class EmpSecurityScanResult
{
    public readonly bool Blocked;
    public readonly string Reason;

    private EmpSecurityScanResult(bool blocked, string reason)
    {
        Blocked = blocked;
        Reason = reason ?? "";
    }

    public static EmpSecurityScanResult Allow()
    {
        return new EmpSecurityScanResult(false, "");
    }

    public static EmpSecurityScanResult Block(string reason)
    {
        return new EmpSecurityScanResult(true, string.IsNullOrWhiteSpace(reason) ? "unsafe EMP content" : reason);
    }
}
