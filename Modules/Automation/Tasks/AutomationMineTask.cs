internal sealed class AutomationMineTask : TaskMine
{
    public override bool CanProgress()
    {
        try { return !isDestroyed && pos != null && pos.HasBlock; }
        catch { return false; }
    }

    public override void OnCreateProgress(Progress_Custom progress)
    {
        base.OnCreateProgress(progress);
        progress.onProgressBegin = delegate
        {
            if (owner.Tool != null)
                owner.Say("mine_start", owner, owner.Tool);
        };
    }
}
