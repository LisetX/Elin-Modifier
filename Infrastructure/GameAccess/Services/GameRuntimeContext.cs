using System;
using System.IO;

internal sealed class GameRuntimeContext : IGameRuntimeContext
{
    private readonly IBoundGameValue<Core> _core;
    private readonly IBoundGameValue<Game> _game;
    private readonly IBoundGameValue<Player> _player;
    private readonly IBoundGameValue<GameSetting> _settings;
    private readonly IBoundGameValue<GameData> _gameData;
    private readonly IBoundGameValue<CoreDebug> _debug;
    private readonly IBoundGameValue<string> _saveId;
    private readonly IBoundGameValue<string> _currentSavePath;

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
        _saveId = binder.BindStaticValue<string>(typeof(Game), GameValueAccess.Read, "id");
        _currentSavePath = binder.BindStaticValue<string>(typeof(GameIO), GameValueAccess.Read, "pathCurrentSave");
    }

    public Core? Core => GameAccessServiceHelpers.GetReference(_core, null);
    public Game? Game => GameAccessServiceHelpers.GetReference(_game, null);
    public Player? Player => GameAccessServiceHelpers.GetReference(_player, null);
    public GameSetting? Settings => GameAccessServiceHelpers.GetReference(_settings, null);
    public GameData? GameData => GameAccessServiceHelpers.GetReference(_gameData, null);
    public CoreDebug? Debug => GameAccessServiceHelpers.GetReference(_debug, null);
    public string? CurrentSaveId
    {
        get
        {
            var saveId = GameAccessServiceHelpers.GetReference(_saveId, null);
            if (!string.IsNullOrWhiteSpace(saveId))
                return saveId.Trim();

            var savePath = GameAccessServiceHelpers.GetReference(_currentSavePath, null);
            if (string.IsNullOrWhiteSpace(savePath))
                return null;

            var normalized = savePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(normalized);
            return string.IsNullOrWhiteSpace(folderName) ? null : folderName.Trim();
        }
    }

}
