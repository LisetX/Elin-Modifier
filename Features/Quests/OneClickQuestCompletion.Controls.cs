public sealed partial class ElinModifierPlugin
{
    private void SetOneClickQuestCompletion(bool enabled)
    {
        if (!_modules.OneClickQuestCompletion.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("一键完成委托已开启", "One-click quest completion enabled")
            : T("一键完成委托已关闭", "One-click quest completion disabled");
    }
}
