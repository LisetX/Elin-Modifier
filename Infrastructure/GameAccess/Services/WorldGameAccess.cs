using System;
using System.Collections.Generic;

internal sealed class WorldGameAccess : IWorldGameAccess
{
    private readonly IBoundGameValue<Map> _currentMap;
    private readonly IBoundGameValue<Zone> _currentZone;
    private readonly IBoundGameValue<World> _currentWorld;
    private readonly IBoundGameValue<FactionBranch> _branch;
    private readonly IBoundGameValue<FactionBranch> _branchOrHomeBranch;
    private readonly IBoundGameValue<Faction> _home;
    private readonly IBoundGameValue<List<Chara>> _characters;
    private readonly IBoundGameValue<List<Thing>> _things;
    private readonly IBoundGameValue<Region> _region;
    private readonly IBoundGameMethod _countHostile;
    private readonly IBoundGameMethod _addCard;
    private readonly IBoundGameMethod _tryAddThing;

    internal WorldGameAccess(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        _currentMap = binder.BindStaticValue<Map>(typeof(EClass), GameValueAccess.Read, "_map");
        _currentZone = binder.BindStaticValue<Zone>(typeof(EClass), GameValueAccess.Read, "_zone");
        _currentWorld = binder.BindStaticValue<World>(typeof(EClass), GameValueAccess.Read, "world");
        _branch = binder.BindStaticValue<FactionBranch>(typeof(EClass), GameValueAccess.Read, "Branch");
        _branchOrHomeBranch = binder.BindStaticValue<FactionBranch>(
            typeof(EClass),
            GameValueAccess.Read,
            "BranchOrHomeBranch");
        _home = binder.BindStaticValue<Faction>(typeof(EClass), GameValueAccess.Read, "Home");
        _characters = binder.BindInstanceValue<List<Chara>>(typeof(Map), GameValueAccess.Read, "charas");
        _things = binder.BindInstanceValue<List<Thing>>(typeof(Map), GameValueAccess.Read, "things");
        _region = binder.BindInstanceValue<Region>(typeof(World), GameValueAccess.Read, "region");
        _countHostile = binder.BindInstanceMethod(typeof(Map), typeof(int), Type.EmptyTypes, "CountHostile");
        _addCard = binder.BindInstanceMethod(
            typeof(Zone),
            typeof(Card),
            new[] { typeof(Card), typeof(Point) },
            "AddCard");
        _tryAddThing = binder.BindInstanceMethod(
            typeof(Zone),
            typeof(bool),
            new[] { typeof(Thing), typeof(Point), typeof(bool) },
            "TryAddThing");
    }

    public Map? CurrentMap => GameAccessServiceHelpers.GetReference(_currentMap, null);
    public Zone? CurrentZone => GameAccessServiceHelpers.GetReference(_currentZone, null);
    public World? CurrentWorld => GameAccessServiceHelpers.GetReference(_currentWorld, null);
    public FactionBranch? Branch => GameAccessServiceHelpers.GetReference(_branch, null);
    public FactionBranch? BranchOrHomeBranch =>
        GameAccessServiceHelpers.GetReference(_branchOrHomeBranch, null);
    public Faction? Home => GameAccessServiceHelpers.GetReference(_home, null);
    public IReadOnlyList<Chara>? CurrentCharacters
    {
        get
        {
            var map = CurrentMap;
            return map == null ? null : GetCharacters(map);
        }
    }
    public IReadOnlyList<Thing>? CurrentThings
    {
        get
        {
            var map = CurrentMap;
            return map == null ? null : GetThings(map);
        }
    }
    public Region? CurrentRegion
    {
        get
        {
            var world = CurrentWorld;
            return world == null ? null : GetRegion(world);
        }
    }

    public IReadOnlyList<Chara>? GetCharacters(Map map)
    {
        return GameAccessServiceHelpers.GetReference(_characters, map);
    }

    public IReadOnlyList<Thing>? GetThings(Map map)
    {
        return GameAccessServiceHelpers.GetReference(_things, map);
    }

    public Region? GetRegion(World world)
    {
        return GameAccessServiceHelpers.GetReference(_region, world);
    }

    public int CountHostile(Map map)
    {
        return GameAccessServiceHelpers.InvokeValue<int>(_countHostile, map);
    }

    public Card? AddCard(Zone zone, Card card, Point point)
    {
        return GameAccessServiceHelpers.InvokeReference<Card>(_addCard, zone, card, point);
    }

    public bool TryAddThing(Zone zone, Thing thing, Point point, bool tryStack)
    {
        return GameAccessServiceHelpers.InvokeValue<bool>(_tryAddThing, zone, thing, point, tryStack);
    }

}
