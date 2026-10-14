using ZTMZ.PacenoteTool.Base.Game;
using System.Buffers.Binary;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>No finish inference from a frozen or missing timer.</summary>
internal sealed class Wrc7RaceSession
{
    private Wrc7Snapshot? previous;
    private long lastProgress;
    private bool started;
    private long? pendingResultSince;
    public bool IsNewSession { get; private set; }
    public GameStateRaceEnd? FinishState { get; private set; }

    internal Dictionary<string, object> EndParameters(Wrc7Snapshot snapshot)
    {
        var parameters = new Dictionary<string, object>
        {
            [GameStateRaceEndProperty.FINISH_STATE] = FinishState ?? throw new InvalidOperationException("No verified WRC7 result")
        };
        if (FinishState == GameStateRaceEnd.Normal)
        {
            parameters[GameStateRaceEndProperty.FINISH_TIME] = snapshot.ResultMilliseconds!.Value / 1000f;
            parameters[GameStateRaceEndProperty.FINISH_TIME_PANALTY] = snapshot.PenaltyMilliseconds / 1000f;
        }
        return parameters;
    }
    internal static int? VerifiedResult(int raw, int penalty, ReadOnlySpan<byte> finish, ReadOnlySpan<byte> profile)
    {
        // CrossEndLine (0x2ca9c0) sets +8c8 and clears +8ca. Require the
        // processed player-profile result as well, including exactly its penalty.
        if (finish.Length < 3 || profile.Length < 14 || finish[0] != 1 || finish[2] != 0 ||
            profile[12] != 1 || profile[13] != 0 || raw <= 0 || penalty < 0 ||
            (long)raw + penalty > 24 * 3600 * 1000) return null;
        var recordedRaw = BinaryPrimitives.ReadInt32LittleEndian(profile);
        var total = BinaryPrimitives.ReadInt32LittleEndian(profile[4..]);
        return recordedRaw == raw && total == raw + penalty ? total : null;
    }
    public GameState Update(Wrc7Snapshot? snapshot, long now)
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
        if (snapshot.ResultMilliseconds.HasValue && native is not { Failed: true } && native is not { ProfileFailed: true })
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
