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
    private void UnpatchAllAiRuntimePatches()
    {
        if (_aiRuntimeHarmony == null)
            return;
        foreach (var pair in new List<KeyValuePair<string, AiRuntimePatchRecord>>(_aiRuntimePatches))
            UnpatchAiRuntimePatch(pair.Value);
        _aiRuntimePatches.Clear();
        _aiRuntimePatchReturnValues.Clear();
    }
    private void UnpatchAiRuntimePatch(AiRuntimePatchRecord record)
    {
        if (_aiRuntimeHarmony == null || record == null || record.Method == null)
            return;
        try
        {
            if (record.PatchMethod != null && record.PatchType != HarmonyPatchType.All)
                _aiRuntimeHarmony.Unpatch(record.Method, record.PatchMethod);
            else
                _aiRuntimeHarmony.Unpatch(record.Method, HarmonyPatchType.All, _aiRuntimeHarmony.Id);
            _aiRuntimePatchReturnValues.Remove(GetAiRuntimeMethodKey(record.Method));
        }
        catch { }
    }
    private static Exception AiRuntimeSuppressExceptionFinalizer(Exception __exception)
    {
        return null;
    }
    private static bool AiRuntimeSkipOriginalPrefix()
    {
        return false;
    }
    private static bool AiRuntimeForceReturnPrefix(MethodBase __originalMethod, ref object __result)
    {
        var instance = Instance;
        if (instance == null || __originalMethod == null)
            return false;
        object value;
        if (instance._aiRuntimePatchReturnValues.TryGetValue(GetAiRuntimeMethodKey(__originalMethod), out value))
            __result = value;
        return false;
    }
    private bool TryResolveAiRuntimeCustomPatchMethod(MethodBase original, string mode, string patchId, string patchMethodText, string code, out MethodInfo patchMethod, out string error)
    {
        patchMethod = null;
        error = "";
        if (!string.IsNullOrWhiteSpace(patchMethodText))
        {
            var methodScan = EmpSecurityScanner.ScanResolvedReference("patch_method", patchMethodText);
            if (methodScan.Blocked)
            {
                error = "EMP/EMG security blocked patch_method: " + methodScan.Reason;
                return false;
            }

            MethodBase method;
            if (!TryResolveAiRuntimeMethodTarget(patchMethodText, out method, out error))
                return false;
            patchMethod = method as MethodInfo;
            if (patchMethod == null)
            {
                error = "patch_method is not a method: " + patchMethodText;
                return false;
            }
            if (!patchMethod.IsStatic)
            {
                error = "patch_method must be static: " + FormatMethodForAiRuntime(patchMethod);
                return false;
            }
            return true;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            error = mode + " mode requires patch_method or code";
            return false;
        }
        var codeScan = EmpSecurityScanner.ScanText("runtime patch code", code, true, true);
        if (codeScan.Blocked)
        {
            error = "EMP/EMG security blocked code: " + codeScan.Reason;
            return false;
        }
        return TryCompileAiRuntimePatchMethod(original, mode, patchId, code, out patchMethod, out error);
    }
    private bool TryCompileAiRuntimePatchMethod(MethodBase original, string mode, string patchId, string code, out MethodInfo patchMethod, out string error)
    {
        patchMethod = null;
        error = "";
        var codeScan = EmpSecurityScanner.ScanText("runtime patch code", code, true, true);
        if (codeScan.Blocked)
        {
            error = "EMP/EMG security blocked code: " + codeScan.Reason;
            return false;
        }
        try
        {
            var compilerType = ResolveAiRuntimeCSharpCodeProviderType(out var compilerLoadNote);
            if (compilerType == null)
            {
                error = "runtime C# compiler is not available. Put Microsoft.CSharp.dll beside ElinModifier.dll or in workspace/Compiler, or use patch_method to reference a static method from a loaded DLL." + compilerLoadNote;
                return false;
            }
            var provider = Activator.CreateInstance(compilerType);
            if (provider == null)
            {
                error = "failed to create CSharpCodeProvider. Use patch_method to reference a static method from a loaded DLL." + compilerLoadNote;
                return false;
            }

            var safeId = MakeAiRuntimeSafeIdentifier(string.IsNullOrWhiteSpace(patchId) ? "patch" : patchId);
            var className = "ElinModifierRuntimePatch_" + safeId + "_" + Sha256Short(code);
            var methodName = mode == "postfix" ? "Postfix" : "Prefix";
            var source = BuildAiRuntimePatchSource(original, className, methodName, mode, code);
            var parameters = CreateAiRuntimeCompilerParameters(out error);
            if (parameters == null)
                return false;
            AddAiRuntimePatchCompilerReferences(parameters);
            var compileMethod = compilerType.GetMethod("CompileAssemblyFromSource", new[] { parameters.GetType(), typeof(string[]) });
            if (compileMethod == null)
            {
                error = "CSharpCodeProvider.CompileAssemblyFromSource was not found";
                return false;
            }
            object result;
            try
            {
                result = compileMethod.Invoke(provider, new object[] { parameters, new[] { source } });
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                var codeDomError = "runtime C# patch compile invocation failed: " + inner.GetType().Name + " - " + inner.Message;
                if (inner.InnerException != null)
                    codeDomError += " | inner: " + inner.InnerException.GetType().Name + " - " + inner.InnerException.Message;
                if (!TryCompileAiRuntimePatchMethodWithCsc(source, className, methodName, mode, patchId, code, out patchMethod, out error))
                {
                    error = codeDomError + "\nCsc fallback: " + error;
                    return false;
                }
                return true;
            }
            if (result == null)
            {
                error = "runtime compiler returned no result";
                return false;
            }
            var errors = GetAiRuntimeCompilerResultErrors(result);
            if (AiRuntimeCompilerErrorsHasErrors(errors))
            {
                var sb = new StringBuilder();
                sb.Append("runtime C# patch compile failed:");
                var count = GetAiRuntimeCompilerErrorsCount(errors);
                for (var i = 0; i < Math.Min(count, 12); i++)
                {
                    var item = GetAiRuntimeCompilerError(errors, i);
                    if (item == null || GetAiRuntimeCompilerErrorBool(item, "IsWarning"))
                        continue;
                    sb.Append("\n")
                        .Append(GetAiRuntimeCompilerErrorInt(item, "Line").ToString(CultureInfo.InvariantCulture))
                        .Append(":")
                        .Append(GetAiRuntimeCompilerErrorInt(item, "Column").ToString(CultureInfo.InvariantCulture))
                        .Append(" ")
                        .Append(GetAiRuntimeCompilerErrorString(item, "ErrorText"));
                }
                error = sb.ToString();
                return false;
            }
            var assembly = GetAiRuntimeCompilerResultAssembly(result);
            if (assembly == null)
            {
                error = "runtime compiler produced no assembly";
                return false;
            }
            var type = assembly.GetType("ElinModifierRuntimePatches." + className, false);
            patchMethod = type == null ? null : type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (patchMethod == null)
                patchMethod = FindAiRuntimeCompiledPatchMethod(assembly, methodName);
            if (patchMethod == null)
            {
                error = "compiled patch method not found";
                return false;
            }
            var key = Sha256Short(mode + "|" + patchId + "|" + code);
            _aiRuntimeCompiledPatchAssemblies[key] = assembly;
            _aiRuntimeCompiledPatchMethods[key] = patchMethod;
            return true;
        }
        catch (Exception ex)
        {
            error = "runtime C# patch compile error: " + ex.GetType().Name + " - " + ex.Message;
            return false;
        }
    }
    private bool TryCompileAiRuntimePatchMethodWithCsc(string source, string className, string methodName, string mode, string patchId, string code, out MethodInfo patchMethod, out string error)
    {
        patchMethod = null;
        error = "";
        var codeScan = EmpSecurityScanner.ScanText("runtime patch code", code + "\n" + source, true, true);
        if (codeScan.Blocked)
        {
            error = "EMP/EMG security blocked code: " + codeScan.Reason;
            return false;
        }
        try
        {
            var csc = FindAiRuntimeCscPath();
            if (string.IsNullOrWhiteSpace(csc) || !File.Exists(csc))
            {
                error = "csc.exe not found. Install .NET Framework 4.x or put csc.exe in workspace/Compiler.";
                return false;
            }

            var tempDir = Path.Combine(GetPluginDirectory(), "workspace", "Compiler", "Temp");
            Directory.CreateDirectory(tempDir);
            var safeName = MakeAiRuntimeSafeIdentifier(className);
            var sourcePath = Path.Combine(tempDir, safeName + ".cs");
            var outputPath = Path.Combine(tempDir, safeName + ".dll");
            File.WriteAllText(sourcePath, source ?? "", Encoding.UTF8);
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }

            var refs = GetAiRuntimePatchCompilerReferencePaths();
            var compilerDir = Path.GetDirectoryName(csc) ?? tempDir;
            var args = new StringBuilder();
            args.Append("/nologo /noconfig /nostdlib+ /target:library /optimize- /debug- ");
            args.Append("/out:").Append(QuoteAiRuntimeCscArg(outputPath)).Append(' ');
            for (var i = 0; i < refs.Count; i++)
                args.Append("/reference:").Append(QuoteAiRuntimeCscArg(refs[i])).Append(' ');
            args.Append(QuoteAiRuntimeCscArg(sourcePath));

            var psi = new ProcessStartInfo
            {
                FileName = csc,
                Arguments = args.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = compilerDir
            };
            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    error = "failed to start csc.exe";
                    return false;
                }
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    error = "csc.exe timed out";
                    return false;
                }
                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    error = "csc.exe failed exit=" + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "\n" + TruncateForLog((stdout + "\n" + stderr).Trim(), 4000);
                    return false;
                }
            }

            var assembly = Assembly.LoadFile(outputPath);
            var type = assembly.GetType("ElinModifierRuntimePatches." + className, false);
            patchMethod = type == null ? null : type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (patchMethod == null)
                patchMethod = FindAiRuntimeCompiledPatchMethod(assembly, methodName);
            if (patchMethod == null)
            {
                error = "compiled patch method not found in csc output";
                return false;
            }
            var key = Sha256Short(mode + "|" + patchId + "|" + code);
            _aiRuntimeCompiledPatchAssemblies[key] = assembly;
            _aiRuntimeCompiledPatchMethods[key] = patchMethod;
            return true;
        }
        catch (Exception ex)
        {
            error = "csc compile error: " + ex.GetType().Name + " - " + ex.Message;
            return false;
        }
    }
    private static string FindAiRuntimeCscPath()
    {
        foreach (var path in GetAiRuntimeCscCandidates())
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return path;
        }
        return "";
    }
    private static IEnumerable<string> GetAiRuntimeCscCandidates()
    {
        var pluginDir = GetPluginDirectory();
        yield return Path.Combine(pluginDir, "workspace", "Compiler", "csc.exe");
        yield return Path.Combine(pluginDir, "workspace", "Compiler", "Roslyn", "csc.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe");
    }
    private static string QuoteAiRuntimeCscArg(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }
    private static MethodInfo FindAiRuntimeCompiledPatchMethod(Assembly assembly, string methodName)
    {
        if (assembly == null || string.IsNullOrWhiteSpace(methodName))
            return null;
        try
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type == null)
                    continue;
                var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                    return method;
            }
        }
        catch { }
        return null;
    }
    private static string BuildAiRuntimePatchSource(MethodBase original, string className, string methodName, string mode, string code)
    {
        var text = code ?? "";
        var trimmed = text.Trim();
        if (trimmed.IndexOf(" class ", StringComparison.Ordinal) >= 0 ||
            trimmed.StartsWith("class ", StringComparison.Ordinal) ||
            trimmed.IndexOf("namespace ", StringComparison.Ordinal) >= 0)
        {
            return "using System;\nusing System.Collections;\nusing System.Collections.Generic;\nusing System.Reflection;\nusing HarmonyLib;\nusing UnityEngine;\nusing BepInEx;\n" + trimmed;
        }

        if (trimmed.IndexOf(" static ", StringComparison.Ordinal) >= 0 && trimmed.IndexOf(methodName, StringComparison.Ordinal) >= 0)
        {
            return
                "using System;\n" +
                "using System.Collections;\n" +
                "using System.Collections.Generic;\n" +
                "using System.Reflection;\n" +
                "using HarmonyLib;\n" +
                "using UnityEngine;\n" +
                "using BepInEx;\n" +
                "namespace ElinModifierRuntimePatches {\n" +
                "    public static class " + className + " {\n" +
                trimmed + "\n" +
                "    }\n" +
                "}\n";
        }

        var returnType = mode == "prefix" ? "bool" : "void";
        var defaultReturn = mode == "prefix" ? "\n            return true;" : "";
        var resultType = GetAiRuntimePatchResultType(original);
        var resultParameter = resultType == typeof(void) ? "" : ", ref " + GetAiRuntimeCSharpTypeName(resultType) + " __result";
        return
            "using System;\n" +
            "using System.Collections;\n" +
            "using System.Collections.Generic;\n" +
            "using System.Reflection;\n" +
            "using HarmonyLib;\n" +
            "using UnityEngine;\n" +
            "using BepInEx;\n" +
            "namespace ElinModifierRuntimePatches {\n" +
            "    public static class " + className + " {\n" +
            "        public static " + returnType + " " + methodName + "(object __instance, MethodBase __originalMethod, object[] __args" + resultParameter + ") {\n" +
            text + defaultReturn + "\n" +
            "        }\n" +
            "    }\n" +
            "}\n";
    }
    private static Type GetAiRuntimePatchResultType(MethodBase original)
    {
        var method = original as MethodInfo;
        if (method == null)
            return typeof(void);
        var type = method.ReturnType ?? typeof(void);
        return type.IsByRef ? type.GetElementType() ?? typeof(void) : type;
    }
    private static string GetAiRuntimeCSharpTypeName(Type type)
    {
        if (type == null || type == typeof(void)) return "void";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(char)) return "char";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";
        if (type.IsArray)
            return GetAiRuntimeCSharpTypeName(type.GetElementType()) + "[]";
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = definition.FullName ?? definition.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
                name = name.Substring(0, tick);
            name = name.Replace('+', '.');
            var args = type.GetGenericArguments();
            var sb = new StringBuilder("global::").Append(name).Append("<");
            for (var i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(GetAiRuntimeCSharpTypeName(args[i]));
            }
            sb.Append(">");
            return sb.ToString();
        }
        return "global::" + ((type.FullName ?? type.Name).Replace('+', '.'));
    }
    private static object CreateAiRuntimeCompilerParameters(out string error)
    {
        error = "";
        var type = Type.GetType("System.CodeDom.Compiler.CompilerParameters, System.CodeDom") ??
                   Type.GetType("System.CodeDom.Compiler.CompilerParameters, System") ??
                   LoadedAssemblyTypeResolver.Resolve(
                       "System.CodeDom.Compiler.CompilerParameters",
                       "System.CodeDom",
                       ignoreCase: true,
                       allowSimpleName: true) ??
                   LoadedAssemblyTypeResolver.Resolve(
                       "System.CodeDom.Compiler.CompilerParameters",
                       "System",
                       ignoreCase: true,
                       allowSimpleName: true);
        if (type == null)
        {
            error = "System.CodeDom.Compiler.CompilerParameters is not available. Use patch_method to reference a static method from a loaded DLL.";
            return null;
        }
        try
        {
            var parameters = Activator.CreateInstance(type);
            SetAiRuntimeProperty(parameters, "GenerateExecutable", false);
            SetAiRuntimeProperty(parameters, "GenerateInMemory", true);
            SetAiRuntimeProperty(parameters, "IncludeDebugInformation", false);
            SetAiRuntimeProperty(parameters, "TreatWarningsAsErrors", false);
            TrySetAiRuntimeCompilerTempFiles(parameters);
            return parameters;
        }
        catch (Exception ex)
        {
            error = "failed to create CompilerParameters: " + ex.GetType().Name + " - " + ex.Message;
            return null;
        }
    }
    private static void TrySetAiRuntimeCompilerTempFiles(object parameters)
    {
        if (parameters == null)
            return;
        try
        {
            var tempDir = Path.Combine(GetPluginDirectory(), "workspace", "Compiler", "Temp");
            Directory.CreateDirectory(tempDir);
            var tempFilesType = Type.GetType("System.CodeDom.Compiler.TempFileCollection, System.CodeDom") ??
                                Type.GetType("System.CodeDom.Compiler.TempFileCollection, System") ??
                                LoadedAssemblyTypeResolver.Resolve(
                                    "System.CodeDom.Compiler.TempFileCollection",
                                    "System.CodeDom",
                                    ignoreCase: true,
                                    allowSimpleName: true) ??
                                LoadedAssemblyTypeResolver.Resolve(
                                    "System.CodeDom.Compiler.TempFileCollection",
                                    "System",
                                    ignoreCase: true,
                                    allowSimpleName: true);
            if (tempFilesType == null)
                return;
            var tempFiles = Activator.CreateInstance(tempFilesType, new object[] { tempDir, false });
            SetAiRuntimeProperty(parameters, "TempFiles", tempFiles);
        }
        catch { }
    }
    private static Type ResolveAiRuntimeCSharpCodeProviderType(out string note)
    {
        note = "";
        var type = Type.GetType("Microsoft.CSharp.CSharpCodeProvider, System") ??
                   Type.GetType("Microsoft.CSharp.CSharpCodeProvider, Microsoft.CSharp") ??
                   LoadedAssemblyTypeResolver.Resolve(
                       "Microsoft.CSharp.CSharpCodeProvider",
                       "System",
                       ignoreCase: true,
                       allowSimpleName: true) ??
                   LoadedAssemblyTypeResolver.Resolve(
                       "Microsoft.CSharp.CSharpCodeProvider",
                       "Microsoft.CSharp",
                       ignoreCase: true,
                       allowSimpleName: true) ??
                   LoadedAssemblyTypeResolver.Resolve(
                       "Microsoft.CSharp.CSharpCodeProvider",
                       ignoreCase: true,
                       allowSimpleName: true);
        if (type != null)
            return type;

        var attempted = new List<string>();
        foreach (var path in GetAiRuntimeCSharpCompilerAssemblyCandidates())
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            attempted.Add(path);
            try
            {
                if (!File.Exists(path))
                    continue;
                var assembly = Assembly.LoadFrom(path);
                type = assembly.GetType("Microsoft.CSharp.CSharpCodeProvider", false, true);
                if (type != null)
                    return type;
            }
            catch { }
        }

        if (attempted.Count > 0)
            note = " Tried: " + string.Join("; ", attempted.Take(8).ToArray());
        return null;
    }
    private static IEnumerable<string> GetAiRuntimeCSharpCompilerAssemblyCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(List<string> list, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try { path = Path.GetFullPath(path); } catch { }
            if (seen.Add(path))
                list.Add(path);
        }

        var result = new List<string>();
        var pluginDir = GetPluginDirectory();
        Add(result, Path.Combine(pluginDir, "Microsoft.CSharp.dll"));
        Add(result, Path.Combine(pluginDir, "System.dll"));
        Add(result, Path.Combine(pluginDir, "workspace", "Compiler", "Microsoft.CSharp.dll"));
        Add(result, Path.Combine(pluginDir, "workspace", "Compiler", "System.dll"));
        Add(result, Path.Combine(pluginDir, "workspace", "Compiler", "CodeDom", "Microsoft.CSharp.dll"));
        Add(result, Path.Combine(pluginDir, "workspace", "Compiler", "CodeDom", "System.dll"));
        try
        {
            var managed = Path.GetDirectoryName(typeof(EClass).Assembly.Location);
            Add(result, Path.Combine(managed ?? "", "System.dll"));
            Add(result, Path.Combine(managed ?? "", "Microsoft.CSharp.dll"));
        }
        catch { }
        try
        {
            Add(result, Path.Combine(Paths.BepInExRootPath ?? "", "core", "Microsoft.CSharp.dll"));
        }
        catch { }
        try
        {
            Add(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "System.dll"));
            Add(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "Microsoft.CSharp.dll"));
            Add(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319", "System.dll"));
            Add(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319", "Microsoft.CSharp.dll"));
        }
        catch { }
        return result;
    }
    private static void AddAiRuntimePatchCompilerReferences(object parameters)
    {
        if (parameters == null)
            return;
        var refs = GetAiRuntimePatchCompilerReferencePaths();
        for (var i = 0; i < refs.Count; i++)
            AddAiRuntimeCompilerReference(parameters, refs[i]);
    }
    private static List<string> GetAiRuntimePatchCompilerReferencePaths()
    {
        var ordered = new List<string>();
        var seenPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try { path = Path.GetFullPath(path); } catch { }
            if (File.Exists(path) && seenPath.Add(path))
                ordered.Add(path);
        }
        void AddManaged(string fileName)
        {
            try
            {
                var managed = Path.Combine(GetAiRuntimeGameDirectory(), "Elin_Data", "Managed", fileName);
                Add(managed);
            }
            catch { }
        }
        void AddBepInExCore(string fileName)
        {
            try
            {
                Add(Path.Combine(GetAiRuntimeGameDirectory(), "BepInEx", "core", fileName));
            }
            catch { }
            try
            {
                Add(Path.Combine(Paths.BepInExRootPath ?? "", "core", fileName));
            }
            catch { }
        }

        AddManaged("mscorlib.dll");
        AddManaged("System.dll");
        AddManaged("System.Core.dll");
        AddManaged("System.Xml.dll");
        AddManaged("System.Data.dll");
        AddManaged("System.Runtime.Serialization.dll");
        AddManaged("netstandard.dll");
        AddManaged("UnityEngine.CoreModule.dll");
        AddManaged("UnityEngine.dll");
        AddManaged("UnityEngine.IMGUIModule.dll");
        AddManaged("UnityEngine.InputLegacyModule.dll");
        AddManaged("UnityEngine.UI.dll");
        AddManaged("Newtonsoft.Json.dll");
        AddManaged("Plugins.BaseCore.dll");
        AddManaged("Plugins.UI.dll");
        AddManaged("Elin.dll");
        AddBepInExCore("0Harmony.dll");
        AddBepInExCore("BepInEx.Core.dll");
        AddBepInExCore("BepInEx.Unity.dll");

        Add(typeof(object).Assembly.Location);
        Add(typeof(Enumerable).Assembly.Location);
        Add(typeof(Harmony).Assembly.Location);
        Add(typeof(UnityEngine.Object).Assembly.Location);
        Add(typeof(BaseUnityPlugin).Assembly.Location);
        try { Add(typeof(EClass).Assembly.Location); } catch { }
        try { Add(Assembly.GetExecutingAssembly().Location); } catch { }
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var location = assembly.Location;
                if (string.IsNullOrWhiteSpace(location))
                    continue;
                if (IsAiRuntimeFrameworkReferencePath(location))
                    continue;
                Add(location);
            }
            catch { }
        }

        return FilterAiRuntimeCompilerReferencesByAssemblyIdentity(ordered);
    }
    private static bool IsAiRuntimeFrameworkReferencePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var normalized = path.Replace('/', '\\');
        return normalized.IndexOf("\\Windows\\Microsoft.NET\\Framework", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
