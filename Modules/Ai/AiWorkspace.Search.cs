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
    private static string GetAiRuntimeAssemblyNameFromDecompareFolder(string folder)
    {
        var name = Path.GetFileName(folder) ?? "";
        if (name.EndsWith(".decompare", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - ".decompare".Length);
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - ".dll".Length);
        return name;
    }
    private static string GetAiRuntimeTypeNameFromSourcePath(string folder, string file)
    {
        var relative = file ?? "";
        try
        {
            if (!string.IsNullOrEmpty(folder) && relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                relative = relative.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { }
        if (relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            relative = relative.Substring(0, relative.Length - 3);
        relative = relative.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        while (relative.StartsWith(".", StringComparison.Ordinal))
            relative = relative.Substring(1);
        return string.IsNullOrWhiteSpace(relative) ? Path.GetFileNameWithoutExtension(file) ?? "" : relative;
    }
    private static string InferAiRuntimeSourceLineKind(string line)
    {
        var text = line == null ? "" : line.Trim();
        if (Regex.IsMatch(text, @"\b(class|struct|interface|enum)\s+[A-Za-z_][A-Za-z0-9_]*"))
            return "type";
        if (Regex.IsMatch(text, @"\b[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*\s+[A-Za-z_][A-Za-z0-9_]*\s*\("))
            return "method";
        if (text.Contains("{ get;") || text.Contains("=>") || Regex.IsMatch(text, @"\b(get|set)\s*;"))
            return "property";
        return "field";
    }
    private static string CompactSingleLine(string text, int max)
    {
        text = (text ?? "").Trim().Replace('\t', ' ');
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        if (max > 0 && text.Length > max)
            text = text.Substring(0, max) + "...";
        return text;
    }
    private static void TrimAiRuntimeScoredEntries(List<AiRuntimeSearchScoredEntry> entries, int keep)
    {
        if (entries == null || entries.Count <= keep)
            return;
        entries.Sort((a, b) =>
        {
            var c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            c = AiRuntimeKindPriority(a.Entry.Kind).CompareTo(AiRuntimeKindPriority(b.Entry.Kind));
            if (c != 0) return c;
            return string.Compare(a.Entry.Description, b.Entry.Description, StringComparison.OrdinalIgnoreCase);
        });
        if (entries.Count > keep)
            entries.RemoveRange(keep, entries.Count - keep);
    }
    private static bool AiRuntimeWorkspaceFolderMatches(string folder, string[] assemblyKeywords)
    {
        if (assemblyKeywords == null || assemblyKeywords.Length == 0)
            return true;
        var text = folder ?? "";
        try
        {
            var metadata = Path.Combine(folder, "metadata.txt");
            if (File.Exists(metadata))
                text += " " + File.ReadAllText(metadata, Encoding.UTF8);
        }
        catch { }
        return AiRuntimeTextMatchesAny(text, assemblyKeywords);
    }
    private static AiRuntimeSearchEntry ParseAiRuntimeWorkspaceIndexLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        var parts = line.Split('\t');
        if (parts.Length < 6)
            return null;
        return new AiRuntimeSearchEntry(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
    }
    private string AiToolListTypeFromWorkspace(string typeText, string assemblyFilter, string memberFilter, bool includeMethods, bool includeFields, bool includeProperties, int limit)
    {
        if (!TryPrepareAiRuntimeWorkspaceForSearch(assemblyFilter, out var preparedMessage, out var prepareError))
            return "failed: " + prepareError;

        var workspace = GetAiRuntimeDecompareDirectory();
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return "";
        string[] folders;
        try { folders = Directory.GetDirectories(workspace, "*.decompare"); }
        catch { return ""; }
        var assemblyKeywords = SplitAiRuntimeSearchKeywords(string.IsNullOrWhiteSpace(assemblyFilter) ? "Elin" : assemblyFilter);
        for (var i = 0; i < folders.Length; i++)
        {
            var folder = folders[i];
            if (!AiRuntimeWorkspaceFolderMatches(folder, assemblyKeywords))
                continue;
            var typeFile = FindAiRuntimeWorkspaceTypeFile(folder, typeText);
            if (string.IsNullOrEmpty(typeFile) || !File.Exists(typeFile))
                continue;
            if (Path.GetExtension(typeFile).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                return FilterAiRuntimeWorkspaceSourceFile(typeFile, folder, memberFilter, includeMethods, includeFields, includeProperties, limit, preparedMessage);
            return FilterAiRuntimeWorkspaceTypeFile(typeFile, memberFilter, includeMethods, includeFields, includeProperties, limit, preparedMessage);
        }
        return "";
    }
    private static string FindAiRuntimeWorkspaceTypeFile(string folder, string typeText)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(typeText))
            return "";
        var key = NormalizeAiKey(typeText);
        var mapPath = Path.Combine(folder, "type_files.txt");
        if (!File.Exists(mapPath))
            return FindAiRuntimeWorkspaceSourceTypeFile(folder, typeText);
        try
        {
            var lines = File.ReadAllLines(mapPath, Encoding.UTF8);
            string containsFile = "";
            for (var i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length < 2)
                    continue;
                var typeName = parts[0];
                var typeKey = NormalizeAiKey(typeName);
                var shortKey = NormalizeAiKey(GetShortTypeName(typeName));
                var path = Path.Combine(folder, parts[1].Replace('/', Path.DirectorySeparatorChar));
                if (typeKey == key || shortKey == key)
                    return path;
                if (string.IsNullOrEmpty(containsFile) && (typeKey.Contains(key) || key.Contains(typeKey) || shortKey.Contains(key)))
                    containsFile = path;
            }
            return containsFile;
        }
        catch
        {
            return FindAiRuntimeWorkspaceSourceTypeFile(folder, typeText);
        }
    }
    private static string FindAiRuntimeWorkspaceSourceTypeFile(string folder, string typeText)
    {
        var key = NormalizeAiKey(typeText);
        if (string.IsNullOrEmpty(key))
            return "";
        string[] files;
        try { files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories); }
        catch { return ""; }
        string containsFile = "";
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var typeName = GetAiRuntimeTypeNameFromSourcePath(folder, file);
            var typeKey = NormalizeAiKey(typeName);
            var shortKey = NormalizeAiKey(Path.GetFileNameWithoutExtension(file));
            if (typeKey == key || shortKey == key)
                return file;
            if (string.IsNullOrEmpty(containsFile) && (typeKey.Contains(key) || shortKey.Contains(key) || key.Contains(shortKey)))
                containsFile = file;
        }
        if (!string.IsNullOrEmpty(containsFile))
            return containsFile;

        for (var i = 0; i < Math.Min(files.Length, 400); i++)
        {
            var file = files[i];
            try
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                if (NormalizeAiKey(text).Contains(key))
                    return file;
            }
            catch { }
        }
        return "";
    }
    private static string FilterAiRuntimeWorkspaceSourceFile(string sourceFile, string folder, string memberFilter, bool includeMethods, bool includeFields, bool includeProperties, int limit, string preparedMessage)
    {
        var sb = new StringBuilder();
        var count = 0;
        try
        {
            var lines = File.ReadAllLines(sourceFile, Encoding.UTF8);
            var assemblyName = GetAiRuntimeAssemblyNameFromDecompareFolder(folder);
            var typeName = GetAiRuntimeTypeNameFromSourcePath(folder, sourceFile);
            sb.AppendLine("source=" + sourceFile);
            sb.AppendLine("type=" + typeName);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i] ?? "";
                var kind = InferAiRuntimeSourceLineKind(line);
                if ((kind == "field" && !includeFields) || (kind == "property" && !includeProperties) || (kind == "method" && !includeMethods))
                    continue;
                if (!AiRuntimeMemberLineMatches(line, memberFilter))
                    continue;
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == "{" || trimmed == "}" || trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;
                var target = BuildAiRuntimeSourceTarget(assemblyName, typeName, line, kind, sourceFile, i + 1);
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(": ").Append(CompactSingleLine(line, 360));
                if (!target.StartsWith("Source:", StringComparison.OrdinalIgnoreCase))
                    sb.Append(" | target=").Append(target);
                sb.AppendLine();
                count++;
                if (count >= limit)
                    return "ok: runtime workspace source lines " + count.ToString(CultureInfo.InvariantCulture) + "+" + preparedMessage + "\n" + sb.ToString().TrimEnd();
            }
        }
        catch (Exception ex)
        {
            return "failed: cannot read source cache: " + ex.GetType().Name + " - " + ex.Message;
        }
        return count == 0
            ? "ok: no matching source lines in workspace type cache" + preparedMessage + "\n" + sb.ToString().TrimEnd()
            : "ok: runtime workspace source lines " + count.ToString(CultureInfo.InvariantCulture) + preparedMessage + "\n" + sb.ToString().TrimEnd();
    }
    private static string FilterAiRuntimeWorkspaceTypeFile(string typeFile, string memberFilter, bool includeMethods, bool includeFields, bool includeProperties, int limit, string preparedMessage)
    {
        var sb = new StringBuilder();
        var count = 0;
        try
        {
            var lines = File.ReadAllLines(typeFile, Encoding.UTF8);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i] ?? "";
                var trimmed = line.TrimStart();
                var isField = trimmed.StartsWith("field ", StringComparison.Ordinal);
                var isProperty = trimmed.StartsWith("property ", StringComparison.Ordinal);
                var isMethod = trimmed.StartsWith("method ", StringComparison.Ordinal);
                if (isField || isProperty || isMethod)
                {
                    if ((isField && !includeFields) || (isProperty && !includeProperties) || (isMethod && !includeMethods))
                        continue;
                    if (!AiRuntimeMemberLineMatches(line, memberFilter))
                        continue;
                    sb.AppendLine(line);
                    count++;
                    if (count >= limit)
                        return "ok: runtime workspace type members " + count.ToString(CultureInfo.InvariantCulture) + "+" + preparedMessage + "\n" + sb.ToString().TrimEnd();
                }
                else if (count == 0 && (trimmed.StartsWith("type=", StringComparison.Ordinal) || trimmed.StartsWith("assembly=", StringComparison.Ordinal) || trimmed.StartsWith("base=", StringComparison.Ordinal) || trimmed.StartsWith("interfaces=", StringComparison.Ordinal)))
                {
                    sb.AppendLine(line);
                }
            }
        }
        catch (Exception ex)
        {
            return "failed: cannot read type cache: " + ex.GetType().Name + " - " + ex.Message;
        }
        return count == 0
            ? "ok: no matching members in workspace type cache" + preparedMessage + "\n" + sb.ToString().TrimEnd()
            : "ok: runtime workspace type members " + count.ToString(CultureInfo.InvariantCulture) + preparedMessage + "\n" + sb.ToString().TrimEnd();
    }
    private static string GetShortTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return "";
        var plus = typeName.LastIndexOf('+');
        var dot = typeName.LastIndexOf('.');
        var index = Math.Max(plus, dot);
        return index >= 0 && index < typeName.Length - 1 ? typeName.Substring(index + 1) : typeName;
    }
    private static string BuildAiRuntimeTypeMemberListFromReflection(Type type, string memberFilter, bool includeMethods, bool includeFields, bool includeProperties, int limit, string sourceLabel)
    {
        var sb = new StringBuilder();
        var count = 0;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var typeName = type.FullName ?? type.Name;
        var assemblyName = "";
        try { assemblyName = type.Assembly.GetName().Name ?? ""; } catch { }
        sb.AppendLine("type=" + typeName + " assembly=" + assemblyName + " source=" + sourceLabel);

        if (includeFields)
        {
            foreach (var field in type.GetFields(flags))
            {
                var line = "field " + (field.IsStatic ? "static " : "instance ") + field.Name + " : " + GetDebugTypeName(field.FieldType) + " | target=Assembly:" + assemblyName + ":" + typeName + "." + field.Name;
                if (!AiRuntimeMemberLineMatches(line, memberFilter))
                    continue;
                sb.AppendLine(line);
                if (++count >= limit) return "ok: runtime type members " + count.ToString(CultureInfo.InvariantCulture) + "+\n" + sb.ToString().TrimEnd();
            }
        }
        if (includeProperties)
        {
            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;
                var line = "property " + (IsStaticPropertyForAiRuntime(property) ? "static " : "instance ") + property.Name + " : " + GetDebugTypeName(property.PropertyType) + " | target=Assembly:" + assemblyName + ":" + typeName + "." + property.Name;
                if (!AiRuntimeMemberLineMatches(line, memberFilter))
                    continue;
                sb.AppendLine(line);
                if (++count >= limit) return "ok: runtime type members " + count.ToString(CultureInfo.InvariantCulture) + "+\n" + sb.ToString().TrimEnd();
            }
        }
        if (includeMethods)
        {
            foreach (var method in type.GetMethods(flags))
            {
                var line = "method " + (method.IsStatic ? "static " : "instance ") + FormatMethodForAiRuntime(method) + " -> " + GetDebugTypeName(method.ReturnType) + " | target=Assembly:" + assemblyName + ":" + typeName + "." + method.Name;
                if (!AiRuntimeMemberLineMatches(line, memberFilter))
                    continue;
                sb.AppendLine(line);
                if (++count >= limit) return "ok: runtime type members " + count.ToString(CultureInfo.InvariantCulture) + "+\n" + sb.ToString().TrimEnd();
            }
        }
        return count == 0 ? "ok: no matching members for " + typeName : "ok: runtime type members " + count.ToString(CultureInfo.InvariantCulture) + "\n" + sb.ToString().TrimEnd();
    }
    private string AiToolLiveSearch(string query, string kind, string assemblyFilter, string typeFilter, int limit)
    {
        if (string.IsNullOrWhiteSpace(assemblyFilter) && string.IsNullOrWhiteSpace(typeFilter))
            return "failed: live runtime search requires assembly_filter or type_filter to avoid full runtime scanning. Use workspace-backed runtime_search without live first.";
        var keywords = SplitAiRuntimeSearchKeywords(query);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var maxAssemblies = string.IsNullOrWhiteSpace(assemblyFilter) ? 4 : 16;
        var scannedAssemblies = 0;
        var collector = new List<AiRuntimeSearchEntry>();
        foreach (var assembly in assemblies)
        {
            if (!AiRuntimeAssemblyMatches(assembly, assemblyFilter))
                continue;
            scannedAssemblies++;
            CollectAiRuntimeAssemblySearchEntries(assembly, typeFilter, string.IsNullOrWhiteSpace(typeFilter) ? 12 : 80, collector);
            if (scannedAssemblies >= maxAssemblies)
                break;
        }

        var scored = new List<AiRuntimeSearchScoredEntry>();
        for (var i = 0; i < collector.Count; i++)
        {
            var entry = collector[i];
            if (!AiRuntimeSearchKindMatches(entry.Kind, kind))
                continue;
            var score = ScoreAiRuntimeSearchEntry(entry, keywords);
            if (score > 0)
                scored.Add(new AiRuntimeSearchScoredEntry(entry, score));
        }
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        var sb = new StringBuilder();
        var count = Math.Min(limit, scored.Count);
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
        return count == 0
            ? "ok: live runtime search found no results. scanned_assemblies=" + scannedAssemblies.ToString(CultureInfo.InvariantCulture) + " scanned_entries=" + collector.Count.ToString(CultureInfo.InvariantCulture)
            : "ok: live runtime search results " + count.ToString(CultureInfo.InvariantCulture) + "/" + scored.Count.ToString(CultureInfo.InvariantCulture) + " scanned_entries=" + collector.Count.ToString(CultureInfo.InvariantCulture) + "\n" + sb.ToString().TrimEnd();
    }
    private void CollectAiRuntimeAssemblySearchEntries(Assembly assembly, string typeFilter, int maxTypes, List<AiRuntimeSearchEntry> result)
    {
        if (assembly == null || result == null)
            return;
        var assemblyName = "";
        try { assemblyName = assembly.GetName().Name ?? ""; }
        catch { }
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = Array.FindAll(ex.Types, t => t != null); }
        catch { return; }

        var addedTypes = 0;
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            if (type == null)
                continue;
            var typeName = type.FullName ?? type.Name;
            if (!string.IsNullOrWhiteSpace(typeFilter) && !AiRuntimeMemberLineMatches(typeName + " " + type.Name + " " + assemblyName, typeFilter))
                continue;
            addedTypes++;
            var typeText = typeName + " " + type.Name + " " + assemblyName;
            result.Add(new AiRuntimeSearchEntry("type", assemblyName, typeName, typeName, "Assembly:" + assemblyName + ":" + typeName, BuildAiRuntimeSearchText(typeText)));
            AddAiRuntimeTypeMembersToCollector(type, assemblyName, result);
            if (maxTypes > 0 && addedTypes >= maxTypes)
                break;
        }
    }
    private static void AddAiRuntimeTypeMembersToCollector(Type type, string assemblyName, List<AiRuntimeSearchEntry> result)
    {
        if (type == null || result == null)
            return;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var typeName = type.FullName ?? type.Name;
        var typeText = typeName + " " + type.Name + " " + assemblyName;
        try
        {
            foreach (var field in type.GetFields(flags))
            {
                var description = typeName + "." + field.Name + " : " + GetDebugTypeName(field.FieldType);
                result.Add(new AiRuntimeSearchEntry("field", assemblyName, typeName, description, "Assembly:" + assemblyName + ":" + typeName + "." + field.Name, BuildAiRuntimeSearchText(typeText + " " + field.Name + " " + GetDebugTypeName(field.FieldType) + " " + description)));
            }
        }
        catch { }
        try
        {
            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;
                var description = typeName + "." + property.Name + " : " + GetDebugTypeName(property.PropertyType);
                result.Add(new AiRuntimeSearchEntry("property", assemblyName, typeName, description, "Assembly:" + assemblyName + ":" + typeName + "." + property.Name, BuildAiRuntimeSearchText(typeText + " " + property.Name + " " + GetDebugTypeName(property.PropertyType) + " " + description)));
            }
        }
        catch { }
        try
        {
            foreach (var method in type.GetMethods(flags))
            {
                var description = FormatMethodForAiRuntime(method);
                result.Add(new AiRuntimeSearchEntry("method", assemblyName, typeName, description, "Assembly:" + assemblyName + ":" + typeName + "." + method.Name, BuildAiRuntimeSearchText(typeText + " " + method.Name + " " + GetDebugTypeName(method.ReturnType) + " " + BuildAiRuntimeParameterSearchText(method) + " " + description)));
            }
        }
        catch { }
    }
}
