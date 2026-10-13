#nullable enable

using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

internal static class RaceEndPlaybackPolicy
{
    public static GameStateRaceEnd GetFinishState(GameStateChangeEvent evt) =>
        evt.Parameters?.TryGetValue(GameStateRaceEndProperty.FINISH_STATE, out var value) == true &&
        value is GameStateRaceEnd result ? result : GameStateRaceEnd.Unknown;

    public static string? GetSoundToken(GameStateChangeEvent evt) => GetFinishState(evt) switch
    {
        GameStateRaceEnd.Normal => Constants.SYSTEM_END_STAGE,
        GameStateRaceEnd.TimeOut or GameStateRaceEnd.Crashed or
        GameStateRaceEnd.Retired or GameStateRaceEnd.Disqualified => Constants.SYSTEM_RETIRED,
        // Missing/unknown results are not evidence of either success or retirement.
        _ => null
    };

    public static bool TryGetReplayFinishTime(GameStateChangeEvent evt, out float finishTime)
    {
        finishTime = 0;
        if (GetFinishState(evt) != GameStateRaceEnd.Normal ||
            evt.Parameters?.TryGetValue(GameStateRaceEndProperty.FINISH_TIME, out var value) != true ||
            value is not float time || !float.IsFinite(time) || time <= 0)
            return false;
        finishTime = time;
        return true;
    }
}
