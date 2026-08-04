namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>
/// Identifies game states where stage telemetry and overlays should remain active.
/// </summary>
public static class GameStateStageActivity
{
    public static bool IsActive(GameState state)
    {
        return state is GameState.RaceBegin
            or GameState.AdHocRaceBegin
            or GameState.CountingDown
            or GameState.Racing
            or GameState.Paused;
    }
}
