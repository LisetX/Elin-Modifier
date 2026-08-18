using UnityEngine;

internal sealed class RightClickInterruptModule
{
    internal bool Enabled { get; private set; }

    internal void Load(bool enabled)
    {
        Enabled = enabled;
    }

    internal void Reset()
    {
        Enabled = false;
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        return true;
    }

    internal void Tick()
    {
        if (!Enabled || !Input.GetMouseButtonDown(1))
            return;

        try
        {
            if (GameAccess.Ui.IsPointerOverUi)
                return;

            var pc = GameAccess.Characters.PlayerCharacter;
            var goal = pc?.ai;
            var current = goal?.Current;
            if (pc == null || goal == null || current == null ||
                pc.IsIdle || current.IsIdle || !current.IsRunning)
                return;

            goal.Cancel();
        }
        catch
        {
        }
    }
}
