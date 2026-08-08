using System;
using System.Threading;

internal static class GameAccess
{
    private static IElinModifierGameServices? _current;

    internal static bool IsInitialized => Volatile.Read(ref _current) != null;
    internal static IGameRuntimeContext Runtime => Current.Runtime;
    internal static IGameSourceRepository Sources => Current.Sources;
    internal static ICharacterGameAccess Characters => Current.Characters;
    internal static IWorldGameAccess World => Current.World;
    internal static IGameUiAccess Ui => Current.Ui;
    internal static IGameRandomService Random => Current.Random;
    internal static IGameSpawnService Spawn => Current.Spawn;

    internal static void Initialize(IElinModifierGameServices services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        var previous = Interlocked.CompareExchange(ref _current, services, null);
        if (previous != null && !ReferenceEquals(previous, services))
            throw new InvalidOperationException("Game access services are already initialized.");
    }

    internal static void Reset(IElinModifierGameServices services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        Interlocked.CompareExchange(ref _current, null, services);
    }

    private static IElinModifierGameServices Current =>
        Volatile.Read(ref _current) ??
        throw new InvalidOperationException("Game access services are not initialized.");
}
