using System;
using System.Threading;
using BepInEx.Logging;

internal interface IElinModifierModule
{
    ElinModifierModuleDescriptor Descriptor { get; }
    object Instance { get; }
    void Attach(ElinModifierModuleContext context);
    void Initialize();
    void Tick();
    void LateTick();
    void DrawGui();
    void Shutdown();
}

internal sealed class ElinModifierModuleContext : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    private bool _disposed;

    internal ElinModifierModuleContext(
        ElinModifierPlugin host,
        ManualLogSource logger,
        ElinModifierServiceProvider services)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    internal ElinModifierPlugin Host { get; }
    internal ManualLogSource Logger { get; }
    internal ElinModifierServiceProvider Services { get; }
    internal CancellationToken LifetimeToken => _lifetime.Token;

    internal T GetRequired<T>()
        where T : class
    {
        return Services.GetRequired<T>();
    }

    internal void Cancel()
    {
        if (_disposed || _lifetime.IsCancellationRequested)
            return;
        try
        {
            _lifetime.Cancel();
        }
        catch (Exception ex)
        {
            try
            {
                Logger.LogError("Module lifetime cancellation failed: " + ex);
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (!_lifetime.IsCancellationRequested)
                _lifetime.Cancel();
        }
        catch
        {
        }
        _lifetime.Dispose();
    }
}

internal sealed class DelegateElinModifierModule : IElinModifierModule
{
    private readonly Action<ElinModifierModuleContext>? _attach;
    private readonly Action? _initialize;
    private readonly Action? _tick;
    private readonly Action? _lateTick;
    private readonly Action? _drawGui;
    private readonly Action? _shutdown;

    internal DelegateElinModifierModule(
        ElinModifierModuleDescriptor descriptor,
        object instance,
        Action<ElinModifierModuleContext>? attach = null,
        Action? initialize = null,
        Action? tick = null,
        Action? lateTick = null,
        Action? drawGui = null,
        Action? shutdown = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _attach = attach;
        _initialize = initialize;
        _tick = tick;
        _lateTick = lateTick;
        _drawGui = drawGui;
        _shutdown = shutdown;
    }

    public ElinModifierModuleDescriptor Descriptor { get; }
    public object Instance { get; }

    public void Attach(ElinModifierModuleContext context) => _attach?.Invoke(context);
    public void Initialize() => _initialize?.Invoke();
    public void Tick() => _tick?.Invoke();
    public void LateTick() => _lateTick?.Invoke();
    public void DrawGui() => _drawGui?.Invoke();
    public void Shutdown() => _shutdown?.Invoke();
}
