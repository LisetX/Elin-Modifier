public sealed partial class ElinModifierPlugin
{
    private static string BuildNpcMoreInfoHoverDetails(Chara chara) =>
        MoreInfoModule.BuildNpcMoreInfoHoverDetails(chara);
    private static string BuildItemMoreInfoHoverDetails(Thing thing) =>
        MoreInfoModule.BuildItemMoreInfoHoverDetails(thing);
    private static string BuildPlantMoreInfoHoverDetails(Point point) =>
        MoreInfoModule.BuildPlantMoreInfoHoverDetails(point);
    private static string BuildPlantMoreInfoHoverDetails(Thing thing) =>
        MoreInfoModule.BuildPlantMoreInfoHoverDetails(thing);
    private static string ApplyNpcMoreInfoExtraFontSize(string key, string text) =>
        MoreInfoModule.ApplyNpcMoreInfoExtraFontSize(key, text);
    private static string RemoveOriginalNpcMoreInfoBuffLine(Chara chara, string text) =>
        MoreInfoModule.RemoveOriginalNpcMoreInfoBuffLine(chara, text);
    private static string RemoveOriginalNpcMoreInfoFavoriteLine(Chara chara, string text) =>
        MoreInfoModule.RemoveOriginalNpcMoreInfoFavoriteLine(chara, text);
    private static string ColorNpcMoreInfoText(string text, string color) =>
        MoreInfoModule.ColorNpcMoreInfoText(text, color);
    private void InvalidateItemMoreInfoCache() => _modules.MoreInfo.InvalidateItemMoreInfoCache();
    private void InvalidateNpcMoreInfoCaches(bool clearResistDefinitions = false) =>
        _modules.MoreInfo.InvalidateNpcMoreInfoCaches(clearResistDefinitions);
}
