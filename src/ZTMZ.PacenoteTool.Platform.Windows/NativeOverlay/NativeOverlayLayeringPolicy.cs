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

        // These games can drop the external Direct2D overlay when focus promotes it to TOPMOST.
        return !string.Equals(processName, "acr", StringComparison.OrdinalIgnoreCase)
            && !(processName?.StartsWith("RichardBurnsRally", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
