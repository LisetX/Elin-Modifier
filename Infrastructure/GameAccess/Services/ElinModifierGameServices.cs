using System;

internal sealed class ElinModifierGameServices : IElinModifierGameServices
{
    internal ElinModifierGameServices(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        Runtime = new GameRuntimeContext(binder);
        Sources = new GameSourceRepository(binder);
        Characters = new CharacterGameAccess(binder);
        World = new WorldGameAccess(binder);
        Ui = new GameUiAccess(binder);
        Random = new GameRandomService(binder);
        Spawn = new GameSpawnService(binder);
    }

    public IGameRuntimeContext Runtime { get; }
    public IGameSourceRepository Sources { get; }
    public ICharacterGameAccess Characters { get; }
    public IWorldGameAccess World { get; }
    public IGameUiAccess Ui { get; }
    public IGameRandomService Random { get; }
    public IGameSpawnService Spawn { get; }
}
