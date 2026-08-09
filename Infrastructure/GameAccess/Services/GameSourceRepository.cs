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

        _sources = binder.BindStaticValue<SourceManager>(typeof(EClass), GameValueAccess.Read, "sources");
        _cards = binder.BindInstanceValue<SourceCard>(typeof(SourceManager), GameValueAccess.Read, "cards");
        _characters = binder.BindInstanceValue<SourceChara>(typeof(SourceManager), GameValueAccess.Read, "charas");
        _things = binder.BindInstanceValue<SourceThing>(typeof(SourceManager), GameValueAccess.Read, "things");
        _elements = binder.BindInstanceValue<SourceElement>(typeof(SourceManager), GameValueAccess.Read, "elements");
        _races = binder.BindInstanceValue<SourceRace>(typeof(SourceManager), GameValueAccess.Read, "races");
        _categories = binder.BindInstanceValue<SourceCategory>(typeof(SourceManager), GameValueAccess.Read, "categories");
        _materials = binder.BindInstanceValue<SourceMaterial>(typeof(SourceManager), GameValueAccess.Read, "materials");
        _spawnLists = binder.BindInstanceValue<SourceSpawnList>(typeof(SourceManager), GameValueAccess.Read, "spawnLists");
        _religions = binder.BindInstanceValue<SourceReligion>(typeof(SourceManager), GameValueAccess.Read, "religions");
        _jobs = binder.BindInstanceValue<SourceJob>(typeof(SourceManager), GameValueAccess.Read, "jobs");
        _recipes = binder.BindInstanceValue<SourceRecipe>(typeof(SourceManager), GameValueAccess.Read, "recipes");
        _objects = binder.BindInstanceValue<SourceObj>(typeof(SourceManager), GameValueAccess.Read, "objs");
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

}
