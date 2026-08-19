using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    internal EmTooltipVisualStyle ResolveModuleEmTooltipVisualStyle()
    {
        var lightTheme = _uiStyleIndex == 5;
        var accent = _uiStyleIndex >= 0 && _uiStyleIndex < UiStyleColors.Length
            ? UiStyleColors[_uiStyleIndex]
            : Color.white;
        var windowColor = lightTheme
            ? new Color(0.88f, 0.88f, 0.85f, 1f)
            : Color.Lerp(new Color(0.035f, 0.04f, 0.047f, 1f), accent, 0.10f);
        var headerColor = lightTheme
            ? new Color(0.98f, 0.98f, 0.94f, 1f)
            : Color.Lerp(new Color(0.085f, 0.09f, 0.105f, 1f), accent, 0.20f);
        var background = lightTheme
            ? Color.Lerp(windowColor, headerColor, 0.22f)
            : Color.Lerp(windowColor, headerColor, 0.34f);
        background.a = 0.98f;
        return new EmTooltipVisualStyle(
            _uiRoundedCorners,
            _uiRoundedCorners ? GetLGuiRoundedSprite(LGuiRoundedImageStyle.Standard) : null,
            background,
            GetActiveUiTextColor(),
            accent,
            lightTheme);
    }
}
