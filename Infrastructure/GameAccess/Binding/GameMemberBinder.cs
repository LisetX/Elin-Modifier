using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

internal sealed class GameMemberBinder : IGameMemberBinder, IDisposable
{
    private readonly ConcurrentDictionary<GameValueSpec, Lazy<object>> _valueBindings =
        new ConcurrentDictionary<GameValueSpec, Lazy<object>>();
    private readonly ConcurrentDictionary<GameMethodSpec, Lazy<IBoundGameMethod>> _methodBindings =
        new ConcurrentDictionary<GameMethodSpec, Lazy<IBoundGameMethod>>();
    private int _valueResolutionCount;
    private int _methodResolutionCount;
    private int _disposed;

    internal int ValueResolutionCount => Volatile.Read(ref _valueResolutionCount);
    internal int MethodResolutionCount => Volatile.Read(ref _methodResolutionCount);

    public IBoundGameValue<T> BindValue<T>(GameValueSpec spec)
    {
        ThrowIfDisposed();
        if (spec == null)
            throw new ArgumentNullException(nameof(spec));
        if (spec.ValueType != typeof(T))
        {
            throw new ArgumentException(
                "The binding value type " + spec.ValueType.FullName +
                " does not match " + typeof(T).FullName + ".",
                nameof(spec));
        }

        var binding = _valueBindings.GetOrAdd(
            spec,
            key => new Lazy<object>(
                () => ResolveValue<T>(key),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return (IBoundGameValue<T>)binding.Value;
    }

    public IBoundGameMethod BindMethod(GameMethodSpec spec)
    {
        ThrowIfDisposed();
        if (spec == null)
            throw new ArgumentNullException(nameof(spec));

        var binding = _methodBindings.GetOrAdd(
            spec,
            key => new Lazy<IBoundGameMethod>(
                () => ResolveMethod(key),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return binding.Value;
    }

    internal void Clear()
    {
        ThrowIfDisposed();
        ClearCore();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        ClearCore();
    }

    private void ClearCore()
    {
        _valueBindings.Clear();
        _methodBindings.Clear();
        Interlocked.Exchange(ref _valueResolutionCount, 0);
        Interlocked.Exchange(ref _methodResolutionCount, 0);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(GameMemberBinder));
    }

    private IBoundGameValue<T> ResolveValue<T>(GameValueSpec spec)
    {
        Interlocked.Increment(ref _valueResolutionCount);
        for (var nameIndex = 0; nameIndex < spec.CandidateNames.Count; nameIndex++)
        {
            var name = spec.CandidateNames[nameIndex];
            var member = FindValueMember(spec, name, out var isPublic);
            if (member != null)
                return new BoundGameValue<T>(spec, member, isPublic);
        }

        return new BoundGameValue<T>(spec);
    }

    private IBoundGameMethod ResolveMethod(GameMethodSpec spec)
    {
        Interlocked.Increment(ref _methodResolutionCount);
        for (var nameIndex = 0; nameIndex < spec.CandidateNames.Count; nameIndex++)
        {
            var name = spec.CandidateNames[nameIndex];
            var method = FindMethod(spec, name, out var isPublic);
            if (method != null)
                return new BoundGameMethod(spec, method, isPublic);
        }

        return new BoundGameMethod(spec);
    }

    private static MemberInfo? FindValueMember(
        GameValueSpec spec,
        string name,
        out bool isPublic)
    {
        for (var type = spec.DeclaringType; type != null; type = type.BaseType)
        {
            var field = FindField(type, spec, name, publicOnly: true);
            if (field != null)
            {
                isPublic = true;
                return field;
            }

            var property = FindProperty(type, spec, name, publicOnly: true);
            if (property != null)
            {
                isPublic = true;
                return property;
            }

            field = FindField(type, spec, name, publicOnly: false);
            if (field != null)
            {
                isPublic = false;
                return field;
            }

            property = FindProperty(type, spec, name, publicOnly: false);
            if (property != null)
            {
                isPublic = false;
                return property;
            }
        }
        isPublic = false;
        return null;
    }

    private static FieldInfo? FindField(
        Type declaringType,
        GameValueSpec spec,
        string name,
        bool publicOnly)
    {
        var flags = BindingFlags.DeclaredOnly |
                    (spec.IsStatic ? BindingFlags.Static : BindingFlags.Instance) |
                    (publicOnly ? BindingFlags.Public : BindingFlags.NonPublic);
        var field = declaringType.GetField(name, flags);
        if (field == null ||
            field.FieldType != spec.ValueType ||
            field.IsStatic != spec.IsStatic ||
            (!publicOnly && field.IsPublic) ||
            ((spec.Access & GameValueAccess.Write) != 0 && (field.IsInitOnly || field.IsLiteral)))
            return null;
        return field;
    }

    private static PropertyInfo? FindProperty(
        Type declaringType,
        GameValueSpec spec,
        string name,
        bool publicOnly)
    {
        var flags = BindingFlags.DeclaredOnly |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    (spec.IsStatic ? BindingFlags.Static : BindingFlags.Instance);
        var properties = declaringType.GetProperties(flags);
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (!string.Equals(property.Name, name, StringComparison.Ordinal) ||
                property.PropertyType != spec.ValueType ||
                property.GetIndexParameters().Length != 0)
                continue;

            var getter = property.GetGetMethod(true);
            var setter = property.GetSetMethod(true);
            if ((spec.Access & GameValueAccess.Read) != 0 && getter == null)
                continue;
            if ((spec.Access & GameValueAccess.Write) != 0 && setter == null)
                continue;

            var accessor = getter ?? setter;
            if (accessor == null || accessor.IsStatic != spec.IsStatic)
                continue;
            if (getter != null && getter.IsStatic != spec.IsStatic)
                continue;
            if (setter != null && setter.IsStatic != spec.IsStatic)
                continue;

            var requiredAccessorsArePublic =
                ((spec.Access & GameValueAccess.Read) == 0 || getter!.IsPublic) &&
                ((spec.Access & GameValueAccess.Write) == 0 || setter!.IsPublic);
            if (publicOnly != requiredAccessorsArePublic)
                continue;
            return property;
        }
        return null;
    }

    private static MethodInfo? FindMethod(
        GameMethodSpec spec,
        string name,
        out bool isPublic)
    {
        for (var type = spec.DeclaringType; type != null; type = type.BaseType)
        {
            var method = FindMethodAtLevel(type, spec, name, publicOnly: true);
            if (method != null)
            {
                isPublic = true;
                return method;
            }

            method = FindMethodAtLevel(type, spec, name, publicOnly: false);
            if (method != null)
            {
                isPublic = false;
                return method;
            }
        }
        isPublic = false;
        return null;
    }

    private static MethodInfo? FindMethodAtLevel(
        Type declaringType,
        GameMethodSpec spec,
        string name,
        bool publicOnly)
    {
        var flags = BindingFlags.DeclaredOnly |
                    (spec.IsStatic ? BindingFlags.Static : BindingFlags.Instance) |
                    (publicOnly ? BindingFlags.Public : BindingFlags.NonPublic);
        var methods = declaringType.GetMethods(flags);
        for (var i = 0; i < methods.Length; i++)
        {
            var method = methods[i];
            if (!string.Equals(method.Name, name, StringComparison.Ordinal) ||
                method.IsStatic != spec.IsStatic ||
                method.IsPublic != publicOnly)
                continue;

            var closedMethod = CloseGenericMethod(method, spec.GenericTypeArguments);
            if (closedMethod == null || closedMethod.ReturnType != spec.ReturnType)
                continue;

            var parameters = closedMethod.GetParameters();
            if (parameters.Length != spec.ParameterTypes.Count)
                continue;
            var matches = true;
            for (var parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                if (parameters[parameterIndex].ParameterType == spec.ParameterTypes[parameterIndex])
                    continue;
                matches = false;
                break;
            }
            if (matches)
                return closedMethod;
        }
        return null;
    }

    private static MethodInfo? CloseGenericMethod(
        MethodInfo method,
        System.Collections.Generic.IReadOnlyList<Type> genericTypeArguments)
    {
        if (genericTypeArguments.Count == 0)
            return method.IsGenericMethod ? null : method;
        if (!method.IsGenericMethodDefinition ||
            method.GetGenericArguments().Length != genericTypeArguments.Count)
            return null;

        var arguments = new Type[genericTypeArguments.Count];
        for (var i = 0; i < arguments.Length; i++)
            arguments[i] = genericTypeArguments[i];
        try
        {
            return method.MakeGenericMethod(arguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

internal sealed class BoundGameValue<T> : IBoundGameValue<T>
{
    private readonly GameValueSpec _spec;
    private readonly FieldInfo? _field;
    private readonly PropertyInfo? _property;
    private readonly Delegate? _getterDelegate;
    private readonly Delegate? _setterDelegate;

    internal BoundGameValue(GameValueSpec spec)
    {
        _spec = spec;
        Status = GameBindingStatus.Missing;
    }

    internal BoundGameValue(GameValueSpec spec, MemberInfo member, bool isPublic)
    {
        _spec = spec;
        _field = member as FieldInfo;
        _property = member as PropertyInfo;
        if (_field == null && _property == null)
            throw new ArgumentException("A value binding must use a field or property.", nameof(member));

        if (!isPublic)
        {
            Status = GameBindingStatus.NonPublicReflection;
            return;
        }

        if (_property != null)
        {
            if ((_spec.Access & GameValueAccess.Read) != 0)
                _getterDelegate = GameDelegateFactory.TryCreate(_property.GetGetMethod(false));
            if ((_spec.Access & GameValueAccess.Write) != 0)
                _setterDelegate = GameDelegateFactory.TryCreate(_property.GetSetMethod(false));
        }

        var allRequiredDelegatesCreated = _property != null &&
            ((_spec.Access & GameValueAccess.Read) == 0 || _getterDelegate != null) &&
            ((_spec.Access & GameValueAccess.Write) == 0 || _setterDelegate != null);
        Status = allRequiredDelegatesCreated
            ? GameBindingStatus.PublicDelegate
            : GameBindingStatus.PublicReflection;
    }

    public bool IsBound => Status != GameBindingStatus.Missing;
    public GameBindingStatus Status { get; }
    public MemberInfo? Member => (MemberInfo?)_field ?? _property;

    public T Get(object? instance)
    {
        if ((_spec.Access & GameValueAccess.Read) == 0)
            throw new GameBindingException("The value binding is not readable: " + _spec);
        EnsureBound();
        EnsureInstance(instance);

        object? value;
        try
        {
            if (_field != null)
            {
                value = _field.GetValue(_spec.IsStatic ? null : instance);
            }
            else if (_getterDelegate != null)
            {
                if (_spec.IsStatic && _getterDelegate is Func<T> staticGetter)
                    value = staticGetter();
                else
                    value = _getterDelegate.DynamicInvoke(new[] { instance });
            }
            else
            {
                value = _property!.GetValue(_spec.IsStatic ? null : instance, null);
            }
        }
        catch (Exception ex)
        {
            GameInvocationException.ThrowUnwrapped(ex);
            throw;
        }

        return (T)value!;
    }

    public void Set(object? instance, T value)
    {
        if ((_spec.Access & GameValueAccess.Write) == 0)
            throw new GameBindingException("The value binding is not writable: " + _spec);
        EnsureBound();
        EnsureInstance(instance);

        try
        {
            if (_field != null)
            {
                _field.SetValue(_spec.IsStatic ? null : instance, value);
            }
            else if (_setterDelegate != null)
            {
                if (_spec.IsStatic && _setterDelegate is Action<T> staticSetter)
                    staticSetter(value);
                else
                    _setterDelegate.DynamicInvoke(new object?[] { instance, value });
            }
            else
            {
                _property!.SetValue(_spec.IsStatic ? null : instance, value, null);
            }
        }
        catch (Exception ex)
        {
            GameInvocationException.ThrowUnwrapped(ex);
            throw;
        }
    }

    public bool TryGet(object? instance, out T value)
    {
        try
        {
            value = Get(instance);
            return true;
        }
        catch (Exception)
        {
            value = default!;
            return false;
        }
    }

    public bool TrySet(object? instance, T value)
    {
        try
        {
            Set(instance, value);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void EnsureBound()
    {
        if (!IsBound)
            throw new GameBindingException("Game value member was not found: " + _spec);
    }

    private void EnsureInstance(object? instance)
    {
        if (!_spec.IsStatic && instance == null)
            throw new ArgumentNullException(nameof(instance), "An instance binding requires a target object.");
    }
}

internal sealed class BoundGameMethod : IBoundGameMethod
{
    private readonly GameMethodSpec _spec;
    private readonly Delegate? _publicDelegate;

    internal BoundGameMethod(GameMethodSpec spec)
    {
        _spec = spec;
        Status = GameBindingStatus.Missing;
    }

    internal BoundGameMethod(GameMethodSpec spec, MethodInfo method, bool isPublic)
    {
        _spec = spec;
        Method = method;
        if (!isPublic)
        {
            Status = GameBindingStatus.NonPublicReflection;
            return;
        }

        _publicDelegate = GameDelegateFactory.TryCreate(method);
        Status = _publicDelegate != null
            ? GameBindingStatus.PublicDelegate
            : GameBindingStatus.PublicReflection;
    }

    public bool IsBound => Status != GameBindingStatus.Missing;
    public GameBindingStatus Status { get; }
    public MethodInfo? Method { get; }

    public object? Invoke(object? instance, params object?[]? arguments)
    {
        if (!IsBound || Method == null)
            throw new GameBindingException("Game method was not found: " + _spec);
        if (!_spec.IsStatic && instance == null)
            throw new ArgumentNullException(nameof(instance), "An instance binding requires a target object.");

        arguments ??= Array.Empty<object?>();
        if (arguments.Length != _spec.ParameterTypes.Count)
        {
            throw new TargetParameterCountException(
                "Expected " + _spec.ParameterTypes.Count + " arguments but received " + arguments.Length + ".");
        }

        try
        {
            if (_publicDelegate == null)
                return Method.Invoke(_spec.IsStatic ? null : instance, arguments);

            if (_spec.IsStatic)
                return _publicDelegate.DynamicInvoke(arguments);

            var delegateArguments = new object?[arguments.Length + 1];
            delegateArguments[0] = instance;
            Array.Copy(arguments, 0, delegateArguments, 1, arguments.Length);
            return _publicDelegate.DynamicInvoke(delegateArguments);
        }
        catch (Exception ex)
        {
            GameInvocationException.ThrowUnwrapped(ex);
            throw;
        }
    }

    public bool TryInvoke(
        object? instance,
        object?[]? arguments,
        out object? result)
    {
        try
        {
            result = Invoke(instance, arguments);
            return true;
        }
        catch (Exception)
        {
            result = null;
            return false;
        }
    }
}

internal static class GameDelegateFactory
{
    internal static Delegate? TryCreate(MethodInfo? method)
    {
        if (method == null ||
            !method.IsPublic ||
            method.ContainsGenericParameters ||
            method.ReturnType.IsByRef ||
            method.ReturnType.IsPointer)
            return null;

        var parameters = method.GetParameters();
        var signatureLength = parameters.Length + (method.IsStatic ? 0 : 1);
        var signature = new Type[signatureLength + (method.ReturnType == typeof(void) ? 0 : 1)];
        var index = 0;
        if (!method.IsStatic)
        {
            if (method.DeclaringType == null)
                return null;
            signature[index++] = method.DeclaringType;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;
            if (parameterType.IsByRef || parameterType.IsPointer)
                return null;
            signature[index++] = parameterType;
        }

        try
        {
            Type delegateType;
            if (method.ReturnType == typeof(void))
            {
                delegateType = signature.Length == 0
                    ? typeof(Action)
                    : Expression.GetActionType(signature);
            }
            else
            {
                signature[index] = method.ReturnType;
                delegateType = Expression.GetFuncType(signature);
            }
            return method.CreateDelegate(delegateType);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal static class GameInvocationException
{
    internal static void ThrowUnwrapped(Exception exception)
    {
        while (exception is TargetInvocationException targetInvocationException &&
               targetInvocationException.InnerException != null)
            exception = targetInvocationException.InnerException;
        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
