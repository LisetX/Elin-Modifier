internal sealed class AutomationChopTask : TaskCut
{
    public override bool CanProgress()
    {
        try { return base.CanProgress() && pos != null && pos.HasObj && pos.cell.growth.IsTree; }
        catch { return false; }
    }
}
