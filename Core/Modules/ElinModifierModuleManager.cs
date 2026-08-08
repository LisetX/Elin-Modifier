using System;
using System.Collections.Generic;
using BepInEx.Logging;

internal sealed class ElinModifierModuleManager : IDisposable
{
    private readonly ManualLogSource _logger;
    private readonly ElinModifierModuleContext _context;
    private readonly List<IElinModifierModule> _registered = new List<IElinModifierModule>();
    private readonly Dictionary<string, IElinModifierModule> _byId =
        new Dictionary<string, IElinModifierModule>(StringComparer.Ordinal);
    private readonly HashSet<string> _frameFailureKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<IElinModifierModule> _attachedModules =
        new HashSet<IElinModifierModule>();
    private readonly HashSet<IElinModifierModule> _shutdownModules =
        new HashSet<IElinModifierModule>();
    private IReadOnlyList<IElinModifierModule> _ordered = Array.Empty<IElinModifierModule>();
    private bool _sealed;
    private bool _running;
    private bool _shutdown;
    private bool _disposed;

    internal ElinModifierModuleManager(
        ElinModifierPlugin host,
        ManualLogSource logger,
        ElinModifierServiceProvider services)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _context = new ElinModifierModuleContext(host, logger, services);
    }

    internal IReadOnlyList<IElinModifierModule> All
    {
        get
        {
            Seal();
            return _ordered;
        }
    }

    internal void Register(IElinModifierModule module)
    {
        if (module == null)
            throw new ArgumentNullException(nameof(module));
        if (_sealed)
            throw new InvalidOperationException("The module registry is already sealed.");

        var id = module.Descriptor.Id;
        if (!_byId.TryAdd(id, module))
            throw new InvalidOperationException("Duplicate module id: " + id);
        _registered.Add(module);
    }

    internal bool TryGet(string id, out IElinModifierModule? module)
    {
        Seal();
        return _byId.TryGetValue(id, out module);
    }

    internal T GetInstance<T>() where T : class
    {
        Seal();
        for (var i = 0; i < _ordered.Count; i++)
        {
            var instance = _ordered[i].Instance as T;
            if (instance != null)
                return instance;
        }
        throw new KeyNotFoundException("Module instance is not registered: " + typeof(T).FullName);
    }

    internal void InitializeAll()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ElinModifierModuleManager));
        if (_running)
            return;
        if (_shutdown)
            throw new InvalidOperationException("A shut down module manager cannot be initialized again.");

        Seal();
        var initialized = new List<IElinModifierModule>();
        try
        {
            for (var i = 0; i < _ordered.Count; i++)
            {
                var module = _ordered[i];
                module.Attach(_context);
                _attachedModules.Add(module);
            }

            for (var i = 0; i < _ordered.Count; i++)
            {
                var module = _ordered[i];
                if (!HasCapability(module, ElinModifierModuleCapabilities.Initialize))
                    continue;
                module.Initialize();
                initialized.Add(module);
            }
            _running = true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Module initialization failed: " + ex);
            _running = false;
            _shutdown = true;
            _context.Cancel();
            for (var i = initialized.Count - 1; i >= 0; i--)
                TryShutdownOnce(initialized[i], "initialization rollback");
            ShutdownAttachedModules("initialization rollback");
            throw;
        }
    }

    internal void TickAll()
    {
        if (!_running || _shutdown)
            return;
        Dispatch(ElinModifierModuleCapabilities.Update, static module => module.Tick(), "Update");
    }

    internal void LateTickAll()
    {
        if (!_running || _shutdown)
            return;
        Dispatch(ElinModifierModuleCapabilities.LateUpdate, static module => module.LateTick(), "LateUpdate");
    }

    internal void DrawGuiAll()
    {
        if (!_running || _shutdown)
            return;
        Dispatch(ElinModifierModuleCapabilities.Gui, static module => module.DrawGui(), "OnGUI");
    }

    internal void ShutdownAll()
    {
        if (_shutdown)
            return;

        _shutdown = true;
        _running = false;
        _context.Cancel();
        Seal();
        ShutdownAttachedModules("shutdown");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        ShutdownAll();
        _disposed = true;
        _context.Dispose();
        _registered.Clear();
        _byId.Clear();
        _ordered = Array.Empty<IElinModifierModule>();
        _frameFailureKeys.Clear();
        _attachedModules.Clear();
        _shutdownModules.Clear();
    }

    private void Seal()
    {
        if (_sealed)
            return;

        var descriptors = new List<ElinModifierModuleDescriptor>(_registered.Count);
        for (var i = 0; i < _registered.Count; i++)
            descriptors.Add(_registered[i].Descriptor);
        var orderedDescriptors = ElinModifierModuleGraph.Order(descriptors);
        var ordered = new List<IElinModifierModule>(orderedDescriptors.Count);
        for (var i = 0; i < orderedDescriptors.Count; i++)
            ordered.Add(_byId[orderedDescriptors[i].Id]);
        _ordered = ordered.AsReadOnly();
        _sealed = true;
    }

    private void Dispatch(
        ElinModifierModuleCapabilities capability,
        Action<IElinModifierModule> action,
        string phase)
    {
        for (var i = 0; i < _ordered.Count; i++)
        {
            var module = _ordered[i];
            if (!HasCapability(module, capability))
                continue;
            try
            {
                action(module);
            }
            catch (Exception ex)
            {
                var key = phase + "|" + module.Descriptor.Id + "|" +
                          ex.GetType().FullName + "|" + ex.Message;
                if (_frameFailureKeys.Add(key))
                    _logger.LogError("Module " + phase + " failed [" + module.Descriptor.Id + "]: " + ex);
            }
        }
    }

    private void ShutdownAttachedModules(string phase)
    {
        var shutdownModules = new List<IElinModifierModule>();
        for (var i = 0; i < _ordered.Count; i++)
        {
            var module = _ordered[i];
            if (_attachedModules.Contains(module) &&
                !_shutdownModules.Contains(module) &&
                HasCapability(module, ElinModifierModuleCapabilities.Shutdown))
                shutdownModules.Add(module);
        }
        shutdownModules.Sort(CompareShutdownOrder);
        for (var i = 0; i < shutdownModules.Count; i++)
            TryShutdownOnce(shutdownModules[i], phase);
    }

    private void TryShutdownOnce(IElinModifierModule module, string phase)
    {
        if (!_attachedModules.Contains(module) || !_shutdownModules.Add(module))
            return;
        try
        {
            module.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError("Module " + phase + " failed [" + module.Descriptor.Id + "]: " + ex);
        }
    }

    private static bool HasCapability(
        IElinModifierModule module,
        ElinModifierModuleCapabilities capability)
    {
        return (module.Descriptor.Capabilities & capability) != 0;
    }

    private static int CompareShutdownOrder(IElinModifierModule left, IElinModifierModule right)
    {
        var order = left.Descriptor.ShutdownOrder.CompareTo(right.Descriptor.ShutdownOrder);
        return order != 0
            ? order
            : string.Compare(left.Descriptor.Id, right.Descriptor.Id, StringComparison.Ordinal);
    }
}
