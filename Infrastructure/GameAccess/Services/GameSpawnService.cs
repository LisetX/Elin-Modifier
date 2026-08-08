using System;

internal sealed class GameSpawnService : IGameSpawnService
{
    private readonly IBoundGameMethod _createThingByMaterialId;
    private readonly IBoundGameMethod _createThingByMaterialAlias;
    private readonly IBoundGameMethod _createThingFromCategory;
    private readonly IBoundGameMethod _createCharacter;

    internal GameSpawnService(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        _createThingByMaterialId = binder.BindMethod(GameMethodSpec.Static(
            typeof(ThingGen),
            typeof(Thing),
            new[] { typeof(string), typeof(int), typeof(int) },
            "Create"));
        _createThingByMaterialAlias = binder.BindMethod(GameMethodSpec.Static(
            typeof(ThingGen),
            typeof(Thing),
            new[] { typeof(string), typeof(string), typeof(int) },
            "Create"));
        _createThingFromCategory = binder.BindMethod(GameMethodSpec.Static(
            typeof(ThingGen),
            typeof(Thing),
            new[] { typeof(string), typeof(int) },
            "CreateFromCategory"));
        _createCharacter = binder.BindMethod(GameMethodSpec.Static(
            typeof(CharaGen),
            typeof(Chara),
            new[] { typeof(string), typeof(int) },
            "Create"));
    }

    public Thing? CreateThing(string id, int materialId, int level)
    {
        return GameAccessServiceHelpers.InvokeReference<Thing>(
            _createThingByMaterialId,
            null,
            id,
            materialId,
            level);
    }

    public Thing? CreateThing(string id, string materialAlias, int level)
    {
        return GameAccessServiceHelpers.InvokeReference<Thing>(
            _createThingByMaterialAlias,
            null,
            id,
            materialAlias,
            level);
    }

    public Thing? CreateThingFromCategory(string categoryId, int level)
    {
        return GameAccessServiceHelpers.InvokeReference<Thing>(
            _createThingFromCategory,
            null,
            categoryId,
            level);
    }

    public Chara? CreateCharacter(string id, int level)
    {
        return GameAccessServiceHelpers.InvokeReference<Chara>(
            _createCharacter,
            null,
            id,
            level);
    }
}
