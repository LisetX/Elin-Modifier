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
    private string AiToolQueueDangerousAction(string toolName, string args)
    {
        var normalized = NormalizeAiKey(toolName);
        if (normalized == "runtime_harmony_patch")
        {
            var validationError = ValidateAiRuntimeHarmonyPatchArguments(args);
            if (!string.IsNullOrEmpty(validationError))
                return "failed: " + validationError;
        }
        if (normalized == "delete_inventory_items")
        {
            var validationError = ValidateAiDeleteInventoryItemsArguments(args);
            if (!string.IsNullOrEmpty(validationError))
                return "failed: " + validationError;
        }
        var summary = BuildAiDangerousActionSummary(normalized, args);
        var action = new AiPendingDangerousAction(++_aiDangerousActionSeq, normalized, args ?? "", summary);
        _aiPendingDangerousActions.Add(action);
        return "pending_confirmation: high-risk runtime action queued, not executed. Reply confirm/确认 in the AI input box to execute, or cancel/取消 to discard. action_id=" +
               action.Id.ToString(CultureInfo.InvariantCulture) + " summary=" + summary;
    }
    private string RunAiRuntimeWorkspaceTextJob(string key, string toolName, Func<string> worker)
    {
        key = string.IsNullOrWhiteSpace(key) ? toolName + "|empty" : key;
        AiRuntimeWorkspaceTextJob job;
        lock (_aiRuntimeWorkspaceJobsLock)
        {
            TrimAiRuntimeWorkspaceJobsLocked();
            if (!_aiRuntimeWorkspaceJobs.TryGetValue(key, out job))
            {
                job = new AiRuntimeWorkspaceTextJob(key, DateTime.UtcNow, System.Threading.Tasks.Task.Run(worker));
                _aiRuntimeWorkspaceJobs[key] = job;
                return "pending: " + toolName + " started in background. Call the same tool again with the same arguments after a moment to get the result. key=" + job.ShortKey;
            }
        }

        if (!job.Task.IsCompleted)
            return "pending: " + toolName + " is still running in background. Call the same tool again after a moment. key=" + job.ShortKey;
        if (job.Task.IsCanceled)
            return "failed: " + toolName + " background job was canceled. key=" + job.ShortKey;
        if (job.Task.IsFaulted)
        {
            var ex = job.Task.Exception != null && job.Task.Exception.InnerException != null ? job.Task.Exception.InnerException : job.Task.Exception;
            return "failed: " + toolName + " background job error: " + (ex == null ? "unknown" : ex.GetType().Name + " - " + ex.Message) + ". key=" + job.ShortKey;
        }
        var result = job.Task.Result ?? "";
        if (IsAiRuntimeWorkspacePendingToolResult(result))
        {
            lock (_aiRuntimeWorkspaceJobsLock)
                _aiRuntimeWorkspaceJobs.Remove(key);
        }
        return result;
    }
    private void TrimAiRuntimeWorkspaceJobsLocked()
    {
        if (_aiRuntimeWorkspaceJobs.Count <= AiRuntimeWorkspaceJobLimit)
            return;

        while (_aiRuntimeWorkspaceJobs.Count > AiRuntimeWorkspaceJobLimit)
        {
            string oldestKey = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (var pair in _aiRuntimeWorkspaceJobs)
            {
                if (!pair.Value.Task.IsCompleted && _aiRuntimeWorkspaceJobs.Count <= AiRuntimeWorkspaceJobLimit)
                    continue;
                if (pair.Value.CreatedUtc < oldest)
                {
                    oldest = pair.Value.CreatedUtc;
                    oldestKey = pair.Key;
                }
            }
            if (string.IsNullOrEmpty(oldestKey))
                break;
            _aiRuntimeWorkspaceJobs.Remove(oldestKey);
        }
    }
    private static string BuildAiRuntimeSearchJobKey(string args)
    {
        return "runtime_search|q=" + AiArgString(args, "query") +
               "|kind=" + NormalizeAiKey(AiArgString(args, "kind", "all")) +
               "|asm=" + NormalizeAiKey(AiArgString(args, "assembly_filter")) +
               "|type=" + NormalizeAiKey(AiArgString(args, "type_filter")) +
               "|live=" + AiArgBool(args, "live", false).ToString(CultureInfo.InvariantCulture) +
               "|limit=" + Clamp(AiArgInt(args, "limit", 80), 1, 500).ToString(CultureInfo.InvariantCulture);
    }
    private static string BuildAiRuntimeListTypeJobKey(string args)
    {
        return "runtime_list_type|type=" + NormalizeAiKey(AiArgString(args, "type")) +
               "|member=" + NormalizeAiKey(AiArgString(args, "member_filter")) +
               "|methods=" + AiArgBool(args, "include_methods", true).ToString(CultureInfo.InvariantCulture) +
               "|fields=" + AiArgBool(args, "include_fields", true).ToString(CultureInfo.InvariantCulture) +
               "|props=" + AiArgBool(args, "include_properties", true).ToString(CultureInfo.InvariantCulture) +
               "|limit=" + Clamp(AiArgInt(args, "limit", 160), 1, 800).ToString(CultureInfo.InvariantCulture);
    }
    private string ValidateAiRuntimeHarmonyPatchArguments(string args)
    {
        var securityScan = EmpSecurityScanner.ScanHarmonyPatchArguments(args, true);
        if (securityScan.Blocked)
            return "EMP/EMG security blocked runtime_harmony_patch: " + securityScan.Reason;

        var targetText = AiArgString(args, "target");
        var mode = NormalizeAiRuntimePatchMode(AiArgString(args, "mode"));
        if (string.IsNullOrWhiteSpace(targetText))
            return "target is empty. runtime_harmony_patch target must be a specific method, for example Assembly:Elin:GameDate.AdvanceMin or Type:GameDate.AdvanceMin.";
        if (!IsSupportedAiRuntimePatchMode(mode))
            return "unsupported patch mode " + mode;

        MethodBase method;
        string error;
        if (!TryResolveAiRuntimeMethodTarget(targetText, out method, out error))
            return error + " " + GetAiRuntimePatchTargetHint();
        if (mode == "prefix" || mode == "postfix")
        {
            var patchMethodText = AiArgString(args, "patch_method");
            var code = AiArgString(args, "code");
            if (string.IsNullOrWhiteSpace(patchMethodText) && string.IsNullOrWhiteSpace(code))
                return mode + " mode requires patch_method or code";
            if (!string.IsNullOrWhiteSpace(patchMethodText))
            {
                MethodBase patchMethod;
                if (!TryResolveAiRuntimeMethodTarget(patchMethodText, out patchMethod, out error))
                    return "patch_method: " + error + " " + GetAiRuntimePatchTargetHint();
                if (!(patchMethod is MethodInfo patchInfo) || !patchInfo.IsStatic)
                    return "patch_method must be a static method";
            }
        }
        return "";
    }
    private static string GetAiRuntimePatchTargetHint()
    {
        return "Use a concrete method target from runtime_search/runtime_list_type, such as Assembly:Elin:GameDate.AdvanceMin, Assembly:Elin:VirtualDate.SimulateHour, or Type:GameDate.AdvanceMin. Do not use an assembly name alone.";
    }
    private static string NormalizeAiRuntimePatchMode(string mode)
    {
        mode = NormalizeAiKey(mode);
        if (mode == "suppressexceptions") return "suppress_exceptions";
        if (mode == "skiporiginal") return "skip_original";
        if (mode == "forcereturn") return "force_return";
        if (mode == "prefixcode" || mode == "prefix_code" || mode == "customprefix" || mode == "custom_prefix") return "prefix";
        if (mode == "postfixcode" || mode == "postfix_code" || mode == "custompostfix" || mode == "custom_postfix") return "postfix";
        return mode;
    }
    private static bool IsSupportedAiRuntimePatchMode(string mode)
    {
        return mode == "suppress_exceptions" ||
               mode == "skip_original" ||
               mode == "force_return" ||
               mode == "prefix" ||
               mode == "postfix";
    }
    private string BuildAiDangerousActionSummary(string toolName, string args)
    {
        if (toolName == "runtime_reflect_set")
            return "Reflect set " + AiArgString(args, "target") + " = " + TruncateForLog(AiArgString(args, "value"), 120);
        if (toolName == "runtime_invoke_method")
            return "Invoke method " + AiArgString(args, "target") + " args=" + TruncateForLog(AiArgString(args, "args"), 120);
        if (toolName == "runtime_harmony_patch")
            return "Harmony patch " + AiArgString(args, "target") + " mode=" + AiArgString(args, "mode") + " id=" + AiArgString(args, "patch_id");
        if (toolName == "runtime_harmony_unpatch")
            return "Harmony unpatch " + AiArgString(args, "patch_id");
        if (toolName == "delete_inventory_items")
            return "Delete inventory items scope=" + AiArgString(args, "scope") + " filter=" + AiArgString(args, "filter") + " item=" + AiArgString(args, "item");
        return toolName + " " + TruncateForLog(args, 160);
    }
    private string ExecuteAiDangerousActionNow(AiPendingDangerousAction action)
    {
        string result;
        switch (action.ToolName)
        {
            case "delete_inventory_items": result = AiToolDeleteInventoryItemsNow(action.Arguments); break;
            case "runtime_reflect_set": result = AiToolReflectSetNow(action.Arguments); break;
            case "runtime_invoke_method": result = AiToolInvokeMethodNow(action.Arguments); break;
            case "runtime_harmony_patch": result = AiToolHarmonyPatchNow(action.Arguments); break;
            case "runtime_harmony_unpatch": result = AiToolHarmonyUnpatchNow(action.Arguments); break;
            default: result = "failed: unsupported high-risk action " + action.ToolName; break;
        }

        MaybeCacheAiPluginFeature(action.ToolName, action.Arguments, result);
        return result;
    }
    private string AiToolReflectGet(string args)
    {
        var target = AiArgString(args, "target");
        target = NormalizeAiRuntimeReflectGetTarget(target);
        var maxLength = Clamp(AiArgInt(args, "max_length", 2000), 200, 20000);
        if (string.IsNullOrWhiteSpace(target))
            return "failed: target is empty";
        if (IsAiRuntimeReflectGetColonMemberMisuse(target))
            return "failed: invalid runtime_reflect_get member path. Use dot syntax such as nearby_npc.faith or EClass.pc.idFaith; do not use colon labels like nearby_npc: faith.";
        if (IsAiRuntimeReflectGetDiscoveryMisuse(target))
            return "failed: runtime_reflect_get requires a concrete readable field/property expression, not an assembly-only, type-only, or method target. Use runtime_list_assemblies for assembly names, runtime_search for source/member discovery, runtime_list_type for type inspection, and runtime_harmony_patch for concrete methods.";
        if (!TryResolveAiRuntimeReadExpression(target, out var description, out var value, out var valueType, out var error))
            return "failed: " + error;
        return "ok: " + description + " = " + TruncateForLog(DebugValueToString(value), maxLength);
    }
    private static string NormalizeAiRuntimeReflectGetTarget(string target)
    {
        target = (target ?? "").Trim();
        if (target.Length == 0)
            return target;
        if (target.StartsWith("Assembly:", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("Type:", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("Plugin:", StringComparison.OrdinalIgnoreCase))
            return target;
        var match = Regex.Match(target, @"^([A-Za-z_][A-Za-z0-9_]*|[\u4e00-\u9fff]+)\s*:\s*(.+)$");
        if (!match.Success)
            return target;
        var root = NormalizeAiKey(match.Groups[1].Value);
        if (root == "nearby_npc" || root == "nearbynpc" || root == "附近npc" ||
            root == "dialogue_npc" || root == "talking_npc" || root == "dialoguenpc" || root == "talkingnpc" || root == "对话npc" || root == "对话中npc" ||
            root == "pc" || root == "player" || root == "eclass" || root == "world" || root == "zone" || root == "map" || root == "scene" || root == "sources")
            return match.Groups[1].Value.Trim() + "." + match.Groups[2].Value.Trim();
        return target;
    }
    private static bool IsAiRuntimeReflectGetDiscoveryMisuse(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;
        var text = target.Trim();
        if (Regex.IsMatch(text, @"^Assembly:[^:.]+$", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(text, @"^Type:[^:.]+$", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(text, @"^Plugin:[^:.]+$", RegexOptions.IgnoreCase))
            return true;
        return false;
    }
    private static bool IsAiRuntimeReflectGetColonMemberMisuse(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;
        var text = target.Trim();
        if (text.StartsWith("Assembly:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Type:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Plugin:", StringComparison.OrdinalIgnoreCase))
            return false;
        return Regex.IsMatch(text, @"^[A-Za-z_][A-Za-z0-9_]*\s*:\s*[A-Za-z_][A-Za-z0-9_]*", RegexOptions.IgnoreCase);
    }
    private string AiToolListAssemblies(string args)
    {
        var filter = AiArgString(args, "filter");
        var limit = Clamp(AiArgInt(args, "limit", 80), 1, 300);
        var includeLoaded = AiArgBool(args, "include_loaded", true);
        var includeWorkspace = AiArgBool(args, "include_workspace", true);
        var sb = new StringBuilder();
        sb.Append("ok: runtime assembly overview");
        if (!string.IsNullOrWhiteSpace(filter))
            sb.Append(" filter=").Append(filter.Trim());
        sb.AppendLine();
        sb.AppendLine("Usage: use runtime_search with assembly_filter=<assembly>; use runtime_list_type for a type; use runtime_reflect_get only for concrete field/property paths.");

        if (includeLoaded)
        {
            var loaded = new List<Tuple<string, string>>();
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == null || !AiRuntimeAssemblyMatches(assembly, filter))
                        continue;
                    var name = SafeText(() => assembly.GetName().Name ?? "", "");
                    var location = SafeText(() => assembly.Location ?? "", "");
                    loaded.Add(Tuple.Create(name, location));
                }
            }
            catch { }
            loaded.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine("Loaded assemblies " + Math.Min(loaded.Count, limit).ToString(CultureInfo.InvariantCulture) + "/" + loaded.Count.ToString(CultureInfo.InvariantCulture) + ":");
            for (var i = 0; i < Math.Min(loaded.Count, limit); i++)
            {
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(loaded[i].Item1);
                if (!string.IsNullOrWhiteSpace(loaded[i].Item2))
                    sb.Append(" | ").Append(loaded[i].Item2);
                sb.AppendLine();
            }
            if (loaded.Count == 0)
                sb.AppendLine("empty");
        }

        if (includeWorkspace)
        {
            var workspace = GetAiRuntimeDecompareDirectory();
            var cacheRows = new List<string>();
            try
            {
                if (Directory.Exists(workspace))
                {
                    foreach (var folder in Directory.GetDirectories(workspace, "*.decompare", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileName(folder);
                        if (!AiRuntimeTextFilterMatches(name, filter))
                            continue;
                        var status = AiRuntimeWorkspaceCacheHasSource(folder) ? "cache-ready" :
                            File.Exists(Path.Combine(folder, ".decompare_running")) ? "decompile-running" : "empty";
                        cacheRows.Add(name + " | " + status);
                    }
                }
            }
            catch { }
            cacheRows.Sort(StringComparer.OrdinalIgnoreCase);
            sb.AppendLine("Workspace Decompare caches " + Math.Min(cacheRows.Count, limit).ToString(CultureInfo.InvariantCulture) + "/" + cacheRows.Count.ToString(CultureInfo.InvariantCulture) + ":");
            for (var i = 0; i < Math.Min(cacheRows.Count, limit); i++)
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").AppendLine(cacheRows[i]);
            if (cacheRows.Count == 0)
                sb.AppendLine("empty");

            if (!string.IsNullOrWhiteSpace(filter))
            {
                var candidates = GetAiRuntimeWorkspaceAssemblyPaths(filter, Math.Min(limit, 80));
                sb.AppendLine("Matching DLL candidates " + candidates.Count.ToString(CultureInfo.InvariantCulture) + ":");
                for (var i = 0; i < candidates.Count; i++)
                    sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").AppendLine(candidates[i]);
                if (candidates.Count == 0)
                    sb.AppendLine("empty");
            }
        }

        return sb.ToString().TrimEnd();
    }
    private static bool AiRuntimeTextFilterMatches(string text, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        var key = NormalizeAiKey(filter);
        var value = NormalizeAiKey(text);
        return value.Contains(key) || key.Contains(value);
    }
    private string AiToolSearch(string args)
    {
        var key = BuildAiRuntimeSearchJobKey(args);
        return RunAiRuntimeWorkspaceTextJob(key, "runtime_search", () => AiToolSearchNow(args));
    }
    private string AiToolSearchNow(string args)
    {
        var query = AiArgString(args, "query");
        var kind = NormalizeAiKey(AiArgString(args, "kind", "all"));
        var assemblyFilter = AiArgString(args, "assembly_filter");
        var typeFilter = AiArgString(args, "type_filter");
        var live = AiArgBool(args, "live", false);
        var limit = Clamp(AiArgInt(args, "limit", 80), 1, 500);
        var keywords = SplitAiRuntimeSearchKeywords(query);
        if (keywords.Length == 0)
            return "failed: query is empty";

        if (live)
            return AiToolLiveSearch(query, kind, assemblyFilter, typeFilter, limit);

        var workspace = GetAiRuntimeDecompareDirectory();
        if (!TryPrepareAiRuntimeWorkspaceForSearch(assemblyFilter, out var preparedMessage, out var prepareError))
            return "failed: " + prepareError;

        var scannedEntries = 0;
        var scored = SearchAiRuntimeWorkspaceEntries(workspace, assemblyFilter, typeFilter, kind, keywords, out scannedEntries);
        scored.Sort((a, b) =>
        {
            var c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            c = AiRuntimeKindPriority(a.Entry.Kind).CompareTo(AiRuntimeKindPriority(b.Entry.Kind));
            if (c != 0) return c;
            return string.Compare(a.Entry.Description, b.Entry.Description, StringComparison.OrdinalIgnoreCase);
        });

        var sb = new StringBuilder();
        var count = Math.Min(scored.Count, limit);
        for (var i = 0; i < count; i++)
        {
            var entry = scored[i].Entry;
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture))
                .Append(". [").Append(entry.Kind).Append("] score=").Append(scored[i].Score.ToString(CultureInfo.InvariantCulture)).Append(" ")
                .Append(entry.Description)
                .Append(" | assembly=").Append(entry.AssemblyName)
                .Append(" | target=").Append(entry.Target)
                .AppendLine();
        }
        if (count == 0)
        {
            var fallback = BuildAiRuntimeSearchFallbackHints(keywords);
            return "ok: runtime workspace search found no results. scanned_entries=" + scannedEntries.ToString(CultureInfo.InvariantCulture) + preparedMessage + (string.IsNullOrEmpty(fallback) ? "" : "\n" + fallback);
        }
        return "ok: runtime search results " + count.ToString(CultureInfo.InvariantCulture) + "/" + scored.Count.ToString(CultureInfo.InvariantCulture) +
               " scanned_entries=" + scannedEntries.ToString(CultureInfo.InvariantCulture) + preparedMessage + "\n" + sb.ToString().TrimEnd();
    }
    private string AiToolListType(string args)
    {
        var key = BuildAiRuntimeListTypeJobKey(args);
        return RunAiRuntimeWorkspaceTextJob(key, "runtime_list_type", () => AiToolListTypeNow(args));
    }
    private string AiToolListTypeNow(string args)
    {
        var typeText = AiArgString(args, "type");
        var memberFilter = AiArgString(args, "member_filter");
        var includeMethods = AiArgBool(args, "include_methods", true);
        var includeFields = AiArgBool(args, "include_fields", true);
        var includeProperties = AiArgBool(args, "include_properties", true);
        var limit = Clamp(AiArgInt(args, "limit", 160), 1, 800);
        if (string.IsNullOrWhiteSpace(typeText))
            return "failed: type is empty";

        string assemblyFilter = "";
        if (typeText.StartsWith("Assembly:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = typeText.Substring("Assembly:".Length);
            var colon = rest.IndexOf(':');
            if (colon >= 0)
            {
                assemblyFilter = rest.Substring(0, colon);
                typeText = rest.Substring(colon + 1);
            }
        }
        if (typeText.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
            typeText = typeText.Substring("Type:".Length);

        var workspaceResult = AiToolListTypeFromWorkspace(typeText, assemblyFilter, memberFilter, includeMethods, includeFields, includeProperties, limit);
        if (!string.IsNullOrEmpty(workspaceResult))
            return workspaceResult;

        return "failed: type not found in ILSpy workspace cache: " + typeText + ". If cache was just started, wait until workspace/Decompare/<dll>.decompare contains .cs files and retry.";
    }
    private string AiToolReflectSetNow(string args)
    {
        var target = AiArgString(args, "target");
        var valueText = AiArgString(args, "value");
        var explicitType = AiArgString(args, "value_type");
        if (string.IsNullOrWhiteSpace(target))
            return "failed: target is empty";
        if (!TryResolveAiRuntimeMemberTarget(target, out var resolved, out var error))
            return "failed: " + error;
        if (!resolved.CanWrite)
            return "failed: target is read-only: " + resolved.Description;

        var valueType = resolved.ValueType ?? typeof(string);
        if (!string.IsNullOrWhiteSpace(explicitType) && NormalizeAiKey(explicitType) == "null")
        {
            resolved.SetValue(null);
            return "ok: " + resolved.Description + " = null";
        }
        object parsed;
        if (!TryParseDebugValue(valueText, valueType, out parsed))
        {
            if (valueType == typeof(object) || !IsDebugEditableType(valueType))
                parsed = valueText;
            else
                return "failed: cannot parse '" + valueText + "' as " + GetDebugTypeName(valueType);
        }
        resolved.SetValue(parsed);
        return "ok: " + resolved.Description + " = " + DebugValueToString(parsed);
    }
    private string AiToolInvokeMethodNow(string args)
    {
        var targetText = AiArgString(args, "target");
        var instanceText = AiArgString(args, "instance");
        var argText = AiArgString(args, "args");
        var maxLength = Clamp(AiArgInt(args, "max_length", 2000), 200, 20000);
        if (string.IsNullOrWhiteSpace(targetText))
            return "failed: target is empty";

        object owner;
        Type ownerType;
        string methodName;
        string error;
        if (!TryResolveAiRuntimeOwnerAndMember(targetText, out owner, out ownerType, out methodName, out error))
            return "failed: " + error;

        var values = ParseAiRuntimeInvokeArguments(argText);
        if (!TryResolveAiRuntimeInvokableMethod(ownerType, methodName, values, out var method, out var parsedArgs, out error))
            return "failed: " + error;

        try
        {
            var invokeTarget = method.IsStatic ? null : owner;
            if (!method.IsStatic && !string.IsNullOrWhiteSpace(instanceText))
            {
                if (!TryResolveAiRuntimeInvokeInstance(instanceText, method.DeclaringType, out invokeTarget, out error))
                    return "failed: " + error;
            }
            if (invokeTarget is Type && !method.IsStatic)
                return "failed: instance method requires an instance target: " + FormatMethodForAiRuntime(method) + ". Retry runtime_invoke_method with instance=EClass.pc, instance=dialogue_npc, instance=nearby_npc, or another concrete object expression.";
            var result = method.Invoke(invokeTarget, parsedArgs);
            return "ok: invoked " + FormatMethodForAiRuntime(method) + " => " + TruncateForLog(DebugValueToString(result), maxLength);
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException ?? ex;
            return "failed: invoked method threw " + inner.GetType().Name + " - " + inner.Message;
        }
        catch (Exception ex)
        {
            return "failed: invoke error " + ex.GetType().Name + " - " + ex.Message;
        }
    }
    private bool TryResolveAiRuntimeInvokeInstance(string text, Type expectedType, out object instance, out string error)
    {
        instance = null;
        error = "";
        var key = NormalizeAiKey(text);
        if (key == "dialogue_npc" || key == "talking_npc" || key == "dialoguenpc" || key == "talkingnpc" || key == "对话npc" || key == "对话中npc")
        {
            instance = GetTalkingNpc();
        }
        else if (key == "nearby_npc" || key == "nearbynpc" || key == "附近npc")
        {
            instance = GetSelectedNearbyNpc();
        }
        else if (key == "player" || key == "pc" || key == "玩家" || key == "主角")
        {
            instance = GetSafePc();
        }
        else if (!TryResolveAiRuntimeReferenceArgument(text, expectedType ?? typeof(object), out instance))
        {
            error = "instance expression could not be resolved: " + text;
            return false;
        }

        if (instance == null)
        {
            error = "instance is null: " + text;
            return false;
        }
        if (expectedType != null && !expectedType.IsInstanceOfType(instance))
        {
            error = "instance type mismatch: expected " + GetDebugTypeName(expectedType) + ", got " + GetDebugTypeName(instance.GetType());
            return false;
        }
        return true;
    }
    private string AiToolHarmonyPatchNow(string args)
    {
        var validationError = ValidateAiRuntimeHarmonyPatchArguments(args);
        if (!string.IsNullOrEmpty(validationError))
            return "failed: " + validationError;

        var targetText = AiArgString(args, "target");
        var mode = NormalizeAiRuntimePatchMode(AiArgString(args, "mode"));
        var returnText = AiArgString(args, "return_value");
        var patchId = AiArgString(args, "patch_id");
        var patchMethodText = AiArgString(args, "patch_method");
        var code = AiArgString(args, "code");
        if (string.IsNullOrWhiteSpace(targetText))
            return "failed: target is empty";
        if (!IsSupportedAiRuntimePatchMode(mode))
            return "failed: unsupported patch mode " + mode;
        if (!TryResolveAiRuntimeMethodTarget(targetText, out var method, out var error))
            return "failed: " + error;
        if (method == null)
            return "failed: method not found";

        var patchMethodKey = "";
        if (!string.IsNullOrWhiteSpace(patchMethodText))
            patchMethodKey = NormalizeAiKey(patchMethodText);
        else if (!string.IsNullOrWhiteSpace(code))
            patchMethodKey = Sha256Short(code);
        var existing = FindAiRuntimePatchRecord(method, mode, patchMethodKey);
        if (existing != null)
            return "ok: runtime Harmony patch already installed | id=" + existing.Id + " | " + existing.TargetDescription + " | mode=" + existing.Mode;

        if (string.IsNullOrWhiteSpace(patchId))
            patchId = "ai_runtime_patch_" + (++_aiRuntimePatchSeq).ToString(CultureInfo.InvariantCulture);
        patchId = MakeUniqueAiRuntimePatchId(patchId);

        EnsureAiRuntimeHarmony();
        MethodInfo installedPatchMethod = null;
        HarmonyPatchType installedPatchType = HarmonyPatchType.All;
        if (mode == "suppress_exceptions")
        {
            installedPatchMethod = typeof(ElinModifierPlugin).GetMethod(nameof(AiRuntimeSuppressExceptionFinalizer), BindingFlags.Static | BindingFlags.NonPublic);
            var finalizer = new HarmonyMethod(installedPatchMethod);
            _aiRuntimeHarmony.Patch(method, finalizer: finalizer);
            installedPatchType = HarmonyPatchType.Finalizer;
        }
        else if (mode == "skip_original")
        {
            installedPatchMethod = typeof(ElinModifierPlugin).GetMethod(nameof(AiRuntimeSkipOriginalPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = new HarmonyMethod(installedPatchMethod);
            _aiRuntimeHarmony.Patch(method, prefix: prefix);
            installedPatchType = HarmonyPatchType.Prefix;
        }
        else if (mode == "force_return")
        {
            var returnType = method is MethodInfo mi ? mi.ReturnType : typeof(void);
            object parsed = null;
            if (returnType != typeof(void))
            {
                if (!TryParseDebugValue(returnText, returnType, out parsed))
                    return "failed: cannot parse return_value '" + returnText + "' as " + GetDebugTypeName(returnType);
                _aiRuntimePatchReturnValues[GetAiRuntimeMethodKey(method)] = parsed;
            }
            installedPatchMethod = typeof(ElinModifierPlugin).GetMethod(nameof(AiRuntimeForceReturnPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = new HarmonyMethod(installedPatchMethod);
            _aiRuntimeHarmony.Patch(method, prefix: prefix);
            installedPatchType = HarmonyPatchType.Prefix;
        }
        else
        {
            MethodInfo customPatch;
            if (!TryResolveAiRuntimeCustomPatchMethod(method, mode, patchId, patchMethodText, code, out customPatch, out error))
                return "failed: " + error;
            installedPatchMethod = customPatch;
            var harmonyMethod = new HarmonyMethod(customPatch);
            if (mode == "prefix")
            {
                _aiRuntimeHarmony.Patch(method, prefix: harmonyMethod);
                installedPatchType = HarmonyPatchType.Prefix;
            }
            else
            {
                _aiRuntimeHarmony.Patch(method, postfix: harmonyMethod);
                installedPatchType = HarmonyPatchType.Postfix;
            }
        }

        var record = new AiRuntimePatchRecord(patchId, method, mode, FormatMethodForAiRuntime(method), installedPatchMethod, installedPatchType, patchMethodKey);
        _aiRuntimePatches[patchId] = record;
        return "ok: runtime Harmony patch installed | id=" + patchId + " | " + record.TargetDescription + " | mode=" + mode;
    }
    private string AiToolHarmonyUnpatchNow(string args)
    {
        var patchId = AiArgString(args, "patch_id");
        if (string.IsNullOrWhiteSpace(patchId))
            return "failed: patch_id is empty";
        if (string.Equals(patchId, "all", StringComparison.OrdinalIgnoreCase))
        {
            var count = _aiRuntimePatches.Count;
            UnpatchAllAiRuntimePatches();
            return "ok: removed all AI runtime patches: " + count.ToString(CultureInfo.InvariantCulture);
        }
        AiRuntimePatchRecord record;
        if (!_aiRuntimePatches.TryGetValue(patchId, out record))
            return "ok: runtime Harmony patch already absent | id=" + patchId;
        UnpatchAiRuntimePatch(record);
        _aiRuntimePatches.Remove(patchId);
        return "ok: removed AI runtime patch: " + patchId;
    }
    private AiRuntimePatchRecord FindAiRuntimePatchRecord(MethodBase method, string mode, string patchMethodKey = "")
    {
        if (method == null)
            return null;
        var targetKey = GetAiRuntimeMethodKey(method);
        foreach (var pair in _aiRuntimePatches)
        {
            var record = pair.Value;
            if (record == null || record.Method == null)
                continue;
            if (string.Equals(record.Mode, mode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetAiRuntimeMethodKey(record.Method), targetKey, StringComparison.Ordinal) &&
                (string.IsNullOrEmpty(patchMethodKey) || string.Equals(record.PatchMethodKey, patchMethodKey, StringComparison.Ordinal)))
                return record;
        }
        return null;
    }
    private string MakeUniqueAiRuntimePatchId(string desiredId)
    {
        var baseId = string.IsNullOrWhiteSpace(desiredId) ? "ai_runtime_patch" : desiredId.Trim();
        if (!_aiRuntimePatches.ContainsKey(baseId))
            return baseId;

        for (var i = 2; i < 10000; i++)
        {
            var candidate = baseId + "_" + i.ToString(CultureInfo.InvariantCulture);
            if (!_aiRuntimePatches.ContainsKey(candidate))
                return candidate;
        }
        return baseId + "_" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
    }
    private Chara? ResolveAiCharacterTarget(string targetName, out bool isPc, out string label)
    {
        var key = NormalizeAiKey(targetName);
        isPc = key == "" || key == "player" || key == "pc" || key == "玩家" || key == "主角";
        if (isPc)
        {
            label = "player";
            return GetSafePc();
        }
        if (key == "dialogue_npc" || key == "talking_npc" || key == "dialoguenpc" || key == "talkingnpc" || key == "对话npc" || key == "对话中npc")
        {
            label = "dialogue_npc";
            return GetTalkingNpc();
        }
        label = "nearby_npc";
        return GetSelectedNearbyNpc();
    }
    private ItemDef? FindAiItem(string text)
    {
        if (_itemRows == null) EnsureItemRows();
        return FindBestMatch(_itemRows, text, item => new[] { item.Id, item.DisplayName, item.Name });
    }
    private NpcDef? FindAiNpc(string text)
    {
        if (_npcRows == null) EnsureNpcRows();
        return FindBestMatch(_npcRows, text, npc => new[] { npc.Id, npc.DisplayName, npc.Name, npc.Race, npc.Job });
    }
    private AbilityDef? FindAiAbility(string text)
    {
        if (_abilityRows == null) EnsureAbilityRows();
        return FindBestMatch(_abilityRows, text, ability => new[] { ability.Id.ToString(CultureInfo.InvariantCulture), ability.DisplayName, ability.Name, ability.Alias, ability.Category });
    }
    private HomeElementDef? FindAiHomeElement(string text, List<HomeElementDef> rows)
    {
        return FindBestMatch(rows, text, row => new[] { row.Id.ToString(CultureInfo.InvariantCulture), row.DisplayName, row.Name, row.Alias, row.Category });
    }
    private RowDef? FindAiRow(string field, string category, bool isPc)
    {
        var rows = new List<RowDef>();
        var cat = NormalizeAiKey(category);
        if (cat == "" || cat == "status" || cat == "状态")
            rows.AddRange(isPc ? _statusRows : _npcStatusRows);
        if (cat == "" || cat == "attribute" || cat == "attributes" || cat == "主能力")
            rows.AddRange(_attributeRows);
        if (isPc && (cat == "" || cat == "evaluation" || cat == "influence" || cat == "评价和影响力"))
            rows.AddRange(_playerRows);
        if (cat == "" || cat == "resistance" || cat == "resist" || cat == "抗性")
        {
            EnsureResistRows();
            rows.AddRange(_resistRows);
        }
        if (cat == "" || cat == "skill" || cat == "trait" || cat == "feat" || cat == "技能" || cat == "特质" || cat == "专长")
        {
            EnsureGameRows();
            if (cat == "" || cat == "skill" || cat == "技能") rows.AddRange(_skillRows);
            if (cat == "" || cat == "trait" || cat == "特质") rows.AddRange(_traitRows);
            if (cat == "" || cat == "feat" || cat == "专长") rows.AddRange(_featRows);
        }
        if (rows.Count == 0 || cat == "element")
            rows.Add(new RowDef(field, field, RowKind.Element));
        return FindBestMatch(rows, field, row => new[] { row.Key, row.Label, row.Alias, GetRowLabel(row) });
    }
    private Zone? FindAiTeleportZone(string text)
    {
        var oldFilter = _teleportFilter;
        try
        {
            _teleportFilter = "";
            _teleportZoneCacheDirty = true;
            _teleportFilterCacheDirty = true;
            var zones = GetFilteredTeleportZones();
            var entry = FindBestMatch(zones, text, zone => new[] { zone.Label, zone.Name, zone.SearchText, SafeText(() => zone.Zone.id, ""), SafeText(() => zone.Zone.uid.ToString(CultureInfo.InvariantCulture), "") });
            return entry == null ? null : entry.Zone;
        }
        finally
        {
            _teleportFilter = oldFilter;
            _teleportFilterCacheDirty = true;
        }
    }
    private static T? FindBestMatch<T>(IEnumerable<T> rows, string text, Func<T, IEnumerable<string>> values) where T : class
    {
        if (rows == null)
            return null;
        var needle = NormalizeAiKey(text);
        if (string.IsNullOrEmpty(needle))
            return null;
        T? firstContains = null;
        foreach (var row in rows)
        {
            if (row == null)
                continue;
            foreach (var value in values(row))
            {
                var key = NormalizeAiKey(value);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (key == needle)
                    return row;
                if (firstContains == null && (key.Contains(needle) || needle.Contains(key)))
                    firstContains = row;
            }
        }
        return firstContains;
    }
    private static string NormalizeAiKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch > 127)
                sb.Append(ch);
            else if (ch == '_' || ch == '-')
                sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }
}
