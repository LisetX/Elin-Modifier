using System;

internal sealed class GameClockAccess : IGameClockAccess
{
    private readonly IBoundGameValue<World> _currentWorld;
    private readonly IBoundGameValue<GameDate> _date;
    private readonly IBoundGameValue<int> _year;
    private readonly IBoundGameValue<int> _month;
    private readonly IBoundGameValue<int> _day;
    private readonly IBoundGameValue<int> _hour;
    private readonly IBoundGameValue<int> _minute;

    internal GameClockAccess(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _currentWorld = binder.BindStaticValue<World>(typeof(EClass), GameValueAccess.Read, "world");
        _date = binder.BindInstanceValue<GameDate>(typeof(World), GameValueAccess.Read, "date");
        _year = binder.BindInstanceValue<int>(typeof(GameDate), GameValueAccess.Read, "year");
        _month = binder.BindInstanceValue<int>(typeof(GameDate), GameValueAccess.Read, "month");
        _day = binder.BindInstanceValue<int>(typeof(GameDate), GameValueAccess.Read, "day");
        _hour = binder.BindInstanceValue<int>(typeof(GameDate), GameValueAccess.Read, "hour");
        _minute = binder.BindInstanceValue<int>(typeof(GameDate), GameValueAccess.Read, "min");
    }

    public bool TryGetCurrent(out GameClockSnapshot snapshot)
    {
        snapshot = default;
        if (!_currentWorld.TryGet(null, out var world) ||
            world == null ||
            !_date.TryGet(world, out var date) ||
            date == null ||
            !_year.TryGet(date, out var year) ||
            !_month.TryGet(date, out var month) ||
            !_day.TryGet(date, out var day) ||
            !_hour.TryGet(date, out var hour) ||
            !_minute.TryGet(date, out var minute))
            return false;

        snapshot = new GameClockSnapshot(year, month, day, hour, minute);
        return true;
    }
}
