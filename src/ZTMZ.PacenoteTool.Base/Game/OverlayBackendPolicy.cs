#nullable enable
using System;

namespace ZTMZ.PacenoteTool.Base.Game;

public enum OverlayDisplayBackend
{
    SeparateWindow = 0,
    InGame = 1
}

public sealed record OverlayBackendDecision(
    OverlayDisplayBackend Requested,
    OverlayDisplayBackend Effective,
    string StatusKey,
    bool CanAttachInGame);

/// <summary>
/// Shared by both desktop hosts. Evaluate before loading any overlay DLL into a game.
/// Graphics API auto-detection runs only after this compatibility check succeeds.
/// </summary>
public static class OverlayBackendPolicy
{
    public static OverlayBackendDecision Evaluate(
        OverlayDisplayBackend requested, IGame? game, bool isWindows,
        bool hostIntegrationAvailable = false)
    {
        var steamId = (game as ISteamGameIdentity)?.SteamApplicationId;
        return Evaluate(requested, steamId, game?.Executable, game != null,
            isWindows, hostIntegrationAvailable);
    }

    public static OverlayBackendDecision Evaluate(
        OverlayDisplayBackend requested, uint? steamId, string? executable,
        bool hasGame, bool isWindows, bool hostIntegrationAvailable)
    {
        if (requested != OverlayDisplayBackend.InGame)
            return Window(OverlayDisplayBackend.SeparateWindow, "overlayBackend.windowActive");

        // Identify the provider independently of translated display names. Check this
        // before availability so WRC never becomes eligible when a DLL is installed.
        if (steamId == 1849250 ||
            string.Equals(executable, "WRC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(executable, "WRC.exe", StringComparison.OrdinalIgnoreCase))
            return Window(requested, "overlayBackend.antiCheat");
        if (!isWindows)
            return Window(requested, "overlayBackend.platformUnavailable");
        if (!hostIntegrationAvailable)
            return Window(requested, "overlayBackend.integrationPending");
        if (!hasGame)
            return Window(requested, "overlayBackend.waitingForGame");
        return new(requested, OverlayDisplayBackend.InGame, "overlayBackend.waitingForRenderer", true);
    }

    private static OverlayBackendDecision Window(OverlayDisplayBackend requested, string key) =>
        new(requested, OverlayDisplayBackend.SeparateWindow, key, false);
}
