using System;
using System.Collections.Generic;

[Flags]
internal enum ElinModifierModuleCapabilities
{
    None = 0,
    Initialize = 1 << 0,
    Update = 1 << 1,
    LateUpdate = 1 << 2,
    Gui = 1 << 3,
    Shutdown = 1 << 4
}

internal sealed class ElinModifierModuleDescriptor
{
    internal ElinModifierModuleDescriptor(
        string id,
        int order,
        int shutdownOrder,
        ElinModifierModuleCapabilities capabilities,
        params string[] dependencies)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Module id cannot be empty.", nameof(id));

        Id = id.Trim();
        Order = order;
        ShutdownOrder = shutdownOrder;
        Capabilities = capabilities;
        Dependencies = dependencies == null || dependencies.Length == 0
            ? Array.Empty<string>()
            : Array.AsReadOnly((string[])dependencies.Clone());
    }

    internal string Id { get; }
    internal int Order { get; }
    internal int ShutdownOrder { get; }
    internal ElinModifierModuleCapabilities Capabilities { get; }
    internal IReadOnlyList<string> Dependencies { get; }
}
