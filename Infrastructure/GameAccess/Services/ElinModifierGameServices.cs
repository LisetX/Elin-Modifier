using System;

internal sealed class ElinModifierGameServices : IElinModifierGameServices
{
    internal ElinModifierGameServices(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        Runtime = new GameRuntimeContext(binder);
        Clock = new GameClockAccess(binder);
        Sources = new GameSourceRepository(binder);
        Characters = new CharacterGameAccess(binder);
        World = new WorldGameAccess(binder);
        Ui = new GameUiAccess(binder);
        Random = new GameRandomService(binder);
        Spawn = new GameSpawnService(binder);
        Messages = new GameMessageService(binder);
    }

    public IGameRuntimeContext Runtime { get; }
    public IGameClockAccess Clock { get; }
    public IGameSourceRepository Sources { get; }
    public ICharacterGameAccess Characters { get; }
    public IWorldGameAccess World { get; }
    public IGameUiAccess Ui { get; }
    public IGameRandomService Random { get; }
    public IGameSpawnService Spawn { get; }
    public IGameMessageService Messages { get; }
}
