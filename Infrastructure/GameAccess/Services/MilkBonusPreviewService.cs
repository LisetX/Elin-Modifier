using System;
using System.Collections.Generic;

internal sealed class MilkBonusPreviewService : IMilkBonusPreviewService
{
    private readonly IBoundGameValue<string> _cardId;
    private readonly IBoundGameValue<string> _referenceCharacterId;
    private readonly IBoundGameValue<int> _enchantLevel;
    private readonly IBoundGameValue<ElementContainerCard> _elements;
    private readonly IBoundGameValue<Chara> _playerCharacter;
    private readonly IBoundGameValue<Game> _game;
    private readonly IBoundGameValue<CardManager> _cards;
    private readonly IBoundGameValue<int> _nextCardUid;
    private readonly IBoundGameValue<System.Random> _random;
    private readonly IBoundGameValue<byte[]> _randomBytes;
    private readonly IBoundGameValue<int> _elementValueWithoutLink;
    private readonly IBoundGameValue<string> _elementName;
    private readonly IBoundGameMethod _setSeed;
    private readonly IBoundGameMethod _createCharacter;
    private readonly IBoundGameMethod _setLevel;
    private readonly IBoundGameMethod _getElementValue;
    private readonly IBoundGameMethod _listBestAttributes;
    private readonly IBoundGameMethod _listBestSkills;
    private readonly IBoundGameMethod _getElementIcon;

    internal MilkBonusPreviewService(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _cardId = binder.BindInstanceValue<string>(typeof(Card), GameValueAccess.Read, "id");
        _referenceCharacterId = binder.BindInstanceValue<string>(typeof(Card), GameValueAccess.Read, "c_idRefCard");
        _enchantLevel = binder.BindInstanceValue<int>(typeof(Card), GameValueAccess.Read, "encLV");
        _elements = binder.BindInstanceValue<ElementContainerCard>(typeof(Card), GameValueAccess.Read, "elements");
        _playerCharacter = binder.BindStaticValue<Chara>(typeof(EClass), GameValueAccess.Read, "pc");
        _game = binder.BindStaticValue<Game>(typeof(EClass), GameValueAccess.Read, "game");
        _cards = binder.BindInstanceValue<CardManager>(typeof(Game), GameValueAccess.Read, "cards");
        _nextCardUid = binder.BindInstanceValue<int>(typeof(CardManager), GameValueAccess.ReadWrite, "uidNext");
        _random = binder.BindStaticValue<System.Random>(typeof(Rand), GameValueAccess.ReadWrite, "_random");
        _randomBytes = binder.BindStaticValue<byte[]>(typeof(Rand), GameValueAccess.ReadWrite, "bytes");
        _elementValueWithoutLink = binder.BindInstanceValue<int>(typeof(Element), GameValueAccess.Read, "ValueWithoutLink");
        _elementName = binder.BindInstanceValue<string>(typeof(Element), GameValueAccess.Read, "Name");
        _setSeed = binder.BindStaticMethod(typeof(Rand), typeof(void), new[] { typeof(int) }, "SetSeed");
        _createCharacter = binder.BindStaticMethod(typeof(CharaGen), typeof(Chara), new[] { typeof(string), typeof(int) }, "Create");
        _setLevel = binder.BindInstanceMethod(typeof(Card), typeof(Card), new[] { typeof(int) }, "SetLv");
        _getElementValue = binder.BindInstanceMethod(typeof(Card), typeof(int), new[] { typeof(int) }, "Evalue");
        _listBestAttributes = binder.BindInstanceMethod(typeof(ElementContainer), typeof(List<Element>), Type.EmptyTypes, "ListBestAttributes");
        _listBestSkills = binder.BindInstanceMethod(typeof(ElementContainer), typeof(List<Element>), Type.EmptyTypes, "ListBestSkills");
        _getElementIcon = binder.BindInstanceMethod(typeof(Element), typeof(UnityEngine.Sprite), new[] { typeof(string) }, "GetIcon");
    }

    public IReadOnlyList<MilkBonusPreviewEntry>? Calculate(Thing milk)
    {
        if (milk == null || !_cardId.TryGet(milk, out var id) || !string.Equals(id, "_milk", StringComparison.Ordinal))
            return null;

        var result = new List<MilkBonusPreviewEntry>();
        if (!_playerCharacter.TryGet(null, out var player) || player == null)
            return result;
        if (!_referenceCharacterId.TryGet(milk, out var referenceId) || string.IsNullOrEmpty(referenceId))
            return result;
        if (!_enchantLevel.TryGet(milk, out var enchantLevel))
            return result;
        if (!TryCreateSourceCharacter(referenceId, enchantLevel, player, out var source) || source == null)
            return result;
        if (!_elements.TryGet(source, out var sourceElements) || sourceElements == null)
            return result;
        var attributes = GameAccessServiceHelpers.InvokeReference<List<Element>>(_listBestAttributes, sourceElements);
        var skills = GameAccessServiceHelpers.InvokeReference<List<Element>>(_listBestSkills, sourceElements);
        AppendBonuses(result, attributes);
        AppendBonuses(result, skills);
        return result;
    }

    private bool TryCreateSourceCharacter(string referenceId, int enchantLevel, Chara player, out Chara? source)
    {
        source = null;
        if (!_game.TryGet(null, out var game) || game == null ||
            !_cards.TryGet(game, out var cards) || cards == null ||
            !_nextCardUid.TryGet(cards, out var savedNextUid) ||
            !_random.TryGet(null, out var savedRandom) ||
            !_randomBytes.TryGet(null, out var savedBytes))
            return false;

        var savedBytesCopy = savedBytes == null ? null : (byte[])savedBytes.Clone();
        try
        {
            if (!_nextCardUid.TrySet(cards, 1) || !_setSeed.TryInvoke(null, new object?[] { 1 }, out _))
                return false;

            source = GameAccessServiceHelpers.InvokeReference<Chara>(_createCharacter, null, referenceId, -1);
            if (source == null)
                return false;

            var levelCap = 20 + GameAccessServiceHelpers.InvokeValue<int>(_getElementValue, player, 237);
            var level = Math.Max(1, Math.Min(5 + enchantLevel * 5, levelCap));
            _setLevel.Invoke(source, level);
            return true;
        }
        finally
        {
            _nextCardUid.TrySet(cards, savedNextUid);
            _random.TrySet(null, savedRandom);
            if (savedBytes != null && savedBytesCopy != null)
                Array.Copy(savedBytesCopy, savedBytes, Math.Min(savedBytesCopy.Length, savedBytes.Length));
            _randomBytes.TrySet(null, savedBytes);
        }
    }

    private void AppendBonuses(List<MilkBonusPreviewEntry> result, List<Element>? sourceElements)
    {
        if (sourceElements == null)
            return;

        var divisor = 100;
        foreach (var sourceElement in sourceElements)
        {
            AppendBonus(result, sourceElement, divisor);
            divisor += 50;
        }
    }

    private void AppendBonus(List<MilkBonusPreviewEntry> result, Element sourceElement, int divisor)
    {
        var sourceValue = GetInt(_elementValueWithoutLink, sourceElement);
        var value = sourceValue * 100.0 / divisor / 2.0;
        if (value < 0.5)
            return;

        var name = _elementName.TryGet(sourceElement, out var resolvedName) ? resolvedName : "";
        if (!string.IsNullOrEmpty(name))
        {
            var icon = GameAccessServiceHelpers.InvokeReference<UnityEngine.Sprite>(_getElementIcon, sourceElement, "");
            result.Add(new MilkBonusPreviewEntry(name, value, icon));
        }
    }

    private static int GetInt(IBoundGameValue<int> binding, object instance)
    {
        return binding.TryGet(instance, out var value) ? value : 0;
    }
}
