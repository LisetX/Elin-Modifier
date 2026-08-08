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

internal sealed class DebugBepInExPlugin
{
    public readonly PluginInfo Info;
    public readonly object Instance;
    public readonly string Source;

    public DebugBepInExPlugin(PluginInfo info, object instance, string source)
    {
        Info = info;
        Instance = instance;
        Source = source ?? "";
    }
}

internal sealed class DebugExceptionTraceRecord
{
    public readonly int Frame;
    public readonly string Channel;
    public readonly string Source;
    public readonly string Level;
    public readonly string Trace;

    public DebugExceptionTraceRecord(int frame, string channel, string source, string level, string trace)
    {
        Frame = frame;
        Channel = channel ?? "";
        Source = source ?? "";
        Level = level ?? "";
        Trace = trace ?? "";
    }
}

internal sealed class DebugSubmoduleTraceEvent
{
    public readonly int Frame;
    public readonly string Method;
    public readonly string Target;
    public readonly string Result;
    public readonly string Exception;
    public readonly string[] Arguments;

    public DebugSubmoduleTraceEvent(int frame, string method, string target, string result, string exception, string[] arguments)
    {
        Frame = frame;
        Method = method ?? "";
        Target = target ?? "";
        Result = result ?? "";
        Exception = exception ?? "";
        Arguments = arguments ?? Array.Empty<string>();
    }
}

internal sealed class DebugStackFrameInfo
{
    public readonly string RawLine;
    public readonly string Wrapper;
    public readonly string TypeName;
    public readonly string MethodName;
    public readonly string Location;
    public readonly Type ResolvedType;
    public readonly MethodBase ResolvedMethod;

    public DebugStackFrameInfo(string rawLine, string wrapper, string typeName, string methodName, string location, Type resolvedType, MethodBase resolvedMethod)
    {
        RawLine = rawLine ?? "";
        Wrapper = wrapper ?? "";
        TypeName = typeName ?? "";
        MethodName = methodName ?? "";
        Location = location ?? "";
        ResolvedType = resolvedType;
        ResolvedMethod = resolvedMethod;
    }

    public Assembly ResolvedAssembly
    {
        get
        {
            if (ResolvedMethod != null && ResolvedMethod.DeclaringType != null)
                return ResolvedMethod.DeclaringType.Assembly;
            return ResolvedType == null ? null : ResolvedType.Assembly;
        }
    }
}

internal sealed class DebugErrorLogListener : ILogListener
{
    private readonly ElinModifierPlugin _owner;

    public DebugErrorLogListener(ElinModifierPlugin owner)
    {
        _owner = owner;
    }

    public LogLevel LogLevelFilter => LogLevel.Error | LogLevel.Fatal;

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        try { _owner?.OnDebugBepInExErrorLog(sender, eventArgs); }
        catch { }
    }

    public void Dispose()
    {
    }
}

internal sealed class DebugRawConfigEntry
{
    public readonly string Path;
    public readonly int LineIndex;
    public readonly string Section;
    public readonly string Key;
    public readonly string Value;

    public DebugRawConfigEntry(string path, int lineIndex, string section, string key, string value)
    {
        Path = path ?? "";
        LineIndex = lineIndex;
        Section = section ?? "";
        Key = key ?? "";
        Value = value ?? "";
    }
}

internal sealed class DebugRawConfigCache
{
    public readonly DateTime LastWriteTimeUtc;
    public readonly DebugRawConfigEntry[] Entries;
    public readonly string SearchText;

    public DebugRawConfigCache(DateTime lastWriteTimeUtc, DebugRawConfigEntry[] entries, string searchText)
    {
        LastWriteTimeUtc = lastWriteTimeUtc;
        Entries = entries ?? Array.Empty<DebugRawConfigEntry>();
        SearchText = searchText ?? "";
    }
}

internal sealed class DebugStringArrayFilterCache
{
    public readonly string[] Values;

    public DebugStringArrayFilterCache(string[] values)
    {
        Values = values ?? Array.Empty<string>();
    }
}

internal sealed class DebugRawConfigEntryFilterCache
{
    public readonly DebugRawConfigEntry[] Entries;

    public DebugRawConfigEntryFilterCache(DebugRawConfigEntry[] entries)
    {
        Entries = entries ?? Array.Empty<DebugRawConfigEntry>();
    }
}

internal sealed class DebugBepInExPluginFilterCache
{
    public readonly List<DebugBepInExPlugin> Plugins;

    public DebugBepInExPluginFilterCache(List<DebugBepInExPlugin> plugins)
    {
        Plugins = plugins ?? new List<DebugBepInExPlugin>();
    }
}

internal sealed class DebugTypeFilterCache
{
    public readonly string FilterKey;
    public readonly List<DebugTypeEntry> Entries;

    public DebugTypeFilterCache(string filterKey, List<DebugTypeEntry> entries)
    {
        FilterKey = filterKey ?? "";
        Entries = entries ?? new List<DebugTypeEntry>();
    }
}

internal sealed class DebugMember
{
    private readonly FieldInfo _field;
    private readonly PropertyInfo _property;
    public readonly string Kind;
    public readonly string Name;
    public readonly Type ValueType;

    public DebugMember(FieldInfo field)
    {
        _field = field;
        Kind = "F";
        Name = field.Name;
        ValueType = field.FieldType;
    }

    public DebugMember(PropertyInfo property)
    {
        _property = property;
        Kind = "P";
        Name = property.Name;
        ValueType = property.PropertyType;
    }

    public bool CanWrite
    {
        get
        {
            if (_field != null)
                return !_field.IsLiteral;
            if (_property == null)
                return false;
            var setter = _property.GetSetMethod(true);
            return setter != null || FindBackingField(_property) != null;
        }
    }

    public object GetValue(object instance)
    {
        if (_field != null)
            return _field.GetValue(_field.IsStatic ? null : instance);
        return _property.GetValue(IsStaticProperty(_property) ? null : instance, null);
    }

    public void SetValue(object instance, object value)
    {
        if (_field != null)
        {
            _field.SetValue(_field.IsStatic ? null : instance, value);
            return;
        }
        var setter = _property.GetSetMethod(true);
        if (setter != null)
        {
            _property.SetValue(IsStaticProperty(_property) ? null : instance, value, null);
            return;
        }
        var backingField = FindBackingField(_property);
        if (backingField != null)
            backingField.SetValue(backingField.IsStatic ? null : instance, value);
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
        var name = "<" + property.Name + ">k__BackingField";
        try
        {
            return property.DeclaringType.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class DebugBinding
{
    private readonly object _instance;
    private readonly DebugMember _member;
    private readonly int _index;
    private readonly object _key;
    private readonly Type _valueType;
    private readonly int _mode;
    private string _lastRawConfigValue = "";
    private int _lastRawConfigFrame = -9999;

    public DebugBinding(object instance, DebugMember member)
    {
        _instance = instance;
        _member = member;
        _index = -1;
        _mode = 0;
        _valueType = member.ValueType;
    }

    public DebugBinding(Array array, int index, Type valueType)
    {
        _instance = array;
        _index = index;
        _valueType = valueType ?? typeof(object);
        _mode = 1;
    }

    public DebugBinding(IList list, int index, Type valueType)
    {
        _instance = list;
        _index = index;
        _valueType = valueType ?? typeof(object);
        _mode = 2;
    }

    public DebugBinding(IDictionary dictionary, object key, Type valueType)
    {
        _instance = dictionary;
        _key = key;
        _valueType = valueType ?? typeof(object);
        _index = -1;
        _mode = 3;
    }

    public DebugBinding(ConfigEntryBase configEntry, Type valueType)
    {
        _instance = configEntry;
        _valueType = valueType ?? typeof(object);
        _index = -1;
        _mode = 4;
    }

    public DebugBinding(DebugRawConfigEntry rawConfigEntry)
    {
        _instance = rawConfigEntry;
        _valueType = typeof(string);
        _index = -1;
        _mode = 5;
    }

    public Type ValueType => _valueType;

    public void SetValue(object value)
    {
        if (_mode == 1)
        {
            ((Array)_instance).SetValue(value, _index);
            return;
        }
        if (_mode == 2)
        {
            ((IList)_instance)[_index] = value;
            return;
        }
        if (_mode == 3)
        {
            ((IDictionary)_instance)[_key] = value;
            return;
        }
        if (_mode == 4)
        {
            ((ConfigEntryBase)_instance).BoxedValue = value;
            return;
        }
        if (_mode == 5)
        {
            var text = value == null ? "" : value.ToString();
            if (string.Equals(text, _lastRawConfigValue, StringComparison.Ordinal) && Time.frameCount - _lastRawConfigFrame < 120)
                return;
            if (ElinModifierPlugin.WriteDebugRawConfigValue((DebugRawConfigEntry)_instance, text))
            {
                _lastRawConfigValue = text;
                _lastRawConfigFrame = Time.frameCount;
            }
            return;
        }
        _member.SetValue(_instance, value);
    }
}

internal sealed class DebugTypeEntry
{
    public readonly Type Type;
    public readonly string SearchText;
    public readonly object SingletonValue;
    public readonly bool HasStaticMembers;
    public readonly bool Interesting;
    public readonly string DisplayName;

    public DebugTypeEntry(Type type, string searchText, object singletonValue, bool hasStaticMembers, bool interesting)
    {
        Type = type;
        SearchText = searchText ?? "";
        SingletonValue = singletonValue;
        HasStaticMembers = hasStaticMembers;
        Interesting = interesting;
        DisplayName = type == null ? "Unknown" : (type.FullName ?? type.Name);
    }
}

