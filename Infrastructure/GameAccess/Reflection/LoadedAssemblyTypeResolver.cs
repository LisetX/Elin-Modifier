using System;
using System.Collections.Generic;
using System.Reflection;

internal static class LoadedAssemblyTypeResolver
{
    internal static Type? ResolveExact(
        string typeName,
        string? assemblyFilter = null,
        bool ignoreCase = false)
    {
        return Resolve(typeName, assemblyFilter, ignoreCase, false);
    }

    internal static Type? Resolve(
        string typeName,
        string? assemblyFilter = null,
        bool ignoreCase = false,
        bool allowSimpleName = false)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var normalizedTypeName = typeName.Trim().Replace('/', '+');
        Assembly[] assemblies;
        try
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies();
        }
        catch
        {
            return null;
        }

        for (var i = 0; i < assemblies.Length; i++)
        {
            var assembly = assemblies[i];
            if (!MatchesAssembly(assembly, assemblyFilter))
                continue;

            try
            {
                var type = assembly.GetType(normalizedTypeName, false, ignoreCase);
                if (type != null)
                    return type;
            }
            catch
            {
            }
        }

        if (!allowSimpleName)
            return null;

        var simpleName = GetSimpleName(normalizedTypeName);
        var comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        for (var i = 0; i < assemblies.Length; i++)
        {
            var assembly = assemblies[i];
            if (!MatchesAssembly(assembly, assemblyFilter))
                continue;

            foreach (var type in GetLoadableTypes(assembly))
            {
                if (string.Equals(type.FullName, normalizedTypeName, comparison) ||
                    string.Equals(type.Name, simpleName, comparison))
                    return type;
            }
        }

        return null;
    }

    private static bool MatchesAssembly(Assembly? assembly, string? assemblyFilter)
    {
        if (assembly == null)
            return false;
        if (string.IsNullOrWhiteSpace(assemblyFilter))
            return true;

        try
        {
            var filter = assemblyFilter.Trim();
            var simpleName = assembly.GetName().Name ?? string.Empty;
            var fullName = assembly.FullName ?? string.Empty;
            return simpleName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetSimpleName(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        var plus = typeName.LastIndexOf('+');
        var separator = Math.Max(dot, plus);
        return separator >= 0 && separator < typeName.Length - 1
            ? typeName.Substring(separator + 1)
            : typeName;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }
        catch
        {
            yield break;
        }

        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            if (type != null)
                yield return type;
        }
    }
}
