using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

internal sealed class ElinModifierServiceProvider : IDisposable
{
    private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
    private readonly List<object> _registrationOrder = new List<object>();
    private readonly HashSet<object> _registeredInstances =
        new HashSet<object>(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    internal T Register<T>(T service)
        where T : class
    {
        Register(typeof(T), service);
        return service;
    }

    internal void Register(Type serviceType, object service)
    {
        ThrowIfDisposed();
        if (serviceType == null)
            throw new ArgumentNullException(nameof(serviceType));
        if (service == null)
            throw new ArgumentNullException(nameof(service));
        if (!serviceType.IsInstanceOfType(service))
        {
            throw new ArgumentException(
                "Service instance is not assignable to " + serviceType.FullName + ".",
                nameof(service));
        }
        if (_services.ContainsKey(serviceType))
            throw new InvalidOperationException("Service type is already registered: " + serviceType.FullName);

        _services.Add(serviceType, service);
        if (_registeredInstances.Add(service))
            _registrationOrder.Add(service);
    }

    internal T GetRequired<T>()
        where T : class
    {
        return (T)GetRequired(typeof(T));
    }

    internal object GetRequired(Type serviceType)
    {
        ThrowIfDisposed();
        if (serviceType == null)
            throw new ArgumentNullException(nameof(serviceType));

        object? service;
        if (_services.TryGetValue(serviceType, out service))
            return service;
        throw new KeyNotFoundException("Service type is not registered: " + serviceType.FullName);
    }

    internal bool TryGet<T>(out T? service)
        where T : class
    {
        object? value;
        if (TryGet(typeof(T), out value))
        {
            service = (T)value!;
            return true;
        }

        service = null;
        return false;
    }

    internal bool TryGet(Type serviceType, out object? service)
    {
        ThrowIfDisposed();
        if (serviceType == null)
            throw new ArgumentNullException(nameof(serviceType));
        return _services.TryGetValue(serviceType, out service);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        List<Exception>? failures = null;
        var disposedServices = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var i = _registrationOrder.Count - 1; i >= 0; i--)
        {
            var service = _registrationOrder[i];
            if (!disposedServices.Add(service) || !(service is IDisposable disposable))
                continue;
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                failures ??= new List<Exception>();
                failures.Add(ex);
            }
        }

        _registrationOrder.Clear();
        _registeredInstances.Clear();
        _services.Clear();
        if (failures != null)
            throw new AggregateException("One or more services failed to dispose.", failures);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ElinModifierServiceProvider));
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

        public new bool Equals(object? left, object? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
