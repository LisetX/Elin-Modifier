using System;

internal sealed class GameRuntimeContext : IGameRuntimeContext
{
    private readonly IBoundGameValue<Core> _core;
    private readonly IBoundGameValue<Game> _game;
    private readonly IBoundGameValue<Player> _player;
    private readonly IBoundGameValue<GameSetting> _settings;
    private readonly IBoundGameValue<GameData> _gameData;
    private readonly IBoundGameValue<CoreDebug> _debug;

    internal GameRuntimeContext(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        _core = BindRoot<Core>(binder, "core");
        _game = BindRoot<Game>(binder, "game");
        _player = BindRoot<Player>(binder, "player");
        _settings = BindRoot<GameSetting>(binder, "setting");
        _gameData = BindRoot<GameData>(binder, "gamedata");
        _debug = BindRoot<CoreDebug>(binder, "debug");
    }

    public Core? Core => GameAccessServiceHelpers.GetReference(_core, null);
    public Game? Game => GameAccessServiceHelpers.GetReference(_game, null);
    public Player? Player => GameAccessServiceHelpers.GetReference(_player, null);
    public GameSetting? Settings => GameAccessServiceHelpers.GetReference(_settings, null);
    public GameData? GameData => GameAccessServiceHelpers.GetReference(_gameData, null);
    public CoreDebug? Debug => GameAccessServiceHelpers.GetReference(_debug, null);

    private static IBoundGameValue<T> BindRoot<T>(IGameMemberBinder binder, string memberName)
        where T : class
    {
        return binder.BindValue<T>(GameValueSpec.Static(
            typeof(EClass),
            typeof(T),
            GameValueAccess.Read,
            memberName));
    }
}
