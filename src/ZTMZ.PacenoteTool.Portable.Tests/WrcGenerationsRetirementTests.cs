using System.Buffers.Binary;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class WrcGenerationsRetirementTests
{
    private sealed class Source : IWrcGenerationsSessionSource
    {
        public WrcGenerationsSessionSnapshot? Value = Active();
        public WrcGenerationsSessionSnapshot? Read() => Value;
        public void Dispose() { }
    }

    private static WrcGenerationsSessionSnapshot Active() => new("stage:car", "Harju", "Puma", "WRC", false, 0, 0,
        new(0x50000, 10000, 0)) { NativeResult = new(0x40000, 0x50000, true, false, false, false, false) };
    private static WrcGenerationsSessionSnapshot Failed() => Active() with
    {
        Timing = null,
        NativeResult = new(0x40000, 0x50000, true, true, true, true, true)
    };
    private static void Send(WrcGenerationsGameDataReader reader, float time, bool finish = false)
    {
        var bytes = new byte[280];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), time);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), time > 0 ? 100 : -1);
        if (finish) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(248), MathF.Round(time * 1000));
        reader.onNewUdpMessage([], bytes);
    }
    private static List<GameStateChangeEvent> Endings(WrcGenerationsGameDataReader reader)
    {
        var events = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += e => { if (e.NewGameState == GameState.RaceEnd) events.Add(e); };
        return events;
    }
    private static void Poll(WrcGenerationsGameDataReader reader)
    {
        reader.CheckTelemetrySilence();
        reader.CheckTelemetrySilence();
    }

    [Fact]
    public void ConfirmedRetirementPublishesOnceWithoutUdpOrFinishTime()
    {
        var source = new Source();
        using var reader = new WrcGenerationsGameDataReader(source, () => 1000);
        var events = Endings(reader);
        Send(reader, 10);
        Poll(reader); // Pause, then retire without another UDP packet.
        source.Value = Failed();
        Poll(reader); Poll(reader);
        var ending = Assert.Single(events);
        Assert.Equal(GameState.Paused, ending.LastGameState);
        Assert.Equal(GameStateRaceEnd.Retired, ending.Parameters[GameStateRaceEndProperty.FINISH_STATE]);
        Assert.False(ending.Parameters.ContainsKey(GameStateRaceEndProperty.FINISH_TIME));
        Assert.Equal(GameState.RaceEnd, reader.GameState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetiredUdpIncludingZeroClockCannotReopenRaceOrPublishNormal(bool zeroClock)
    {
        long now = 0;
        var source = new Source();
        using var reader = new WrcGenerationsGameDataReader(source, () => now);
        var events = Endings(reader);
        Send(reader, 10);
        source.Value = Failed();
        now = 1000;
        Send(reader, zeroClock ? 0 : 10);
        Send(reader, 0);
        now = 2000;
        Send(reader, 10, true);
        Assert.Single(events);
        Assert.Equal(GameStateRaceEnd.Retired, events[0].Parameters[GameStateRaceEndProperty.FINISH_STATE]);
        Assert.Equal(GameState.RaceEnd, reader.GameState);
    }

    [Theory]
    [InlineData("attach-at-result")]
    [InlineData("pre-start")]
    [InlineData("no-timing")]
    [InlineData("unmatched-timing")]
    [InlineData("different-timer")]
    [InlineData("different-race")]
    [InlineData("different-stage")]
    [InlineData("timer-only")]
    [InlineData("profile-only")]
    [InlineData("missing")]
    public void UnverifiedOrStaleFailureDoesNotPublishRetirement(string scenario)
    {
        var source = new Source();
        if (scenario == "attach-at-result") source.Value = Failed();
        if (scenario == "no-timing") source.Value = Active() with { Timing = null };
        if (scenario == "unmatched-timing") source.Value = Active() with { Timing = new(0x50000, 1000, 0) };
        using var reader = new WrcGenerationsGameDataReader(source, () => 1000);
        var events = Endings(reader);
        Send(reader, scenario == "pre-start" ? 0 : 10);
        var failed = Failed();
        source.Value = scenario switch
        {
            "different-timer" => failed with { NativeResult = failed.NativeResult! with { TimerAddress = 0x60000 } },
            "different-race" => failed with { NativeResult = failed.NativeResult! with { RaceAddress = 0x70000 } },
            "different-stage" => failed with { Identity = "other" },
            "timer-only" => failed with { NativeResult = failed.NativeResult! with { ProfileReady = false, ProfileFailed = false } },
            "profile-only" => failed with { NativeResult = failed.NativeResult! with { TimerFailed = false } },
            "missing" => null,
            _ => failed
        };
        Poll(reader);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartDisarmsOldAttemptEvenWhenTimerAddressIsReused(bool silent)
    {
        var source = new Source();
        using var reader = new WrcGenerationsGameDataReader(source, () => 1000);
        var events = Endings(reader);
        Send(reader, 10);
        source.Value = Active() with
        {
            Timing = new(0x50000, 0, 0),
            NativeResult = Active().NativeResult! with { TimerStarted = false }
        };
        if (silent) Poll(reader); else Send(reader, 0);
        source.Value = Failed(); // A subsequent pre-start abandonment is not an observed race.
        Poll(reader);
        Assert.Empty(events);
    }

    [Fact]
    public void NewAttemptAfterRetirementCanRetireAgain()
    {
        long now = 0;
        var source = new Source();
        using var reader = new WrcGenerationsGameDataReader(source, () => now);
        var events = Endings(reader);
        Send(reader, 10);
        source.Value = Failed(); Poll(reader);
        source.Value = Active() with { Timing = new(0x50000, 0, 0) };
        now = 1000; Send(reader, 0);
        source.Value = Active();
        now = 2000; Send(reader, 10);
        source.Value = Failed(); Poll(reader);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void AttachingToFailedResultsDoesNotInventNormalFinishFromUdp()
    {
        var source = new Source { Value = Failed() };
        using var reader = new WrcGenerationsGameDataReader(source);
        var events = Endings(reader);
        Send(reader, 10, true);
        Assert.Empty(events);
        Assert.Equal(GameState.Unknown, reader.GameState);
    }

    [Fact]
    public void ConfirmedNormalFinishIsNotOverwrittenByFailure()
    {
        var source = new Source();
        using var reader = new WrcGenerationsGameDataReader(source);
        var events = Endings(reader);
        Send(reader, 10);
        source.Value = Active() with { ResultReady = true, RaceMilliseconds = 11000, ResultMilliseconds = 11000 };
        Send(reader, 11, true);
        source.Value = Failed(); Poll(reader);
        Assert.Equal(GameStateRaceEnd.Normal, Assert.Single(events).Parameters[GameStateRaceEndProperty.FINISH_STATE]);
    }
}
