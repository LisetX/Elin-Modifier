using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void ClearLGuiDebugLocks()
    {
        _debugLocks.Clear();
        _debugBindings.Clear();
        _debugLog = "Debug locks cleared";
        RebuildLGuiDebugRows();
    }
    private void ClearLGuiDebugInputs()
    {
        _debugInputs.Clear();
        _debugLog = "Debug inputs cleared";
        RebuildLGuiDebugRows();
    }
    private void OpenLGuiDebugDiagnostics()
    {
        if (!_debugAuthorized) return;
        var modal = CreateLGuiCompleteModal("RuntimeDebugDiagnostics", "Diagnostics", out var content, 1600f, 1030f);
        if (modal == null) return;
        var y = 4f;
        CreateLGuiButton(content, "ConfigFiles", "BepInEx Config Files", 0f, y, 240f, 44f, OpenLGuiDebugConfigFiles);
        y += 56f;

        y = AddLGuiSectionTitle(content, "Exception trace", y);
        CreateLGuiButton(content, "TracePrev", "◀", 0f, y, 48f, 42f, () => { SelectDebugExceptionTraceRecord(_debugExceptionTraceRecordIndex - 1); OpenLGuiDebugDiagnostics(); });
        CreateLGuiButton(content, "TraceNext", "▶", 58f, y, 48f, 42f, () => { SelectDebugExceptionTraceRecord(_debugExceptionTraceRecordIndex + 1); OpenLGuiDebugDiagnostics(); });
        CreateLGuiButton(content, "TraceLatest", "Latest", 116f, y, 80f, 42f, () => { SelectDebugExceptionTraceRecord(_debugExceptionTraceRecords.Count - 1); OpenLGuiDebugDiagnostics(); });
        CreateLGuiButton(content, "TraceClear", "Clear", 206f, y, 80f, 42f, () => { ClearDebugExceptionTraceRecords(); OpenLGuiDebugDiagnostics(); });
        var traceLabel = CreateLGuiText(content, "TraceLabel", GetDebugExceptionTraceRecordLabel() + " | Frame " + _debugExceptionTraceFrame, 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(traceLabel.rectTransform, 306f, y, 880f, 42f);
        y += 50f;
        var trace = CreateLGuiMultilineInput(content, "Trace", 0f, y, 1480f, 280f, true);
        trace.text = _debugExceptionTrace;
        y += 294f;

        y = AddLGuiSectionTitle(content, "Game and Plugin Stability Test", y);
        CreateLGuiButton(content, "RunStability", "Run Test", 0f, y, 110f, 42f, () => { RunDebugStabilityTest(); OpenLGuiDebugDiagnostics(); });
        CreateLGuiButton(content, "ClearStability", "Clear", 122f, y, 90f, 42f, () => { _debugStabilityTestResult = "Not run."; OpenLGuiDebugDiagnostics(); });
        y += 50f;
        var stability = CreateLGuiMultilineInput(content, "Stability", 0f, y, 1480f, 260f, true);
        stability.text = _debugStabilityTestResult;
        y += 274f;
        content.sizeDelta = new Vector2(0f, Math.Max(900f, y + 20f));
    }
    private void OpenLGuiDebugRootSelector()
    {
        if (!_debugAuthorized) return;
        EnsureDebugGameTypeEntries();
        var modal = CreateLGuiCompleteModal("RuntimeDebugRootSelector", "Debug root selector", out var content, 1600f, 1030f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "RootFilter", "Search types / mods", 0f, y, 500f, 44f);
        filter.text = _debugGameModuleFilter;
        filter.onValueChanged.AddListener(value => _debugGameModuleFilter = value ?? "");
        CreateLGuiButton(content, "Search", "Search", 514f, y, 100f, 44f, () => { _lGuiDebugRootPage = 0; OpenLGuiDebugRootSelector(); });
        CreateLGuiButton(content, "Rescan", "Rescan modules", 628f, y, 160f, 44f, RefreshLGuiDebugModuleCatalog);
        y += 54f;

        var roots = new List<Tuple<string, object>>();
        var seenRoots = new HashSet<string>(StringComparer.Ordinal);
        var filterText = (_debugGameModuleFilter ?? "").Trim();
        void AddVisibleRoot(string label, object target, string searchText = "")
        {
            if (target == null)
                return;
            if (!string.IsNullOrEmpty(filterText) &&
                label.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0 &&
                (searchText ?? "").IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                return;
            var key = label + "|" + (target is Type rootType
                ? rootType.AssemblyQualifiedName
                : target.GetType().AssemblyQualifiedName);
            if (seenRoots.Add(key))
                roots.Add(Tuple.Create(label, target));
        }

        BuildLGuiDebugRoots();
        foreach (var root in _lGuiDebugRoots)
            AddVisibleRoot(root.Label, root.Target, root.Target.GetType().FullName ?? "");

        var plugins = GetOtherLoadedBepInExPluginsCached();
        var pluginOwners = new Dictionary<Assembly, string>();
        foreach (var plugin in plugins)
        {
            if (plugin?.Instance == null)
                continue;
            var assembly = plugin.Instance.GetType().Assembly;
            if (!pluginOwners.ContainsKey(assembly))
                pluginOwners[assembly] = GetDebugBepInExPluginDisplayName(plugin);
        }

        var types = _debugGameTypeEntries
            .Where(entry => entry?.Type != null)
            .OrderBy(entry => GetLGuiDebugTypePriority(entry.Type, pluginOwners))
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var entry in types)
        {
            var label = GetLGuiDebugTypeLabel(entry.Type, pluginOwners);
            AddVisibleRoot(label, entry.SingletonValue ?? (object)entry.Type, entry.SearchText);
        }

        const int perPage = 14;
        var pages = Math.Max(1, (roots.Count + perPage - 1) / perPage);
        _lGuiDebugRootPage = Clamp(_lGuiDebugRootPage, 0, pages - 1);
        y = BuildLGuiReferencePager(content, roots.Count, _lGuiDebugRootPage, y, next => { _lGuiDebugRootPage = next; OpenLGuiDebugRootSelector(); }, perPage);
        var start = _lGuiDebugRootPage * perPage;
        var end = Math.Min(roots.Count, start + perPage);
        for (var i = start; i < end; i++)
        {
            var pair = roots[i];
            var local = pair;
            CreateLGuiButton(content, "Browse" + i, local.Item1, 0f, y, 1120f, 44f, () => SelectLGuiDebugRoot(local.Item1, local.Item2));
            CreateLGuiButton(content, "Methods" + i, "Methods", 1134f, y, 110f, 44f, () => OpenLGuiDebugMethods(local.Item1, local.Item2));
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 20f));
    }
    private void RefreshLGuiDebugModuleCatalog()
    {
        _debugGameTypeEntries = null;
        _debugTypeCategoryCache.Clear();
        _debugTypeFilterCache.Clear();
        _debugMemberCache.Clear();
        _debugMethodCache.Clear();
        _debugMethodSignatureCache.Clear();
        _debugBepInExPluginFilterCache.Clear();
        _debugCachedBepInExPluginsFrame = -9999;
        _debugCachedBepInExPlugins = new List<DebugBepInExPlugin>();
        _lGuiDebugRootPage = 0;
        EnsureDebugGameTypeEntries();
        OpenLGuiDebugRootSelector();
    }
    private static int GetLGuiDebugTypePriority(Type type, Dictionary<Assembly, string> pluginOwners)
    {
        if (type == null)
            return 5;
        var assembly = type.Assembly;
        if (assembly == typeof(EClass).Assembly)
            return 0;
        var assemblyName = "";
        try { assemblyName = assembly.GetName().Name ?? ""; } catch { }
        if (assemblyName.StartsWith("Plugins.", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (pluginOwners != null && pluginOwners.ContainsKey(assembly))
            return 1;
        if (assembly == typeof(ElinModifierPlugin).Assembly)
            return 4;
        var location = "";
        try { location = assembly.Location ?? ""; } catch { }
        if (location.IndexOf("\\BepInEx\\plugins\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
            location.IndexOf("\\Package\\", StringComparison.OrdinalIgnoreCase) >= 0)
            return 1;
        return 2;
    }
    private static string GetLGuiDebugTypeLabel(Type type, Dictionary<Assembly, string> pluginOwners)
    {
        if (type == null)
            return "[Unknown module]";
        var assembly = type.Assembly;
        var assemblyName = "";
        try { assemblyName = assembly.GetName().Name ?? ""; } catch { }
        var typeName = type.FullName ?? type.Name;
        if (assembly == typeof(ElinModifierPlugin).Assembly)
            return "[Self - reduced] " + typeName;
        if (assembly == typeof(EClass).Assembly || assemblyName.StartsWith("Plugins.", StringComparison.OrdinalIgnoreCase))
            return "[Game module: " + assemblyName + "] " + typeName;
        if (pluginOwners != null && pluginOwners.TryGetValue(assembly, out var owner) && !string.IsNullOrEmpty(owner))
            return "[Plugin/Mod: " + owner + "] " + typeName;
        var location = "";
        try { location = assembly.Location ?? ""; } catch { }
        if (location.IndexOf("\\BepInEx\\plugins\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
            location.IndexOf("\\Package\\", StringComparison.OrdinalIgnoreCase) >= 0)
            return "[Plugin/Mod module: " + assemblyName + "] " + typeName;
        return "[Loaded module: " + assemblyName + "] " + typeName;
    }
    private void SelectLGuiDebugRoot(string label, object target)
    {
        _lGuiDebugObjectStack.Clear();
        _lGuiDebugPathStack.Clear();
        _lGuiDebugTarget = target;
        _lGuiDebugTargetLabel = label;
        _lGuiDebugTargetPath = "debug:selected:" + label;
        RebuildLGuiDebugRows();
        if (_lGuiDebugTargetText != null) _lGuiDebugTargetText.text = label;
        CloseLGuiEditorModal(true);
    }
    private void OpenLGuiDebugMethods(string label, object target)
    {
        var type = target as Type ?? target.GetType();
        var modal = CreateLGuiCompleteModal("RuntimeDebugMethods", label + " | Methods", out var content, 1600f, 1030f);
        if (modal == null) return;
        var y = 4f;
        var methods = GetDebugMethods(type).Where(method => string.IsNullOrWhiteSpace(_debugGameModuleFilter) || GetDebugMethodSignature(method).IndexOf(_debugGameModuleFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        const int perPage = 18;
        var pages = Math.Max(1, (methods.Count + perPage - 1) / perPage);
        _debugMethodPages.TryGetValue(label, out var page);
        page = Clamp(page, 0, pages - 1);
        y = BuildLGuiReferencePager(content, methods.Count, page, y, next => { _debugMethodPages[label] = next; OpenLGuiDebugMethods(label, target); }, perPage);
        var start = page * perPage;
        var end = Math.Min(methods.Count, start + perPage);
        for (var i = start; i < end; i++)
            y = AddLGuiReadOnlyRow(content, "#" + (i + 1), GetDebugMethodSignature(methods[i]), y, 70f);
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 20f));
    }
    private void OpenLGuiDebugConfigFiles()
    {
        var path = GetDebugBepInExConfigPath();
        var files = string.IsNullOrEmpty(path) ? Array.Empty<string>() : GetDebugConfigFilesCached(path);
        var visible = GetDebugFilteredConfigFiles(files, _debugFilter, _debugConfigFileFilter);
        if (visible.Length == 0) visible = files;
        _lGuiDebugConfigFileIndex = Clamp(_lGuiDebugConfigFileIndex, 0, Math.Max(0, visible.Length - 1));
        var modal = CreateLGuiCompleteModal("RuntimeDebugConfigFiles", "BepInEx Config Files", out var content, 1600f, 1030f);
        if (modal == null) return;
        var y = 4f;
        var filter = CreateLGuiInput(content, "ConfigFilter", "Search", 0f, y, 430f, 44f);
        filter.text = _debugConfigFileFilter;
        filter.onValueChanged.AddListener(value => _debugConfigFileFilter = value ?? "");
        CreateLGuiButton(content, "Search", "Search", 444f, y, 90f, 44f, () => { _lGuiDebugConfigFileIndex = 0; _lGuiDebugConfigEntryPage = 0; OpenLGuiDebugConfigFiles(); });
        CreateLGuiButton(content, "PrevFile", "◀", 550f, y, 48f, 44f, () => { _lGuiDebugConfigFileIndex = Math.Max(0, _lGuiDebugConfigFileIndex - 1); _lGuiDebugConfigEntryPage = 0; OpenLGuiDebugConfigFiles(); });
        CreateLGuiButton(content, "NextFile", "▶", 608f, y, 48f, 44f, () => { _lGuiDebugConfigFileIndex = Math.Min(Math.Max(0, visible.Length - 1), _lGuiDebugConfigFileIndex + 1); _lGuiDebugConfigEntryPage = 0; OpenLGuiDebugConfigFiles(); });
        var file = visible.Length == 0 ? "" : visible[_lGuiDebugConfigFileIndex];
        var fileLabel = CreateLGuiText(content, "File", string.IsNullOrEmpty(file) ? "No config files" : System.IO.Path.GetFileName(file), 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(fileLabel.rectTransform, 674f, y, 760f, 44f);
        y += 54f;
        var entries = string.IsNullOrEmpty(file) ? Array.Empty<DebugRawConfigEntry>() : GetDebugRawConfigEntries(file);
        var filtered = GetDebugFilteredRawConfigEntries(entries, _debugFilter, _debugConfigFileFilter);
        if (filtered.Length == 0 && string.IsNullOrWhiteSpace(_debugConfigFileFilter)) filtered = entries;
        const int perPage = 12;
        var pages = Math.Max(1, (filtered.Length + perPage - 1) / perPage);
        _lGuiDebugConfigEntryPage = Clamp(_lGuiDebugConfigEntryPage, 0, pages - 1);
        y = BuildLGuiReferencePager(content, filtered.Length, _lGuiDebugConfigEntryPage, y, next => { _lGuiDebugConfigEntryPage = next; OpenLGuiDebugConfigFiles(); }, perPage);
        var start = _lGuiDebugConfigEntryPage * perPage;
        var end = Math.Min(filtered.Length, start + perPage);
        for (var i = start; i < end; i++)
        {
            var entry = filtered[i];
            var key = "runtime:config:" + entry.Path + ":" + entry.Section + ":" + entry.Key;
            if (!_debugInputs.ContainsKey(key) || !_debugLocks.TryGetValue(key, out var locked) || !locked)
                _debugInputs[key] = entry.Value;
            var label = CreateLGuiText(content, "ConfigKey", "[" + entry.Section + "] " + entry.Key, 15, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(label.rectTransform, 0f, y, 460f, 42f);
            var input = CreateLGuiInput(content, "ConfigValue", "Value", 470f, y, 560f, 42f);
            input.text = _debugInputs[key];
            input.onValueChanged.AddListener(value => _debugInputs[key] = value ?? "");
            CreateLGuiButton(content, "Apply" + i, "Apply", 1042f, y, 90f, 42f, () => { ApplyDebugRawConfigValue(key, entry); OpenLGuiDebugConfigFiles(); });
            var toggle = CreateLGuiToggle(content, "Lock" + i, 1144f, y, 130f, 42f, out var toggleLabel);
            toggleLabel.text = "Lock";
            toggle.isOn = _debugLocks.TryGetValue(key, out var isLocked) && isLocked;
            toggle.onValueChanged.AddListener(value =>
            {
                _debugLocks[key] = value;
                if (value) _debugBindings[key] = new DebugBinding(entry); else _debugBindings.Remove(key);
            });
            y += 48f;
        }
        content.sizeDelta = new Vector2(0f, Math.Max(820f, y + 20f));
    }
}
