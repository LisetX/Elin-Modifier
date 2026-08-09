using System;

internal static class GameMemberBindingExtensions
{
    internal static IBoundGameValue<T> BindInstanceValue<T>(
        this IGameMemberBinder binder,
        Type declaringType,
        GameValueAccess access,
        params string[] candidateNames)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        return binder.BindValue<T>(GameValueSpec.Instance(
            declaringType,
            typeof(T),
            access,
            candidateNames));
    }

    internal static IBoundGameValue<T> BindStaticValue<T>(
        this IGameMemberBinder binder,
        Type declaringType,
        GameValueAccess access,
        params string[] candidateNames)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        return binder.BindValue<T>(GameValueSpec.Static(
            declaringType,
            typeof(T),
            access,
            candidateNames));
    }

    internal static IBoundGameMethod BindInstanceMethod(
        this IGameMemberBinder binder,
        Type declaringType,
        Type returnType,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        return binder.BindMethod(GameMethodSpec.Instance(
            declaringType,
            returnType,
            parameterTypes,
            candidateNames));
    }

    internal static IBoundGameMethod BindStaticMethod(
        this IGameMemberBinder binder,
        Type declaringType,
        Type returnType,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        return binder.BindMethod(GameMethodSpec.Static(
            declaringType,
            returnType,
            parameterTypes,
            candidateNames));
    }

    internal static IBoundGameMethod BindInstanceGenericMethod(
        this IGameMemberBinder binder,
        Type declaringType,
        Type returnType,
        Type[] genericTypeArguments,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        return binder.BindMethod(GameMethodSpec.InstanceGeneric(
            declaringType,
            returnType,
            genericTypeArguments,
            parameterTypes,
            candidateNames));
    }

    internal static IBoundGameMethod BindStaticGenericMethod(
        this IGameMemberBinder binder,
        Type declaringType,
        Type returnType,
        Type[] genericTypeArguments,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        return binder.BindMethod(GameMethodSpec.StaticGeneric(
            declaringType,
            returnType,
            genericTypeArguments,
            parameterTypes,
            candidateNames));
    }
}
