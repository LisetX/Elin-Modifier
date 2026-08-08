using System.Collections.Generic;

internal interface IElinModifierGameServices
{
    IGameRuntimeContext Runtime { get; }
    IGameSourceRepository Sources { get; }
    ICharacterGameAccess Characters { get; }
    IWorldGameAccess World { get; }
    IGameUiAccess Ui { get; }
    IGameRandomService Random { get; }
    IGameSpawnService Spawn { get; }
}

internal interface IGameRuntimeContext
{
    Core? Core { get; }
    Game? Game { get; }
    Player? Player { get; }
    GameSetting? Settings { get; }
    GameData? GameData { get; }
    CoreDebug? Debug { get; }
}

internal interface IGameSourceRepository
{
    SourceManager? Manager { get; }
    SourceCard? Cards { get; }
    SourceChara? Characters { get; }
    SourceThing? Things { get; }
    SourceElement? Elements { get; }
    SourceRace? Races { get; }
    SourceCategory? Categories { get; }
    SourceMaterial? Materials { get; }
    SourceSpawnList? SpawnLists { get; }
    SourceReligion? Religions { get; }
    SourceJob? Jobs { get; }
    SourceRecipe? Recipes { get; }
    SourceObj? Objects { get; }
}

internal interface ICharacterGameAccess
{
    Chara? PlayerCharacter { get; }
    ElementContainer? PlayerElements { get; }
    ElementContainer? GetElements(Card card);
    string? GetName(Card card, NameStyle style, int article);
    int GetElementValue(Card card, int elementId);
    int GetPlayerElementValue(int elementId);
    void Refresh(Chara character, bool fullRefresh);
}

internal interface IWorldGameAccess
{
    Map? CurrentMap { get; }
    Zone? CurrentZone { get; }
    World? CurrentWorld { get; }
    FactionBranch? Branch { get; }
    FactionBranch? BranchOrHomeBranch { get; }
    Faction? Home { get; }
    IReadOnlyList<Chara>? CurrentCharacters { get; }
    IReadOnlyList<Thing>? CurrentThings { get; }
    Region? CurrentRegion { get; }
    IReadOnlyList<Chara>? GetCharacters(Map map);
    IReadOnlyList<Thing>? GetThings(Map map);
    Region? GetRegion(World world);
    int CountHostile(Map map);
    Card? AddCard(Zone zone, Card card, Point point);
    bool TryAddThing(Zone zone, Thing thing, Point point, bool tryStack);
}

internal interface IGameUiAccess
{
    UI? Root { get; }
    Scene? Scene { get; }
    BaseGameScreen? Screen { get; }
    ColorProfile? Colors { get; }
    bool IsActive { get; }
    bool IsPointerOverUi { get; }
}

internal interface IGameRandomService
{
    int Next(int maximum);
    int Next(long maximum);
    float NextFloat(float maximum);
    int Curve(long value, int start, int end, int maximum = 100);
}

internal interface IGameSpawnService
{
    Thing? CreateThing(string id, int materialId, int level);
    Thing? CreateThing(string id, string materialAlias, int level);
    Thing? CreateThingFromCategory(string categoryId, int level);
    Chara? CreateCharacter(string id, int level);
}
