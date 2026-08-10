public sealed partial class ElinModifierPlugin
{
    private void SetAiInstruction(bool enabled)
    {
        if (!_modules.AiInstruction.SetEnabled(enabled))
            return;
        _log = enabled
            ? T("AI指示已开启。", "AI instructions enabled.")
            : T("AI指示已关闭。", "AI instructions disabled.");
    }
}
