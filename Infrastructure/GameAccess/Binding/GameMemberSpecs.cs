using System;
using System.Collections.Generic;

internal sealed class GameValueSpec : IEquatable<GameValueSpec>
{
    private readonly string[] _candidateNames;
    private readonly IReadOnlyList<string> _candidateNamesView;

    internal GameValueSpec(
        Type declaringType,
        Type valueType,
        bool isStatic,
        GameValueAccess access,
        params string[] candidateNames)
    {
        DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        if (valueType == typeof(void) || valueType.IsByRef)
            throw new ArgumentException("A value member must have a non-void, non-by-ref type.", nameof(valueType));
        if (access == GameValueAccess.None || (access & ~GameValueAccess.ReadWrite) != 0)
            throw new ArgumentOutOfRangeException(nameof(access));

        IsStatic = isStatic;
        Access = access;
        _candidateNames = GameMemberSpecNames.Normalize(candidateNames);
        _candidateNamesView = Array.AsReadOnly(_candidateNames);
    }

    internal Type DeclaringType { get; }
    internal Type ValueType { get; }
    internal bool IsStatic { get; }
    internal GameValueAccess Access { get; }
    internal IReadOnlyList<string> CandidateNames => _candidateNamesView;

    internal static GameValueSpec Instance(
        Type declaringType,
        Type valueType,
        GameValueAccess access,
        params string[] candidateNames)
    {
        return new GameValueSpec(declaringType, valueType, false, access, candidateNames);
    }

    internal static GameValueSpec Static(
        Type declaringType,
        Type valueType,
        GameValueAccess access,
        params string[] candidateNames)
    {
        return new GameValueSpec(declaringType, valueType, true, access, candidateNames);
    }

    public bool Equals(GameValueSpec? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other == null ||
            DeclaringType != other.DeclaringType ||
            ValueType != other.ValueType ||
            IsStatic != other.IsStatic ||
            Access != other.Access ||
            _candidateNames.Length != other._candidateNames.Length)
            return false;

        for (var i = 0; i < _candidateNames.Length; i++)
            if (!string.Equals(_candidateNames[i], other._candidateNames[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as GameValueSpec);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = DeclaringType.GetHashCode();
            hash = (hash * 397) ^ ValueType.GetHashCode();
            hash = (hash * 397) ^ IsStatic.GetHashCode();
            hash = (hash * 397) ^ (int)Access;
            for (var i = 0; i < _candidateNames.Length; i++)
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_candidateNames[i]);
            return hash;
        }
    }

    public override string ToString()
    {
        return DeclaringType.FullName + "." + string.Join("|", _candidateNames) +
               " : " + ValueType.FullName + (IsStatic ? " static" : " instance");
    }
}

internal sealed class GameMethodSpec : IEquatable<GameMethodSpec>
{
    private readonly string[] _candidateNames;
    private readonly IReadOnlyList<string> _candidateNamesView;
    private readonly Type[] _genericTypeArguments;
    private readonly IReadOnlyList<Type> _genericTypeArgumentsView;
    private readonly Type[] _parameterTypes;
    private readonly IReadOnlyList<Type> _parameterTypesView;

    internal GameMethodSpec(
        Type declaringType,
        Type returnType,
        bool isStatic,
        Type[] genericTypeArguments,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        if (returnType.IsByRef || returnType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                "Method return types must be closed and cannot be by-ref.",
                nameof(returnType));
        }
        if (genericTypeArguments == null)
            throw new ArgumentNullException(nameof(genericTypeArguments));
        if (parameterTypes == null)
            throw new ArgumentNullException(nameof(parameterTypes));

        _genericTypeArguments = (Type[])genericTypeArguments.Clone();
        for (var i = 0; i < _genericTypeArguments.Length; i++)
        {
            var argument = _genericTypeArguments[i];
            if (argument == null ||
                argument == typeof(void) ||
                argument.IsByRef ||
                argument.IsPointer ||
                argument.ContainsGenericParameters)
            {
                throw new ArgumentException(
                    "Generic method type arguments must be closed ordinary types.",
                    nameof(genericTypeArguments));
            }
        }

        _parameterTypes = (Type[])parameterTypes.Clone();
        for (var i = 0; i < _parameterTypes.Length; i++)
        {
            if (_parameterTypes[i] == null || _parameterTypes[i].ContainsGenericParameters)
            {
                throw new ArgumentException(
                    "Method parameter types must be non-null closed types.",
                    nameof(parameterTypes));
            }
        }

        IsStatic = isStatic;
        _genericTypeArgumentsView = Array.AsReadOnly(_genericTypeArguments);
        _parameterTypesView = Array.AsReadOnly(_parameterTypes);
        _candidateNames = GameMemberSpecNames.Normalize(candidateNames);
        _candidateNamesView = Array.AsReadOnly(_candidateNames);
    }

    internal Type DeclaringType { get; }
    internal Type ReturnType { get; }
    internal bool IsStatic { get; }
    internal IReadOnlyList<Type> GenericTypeArguments => _genericTypeArgumentsView;
    internal IReadOnlyList<Type> ParameterTypes => _parameterTypesView;
    internal IReadOnlyList<string> CandidateNames => _candidateNamesView;

    internal static GameMethodSpec Instance(
        Type declaringType,
        Type returnType,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        return new GameMethodSpec(
            declaringType,
            returnType,
            false,
            Type.EmptyTypes,
            parameterTypes,
            candidateNames);
    }

    internal static GameMethodSpec Static(
        Type declaringType,
        Type returnType,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        return new GameMethodSpec(
            declaringType,
            returnType,
            true,
            Type.EmptyTypes,
            parameterTypes,
            candidateNames);
    }

    internal static GameMethodSpec InstanceGeneric(
        Type declaringType,
        Type returnType,
        Type[] genericTypeArguments,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        return new GameMethodSpec(
            declaringType,
            returnType,
            false,
            genericTypeArguments,
            parameterTypes,
            candidateNames);
    }

    internal static GameMethodSpec StaticGeneric(
        Type declaringType,
        Type returnType,
        Type[] genericTypeArguments,
        Type[] parameterTypes,
        params string[] candidateNames)
    {
        return new GameMethodSpec(
            declaringType,
            returnType,
            true,
            genericTypeArguments,
            parameterTypes,
            candidateNames);
    }

    public bool Equals(GameMethodSpec? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other == null ||
            DeclaringType != other.DeclaringType ||
            ReturnType != other.ReturnType ||
            IsStatic != other.IsStatic ||
            _genericTypeArguments.Length != other._genericTypeArguments.Length ||
            _parameterTypes.Length != other._parameterTypes.Length ||
            _candidateNames.Length != other._candidateNames.Length)
            return false;

        for (var i = 0; i < _genericTypeArguments.Length; i++)
            if (_genericTypeArguments[i] != other._genericTypeArguments[i])
                return false;
        for (var i = 0; i < _parameterTypes.Length; i++)
            if (_parameterTypes[i] != other._parameterTypes[i])
                return false;
        for (var i = 0; i < _candidateNames.Length; i++)
            if (!string.Equals(_candidateNames[i], other._candidateNames[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as GameMethodSpec);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = DeclaringType.GetHashCode();
            hash = (hash * 397) ^ ReturnType.GetHashCode();
            hash = (hash * 397) ^ IsStatic.GetHashCode();
            for (var i = 0; i < _genericTypeArguments.Length; i++)
                hash = (hash * 397) ^ _genericTypeArguments[i].GetHashCode();
            for (var i = 0; i < _parameterTypes.Length; i++)
                hash = (hash * 397) ^ _parameterTypes[i].GetHashCode();
            for (var i = 0; i < _candidateNames.Length; i++)
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_candidateNames[i]);
            return hash;
        }
    }

    public override string ToString()
    {
        var parameterNames = new string[_parameterTypes.Length];
        for (var i = 0; i < _parameterTypes.Length; i++)
            parameterNames[i] = _parameterTypes[i].FullName ?? _parameterTypes[i].Name;
        var genericArgumentNames = new string[_genericTypeArguments.Length];
        for (var i = 0; i < _genericTypeArguments.Length; i++)
        {
            genericArgumentNames[i] =
                _genericTypeArguments[i].FullName ?? _genericTypeArguments[i].Name;
        }
        var genericSuffix = genericArgumentNames.Length == 0
            ? ""
            : "<" + string.Join(", ", genericArgumentNames) + ">";
        return DeclaringType.FullName + "." + string.Join("|", _candidateNames) + genericSuffix +
               "(" + string.Join(", ", parameterNames) + ") : " + ReturnType.FullName +
               (IsStatic ? " static" : " instance");
    }
}

internal static class GameMemberSpecNames
{
    internal static string[] Normalize(string[]? candidateNames)
    {
        if (candidateNames == null || candidateNames.Length == 0)
            throw new ArgumentException("At least one candidate member name is required.", nameof(candidateNames));

        var result = new List<string>(candidateNames.Length);
        for (var i = 0; i < candidateNames.Length; i++)
        {
            var name = candidateNames[i]?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Candidate member names cannot be empty.", nameof(candidateNames));
            if (!ContainsOrdinal(result, name))
                result.Add(name);
        }
        return result.ToArray();
    }

    private static bool ContainsOrdinal(List<string> values, string candidate)
    {
        for (var i = 0; i < values.Count; i++)
            if (string.Equals(values[i], candidate, StringComparison.Ordinal))
                return true;
        return false;
    }
}
