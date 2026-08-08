using System;

internal static class MoongateContainerTransferPolicy
{
    internal const int RestoredLockLevel = 0;

    internal static bool ShouldTransfer(bool isContainer, string alternateName)
    {
        return isContainer &&
               !string.Equals(alternateName, "DebugContainer", StringComparison.Ordinal);
    }
}
