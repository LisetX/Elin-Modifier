using System;

internal interface IGameMessageService
{
    string SayRaw(string text);
    string Say(string id);
    string Say(string id, Card card, string? text1 = null, string? text2 = null, string? text3 = null);
    string Say(string id, string? text1, string? text2, string? text3, string? text4);
    string Say(string id, Card card1, Card? card2, string? text1, string? text2);
    string Say(string id, Card card, int value, string? text);
    string Say(string id, int value, string? text1, string? text2);
}

internal sealed class GameMessageService : IGameMessageService
{
    private readonly IBoundGameMethod _sayRaw;
    private readonly IBoundGameMethod _say;
    private readonly IBoundGameMethod _sayCard;
    private readonly IBoundGameMethod _sayText;
    private readonly IBoundGameMethod _sayCards;
    private readonly IBoundGameMethod _sayCardValue;
    private readonly IBoundGameMethod _sayValue;

    internal GameMessageService(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _sayRaw = binder.BindStaticMethod(
            typeof(Msg),
            typeof(string),
            new[] { typeof(string) },
            "SayRaw");
        _say = binder.BindStaticMethod(
            typeof(Msg),
            typeof(string),
            new[] { typeof(string) },
            "Say");
        _sayCard = binder.BindStaticMethod(
            typeof(Msg),
            typeof(string),
            new[] { typeof(string), typeof(Card), typeof(string), typeof(string), typeof(string) },
            "Say");
        _sayText = binder.BindStaticMethod(
            typeof(Msg),
            typeof(string),
            new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) },
            "Say");
        _sayCards = binder.BindStaticMethod(
            typeof(Msg),
            typeof(string),
            new[] { typeof(string), typeof(Card), typeof(Card), typeof(string), typeof(string) },
            "Say");
        _sayCardValue = binder.BindStaticMethod(
            typeof(Msg),
            typeof(string),
            new[] { typeof(string), typeof(Card), typeof(int), typeof(string) },
            "Say");
        _sayValue = binder.BindStaticMethod(
            typeof(Msg),
            typeof(string),
            new[] { typeof(string), typeof(int), typeof(string), typeof(string) },
            "Say");
    }

    public string SayRaw(string text)
    {
        return Invoke(_sayRaw, text);
    }

    public string Say(string id)
    {
        return Invoke(_say, id);
    }

    public string Say(string id, Card card, string? text1 = null, string? text2 = null, string? text3 = null)
    {
        return Invoke(_sayCard, id, card, text1, text2, text3);
    }

    public string Say(string id, string? text1, string? text2, string? text3, string? text4)
    {
        return Invoke(_sayText, id, text1, text2, text3, text4);
    }

    public string Say(string id, Card card1, Card? card2, string? text1, string? text2)
    {
        return Invoke(_sayCards, id, card1, card2, text1, text2);
    }

    public string Say(string id, Card card, int value, string? text)
    {
        return Invoke(_sayCardValue, id, card, value, text);
    }

    public string Say(string id, int value, string? text1, string? text2)
    {
        return Invoke(_sayValue, id, value, text1, text2);
    }

    private static string Invoke(IBoundGameMethod method, params object?[] arguments)
    {
        return method.Invoke(null, arguments) as string ?? "";
    }
}
