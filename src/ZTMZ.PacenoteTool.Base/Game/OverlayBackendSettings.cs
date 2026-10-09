#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.Game;

public static class OverlayBackendSettings
{
    public static event Action? Changed;
    public static Func<Func<bool>, Task<bool>>? ConfirmSelection { get; set; }
    private static long _selectionRevision;

    public static async Task SelectAsync(int selectedIndex)
    {
        if (selectedIndex is < 0 or > 1) return;
        var revision = Interlocked.Increment(ref _selectionRevision);
        if (selectedIndex == (int)Requested) return;
        bool IsCurrent() => revision == Interlocked.Read(ref _selectionRevision);
        if (selectedIndex == (int)OverlayDisplayBackend.InGame &&
            !await InGameOverlayConsent.EnsureAsync(IsCurrent, ConfirmSelection)) return;
        if (IsCurrent()) SetRequested(selectedIndex);
    }

    public static OverlayDisplayBackend Requested =>
        (OverlayDisplayBackend)Config.Instance.OverlayDisplayBackend;

    public static void SetRequested(int selectedIndex)
    {
        if (selectedIndex is < 0 or > 1 || selectedIndex == Config.Instance.OverlayDisplayBackend)
            return;
        Config.Instance.OverlayDisplayBackend = selectedIndex;
        Config.Instance.SaveUserConfig();
        Changed?.Invoke();
    }

    // Without a live session, never equate a saved preference with active rendering.
    public static OverlayBackendDecision GetDecision(IGame? game) =>
        OverlayBackendPolicy.Evaluate(Requested, game, OperatingSystem.IsWindows());
}
