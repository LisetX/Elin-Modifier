using System;

internal sealed class GameUiAccess : IGameUiAccess
{
    private readonly IBoundGameValue<UI> _current;
    private readonly IBoundGameValue<Scene> _scene;
    private readonly IBoundGameValue<BaseGameScreen> _screen;
    private readonly IBoundGameValue<ColorProfile> _colors;
    private readonly IBoundGameValue<bool> _isActive;
    private readonly IBoundGameValue<bool> _isPointerOverUi;

    internal GameUiAccess(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        _current = binder.BindValue<UI>(GameValueSpec.Static(
            typeof(EClass),
            typeof(UI),
            GameValueAccess.Read,
            "ui"));
        _scene = BindRoot<Scene>(binder, "scene");
        _screen = BindRoot<BaseGameScreen>(binder, "screen");
        _colors = BindRoot<ColorProfile>(binder, "Colors");
        _isActive = binder.BindValue<bool>(GameValueSpec.Instance(
            typeof(UI),
            typeof(bool),
            GameValueAccess.Read,
            "IsActive"));
        _isPointerOverUi = binder.BindValue<bool>(GameValueSpec.Instance(
            typeof(UI),
            typeof(bool),
            GameValueAccess.Read,
            "isPointerOverUI"));
    }

    public UI? Root => GameAccessServiceHelpers.GetReference(_current, null);
    public Scene? Scene => GameAccessServiceHelpers.GetReference(_scene, null);
    public BaseGameScreen? Screen => GameAccessServiceHelpers.GetReference(_screen, null);
    public ColorProfile? Colors => GameAccessServiceHelpers.GetReference(_colors, null);

    public bool IsActive
    {
        get
        {
            var current = Root;
            return current != null && GameAccessServiceHelpers.GetValueOrDefault(_isActive, current, false);
        }
    }

    public bool IsPointerOverUi
    {
        get
        {
            var current = Root;
            return current != null && GameAccessServiceHelpers.GetValueOrDefault(_isPointerOverUi, current, false);
        }
    }

    private static IBoundGameValue<T> BindRoot<T>(IGameMemberBinder binder, string memberName)
        where T : class
    {
        return binder.BindValue<T>(GameValueSpec.Static(
            typeof(EClass),
            typeof(T),
            GameValueAccess.Read,
            memberName));
    }
}
