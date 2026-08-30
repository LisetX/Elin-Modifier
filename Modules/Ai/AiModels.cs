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

internal sealed class AiChatMessage
{
    public readonly string Role;
    public readonly string Content;

    public AiChatMessage(string role, string content)
    {
        Role = role ?? "";
        Content = content ?? "";
    }
}

internal sealed class AiStreamResult
{
    public readonly string ResponseText;
    public readonly string RawBody;

    public AiStreamResult(string responseText, string rawBody)
    {
        ResponseText = responseText ?? "";
        RawBody = rawBody ?? "";
    }
}

internal sealed class AiToolCall
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Arguments;

    public AiToolCall(string id, string name, string arguments)
    {
        Id = id ?? "";
        Name = name ?? "";
        Arguments = arguments ?? "";
    }
}

internal sealed class AiPendingDangerousAction
{
    public readonly int Id;
    public readonly string ToolName;
    public readonly string Arguments;
    public readonly string Summary;

    public AiPendingDangerousAction(int id, string toolName, string arguments, string summary)
    {
        Id = id;
        ToolName = toolName ?? "";
        Arguments = arguments ?? "";
        Summary = summary ?? "";
    }
}

internal sealed class AiPluginCacheEntry
{
    public readonly string Signature;
    public readonly string ToolName;
    public string Arguments;
    public string DisplayTitle;
    public string DisplayKind;
    public string Summary;
    public DateTime CachedUtc;

    public AiPluginCacheEntry(string signature, string toolName, string arguments, string displayTitle, string displayKind, string summary, DateTime cachedUtc)
    {
        Signature = signature ?? "";
        ToolName = toolName ?? "";
        Arguments = arguments ?? "";
        DisplayTitle = displayTitle ?? "";
        DisplayKind = displayKind ?? "";
        Summary = summary ?? "";
        CachedUtc = cachedUtc;
    }
}

internal sealed class AiRuntimePatchRecord
{
    public readonly string Id;
    public readonly MethodBase Method;
    public readonly string Mode;
    public readonly string TargetDescription;
    public readonly MethodInfo PatchMethod;
    public readonly HarmonyPatchType PatchType;
    public readonly string PatchMethodKey;

    public AiRuntimePatchRecord(string id, MethodBase method, string mode, string targetDescription)
        : this(id, method, mode, targetDescription, null, HarmonyPatchType.All, "")
    {
    }

    public AiRuntimePatchRecord(string id, MethodBase method, string mode, string targetDescription, MethodInfo patchMethod, HarmonyPatchType patchType, string patchMethodKey)
    {
        Id = id ?? "";
        Method = method;
        Mode = mode ?? "";
        TargetDescription = targetDescription ?? "";
        PatchMethod = patchMethod;
        PatchType = patchType;
        PatchMethodKey = patchMethodKey ?? "";
    }
}

internal sealed class AiRuntimeWorkspaceTextJob
{
    public readonly string Key;
    public readonly string ShortKey;
    public readonly DateTime CreatedUtc;
    public readonly Task<string> Task;

    public AiRuntimeWorkspaceTextJob(string key, DateTime createdUtc, Task<string> task)
    {
        Key = key ?? "";
        ShortKey = ElinModifierPlugin.Sha256Short(Key);
        CreatedUtc = createdUtc;
        Task = task;
    }
}

internal sealed class AiRuntimeSearchEntry
{
    public readonly string Kind;
    public readonly string AssemblyName;
    public readonly string TypeName;
    public readonly string Description;
    public readonly string Target;
    public readonly string SearchText;

    public AiRuntimeSearchEntry(string kind, string assemblyName, string typeName, string description, string target, string searchText)
    {
        Kind = kind ?? "";
        AssemblyName = assemblyName ?? "";
        TypeName = typeName ?? "";
        Description = description ?? "";
        Target = target ?? "";
        SearchText = searchText ?? "";
    }
}

internal sealed class AiRuntimeSearchScoredEntry
{
    public readonly AiRuntimeSearchEntry Entry;
    public readonly int Score;

    public AiRuntimeSearchScoredEntry(AiRuntimeSearchEntry entry, int score)
    {
        Entry = entry;
        Score = score;
    }
}

internal sealed class AiRuntimeMemberTarget
{
    private readonly object _owner;
    private readonly FieldInfo _field;
    private readonly PropertyInfo _property;

    public AiRuntimeMemberTarget(object owner, FieldInfo field)
    {
        _owner = owner;
        _field = field;
        _property = null;
    }

    public AiRuntimeMemberTarget(object owner, PropertyInfo property)
    {
        _owner = owner;
        _property = property;
        _field = null;
    }

    public Type ValueType => _field != null ? _field.FieldType : _property.PropertyType;

    public bool CanWrite
    {
        get
        {
            if (_field != null)
                return !_field.IsLiteral;
            return _property != null && (_property.GetSetMethod(true) != null || FindBackingField(_property) != null);
        }
    }

    public string Description
    {
        get
        {
            var member = _field != null ? (MemberInfo)_field : _property;
            var type = member == null ? null : member.DeclaringType;
            return (type == null ? "<unknown>" : type.FullName) + "." + (member == null ? "<null>" : member.Name);
        }
    }

    public object GetValue()
    {
        if (_field != null)
            return _field.GetValue(_field.IsStatic ? null : _owner);
        return _property.GetValue(IsStaticProperty(_property) ? null : _owner, null);
    }

    public void SetValue(object value)
    {
        if (_field != null)
        {
            _field.SetValue(_field.IsStatic ? null : _owner, value);
            return;
        }
        var setter = _property.GetSetMethod(true);
        if (setter != null)
        {
            _property.SetValue(IsStaticProperty(_property) ? null : _owner, value, null);
            return;
        }
        var backingField = FindBackingField(_property);
        if (backingField != null)
            backingField.SetValue(backingField.IsStatic ? null : _owner, value);
    }

    private static bool IsStaticProperty(PropertyInfo property)
    {
        var getter = property.GetGetMethod(true);
        if (getter != null)
            return getter.IsStatic;
        var setter = property.GetSetMethod(true);
        return setter != null && setter.IsStatic;
    }

    private static FieldInfo FindBackingField(PropertyInfo property)
    {
        if (property == null || property.DeclaringType == null)
            return null;
        try
        {
            return property.DeclaringType.GetField("<" + property.Name + ">k__BackingField", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class AiNameEntry
{
    public readonly string Kind;
    public readonly string Id;
    public readonly string Name;
    public readonly string Alias;
    public readonly string Category;
    public readonly string Extra;

    public AiNameEntry(string kind, string id, string name, string alias, string category, string extra)
    {
        Kind = kind ?? "";
        Id = id ?? "";
        Name = name ?? "";
        Alias = alias ?? "";
        Category = category ?? "";
        Extra = extra ?? "";
    }
}

