public sealed partial class ElinModifierPlugin
{
    private void SetMerchantRefreshNoCost(bool enabled)
    {
        if (!_modules.MerchantRefreshNoCost.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("商人刷新商品无消耗已开启", "Free merchant restocking enabled")
            : T("商人刷新商品无消耗已关闭", "Free merchant restocking disabled");
    }
}
