using System;

internal sealed class CharacterGameAccess : ICharacterGameAccess
{
    private readonly IBoundGameValue<Chara> _playerCharacter;
    private readonly IBoundGameValue<ElementContainerCard> _elements;
    private readonly IBoundGameMethod _getName;
    private readonly IBoundGameMethod _getElementValue;
    private readonly IBoundGameMethod _refresh;

    internal CharacterGameAccess(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        _playerCharacter = binder.BindValue<Chara>(GameValueSpec.Static(
            typeof(EClass),
            typeof(Chara),
            GameValueAccess.Read,
            "pc"));
        _elements = binder.BindValue<ElementContainerCard>(GameValueSpec.Instance(
            typeof(Card),
            typeof(ElementContainerCard),
            GameValueAccess.Read,
            "elements"));
        _getName = binder.BindMethod(GameMethodSpec.Instance(
            typeof(Card),
            typeof(string),
            new[] { typeof(NameStyle), typeof(int) },
            "GetName"));
        _getElementValue = binder.BindMethod(GameMethodSpec.Instance(
            typeof(Card),
            typeof(int),
            new[] { typeof(int) },
            "Evalue"));
        _refresh = binder.BindMethod(GameMethodSpec.Instance(
            typeof(Chara),
            typeof(void),
            new[] { typeof(bool) },
            "Refresh"));
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

    public void Refresh(Chara character, bool fullRefresh)
    {
        _refresh.Invoke(character, fullRefresh);
    }
}
