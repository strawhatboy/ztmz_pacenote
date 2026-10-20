using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Core;

internal static class RaceStartPlaybackPolicy
{
    internal static bool ShouldPlay(IGame game, GameStateChangeEvent change, bool enabled) =>
        enabled && change.NewGameState == GameState.RaceBegin &&
        change.LastGameState != GameState.Paused &&
        game is not IGamePacenoteSourcePolicy { UseRaceBeginStartSound: false };

    internal static bool AllowsLiveSound(string token, bool playStartAndEnd, bool playGo) => token switch
    {
        Constants.SYSTEM_START_STAGE => playStartAndEnd,
        Constants.SYSTEM_GO => playGo,
        _ => true
    };
}
