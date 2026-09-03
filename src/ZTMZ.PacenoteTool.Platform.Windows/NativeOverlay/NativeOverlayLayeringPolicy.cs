using System;

namespace ZTMZ.PacenoteTool.UI.Windows;

public static class NativeOverlayLayeringPolicy
{
    public static bool ShouldPromoteToTopMost(string? processName, bool isGameWindowForeground)
    {
        if (!isGameWindowForeground)
        {
            return false;
        }

        // Preserve ACR's existing opt-out until its focus behavior is revalidated.
        return !string.Equals(processName, "acr", StringComparison.OrdinalIgnoreCase);
    }
}
