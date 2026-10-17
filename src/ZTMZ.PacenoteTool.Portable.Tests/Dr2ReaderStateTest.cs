using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2ReaderStateTest
{
    private sealed class Reader : DirtGameDataReader
    {
        public Dr2StartLightsSnapshot? Lights;
        public readonly List<GameStateChangeEvent> Changes = [];
        private byte[]? previous;
        public Reader()
        {
            _game = new DirtRally2();
            onGameStateChanged += Changes.Add;
        }
        protected override Dr2StartLightsSnapshot? ReadDr2StartLights() => Lights;
        public void Poll(int? phase)
        {
            Lights = phase.HasValue ? new(0x10000, phase.Value) : null;
            PollDr2Countdown();
        }
        public void Send(float time, float lap = 0, float distance = -2.3893f, float track = 6681.61f)
        {
            var packet = Packet(time, lap, distance, track);
            onNewUdpMessage(previous!, packet);
            previous = packet;
        }
        public int[] Numbers => Changes.Where(e => e.Parameters?.ContainsKey("number") == true)
            .Select(e => (int)e.Parameters["number"]).ToArray();
        public void Silence() => UpdateStateForUdpSilence();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdvancingOnlineUdpBeforeHandbrakeDoesNotAnnounceNumbers(bool memoryAvailable)
    {
        var reader = new Reader();
        reader.Silence();
        for (var i = 0; i < 900; i++)
        {
            reader.Send(19.6f + i / 30f);
            reader.Poll(memoryAvailable ? 0 : null);
        }
        Assert.Empty(reader.Numbers);
        Assert.Equal(GameState.RaceBegin, reader.GameState);
    }

    [Fact]
    public void NativeNumbersContinueWithoutUdpAndGoUsesActualLapStart()
    {
        var reader = new Reader();
        reader.Send(46, 13.2f, 35);
        reader.Send(46); // restart packet, before holding handbrake
        reader.Poll(0);
        reader.Silence();
        Assert.Empty(reader.Numbers);
        Assert.Equal(GameState.RaceBegin, reader.GameState);
        // No further packets during the native countdown. Polling is independent.
        reader.Poll(1);
        reader.Poll(2);
        reader.Send(46); // identical packet: paused
        reader.Poll(2);
        reader.Poll(null);
        reader.Poll(2);
        reader.Poll(3);
        reader.Poll(4);
        reader.Poll(5);
        reader.Poll(6);
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, reader.Numbers);
        reader.Send(139.47037f, .033332f);
        reader.Send(139.50f, .066f, -2.3f);
        var go = Assert.Single(reader.Changes.Where(e => e.NewGameState == GameState.Racing));
        Assert.Equal(GameState.CountingDown, go.LastGameState);
        reader.Poll(1); // stale start state after GO must not play
        Assert.Equal(5, reader.Numbers.Length);
    }

    [Fact]
    public void UdpClockResetOrJumpCannotReplayOrAdvanceNativeNumbers()
    {
        var reader = new Reader();
        reader.Send(20);
        reader.Poll(1);
        foreach (var clock in new[] { 100f, 0f, 30f, 999f })
        {
            reader.Send(clock);
            reader.Poll(1);
        }
        Assert.Equal(new[] { 5 }, reader.Numbers);
        reader.Poll(2);
        Assert.Equal(new[] { 5, 4 }, reader.Numbers);
    }

    [Fact]
    public void AttachmentDuringCountdownOnlyAnnouncesTheCurrentNativeNumber()
    {
        var reader = new Reader();
        reader.Send(23.85f);
        reader.Poll(3);
        reader.Send(23.88f);
        reader.Poll(3);
        Assert.Equal(new[] { 3 }, reader.Numbers);
    }

    [Fact]
    public void LeavingGridSuppressesStaleMemoryAndNewStageCanCountAgain()
    {
        var reader = new Reader();
        reader.Send(90);
        reader.Poll(1);
        reader.Send(0, track: 0);
        reader.Poll(2);
        reader.Send(20.830814f);
        reader.Poll(0);
        reader.Poll(1);
        Assert.Equal(new[] { 5, 5 }, reader.Numbers);
        reader.Uninitialize(new DirtRally2());
        reader.Poll(2);
        Assert.Equal(new[] { 5, 5 }, reader.Numbers);
    }

    [Fact]
    public void EmptyOrTruncatedPacketsDoNotPublishDataOrArmCountdown()
    {
        var reader = new Reader();
        var count = 0;
        reader.onNewGameData += (_, _) => count++;
        reader.onNewUdpMessage(null!, new byte[12]);
        reader.Poll(1);
        Assert.Equal(0, count);
        Assert.Empty(reader.Numbers);
        reader.onNewUdpMessage(null!, new byte[264]);
        Assert.Equal(GameState.Unknown, reader.GameState);
    }

    private static byte[] Packet(float time, float lap, float distance, float track)
    {
        var bytes = new byte[264];
        void Put(int offset, float value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        Put(0, time); Put(4, lap); Put(8, distance); Put(244, track);
        return bytes;
    }
}
