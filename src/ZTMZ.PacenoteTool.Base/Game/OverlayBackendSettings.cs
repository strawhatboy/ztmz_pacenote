#nullable enable
using System;

namespace ZTMZ.PacenoteTool.Base.Game;

public static class OverlayBackendSettings
{
    public static event Action? Changed;

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

    // The current DLL has no desktop-host control/status connection yet. Do not
    // equate the presence of the experimental DLL with a working integration.
    public static OverlayBackendDecision GetDecision(IGame? game) =>
        OverlayBackendPolicy.Evaluate(Requested, game, OperatingSystem.IsWindows());
}
