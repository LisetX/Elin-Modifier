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
    private void EnsureAiRuntimeHarmony()
    {
        if (_aiRuntimeHarmony == null)
            _aiRuntimeHarmony = new Harmony("liset.elin.modifier.ai.runtime");
    }
    private static string GetAiRuntimeWorkspaceDirectory()
    {
        return Path.Combine(GetPluginDirectory(), "workspace");
    }
    private static string GetAiRuntimeDecompareDirectory()
    {
        return Path.Combine(GetAiRuntimeWorkspaceDirectory(), "Decompare");
    }
    private bool TryPrepareAiRuntimeWorkspaceForSearch(string assemblyFilter, out string message, out string error)
    {
        message = "";
        error = "";
        var workspace = GetAiRuntimeWorkspaceDirectory();
        var decompare = GetAiRuntimeDecompareDirectory();
        try
        {
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(decompare);
        }
        catch (Exception ex)
        {
            error = "cannot create AI workspace: " + ex.Message;
            return false;
        }

        var prepared = new List<string>();
        if (string.IsNullOrWhiteSpace(assemblyFilter))
        {
            var elinDll = GetAiRuntimeKnownAssemblyPath("Elin");
            if (EnsureAiRuntimeIlSpyWorkspaceCache(elinDll, out var folder, out var status, out error))
                prepared.Add(Path.GetFileName(folder));
            else if (!string.IsNullOrEmpty(error))
                return false;
            message = " | workspace=" + workspace + " | decompare=" + decompare + " | prepared=" + string.Join(",", prepared.ToArray()) + " | status=" + status + " | note=without assembly_filter only Elin.dll cache is searched by default; use assembly_filter for other DLLs";
            return true;
        }

        var dllPaths = GetAiRuntimeWorkspaceAssemblyPaths(assemblyFilter, 8);
        var statuses = new List<string>();
        for (var i = 0; i < dllPaths.Count; i++)
        {
            if (EnsureAiRuntimeIlSpyWorkspaceCache(dllPaths[i], out var folder, out var status, out error))
            {
                prepared.Add(Path.GetFileName(folder));
                statuses.Add(status);
            }
            else if (!string.IsNullOrEmpty(error))
                return false;
        }
        message = " | workspace=" + workspace + " | decompare=" + decompare + " | prepared=" + (prepared.Count == 0 ? "none" : string.Join(",", prepared.ToArray())) + " | status=" + (statuses.Count == 0 ? "none" : string.Join(",", statuses.ToArray()));
        return true;
    }
    private bool EnsureAiRuntimeIlSpyWorkspaceCache(string dllPath, out string folder, out string status, out string error)
    {
        folder = "";
        status = "none";
        error = "";
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
            return false;

        var dllName = Path.GetFileName(dllPath);
        if (string.IsNullOrWhiteSpace(dllName))
            dllName = "unknown.dll";

        var workspace = GetAiRuntimeWorkspaceDirectory();
        var decompare = GetAiRuntimeDecompareDirectory();
        folder = Path.Combine(decompare, MakeAiRuntimeWorkspaceSafeFileName(dllName) + ".decompare");
        if (AiRuntimeWorkspaceCacheHasSource(folder))
        {
            status = "cache-ready";
            return true;
        }

        var ilspy = GetAiRuntimeIlSpyPath();
        if (string.IsNullOrWhiteSpace(ilspy) || !File.Exists(ilspy))
        {
            error = "ILSpy not found. Put ilspycmd.exe in " + Path.Combine(workspace, "ILSpy");
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
            if (AiRuntimeIlSpyAlreadyRunningFor(folder))
            {
                status = "decompile-running";
                return true;
            }

            StartAiRuntimeIlSpyDecompile(ilspy, dllPath, folder);
            status = "decompile-started";
            return true;
        }
        catch (Exception ex)
        {
            error = "cannot start ILSpy decompare for " + dllName + ": " + ex.GetType().Name + " - " + ex.Message;
            return false;
        }
    }
    private static string GetAiRuntimeWorkspaceSourceStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length.ToString(CultureInfo.InvariantCulture) + "|" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }
    private static string GetAiRuntimeIlSpyPath()
    {
        var workspace = GetAiRuntimeWorkspaceDirectory();
        var direct = Path.Combine(workspace, "ILSpy", "ilspycmd.exe");
        if (File.Exists(direct))
            return direct;
        var alt = Path.Combine(workspace, "ILSpy", "ilspycmd.dll");
        return File.Exists(alt) ? alt : "";
    }
    private static bool AiRuntimeWorkspaceCacheHasSource(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return false;
        try
        {
            if (File.Exists(Path.Combine(folder, ".decompare_running")))
                return false;
            if (Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Length > 0)
                return true;
            if (File.Exists(Path.Combine(folder, "index.txt")))
                return true;
        }
        catch { }
        return false;
    }
    private static bool AiRuntimeIlSpyAlreadyRunningFor(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return false;
        var marker = Path.Combine(folder, ".decompare_running");
        if (!File.Exists(marker))
            return false;
        try
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(marker);
            if (age.TotalMinutes < 10)
                return true;
            File.Delete(marker);
        }
        catch { return true; }
        return false;
    }
    private static void StartAiRuntimeIlSpyDecompile(string ilspyPath, string dllPath, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var marker = Path.Combine(outputFolder, ".decompare_running");
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Encoding.UTF8);

        var args = "";
        var fileName = ilspyPath;
        if (string.Equals(Path.GetExtension(ilspyPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "dotnet";
            args = QuoteArg(ilspyPath) + " -p -o " + QuoteArg(outputFolder) + " " + QuoteArg(dllPath);
        }
        else
        {
            args = "-p -o " + QuoteArg(outputFolder) + " " + QuoteArg(dllPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(ilspyPath) ?? GetAiRuntimeWorkspaceDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (sender, evt) =>
        {
            try
            {
                var log = new StringBuilder();
                log.AppendLine("exit_code=" + process.ExitCode.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(outputFolder, "ilspy.log"), log.ToString(), Encoding.UTF8);
                if (File.Exists(marker))
                    File.Delete(marker);
                process.Dispose();
            }
            catch { }
        };
        process.Start();
    }
    private static string QuoteArg(string text)
    {
        return "\"" + (text ?? "").Replace("\"", "\\\"") + "\"";
    }
    private static string MakeAiRuntimeWorkspaceSafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            var bad = false;
            for (var j = 0; j < invalid.Length; j++)
                if (ch == invalid[j])
                {
                    bad = true;
                    break;
                }
            sb.Append(bad ? '_' : ch);
        }
        return sb.ToString().Trim();
    }
    private static string MakeAiRuntimeWorkspaceTypeFileName(string typeName)
    {
        var safe = MakeAiRuntimeWorkspaceSafeFileName((typeName ?? "unknown").Replace('+', '.'));
        if (safe.Length > 96)
            safe = safe.Substring(0, 96);
        return safe + "_" + Sha256Short(typeName ?? "") + ".txt";
    }
    internal static string Sha256Short(string text)
    {
        try
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(16);
                for (var i = 0; i < Math.Min(8, bytes.Length); i++)
                    sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
        catch
        {
            return Math.Abs((text ?? "").GetHashCode()).ToString("x", CultureInfo.InvariantCulture);
        }
    }
    private static string GetAiRuntimeKnownAssemblyPath(string assemblyName)
    {
        var pluginDir = GetPluginDirectory();
        var gameDir = GetAiRuntimeGameDirectory();

        var dllName = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? assemblyName : assemblyName + ".dll";
        var managed = Path.Combine(gameDir, "Elin_Data", "Managed", dllName);
        if (File.Exists(managed))
            return managed;
        var package = Path.Combine(gameDir, "Package", "ElinModifier", dllName);
        if (File.Exists(package))
            return package;
        var pluginLocal = Path.Combine(pluginDir, dllName);
        return File.Exists(pluginLocal) ? pluginLocal : "";
    }
    private static string GetAiRuntimeGameDirectory()
    {
        var pluginDir = GetPluginDirectory();
        try
        {
            var dir = new DirectoryInfo(pluginDir);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Elin_Data")))
                dir = dir.Parent;
            if (dir != null)
                return dir.FullName;
        }
        catch { }
        return pluginDir;
    }
    private List<string> GetAiRuntimeWorkspaceAssemblyPaths(string assemblyFilter, int limit)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var known = new[]
        {
            GetAiRuntimeKnownAssemblyPath(assemblyFilter),
            GetAiRuntimeKnownAssemblyPath(assemblyFilter + ".dll"),
            GetAiRuntimeKnownAssemblyPath("Elin"),
            GetAiRuntimeKnownAssemblyPath("Plugins.BaseCore"),
            GetAiRuntimeKnownAssemblyPath("Plugins.UI"),
            typeof(ElinModifierPlugin).Assembly.Location
        };
        for (var i = 0; i < known.Length; i++)
        {
            var path = known[i];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            if (!AiRuntimePathMatchesAssemblyFilter(path, assemblyFilter))
                continue;
            if (seen.Add(path))
                result.Add(path);
            if (limit > 0 && result.Count >= limit)
                return result;
        }

        var gameDir = GetAiRuntimeGameDirectory();
        AddAiRuntimeWorkspaceDllCandidates(result, seen, Path.Combine(gameDir, "Elin_Data", "Managed"), assemblyFilter, limit, false);
        AddAiRuntimeWorkspaceDllCandidates(result, seen, Path.Combine(gameDir, "Package"), assemblyFilter, limit, true);
        AddAiRuntimeWorkspaceDllCandidates(result, seen, Path.Combine(gameDir, "BepInEx", "plugins"), assemblyFilter, limit, true);
        AddAiRuntimeWorkspaceDllCandidates(result, seen, GetPluginDirectory(), assemblyFilter, limit, false);
        return result;
    }
    private static void AddAiRuntimeWorkspaceDllCandidates(List<string> result, HashSet<string> seen, string directory, string assemblyFilter, int limit, bool recursive)
    {
        if (result == null || seen == null || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;
        if (limit > 0 && result.Count >= limit)
            return;
        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", option))
            {
                if (!AiRuntimePathMatchesAssemblyFilter(path, assemblyFilter))
                    continue;
                if (seen.Add(path))
                    result.Add(path);
                if (limit > 0 && result.Count >= limit)
                    return;
            }
        }
        catch { }
    }
    private static bool AiRuntimePathMatchesAssemblyFilter(string path, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        var key = NormalizeAiKey(filter);
        var text = NormalizeAiKey(path + " " + Path.GetFileNameWithoutExtension(path) + " " + Path.GetFileName(path));
        return text.Contains(key) || key.Contains(NormalizeAiKey(Path.GetFileNameWithoutExtension(path)));
    }
    private static List<AiRuntimeSearchScoredEntry> SearchAiRuntimeWorkspaceEntries(string workspace, string assemblyFilter, string typeFilter, string kind, string[] keywords, out int scannedEntries)
    {
        scannedEntries = 0;
        var result = new List<AiRuntimeSearchScoredEntry>();
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return result;
        string[] folders;
        try { folders = Directory.GetDirectories(workspace, "*.decompare"); }
        catch { return result; }
        var assemblyKeywords = SplitAiRuntimeSearchKeywords(string.IsNullOrWhiteSpace(assemblyFilter) ? "Elin" : assemblyFilter);
        var typeKeywords = SplitAiRuntimeSearchKeywords(typeFilter);
        for (var i = 0; i < folders.Length; i++)
        {
            var folder = folders[i];
            if (!AiRuntimeWorkspaceFolderMatches(folder, assemblyKeywords))
                continue;
            var indexPath = Path.Combine(folder, "index.txt");
            if (File.Exists(indexPath))
                SearchAiRuntimeWorkspaceIndexFile(indexPath, typeKeywords, kind, keywords, result, ref scannedEntries);
            else
                SearchAiRuntimeWorkspaceSourceFolder(folder, typeKeywords, kind, keywords, result, ref scannedEntries);
        }
        TrimAiRuntimeScoredEntries(result, 600);
        return result;
    }
    private static void SearchAiRuntimeWorkspaceIndexFile(string indexPath, string[] typeKeywords, string kind, string[] keywords, List<AiRuntimeSearchScoredEntry> result, ref int scannedEntries)
    {
        try
        {
            using (var reader = new StreamReader(indexPath, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var entry = ParseAiRuntimeWorkspaceIndexLine(line);
                    if (entry == null)
                        continue;
                    scannedEntries++;
                    if (typeKeywords.Length > 0 && !AiRuntimeTextMatchesAny(entry.TypeName, typeKeywords))
                        continue;
                    if (!AiRuntimeSearchKindMatches(entry.Kind, kind))
                        continue;
                    var score = ScoreAiRuntimeSearchEntry(entry, keywords);
                    if (score <= 0)
                        continue;
                    result.Add(new AiRuntimeSearchScoredEntry(entry, score));
                    if (result.Count > 600)
                        TrimAiRuntimeScoredEntries(result, 400);
                }
            }
        }
        catch { }
    }
    private static void SearchAiRuntimeWorkspaceSourceFolder(string folder, string[] typeKeywords, string kind, string[] keywords, List<AiRuntimeSearchScoredEntry> result, ref int scannedEntries)
    {
        string[] files;
        try { files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories); }
        catch { return; }
        var assemblyName = GetAiRuntimeAssemblyNameFromDecompareFolder(folder);
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var typeName = GetAiRuntimeTypeNameFromSourcePath(folder, file);
            if (typeKeywords.Length > 0 && !AiRuntimeTextMatchesAny(typeName + " " + file, typeKeywords))
                continue;
            try
            {
                var lineNo = 0;
                using (var reader = new StreamReader(file, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNo++;
                        scannedEntries++;
                        var inferredKind = InferAiRuntimeSourceLineKind(line);
                        if (!AiRuntimeSearchKindMatches(inferredKind, kind))
                            continue;
                        var searchText = BuildAiRuntimeSearchText(typeName + " " + Path.GetFileName(file) + " " + line);
                        var target = BuildAiRuntimeSourceTarget(assemblyName, typeName, line, inferredKind, file, lineNo);
                        var entry = new AiRuntimeSearchEntry(inferredKind, assemblyName, typeName, BuildAiRuntimeSourceDescription(file, lineNo, line), target, searchText);
                        var score = ScoreAiRuntimeSearchEntry(entry, keywords);
                        if (score <= 0)
                            continue;
                        result.Add(new AiRuntimeSearchScoredEntry(entry, score));
                        if (result.Count > 600)
                            TrimAiRuntimeScoredEntries(result, 400);
                    }
                }
            }
            catch { }
        }
    }
    private static string BuildAiRuntimeSourceDescription(string file, int lineNo, string line)
    {
        return Path.GetFileName(file) + ":" + lineNo.ToString(CultureInfo.InvariantCulture) + " " + CompactSingleLine(line, 220);
    }
    private static string BuildAiRuntimeSourceTarget(string assemblyName, string typeName, string line, string kind, string file, int lineNo)
    {
        var memberName = ExtractAiRuntimeSourceMemberName(line, kind, typeName);
        if (!string.IsNullOrWhiteSpace(memberName) && !string.IsNullOrWhiteSpace(typeName))
            return "Assembly:" + assemblyName + ":" + typeName + "." + memberName;
        return "Source:" + file + ":" + lineNo.ToString(CultureInfo.InvariantCulture);
    }
    private static string ExtractAiRuntimeSourceMemberName(string line, string kind, string typeName)
    {
        var text = (line ?? "").Trim();
        if (string.IsNullOrEmpty(text))
            return "";
        var normalizedKind = NormalizeAiKey(kind);
        if (normalizedKind == "method")
        {
            var paren = text.IndexOf('(');
            if (paren <= 0)
                return "";
            var before = text.Substring(0, paren).TrimEnd();
            var match = Regex.Match(before, @"([A-Za-z_][A-Za-z0-9_]*)\s*$");
            if (!match.Success)
                return "";
            var name = match.Groups[1].Value;
            var key = NormalizeAiKey(name);
            if (key == "if" || key == "for" || key == "foreach" || key == "while" || key == "switch" || key == "catch" || key == "using" || key == "lock")
                return "";
            return name;
        }
        if (normalizedKind == "property")
        {
            var brace = text.IndexOf('{');
            if (brace > 0)
                text = text.Substring(0, brace).TrimEnd();
            var arrow = text.IndexOf("=>", StringComparison.Ordinal);
            if (arrow > 0)
                text = text.Substring(0, arrow).TrimEnd();
            var match = Regex.Match(text, @"([A-Za-z_][A-Za-z0-9_]*)\s*$");
            return match.Success ? match.Groups[1].Value : "";
        }
        if (normalizedKind == "field")
        {
            var equals = text.IndexOf('=');
            if (equals > 0)
                text = text.Substring(0, equals).TrimEnd();
            text = text.TrimEnd(';').TrimEnd();
            var match = Regex.Match(text, @"([A-Za-z_][A-Za-z0-9_]*)\s*$");
            return match.Success ? match.Groups[1].Value : "";
        }
        return "";
    }
}
