#nullable enable
using System;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.Game;

public static class InGameOverlayConsent
{
    public static async Task<bool> EnsureAsync(Func<bool> isCurrent,
        Func<Func<bool>, Task<bool>>? showWarning)
    {
        using var lease = await InjectionConfirmationQueue.EnterAsync();
        if (!isCurrent()) return false;
        if (showWarning == null) return false; // A headless host cannot grant consent.
        var accepted = await showWarning(isCurrent);
        // The caller persists the display-mode selection only after confirmation.
        // Do not maintain a second per-game permission that can contradict it.
        return isCurrent() && accepted;
    }
}
