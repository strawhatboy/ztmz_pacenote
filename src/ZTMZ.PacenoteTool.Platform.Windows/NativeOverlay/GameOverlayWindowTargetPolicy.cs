using System;

namespace ZTMZ.PacenoteTool.UI.Windows;

public static class GameOverlayWindowTargetPolicy
{
    public static IntPtr SelectTarget(
        IntPtr foregroundWindow,
        bool foregroundWindowBelongsToGame,
        IntPtr mainWindow,
        bool mainWindowBelongsToGame,
        IntPtr lastKnownWindow,
        bool lastKnownWindowBelongsToGame)
    {
        if (foregroundWindow != IntPtr.Zero && foregroundWindowBelongsToGame)
        {
            return foregroundWindow;
        }

        if (mainWindow != IntPtr.Zero && mainWindowBelongsToGame)
        {
            return mainWindow;
        }

        return lastKnownWindow != IntPtr.Zero && lastKnownWindowBelongsToGame
            ? lastKnownWindow
            : IntPtr.Zero;
    }

    public static bool NeedsReattachment(IntPtr attachedWindow, IntPtr targetWindow)
    {
        return targetWindow != IntPtr.Zero && attachedWindow != targetWindow;
    }
}
