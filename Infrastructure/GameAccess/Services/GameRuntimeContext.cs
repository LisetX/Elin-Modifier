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

        _core = binder.BindStaticValue<Core>(typeof(EClass), GameValueAccess.Read, "core");
        _game = binder.BindStaticValue<Game>(typeof(EClass), GameValueAccess.Read, "game");
        _player = binder.BindStaticValue<Player>(typeof(EClass), GameValueAccess.Read, "player");
        _settings = binder.BindStaticValue<GameSetting>(typeof(EClass), GameValueAccess.Read, "setting");
        _gameData = binder.BindStaticValue<GameData>(typeof(EClass), GameValueAccess.Read, "gamedata");
        _debug = binder.BindStaticValue<CoreDebug>(typeof(EClass), GameValueAccess.Read, "debug");
    }

    public Core? Core => GameAccessServiceHelpers.GetReference(_core, null);
    public Game? Game => GameAccessServiceHelpers.GetReference(_game, null);
    public Player? Player => GameAccessServiceHelpers.GetReference(_player, null);
    public GameSetting? Settings => GameAccessServiceHelpers.GetReference(_settings, null);
    public GameData? GameData => GameAccessServiceHelpers.GetReference(_gameData, null);
    public CoreDebug? Debug => GameAccessServiceHelpers.GetReference(_debug, null);

}
