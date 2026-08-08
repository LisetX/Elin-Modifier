using System;

internal static class GameAccessServiceHelpers
{
    internal static T? GetReference<T>(IBoundGameValue<T> binding, object? instance)
        where T : class
    {
        return binding.TryGet(instance, out var value) ? value : null;
    }

    internal static T GetValueOrDefault<T>(IBoundGameValue<T> binding, object? instance, T fallback)
        where T : struct
    {
        return binding.TryGet(instance, out var value) ? value : fallback;
    }

    internal static T InvokeValue<T>(IBoundGameMethod binding, object? instance, params object?[] arguments)
        where T : struct
    {
        var result = binding.Invoke(instance, arguments);
        if (result is T value)
            return value;
        throw new InvalidCastException("The bound game method returned an unexpected value type.");
    }

    internal static T? InvokeReference<T>(IBoundGameMethod binding, object? instance, params object?[] arguments)
        where T : class
    {
        return binding.Invoke(instance, arguments) as T;
    }
}
