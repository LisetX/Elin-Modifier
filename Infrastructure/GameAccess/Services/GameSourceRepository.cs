using System;

internal sealed class GameSourceRepository : IGameSourceRepository
{
    private readonly IBoundGameValue<SourceManager> _sources;
    private readonly IBoundGameValue<SourceCard> _cards;
    private readonly IBoundGameValue<SourceChara> _characters;
    private readonly IBoundGameValue<SourceThing> _things;
    private readonly IBoundGameValue<SourceElement> _elements;
    private readonly IBoundGameValue<SourceRace> _races;
    private readonly IBoundGameValue<SourceCategory> _categories;
    private readonly IBoundGameValue<SourceMaterial> _materials;
    private readonly IBoundGameValue<SourceSpawnList> _spawnLists;
    private readonly IBoundGameValue<SourceReligion> _religions;
    private readonly IBoundGameValue<SourceJob> _jobs;
    private readonly IBoundGameValue<SourceRecipe> _recipes;
    private readonly IBoundGameValue<SourceObj> _objects;

    internal GameSourceRepository(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        _sources = binder.BindValue<SourceManager>(GameValueSpec.Static(
            typeof(EClass),
            typeof(SourceManager),
            GameValueAccess.Read,
            "sources"));
        _cards = BindTable<SourceCard>(binder, "cards");
        _characters = BindTable<SourceChara>(binder, "charas");
        _things = BindTable<SourceThing>(binder, "things");
        _elements = BindTable<SourceElement>(binder, "elements");
        _races = BindTable<SourceRace>(binder, "races");
        _categories = BindTable<SourceCategory>(binder, "categories");
        _materials = BindTable<SourceMaterial>(binder, "materials");
        _spawnLists = BindTable<SourceSpawnList>(binder, "spawnLists");
        _religions = BindTable<SourceReligion>(binder, "religions");
        _jobs = BindTable<SourceJob>(binder, "jobs");
        _recipes = BindTable<SourceRecipe>(binder, "recipes");
        _objects = BindTable<SourceObj>(binder, "objs");
    }

    public SourceManager? Manager => GameAccessServiceHelpers.GetReference(_sources, null);
    public SourceCard? Cards => GetTable(_cards);
    public SourceChara? Characters => GetTable(_characters);
    public SourceThing? Things => GetTable(_things);
    public SourceElement? Elements => GetTable(_elements);
    public SourceRace? Races => GetTable(_races);
    public SourceCategory? Categories => GetTable(_categories);
    public SourceMaterial? Materials => GetTable(_materials);
    public SourceSpawnList? SpawnLists => GetTable(_spawnLists);
    public SourceReligion? Religions => GetTable(_religions);
    public SourceJob? Jobs => GetTable(_jobs);
    public SourceRecipe? Recipes => GetTable(_recipes);
    public SourceObj? Objects => GetTable(_objects);

    private T? GetTable<T>(IBoundGameValue<T> binding)
        where T : class
    {
        var sources = Manager;
        return sources == null
            ? null
            : GameAccessServiceHelpers.GetReference(binding, sources);
    }

    private static IBoundGameValue<T> BindTable<T>(IGameMemberBinder binder, string memberName)
        where T : class
    {
        return binder.BindValue<T>(GameValueSpec.Instance(
            typeof(SourceManager),
            typeof(T),
            GameValueAccess.Read,
            memberName));
    }
}
