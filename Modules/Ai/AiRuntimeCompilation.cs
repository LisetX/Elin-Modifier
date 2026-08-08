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

public sealed partial class ElinModifierPlugin
{
    private static List<string> FilterAiRuntimeCompilerReferencesByAssemblyIdentity(IEnumerable<string> paths)
    {
        var result = new List<string>();
        var seenIdentity = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (paths == null)
            return result;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            string key;
            try
            {
                var name = AssemblyName.GetAssemblyName(path);
                key = name.FullName ?? Path.GetFileName(path);
            }
            catch
            {
                key = Path.GetFileName(path);
            }
            if (string.IsNullOrWhiteSpace(key))
                key = path;
            if (seenIdentity.Add(key))
                result.Add(path);
        }
        return result;
    }
    private static void AddAiRuntimeCompilerReference(object parameters, string path)
    {
        if (parameters == null || string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            var prop = parameters.GetType().GetProperty("ReferencedAssemblies", BindingFlags.Instance | BindingFlags.Public);
            var collection = prop == null ? null : prop.GetValue(parameters, null);
            if (collection == null)
                return;
            var add = collection.GetType().GetMethod("Add", new[] { typeof(string) });
            if (add != null)
                add.Invoke(collection, new object[] { path });
        }
        catch { }
    }
    private static object GetAiRuntimeCompilerResultErrors(object result)
    {
        if (result == null)
            return null;
        try
        {
            var prop = result.GetType().GetProperty("Errors", BindingFlags.Instance | BindingFlags.Public);
            return prop == null ? null : prop.GetValue(result, null);
        }
        catch { return null; }
    }
    private static bool AiRuntimeCompilerErrorsHasErrors(object errors)
    {
        if (errors == null)
            return false;
        try
        {
            var prop = errors.GetType().GetProperty("HasErrors", BindingFlags.Instance | BindingFlags.Public);
            var value = prop == null ? null : prop.GetValue(errors, null);
            return value is bool b && b;
        }
        catch { return false; }
    }
    private static int GetAiRuntimeCompilerErrorsCount(object errors)
    {
        if (errors == null)
            return 0;
        try
        {
            var prop = errors.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            var value = prop == null ? null : prop.GetValue(errors, null);
            return value is int i ? i : 0;
        }
        catch { return 0; }
    }
    private static object GetAiRuntimeCompilerError(object errors, int index)
    {
        if (errors == null)
            return null;
        try
        {
            var prop = errors.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
            if (prop != null)
                return prop.GetValue(errors, new object[] { index });
        }
        catch { }
        try
        {
            var method = errors.GetType().GetMethod("get_Item", new[] { typeof(int) });
            return method == null ? null : method.Invoke(errors, new object[] { index });
        }
        catch { return null; }
    }
    private static bool GetAiRuntimeCompilerErrorBool(object error, string name)
    {
        try
        {
            var prop = error == null ? null : error.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var value = prop == null ? null : prop.GetValue(error, null);
            return value is bool b && b;
        }
        catch { return false; }
    }
    private static int GetAiRuntimeCompilerErrorInt(object error, string name)
    {
        try
        {
            var prop = error == null ? null : error.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var value = prop == null ? null : prop.GetValue(error, null);
            return value is int i ? i : 0;
        }
        catch { return 0; }
    }
    private static string GetAiRuntimeCompilerErrorString(object error, string name)
    {
        try
        {
            var prop = error == null ? null : error.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var value = prop == null ? null : prop.GetValue(error, null);
            return value == null ? "" : value.ToString();
        }
        catch { return ""; }
    }
    private static Assembly GetAiRuntimeCompilerResultAssembly(object result)
    {
        if (result == null)
            return null;
        try
        {
            var prop = result.GetType().GetProperty("CompiledAssembly", BindingFlags.Instance | BindingFlags.Public);
            return prop == null ? null : prop.GetValue(result, null) as Assembly;
        }
        catch { return null; }
    }
    private static void SetAiRuntimeProperty(object target, string name, object value)
    {
        if (target == null)
            return;
        try
        {
            var prop = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop != null && prop.CanWrite)
                prop.SetValue(target, value, null);
        }
        catch { }
    }
    private static string MakeAiRuntimeSafeIdentifier(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Patch";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                sb.Append(ch);
            else
                sb.Append('_');
        }
        if (sb.Length == 0 || char.IsDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }
    private bool TryResolveAiRuntimeMemberTarget(string expression, out AiRuntimeMemberTarget target, out string error)
    {
        target = null;
        error = "";
        object owner;
        Type ownerType;
        string memberName;
        if (!TryResolveAiRuntimeOwnerAndMember(expression, out owner, out ownerType, out memberName, out error))
            return false;
        if (string.IsNullOrWhiteSpace(memberName))
        {
            error = "member name is empty";
            return false;
        }

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var field = ownerType.GetField(memberName, flags);
        if (field != null)
        {
            target = new AiRuntimeMemberTarget(owner, field);
            return true;
        }
        var prop = ownerType.GetProperty(memberName, flags);
        if (prop != null && prop.GetIndexParameters().Length == 0)
        {
            target = new AiRuntimeMemberTarget(owner, prop);
            return true;
        }
        error = "field/property not found: " + memberName + " on " + GetDebugTypeName(ownerType);
        return false;
    }
    private bool TryResolveAiRuntimeOwnerAndMember(string expression, out object owner, out Type ownerType, out string memberName, out string error)
    {
        owner = null;
        ownerType = null;
        memberName = "";
        error = "";
        expression = (expression ?? "").Trim();
        if (string.IsNullOrEmpty(expression))
        {
            error = "empty expression";
            return false;
        }

        if (expression.StartsWith("Plugin:", StringComparison.OrdinalIgnoreCase))
            return TryResolveAiRuntimePluginExpression(expression.Substring("Plugin:".Length), out owner, out ownerType, out memberName, out error);
        if (expression.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
            return TryResolveAiRuntimeTypeExpression(expression.Substring("Type:".Length), out owner, out ownerType, out memberName, out error);
        if (expression.StartsWith("Assembly:", StringComparison.OrdinalIgnoreCase))
            return TryResolveAiRuntimeAssemblyExpression(expression.Substring("Assembly:".Length), out owner, out ownerType, out memberName, out error);

        var parts = SplitAiRuntimePath(expression);
        if (parts.Count < 2)
        {
            error = "expression must contain at least root.member. " + GetAiRuntimePatchTargetHint();
            return false;
        }
        if (!TryResolveAiRuntimeRoot(parts[0], out owner, out ownerType, out error))
        {
            var firstDot = expression.IndexOf('.');
            if (firstDot > 0)
            {
                var assemblyName = expression.Substring(0, firstDot);
                var rest = expression.Substring(firstDot + 1);
                if (TryResolveAiRuntimeAssemblyExpression(assemblyName + ":" + rest, out owner, out ownerType, out memberName, out error))
                    return true;
            }
            error = error + ". " + GetAiRuntimePatchTargetHint();
            return false;
        }
        for (var i = 1; i < parts.Count - 1; i++)
        {
            if (!TryGetAiRuntimeMemberValue(owner, ownerType, parts[i], out owner, out ownerType, out error))
                return false;
            if (owner == null && ownerType == null)
            {
                error = "null while resolving " + parts[i];
                return false;
            }
        }
        memberName = parts[parts.Count - 1];
        return true;
    }
    private bool TryResolveAiRuntimeMethodTarget(string expression, out MethodBase method, out string error)
    {
        method = null;
        error = "";
        object owner;
        Type ownerType;
        string methodName;
        if (!TryResolveAiRuntimeOwnerAndMember(expression, out owner, out ownerType, out methodName, out error))
            return false;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var methods = ownerType.GetMethods(flags);
        var matches = new List<MethodInfo>();
        foreach (var candidate in methods)
        {
            if (string.Equals(candidate.Name, methodName, StringComparison.Ordinal) ||
                string.Equals(candidate.Name, methodName, StringComparison.OrdinalIgnoreCase))
                matches.Add(candidate);
        }
        if (matches.Count == 0)
        {
            error = "method not found: " + methodName + " on " + GetDebugTypeName(ownerType);
            return false;
        }
        if (matches.Count > 1)
        {
            var sb = new StringBuilder();
            sb.Append("ambiguous method: ").Append(methodName).Append(" candidates:");
            for (var i = 0; i < Math.Min(matches.Count, 8); i++)
                sb.Append("\n").Append(FormatMethodForAiRuntime(matches[i]));
            error = sb.ToString();
            return false;
        }
        method = matches[0];
        return true;
    }
    private bool TryResolveAiRuntimePluginExpression(string text, out object owner, out Type ownerType, out string memberName, out string error)
    {
        owner = null;
        ownerType = null;
        memberName = "";
        error = "";
        var colon = text.IndexOf(':');
        string pluginKey;
        string path;
        if (colon >= 0)
        {
            pluginKey = text.Substring(0, colon);
            path = text.Substring(colon + 1);
        }
        else
        {
            var dot = text.IndexOf('.');
            if (dot < 0)
            {
                error = "Plugin expression must be Plugin:guid:member or Plugin:guid.member";
                return false;
            }
            pluginKey = text.Substring(0, dot);
            path = text.Substring(dot + 1);
        }
        var plugin = FindAiRuntimePlugin(pluginKey);
        if (plugin == null)
        {
            error = "plugin not found: " + pluginKey;
            return false;
        }
        owner = plugin.Instance ?? SafeDebugValue(() => plugin.Info?.Instance);
        if (owner == null)
        {
            error = "plugin instance is null: " + pluginKey;
            return false;
        }
        ownerType = owner.GetType();
        return ResolveAiRuntimePathAfterOwner(path, ref owner, ref ownerType, out memberName, out error);
    }
    private bool TryResolveAiRuntimeTypeExpression(string text, out object owner, out Type ownerType, out string memberName, out string error)
    {
        owner = null;
        ownerType = null;
        memberName = "";
        error = "";
        var split = FindAiRuntimeLastPathDot(text);
        if (split < 0)
        {
            error = "Type expression must end with .member";
            return false;
        }
        var typeName = text.Substring(0, split);
        var path = text.Substring(split + 1);
        var type = FindAiRuntimeType(typeName, "");
        if (type == null)
        {
            error = "type not found: " + typeName;
            return false;
        }
        owner = type;
        ownerType = type;
        return ResolveAiRuntimePathAfterOwner(path, ref owner, ref ownerType, out memberName, out error);
    }
    private bool TryResolveAiRuntimeAssemblyExpression(string text, out object owner, out Type ownerType, out string memberName, out string error)
    {
        owner = null;
        ownerType = null;
        memberName = "";
        error = "";
        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            error = "Assembly expression must be Assembly:assemblyName:Namespace.Type.member";
            return false;
        }
        var assemblyName = text.Substring(0, colon);
        var rest = text.Substring(colon + 1);
        var split = FindAiRuntimeLastPathDot(rest);
        if (split < 0)
        {
            error = "Assembly expression must end with .member";
            return false;
        }
        var typeName = rest.Substring(0, split);
        var path = rest.Substring(split + 1);
        var type = FindAiRuntimeType(typeName, assemblyName);
        if (type == null)
        {
            error = "type not found in assembly " + assemblyName + ": " + typeName;
            return false;
        }
        owner = type;
        ownerType = type;
        return ResolveAiRuntimePathAfterOwner(path, ref owner, ref ownerType, out memberName, out error);
    }
    private bool ResolveAiRuntimePathAfterOwner(string path, ref object owner, ref Type ownerType, out string memberName, out string error)
    {
        memberName = "";
        error = "";
        var parts = SplitAiRuntimePath(path);
        if (parts.Count == 0)
        {
            error = "member path is empty";
            return false;
        }
        for (var i = 0; i < parts.Count - 1; i++)
        {
            if (!TryGetAiRuntimeMemberValue(owner, ownerType, parts[i], out owner, out ownerType, out error))
                return false;
        }
        memberName = parts[parts.Count - 1];
        return true;
    }
    private bool TryResolveAiRuntimeRoot(string root, out object owner, out Type ownerType, out string error)
    {
        owner = null;
        ownerType = null;
        error = "";
        var key = NormalizeAiKey(root);
        if (key == "plugin" || key == "plugins")
        {
            owner = UnityChainloader.Instance;
            ownerType = owner == null ? typeof(UnityChainloader) : owner.GetType();
            return owner != null;
        }
        if (key == "instance" || key == "elinmodifier" || key == "mod")
        {
            owner = this;
            ownerType = GetType();
            return true;
        }
        if (key == "eclass")
        {
            owner = typeof(EClass);
            ownerType = typeof(EClass);
            return true;
        }
        if (key == "pc" || key == "playerchara")
        {
            owner = GameAccess.Characters.PlayerCharacter;
            ownerType = owner == null ? typeof(Chara) : owner.GetType();
            return owner != null;
        }
        if (key == "player")
        {
            owner = GameAccess.Runtime.Player;
            ownerType = owner == null ? typeof(Player) : owner.GetType();
            return owner != null;
        }
        if (key == "dialogue_npc" || key == "talking_npc" || key == "dialoguenpc" || key == "talkingnpc" || key == "对话npc" || key == "对话中npc")
        {
            owner = GetTalkingNpc();
            ownerType = owner == null ? typeof(Chara) : owner.GetType();
            if (owner == null)
                error = "dialogue_npc is null";
            return owner != null;
        }
        if (key == "nearby_npc" || key == "nearbynpc" || key == "附近npc")
        {
            owner = GetSelectedNearbyNpc();
            ownerType = owner == null ? typeof(Chara) : owner.GetType();
            if (owner == null)
                error = "nearby_npc is null or not selected";
            return owner != null;
        }
        if (key == "zone")
        {
            owner = GameAccess.World.CurrentZone;
            ownerType = owner == null ? typeof(Zone) : owner.GetType();
            return owner != null;
        }
        if (key == "map")
        {
            owner = GameAccess.World.CurrentMap;
            ownerType = owner == null ? typeof(Map) : owner.GetType();
            return owner != null;
        }
        if (key == "world")
        {
            owner = GameAccess.World.CurrentWorld;
            ownerType = owner == null ? typeof(World) : owner.GetType();
            return owner != null;
        }
        if (key == "scene")
        {
            owner = GameAccess.Ui.Scene;
            ownerType = owner == null ? typeof(Scene) : owner.GetType();
            return owner != null;
        }
        if (key == "sources")
        {
            owner = GameAccess.Sources.Manager;
            ownerType = owner == null ? typeof(SourceManager) : owner.GetType();
            return owner != null;
        }
        var type = FindAiRuntimeType(root, "");
        if (type != null)
        {
            owner = type;
            ownerType = type;
            return true;
        }
        error = "unknown root: " + root;
        return false;
    }
    private bool TryGetAiRuntimeMemberValue(object owner, Type ownerType, string memberName, out object value, out Type valueType, out string error)
    {
        value = null;
        valueType = null;
        error = "";
        if (ownerType == null)
        {
            error = "owner type is null";
            return false;
        }
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var field = ownerType.GetField(memberName, flags);
        if (field != null)
        {
            value = field.GetValue(field.IsStatic ? null : owner);
            valueType = value == null ? field.FieldType : value.GetType();
            return true;
        }
        var prop = ownerType.GetProperty(memberName, flags);
        if (prop != null && prop.GetIndexParameters().Length == 0)
        {
            value = prop.GetValue(IsStaticPropertyForAiRuntime(prop) ? null : owner, null);
            valueType = value == null ? prop.PropertyType : value.GetType();
            return true;
        }
        error = "member not found: " + memberName + " on " + GetDebugTypeName(ownerType);
        return false;
    }
    private bool TryResolveAiRuntimeReadExpression(string expression, out string description, out object value, out Type valueType, out string error)
    {
        description = "";
        value = null;
        valueType = null;
        error = "";
        expression = (expression ?? "").Trim();
        if (string.IsNullOrEmpty(expression))
        {
            error = "empty expression";
            return false;
        }

        if (expression.StartsWith("Plugin:", StringComparison.OrdinalIgnoreCase))
            return TryResolveAiRuntimePluginReadExpression(expression.Substring("Plugin:".Length), out description, out value, out valueType, out error);
        if (expression.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
            return TryResolveAiRuntimeTypeReadExpression(expression.Substring("Type:".Length), "", out description, out value, out valueType, out error);
        if (expression.StartsWith("Assembly:", StringComparison.OrdinalIgnoreCase))
            return TryResolveAiRuntimeAssemblyReadExpression(expression.Substring("Assembly:".Length), out description, out value, out valueType, out error);

        var parts = SplitAiRuntimePath(expression);
        if (parts.Count == 0)
        {
            error = "expression must contain at least root.member or root[index]";
            return false;
        }
        if (!TryResolveAiRuntimeRoot(parts[0], out var owner, out var ownerType, out error))
        {
            var firstDot = FindAiRuntimeFirstTopLevelDot(expression);
            if (firstDot > 0)
            {
                var assemblyName = expression.Substring(0, firstDot);
                var rest = expression.Substring(firstDot + 1);
                if (TryResolveAiRuntimeTypeReadExpression(rest, assemblyName, out description, out value, out valueType, out error))
                {
                    description = "Assembly:" + assemblyName + ":" + description;
                    return true;
                }
            }
            error = error + ". Use dot paths such as EClass.pc.idFaith or nearby_npc.faith; use [\"key\"] or [0] for dictionaries/lists.";
            return false;
        }

        description = parts[0];
        if (parts.Count == 1)
        {
            value = owner;
            valueType = owner == null ? ownerType : owner.GetType();
            return true;
        }
        return TryResolveAiRuntimeReadPathAfterOwner(parts, 1, ref owner, ref ownerType, ref description, out value, out valueType, out error);
    }
    private bool TryResolveAiRuntimePluginReadExpression(string text, out string description, out object value, out Type valueType, out string error)
    {
        description = "";
        value = null;
        valueType = null;
        error = "";
        var colon = text.IndexOf(':');
        string pluginKey;
        string path;
        if (colon >= 0)
        {
            pluginKey = text.Substring(0, colon);
            path = text.Substring(colon + 1);
        }
        else
        {
            var dot = FindAiRuntimeFirstTopLevelDot(text);
            if (dot < 0)
            {
                error = "Plugin expression must be Plugin:guid.member or Plugin:guid:member";
                return false;
            }
            pluginKey = text.Substring(0, dot);
            path = text.Substring(dot + 1);
        }
        var plugin = FindAiRuntimePlugin(pluginKey);
        if (plugin == null)
        {
            error = "plugin not found: " + pluginKey;
            return false;
        }
        var owner = plugin.Instance ?? SafeDebugValue(() => plugin.Info?.Instance);
        if (owner == null)
        {
            error = "plugin instance is null: " + pluginKey;
            return false;
        }
        var ownerType = owner.GetType();
        description = "Plugin:" + pluginKey;
        var parts = SplitAiRuntimePath(path);
        if (parts.Count == 0)
        {
            value = owner;
            valueType = ownerType;
            return true;
        }
        return TryResolveAiRuntimeReadPathAfterOwner(parts, 0, ref owner, ref ownerType, ref description, out value, out valueType, out error);
    }
    private bool TryResolveAiRuntimeAssemblyReadExpression(string text, out string description, out object value, out Type valueType, out string error)
    {
        description = "";
        value = null;
        valueType = null;
        error = "";
        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            error = "Assembly expression must be Assembly:assemblyName:Namespace.Type.member";
            return false;
        }
        var assemblyName = text.Substring(0, colon);
        var rest = text.Substring(colon + 1);
        return TryResolveAiRuntimeTypeReadExpression(rest, assemblyName, out description, out value, out valueType, out error);
    }
}
