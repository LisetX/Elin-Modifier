using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;

internal sealed class CwlErrorNotificationModule
{
    private const string ExceptionProfileTypeName = "EModding.Helper.Runtime.Exceptions.ExceptionProfile";

    private readonly object _sync = new object();
    private Harmony? _harmony;
    private Type? _profileType;
    private Type? _pendingProfileType;
    private int _patchRequestPending;
    private bool _initialized;
    private bool _patchInstalled;

    internal bool Disabled { get; private set; }

    internal void SetDisabled(bool value, Harmony? harmony)
    {
        Disabled = value;
        if (harmony != null)
            Initialize(harmony);
        Tick(harmony);
        if (value)
            CloseActiveNotifications();
    }

    internal void Tick(Harmony? harmony)
    {
        if (harmony != null && !_initialized)
            Initialize(harmony);
        if (_patchInstalled || _harmony == null)
            return;
        if (Interlocked.Exchange(ref _patchRequestPending, 0) == 0)
            return;

        Type? pending;
        lock (_sync)
        {
            pending = _pendingProfileType;
            _pendingProfileType = null;
        }
        if (pending == null)
            return;

        TryInstallPatch(pending);
    }

    internal void EnsurePatch(Harmony? harmony, bool force)
    {
        if (harmony == null)
            return;
        Initialize(harmony);
        Tick(harmony);
    }

    internal void Initialize(Harmony harmony)
    {
        if (_initialized)
            return;

        _initialized = true;
        _harmony = harmony;
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

        var loadedType = FindLoadedTypeExact(ExceptionProfileTypeName);
        if (loadedType != null)
        {
            lock (_sync)
                _pendingProfileType = loadedType;
            Interlocked.Exchange(ref _patchRequestPending, 1);
        }
    }

    internal void Shutdown()
    {
        if (_initialized)
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        _initialized = false;
        _patchInstalled = false;
        _harmony = null;
        _profileType = null;
        lock (_sync)
            _pendingProfileType = null;
        Interlocked.Exchange(ref _patchRequestPending, 0);
    }

    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        try
        {
            var profileType = args.LoadedAssembly.GetType(ExceptionProfileTypeName, false);
            if (profileType == null)
                return;
            lock (_sync)
                _pendingProfileType = profileType;
            Interlocked.Exchange(ref _patchRequestPending, 1);
        }
        catch
        {
        }
    }

    private void TryInstallPatch(Type profileType)
    {
        try
        {
            var target = AccessTools.Method(profileType, "CreateAndPop", new[] { typeof(string) });
            var prefix = AccessTools.Method(typeof(CwlErrorNotificationPatch), "Prefix");
            if (target == null || prefix == null || _harmony == null)
                return;

            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            _profileType = profileType;
            _patchInstalled = true;
            if (Disabled)
                CloseActiveNotifications();
        }
        catch
        {
        }
    }

    private static Type? FindLoadedTypeExact(string fullName)
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    var type = assemblies[i].GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private void CloseActiveNotifications()
    {
        try
        {
            var profileType = _profileType ?? FindLoadedTypeExact(ExceptionProfileTypeName);
            if (profileType == null)
                return;

            var cacheField = AccessTools.Field(profileType, "_cached");
            var guiField = AccessTools.Field(profileType, "_gui");
            var cache = cacheField?.GetValue(null) as IDictionary;
            if (cache == null || guiField == null)
                return;

            foreach (DictionaryEntry entry in cache)
            {
                var profile = entry.Value;
                if (profile == null)
                    continue;
                var gui = guiField.GetValue(profile);
                if (gui == null)
                    continue;
                var kill = AccessTools.Method(gui.GetType(), "Kill", Type.EmptyTypes);
                kill?.Invoke(gui, null);
            }
        }
        catch
        {
        }
    }
}

internal static class CwlErrorNotificationPatch
{
    internal static bool Prefix()
    {
        var module = ElinModifierPlugin.ActiveModules?.CwlErrorNotifications;
        return module == null || !module.Disabled;
    }
}

internal sealed class DebugSimulationModule
{
    internal void SimulateError(bool authorized)
    {
        if (!authorized)
            return;

        try
        {
            throw new InvalidOperationException("Elin Modifier Debug: simulated Error.");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    internal void SimulateWarning(bool authorized)
    {
        if (!authorized)
            return;

        Debug.LogWarning("Elin Modifier Debug: simulated Warning.");
    }
}

public sealed partial class ElinModifierPlugin
{
    private bool DisableCwlErrorNotification => _modules.CwlErrorNotifications.Disabled;
    private Harmony CwlHarmony => _modules.Harmony.GetGroupHarmony("compatibility-cwl");

    private void SetDisableCwlErrorNotification(bool value)
    {
        _modules.CwlErrorNotifications.SetDisabled(value, CwlHarmony);
    }

    private void TickCwlErrorNotificationPatch()
    {
        _modules.CwlErrorNotifications.Tick(CwlHarmony);
    }

    private void EnsureCwlErrorNotificationPatch(bool force)
    {
        _modules.CwlErrorNotifications.EnsurePatch(CwlHarmony, force);
    }

    private void SimulateDebugError()
    {
        _modules.DebugSimulation.SimulateError(_debugAuthorized);
    }

    private void SimulateDebugWarning()
    {
        _modules.DebugSimulation.SimulateWarning(_debugAuthorized);
    }
}
