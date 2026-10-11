using System;

namespace ZTMZ.PacenoteTool.Codemasters;

/// <summary>
/// A restart sends a reset packet BEFORE the handbrake wait. Only the subsequent
/// advancing UDP stream represents the five-second start sequence. Confirm two
/// adjacent simulation frames before emitting anything; never count loading or
/// the handbrake wait as part of the sequence.
/// </summary>
public sealed class Dr2Countdown
{
    private float? startedAt;
    private int lastNumber;
    private float? candidate;
    private float? resetTime;
    private bool armed;

    public void Begin(float gameTime, bool observedStart)
    {
        startedAt = candidate = null;
        resetTime = float.IsFinite(gameTime) ? gameTime : null;
        armed = observedStart && resetTime.HasValue;
        lastNumber = 0;
    }

    public int? Update(float gameTime)
    {
        if (!float.IsFinite(gameTime)) return null;
        if (startedAt is null)
        {
            if (!armed || resetTime is not { } reset || gameTime <= reset) return null;
            if (candidate is not { } first)
            {
                // A reset packet can retain the previous run's simulation time.
                // A jump is loading/waiting time, not countdown elapsed time.
                candidate = gameTime - reset <= 0.25f ? reset : gameTime;
                if (candidate == gameTime) return null;
                first = candidate.Value;
            }
            if (gameTime <= first) return null;
            if (gameTime - first > 0.25f) { candidate = gameTime; return null; }
            startedAt = first;
        }
        var start = startedAt.Value;
        var elapsed = gameTime - start;
        if (elapsed < 0) { startedAt = null; return null; }
        var number = (int)MathF.Ceiling(5f - elapsed);
        if (number is < 1 or > 5 || number == lastNumber) return null;
        lastNumber = number;
        return number;
    }

    public void Reset() { startedAt = candidate = resetTime = null; armed = false; lastNumber = 0; }
}
