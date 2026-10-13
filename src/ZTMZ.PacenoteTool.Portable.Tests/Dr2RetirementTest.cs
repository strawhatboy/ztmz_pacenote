using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2RetirementTest
{
    private sealed class Reader : DirtGameDataReader
    {
        public Dr2RaceStateSnapshot? Native;
        private byte[]? previous;
        public List<GameStateChangeEvent> Changes { get; } = new();
        public Reader(bool dr1 = false)
        {
            _game = dr1 ? new DirtRally() : new DirtRally2();
            onGameStateChanged += Changes.Add;
        }
        protected override Dr2RaceStateSnapshot? ReadDr2RaceState() => Native;
        public void Poll() => UpdateStateForUdpSilence();
        public void Send(float time = 20, float lap = 10, float distance = 100, float lastLap = 0)
        {
            var packet = new byte[264];
            void Put(int offset, float value) => BitConverter.GetBytes(value).CopyTo(packet, offset);
            Put(0, time); Put(4, lap); Put(8, distance); Put(244, 6681.61f); Put(248, lastLap);
            Put(240, 1); // not a completed DR1 lap
            onNewUdpMessage(previous!, packet);
            previous = packet;
        }
        public void State(Dr2RaceState state, ulong record = 0x20000) => Native = new(record, state);
    }

    [Theory]
    [InlineData(Dr2RaceState.Retired, GameStateRaceEnd.Retired)]
    [InlineData(Dr2RaceState.TerminalDamage, GameStateRaceEnd.Crashed)]
    [InlineData(Dr2RaceState.Disqualified, GameStateRaceEnd.Disqualified)]
    [InlineData(Dr2RaceState.TimedOut, GameStateRaceEnd.TimeOut)]
    public void NativeResultEndsOnceEvenWithoutUdpAndOldPacketsCannotReviveIt(Dr2RaceState state, GameStateRaceEnd expected)
    {
        var reader = new Reader();
        reader.State(Dr2RaceState.Racing);
        reader.Send();
        reader.Poll(); // observe current player's racing record
        reader.State(state);
        reader.Poll(); // UDP has stopped
        reader.Poll();
        reader.Send(21, 11, 101); // delayed racing packet
        reader.Send(0, 0, 0, 200); // stale finish time must not generate Normal
        var end = Assert.Single(reader.Changes.Where(e => e.NewGameState == GameState.RaceEnd));
        Assert.Equal(expected, Assert.IsType<GameStateRaceEnd>(end.Parameters[GameStateRaceEndProperty.FINISH_STATE]));
        Assert.False(end.Parameters.ContainsKey(GameStateRaceEndProperty.FINISH_TIME));
        Assert.Equal(GameState.RaceEnd, reader.GameState);
    }

    [Fact]
    public void PauseAndMissingMemoryDoNotInventRetirement()
    {
        var reader = new Reader();
        reader.State(Dr2RaceState.Racing);
        reader.Send(); reader.Poll(); reader.Send();
        Assert.Equal(GameState.Paused, reader.GameState);
        reader.Native = null;
        for (var i = 0; i < 15; i++) reader.Poll();
        Assert.DoesNotContain(reader.Changes, e => e.NewGameState == GameState.RaceEnd);
        reader.State(Dr2RaceState.Retired); reader.Poll();
        var end = Assert.Single(reader.Changes.Where(e => e.NewGameState == GameState.RaceEnd));
        Assert.Equal(GameState.Paused, end.LastGameState);
    }

    [Theory]
    [InlineData(Dr2RaceState.None)]
    [InlineData(Dr2RaceState.Spectated)]
    [InlineData(Dr2RaceState.Disconnected)]
    [InlineData(Dr2RaceState.Griefing)]
    [InlineData(Dr2RaceState.Eliminated)]
    public void UnhandledStatesDoNotInventAResult(Dr2RaceState state)
    {
        var reader = new Reader();
        reader.State(Dr2RaceState.Racing); reader.Send(); reader.Poll();
        reader.State(state); reader.Poll();
        reader.State(Dr2RaceState.Retired); reader.Poll();
        Assert.DoesNotContain(reader.Changes, e => e.NewGameState == GameState.RaceEnd);
    }

    [Fact]
    public void AttachingToOldResultOrDifferentRecordDoesNotAnnounceRetirement()
    {
        var reader = new Reader();
        reader.State(Dr2RaceState.Retired); reader.Send(); reader.Poll();
        reader.State(Dr2RaceState.Racing); reader.Poll();
        reader.State(Dr2RaceState.Retired, 0x30000); reader.Poll();
        Assert.DoesNotContain(reader.Changes, e => e.NewGameState == GameState.RaceEnd);
    }

    [Fact]
    public void RestartDoesNotRetireAndNewAttemptCanRetire()
    {
        var reader = new Reader();
        reader.State(Dr2RaceState.Racing); reader.Send(); reader.Poll();
        reader.Send(20, 0, -2); reader.Poll();
        reader.Send(26, 0.1f, -2); reader.Poll();
        Assert.DoesNotContain(reader.Changes, e => e.NewGameState == GameState.RaceEnd);
        reader.State(Dr2RaceState.Retired); reader.Poll();
        Assert.Single(reader.Changes.Where(e => e.NewGameState == GameState.RaceEnd));
        reader.State(Dr2RaceState.Racing); reader.Poll();
        reader.Send(40, 0, -2);
        reader.Send(46, 0.1f, -2); reader.Poll();
        reader.State(Dr2RaceState.Retired); reader.Poll();
        Assert.Equal(2, reader.Changes.Count(e => e.NewGameState == GameState.RaceEnd));
    }

    [Fact]
    public void LeavingBeforeGoDoesNotRetire()
    {
        var reader = new Reader();
        reader.State(Dr2RaceState.Racing); reader.Send(20, 0, -2); reader.Poll();
        reader.State(Dr2RaceState.Retired); reader.Poll();
        Assert.DoesNotContain(reader.Changes, e => e.NewGameState == GameState.RaceEnd);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NormalUdpFinishStillPublishesTime(bool nativeAvailable)
    {
        var reader = new Reader();
        if (nativeAvailable) reader.State(Dr2RaceState.Racing);
        reader.Send(); reader.Poll();
        if (nativeAvailable) reader.State(Dr2RaceState.Finished);
        reader.Poll(); reader.Send(0, 0, 0, 123.45f);
        var end = Assert.Single(reader.Changes.Where(e => e.NewGameState == GameState.RaceEnd));
        Assert.Equal(GameStateRaceEnd.Normal, Assert.IsType<GameStateRaceEnd>(end.Parameters[GameStateRaceEndProperty.FINISH_STATE]));
        Assert.Equal(123.45f, end.Parameters[GameStateRaceEndProperty.FINISH_TIME]);
    }

    [Fact]
    public void RetirementOnUdpResetOverridesStaleFinishTimeWithoutWaitingForTimer()
    {
        var reader = new Reader();
        reader.State(Dr2RaceState.Racing); reader.Send(); reader.Poll();
        reader.State(Dr2RaceState.Retired);
        reader.Send(0, 0, 0, 123.45f);
        var end = Assert.Single(reader.Changes.Where(e => e.NewGameState == GameState.RaceEnd));
        Assert.Equal(GameStateRaceEnd.Retired, end.Parameters[GameStateRaceEndProperty.FINISH_STATE]);
        Assert.False(end.Parameters.ContainsKey(GameStateRaceEndProperty.FINISH_TIME));
    }

    [Fact]
    public void LeavingRestartGridDoesNotReusePreviousAttemptRacingObservation()
    {
        var reader = new Reader();
        reader.State(Dr2RaceState.Racing); reader.Send(); reader.Poll();
        reader.Send(20, 0, -2); reader.Poll();
        reader.State(Dr2RaceState.Retired); reader.Poll();
        Assert.DoesNotContain(reader.Changes, e => e.NewGameState == GameState.RaceEnd);
    }

    [Fact]
    public void Dr1DoesNotUseDr2NativeResults()
    {
        var reader = new Reader(dr1: true);
        reader.State(Dr2RaceState.Racing); reader.Send(); reader.Poll();
        reader.State(Dr2RaceState.Retired); reader.Poll();
        Assert.DoesNotContain(reader.Changes, e => e.NewGameState == GameState.RaceEnd);
    }
}
