using System;

internal sealed class CharacterGameAccess : ICharacterGameAccess
{
    private readonly IBoundGameValue<Chara> _playerCharacter;
    private readonly IBoundGameValue<string> _id;
    private readonly IBoundGameValue<ElementContainerCard> _elements;
    private readonly IBoundGameValue<Religion> _faith;
    private readonly IBoundGameMethod _getName;
    private readonly IBoundGameMethod _getElementValue;
    private readonly IBoundGameMethod _getGiftRankWithCharacter;
    private readonly IBoundGameMethod _getGiftRankLegacy;
    private readonly IBoundGameMethod _refresh;

    internal CharacterGameAccess(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        _playerCharacter = binder.BindStaticValue<Chara>(typeof(EClass), GameValueAccess.Read, "pc");
        _id = binder.BindInstanceValue<string>(typeof(Card), GameValueAccess.Read, "id");
        _elements = binder.BindInstanceValue<ElementContainerCard>(typeof(Card), GameValueAccess.Read, "elements");
        _faith = binder.BindInstanceValue<Religion>(typeof(Chara), GameValueAccess.Read, "faith");
        _getName = binder.BindInstanceMethod(
            typeof(Card),
            typeof(string),
            new[] { typeof(NameStyle), typeof(int) },
            "GetName");
        _getElementValue = binder.BindInstanceMethod(
            typeof(Card),
            typeof(int),
            new[] { typeof(int) },
            "Evalue");
        _getGiftRankWithCharacter = binder.BindInstanceMethod(
            typeof(Religion),
            typeof(int),
            new[] { typeof(Chara) },
            "GetGiftRank");
        _getGiftRankLegacy = binder.BindInstanceMethod(
            typeof(Religion),
            typeof(int),
            Type.EmptyTypes,
            "GetGiftRank");
        _refresh = binder.BindInstanceMethod(
            typeof(Chara),
            typeof(void),
            new[] { typeof(bool) },
            "Refresh");
    }

    public Chara? PlayerCharacter => GameAccessServiceHelpers.GetReference(_playerCharacter, null);
    public ElementContainer? PlayerElements
    {
        get
        {
            var playerCharacter = PlayerCharacter;
            return playerCharacter == null ? null : GetElements(playerCharacter);
        }
    }

    public string? GetId(Card card)
    {
        return GameAccessServiceHelpers.GetReference(_id, card);
    }

    public ElementContainer? GetElements(Card card)
    {
        return GameAccessServiceHelpers.GetReference(_elements, card);
    }

    public string? GetName(Card card, NameStyle style, int article)
    {
        return GameAccessServiceHelpers.InvokeReference<string>(_getName, card, style, article);
    }

    public int GetElementValue(Card card, int elementId)
    {
        return GameAccessServiceHelpers.InvokeValue<int>(_getElementValue, card, elementId);
    }

    public int GetPlayerElementValue(int elementId)
    {
        var playerCharacter = PlayerCharacter;
        return playerCharacter == null ? 0 : GetElementValue(playerCharacter, elementId);
    }

    public int GetFaithGiftRank(Chara character)
    {
        var faith = GameAccessServiceHelpers.GetReference(_faith, character);
        if (faith == null)
            return -1;

        if (_getGiftRankWithCharacter.TryInvoke(
                faith,
                new object?[] { character },
                out var currentResult) &&
            currentResult is int currentRank)
            return currentRank;

        if (_getGiftRankLegacy.TryInvoke(
                faith,
                Array.Empty<object?>(),
                out var legacyResult) &&
            legacyResult is int legacyRank)
            return legacyRank;

        return -1;
    }

    public void Refresh(Chara character, bool fullRefresh)
    {
        _refresh.Invoke(character, fullRefresh);
    }
}
