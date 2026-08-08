using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

internal sealed class HarmonyPatchModule
{
    private const string HarmonyIdPrefix = "local.elin.modifier";

    private readonly Dictionary<string, Harmony> _groups =
        new Dictionary<string, Harmony>(StringComparer.Ordinal);
    private readonly List<string> _failures = new List<string>();

    private bool _installAttempted;
    private int _discoveredPatchCount;
    private int _installedPatchCount;

    internal bool IsInstalled => _installAttempted && _installedPatchCount > 0;
    internal int DiscoveredPatchCount => _discoveredPatchCount;
    internal int InstalledPatchCount => _installedPatchCount;
    internal IReadOnlyList<string> Failures => _failures;

    internal Harmony GetGroupHarmony(string group)
    {
        group = NormalizeGroupName(group);
        Harmony harmony;
        if (_groups.TryGetValue(group, out harmony))
            return harmony;

        harmony = new Harmony(HarmonyIdPrefix + "." + group);
        _groups[group] = harmony;
        return harmony;
    }

    internal void Install(Assembly assembly, ManualLogSource logger)
    {
        if (_installAttempted)
            return;

        _installAttempted = true;
        _failures.Clear();
        _discoveredPatchCount = 0;
        _installedPatchCount = 0;

        var patchTypes = FindPatchTypes(assembly);
        _discoveredPatchCount = patchTypes.Count;
        for (var i = 0; i < patchTypes.Count; i++)
        {
            var patchType = patchTypes[i];
            var group = ResolvePatchGroup(patchType);
            try
            {
                GetGroupHarmony(group).CreateClassProcessor(patchType).Patch();
                _installedPatchCount++;
            }
            catch (Exception ex)
            {
                var message = (patchType.FullName ?? patchType.Name) + ": " +
                              ex.GetType().Name + " - " + ex.Message;
                _failures.Add(message);
                logger.LogError("Harmony patch class failed [" + group + "] " + message);
            }
        }
    }

    internal void Shutdown(ManualLogSource logger)
    {
        foreach (var pair in _groups)
        {
            try
            {
                pair.Value.UnpatchSelf();
            }
            catch (Exception ex)
            {
                logger.LogError("Harmony group unload failed [" + pair.Key + "]: " + ex);
            }
        }

        _groups.Clear();
        _failures.Clear();
        _installAttempted = false;
        _discoveredPatchCount = 0;
        _installedPatchCount = 0;
    }

    private static List<Type> FindPatchTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaded = new List<Type>();
            var partialTypes = ex.Types;
            for (var i = 0; i < partialTypes.Length; i++)
                if (partialTypes[i] != null)
                    loaded.Add(partialTypes[i]!);
            types = loaded.ToArray();
        }

        var result = new List<Type>();
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            try
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                    result.Add(type);
            }
            catch
            {
            }
        }

        result.Sort((left, right) =>
            string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
        return result;
    }

    private static string ResolvePatchGroup(Type patchType)
    {
        var owner = patchType;
        while (owner.DeclaringType != null)
            owner = owner.DeclaringType;

        var ownerName = owner.Name;
        if (string.Equals(ownerName, nameof(ElinModifierPlugin), StringComparison.Ordinal))
            return "gameplay";
        if (ownerName.IndexOf("Probability", StringComparison.OrdinalIgnoreCase) >= 0)
            return "probability";
        if (ownerName.IndexOf("Automation", StringComparison.OrdinalIgnoreCase) >= 0)
            return "automation";
        if (ownerName.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0)
            return "main-menu";
        if (ownerName.IndexOf("Affinity", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ownerName.IndexOf("Karma", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ownerName.IndexOf("Interrupted", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ownerName.IndexOf("FriendlyFire", StringComparison.OrdinalIgnoreCase) >= 0)
            return "character-protection";
        return ownerName;
    }

    private static string NormalizeGroupName(string group)
    {
        if (string.IsNullOrWhiteSpace(group))
            return "core";

        var chars = group.Trim().ToLowerInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var value = chars[i];
            if ((value >= 'a' && value <= 'z') || (value >= '0' && value <= '9') || value == '-')
                continue;
            chars[i] = '-';
        }
        return new string(chars);
    }
}
