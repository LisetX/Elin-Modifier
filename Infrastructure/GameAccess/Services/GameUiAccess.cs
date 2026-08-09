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

        _current = binder.BindStaticValue<UI>(typeof(EClass), GameValueAccess.Read, "ui");
        _scene = binder.BindStaticValue<Scene>(typeof(EClass), GameValueAccess.Read, "scene");
        _screen = binder.BindStaticValue<BaseGameScreen>(typeof(EClass), GameValueAccess.Read, "screen");
        _colors = binder.BindStaticValue<ColorProfile>(typeof(EClass), GameValueAccess.Read, "Colors");
        _isActive = binder.BindInstanceValue<bool>(typeof(UI), GameValueAccess.Read, "IsActive");
        _isPointerOverUi = binder.BindInstanceValue<bool>(typeof(UI), GameValueAccess.Read, "isPointerOverUI");
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

}
