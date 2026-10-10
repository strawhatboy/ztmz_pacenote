using ZTMZ.PacenoteTool.Base.Game;
using System.Buffers.Binary;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>No finish inference from a frozen or missing timer.</summary>
internal sealed class Wrc7RaceSession
{
    private Wrc7Snapshot? previous;
    private long lastProgress;
    public bool IsNewSession { get; private set; }
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
        if (snapshot is null) { previous = null; return GameState.Unknown; }
        if (previous is null || previous.Identity != snapshot.Identity || snapshot.RawMilliseconds < previous.RawMilliseconds)
        {
            IsNewSession = true;
            lastProgress = now;
        }
        else if (snapshot.RawMilliseconds > previous.RawMilliseconds) lastProgress = now;
        previous = snapshot;
        if (snapshot.ResultMilliseconds.HasValue) return GameState.RaceEnd;
        if (snapshot.RawMilliseconds <= 0) return GameState.CountingDown;
        return now - lastProgress >= 500 ? GameState.Paused : GameState.Racing;
    }
}
