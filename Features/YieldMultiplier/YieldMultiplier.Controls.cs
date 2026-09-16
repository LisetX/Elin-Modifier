public sealed partial class ElinModifierPlugin
{
    private void SetYieldMultiplierEnabled(bool enabled)
    {
        if (!_modules.YieldMultiplier.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("产出倍率调整已开启", "Drop multiplier adjustment enabled")
            : T("产出倍率调整已关闭", "Drop multiplier adjustment disabled");
    }

    private bool TryApplyYieldMultiplierSettings(out string status)
    {
        if (!_modules.YieldMultiplier.TryApplyMultiplierTextFields())
        {
            status = T("倍率输入无效", "Invalid multiplier value");
            return false;
        }

        status = "";
        return true;
    }
}
