using System;
using System.Reflection;

[Flags]
internal enum GameValueAccess
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    ReadWrite = Read | Write
}

internal enum GameBindingStatus
{
    Missing = 0,
    PublicDelegate = 1,
    PublicReflection = 2,
    NonPublicReflection = 3
}

internal interface IBoundGameValue<T>
{
    bool IsBound { get; }
    GameBindingStatus Status { get; }
    MemberInfo? Member { get; }
    T Get(object? instance);
    void Set(object? instance, T value);
    bool TryGet(object? instance, out T value);
    bool TrySet(object? instance, T value);
}

internal interface IBoundGameMethod
{
    bool IsBound { get; }
    GameBindingStatus Status { get; }
    MethodInfo? Method { get; }
    object? Invoke(object? instance, params object?[]? arguments);
    bool TryInvoke(object? instance, object?[]? arguments, out object? result);
}

internal interface IGameMemberBinder
{
    IBoundGameValue<T> BindValue<T>(GameValueSpec spec);
    IBoundGameMethod BindMethod(GameMethodSpec spec);
}

internal sealed class GameBindingException : InvalidOperationException
{
    internal GameBindingException(string message)
        : base(message)
    {
    }

    internal GameBindingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
