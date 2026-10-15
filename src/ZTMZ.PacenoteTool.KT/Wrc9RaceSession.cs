using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>No finish inference from a frozen or missing timer.</summary>
internal sealed class Wrc9RaceSession
{
    private Wrc9Snapshot? previous;
    private long lastProgress;
    private bool started;
    private long? pendingResultSince;
    public bool IsNewSession { get; private set; }
    public GameStateRaceEnd? FinishState { get; private set; }

    internal Dictionary<string, object> EndParameters(Wrc9Snapshot snapshot)
    {
        var parameters = new Dictionary<string, object>
        {
            [GameStateRaceEndProperty.FINISH_STATE] = FinishState ?? throw new InvalidOperationException("No verified WRC9 result")
        };
        if (FinishState == GameStateRaceEnd.Normal)
        {
            parameters[GameStateRaceEndProperty.FINISH_TIME] = snapshot.ResultMilliseconds!.Value / 1000f;
            parameters[GameStateRaceEndProperty.FINISH_TIME_PANALTY] = snapshot.PenaltyMilliseconds / 1000f;
        }
        return parameters;
    }
    public GameState Update(Wrc9Snapshot? snapshot, long now)
    {
        IsNewSession = false;
        if (snapshot is null) { previous = null; started = false; pendingResultSince = null; FinishState = null; return GameState.Unknown; }
        var sameOwner = previous?.Identity == snapshot.Identity;
        var native = snapshot.NativeResult;
        if (!sameOwner) { started = false; pendingResultSince = null; FinishState = null; }
        // Giveup clears the clock. Check its result before detecting a restart.
        if (sameOwner && FinishState.HasValue && native is { Pending: false })
        {
            previous = snapshot;
            return GameState.RaceEnd;
        }
        if (sameOwner && started && native?.Retired == true &&
            (!pendingResultSince.HasValue || now - pendingResultSince.Value <= 2000))
        {
            FinishState = GameStateRaceEnd.Retired;
            previous = snapshot;
            return GameState.RaceEnd;
        }
        if (native is { Pending: false } && !snapshot.ResultMilliseconds.HasValue)
        {
            // ProcessRaceStats may publish the profile one poll after the
            // failure flag/clock reset. Do not discard this observed attempt.
            previous = snapshot;
            if (started)
            {
                pendingResultSince ??= now;
                // Preserve a started state for the shared end-voice handler,
                // but never keep an incomplete result alive indefinitely.
                if (now - pendingResultSince.Value <= 2000) return GameState.Paused;
                started = false;
            }
            return GameState.Unknown;
        }
        pendingResultSince = null;
        if (!sameOwner || snapshot.RawMilliseconds < previous!.RawMilliseconds || FinishState.HasValue)
        {
            IsNewSession = true;
            lastProgress = now;
            started = false;
            FinishState = null;
        }
        else if (snapshot.RawMilliseconds > previous.RawMilliseconds) lastProgress = now;
        previous = snapshot;
        if (snapshot.ResultMilliseconds.HasValue && native is not { TimerFailed: true } && native is not { ProfileFailed: true })
        {
            FinishState = GameStateRaceEnd.Normal;
            return GameState.RaceEnd;
        }
        if (native is not null && !native.Pending) { IsNewSession = false; return GameState.Unknown; }
        if (snapshot.RawMilliseconds > 0 && native?.Pending == true) started = true;
        if (snapshot.RawMilliseconds <= 0) return GameState.CountingDown;
        return now - lastProgress >= 500 ? GameState.Paused : GameState.Racing;
    }
}
