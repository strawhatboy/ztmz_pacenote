using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2ReaderStateTest
{
    private sealed class Reader : DirtGameDataReader
    {
        public Reader() { _game = new DirtRally2(); }
        public void Silence() { for (var i = 0; i < 35; i++) UpdateStateForUdpSilence(); }
    }

    [Fact]
    public void RestartCountsDownAndStartsBeforeCrossingTheLine()
    {
        var reader = new Reader();
        var changes = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += changes.Add;
        byte[]? previous = null;
        void Send(float time, float lap, float distance)
        {
            var packet = Packet(time, lap, distance);
            reader.onNewUdpMessage(previous!, packet);
            previous = packet;
        }
        Send(46, 13.2f, 35);
        Send(46, 0, -2.394f); // restart packet, before holding handbrake
        Assert.DoesNotContain(changes, e => e.NewGameState == GameState.CountingDown);
        reader.Silence();
        Assert.Equal(GameState.RaceBegin, reader.GameState);
        Send(134.41861f, 0, -2.3893f);
        Send(134.45194f, 0, -2.3893f);
        Send(135.45f, 0, -2.3893f);
        Send(136.45f, 0, -2.3893f);
        Send(136.45f, 0, -2.3893f); // paused
        Send(137.45f, 0, -2.3893f);
        Send(138.45f, 0, -2.3893f);
        Send(139.47037f, 0.033332f, -2.3893f);
        Send(139.50f, 0.066f, -2.3f);
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, changes
            .Where(e => e.Parameters?.ContainsKey("number") == true)
            .Select(e => (int)e.Parameters["number"]));
        Assert.Single(changes.Where(e => e.NewGameState == GameState.Racing));
        Assert.Equal(GameState.CountingDown, changes.Last(e => e.NewGameState == GameState.Racing).LastGameState);
    }

    [Fact]
    public void NewRaceAfterWaitingForFirstPacketCountsDown()
    {
        var reader = new Reader();
        var numbers = new List<int>();
        reader.onGameStateChanged += e =>
        {
            if (e.Parameters?.TryGetValue("number", out var number) == true)
                numbers.Add((int)number);
        };
        reader.Silence(); // listener is ready during loading / handbrake wait
        Assert.Empty(numbers);
        byte[]? previous = null;
        foreach (var time in new[] { 20.830814f, 20.848988f, 21.85f, 22.85f, 23.85f, 24.85f })
        {
            var packet = Packet(time, 0, -2.389556f);
            reader.onNewUdpMessage(previous!, packet);
            previous = packet;
        }
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, numbers);
        reader.onNewUdpMessage(previous!, Packet(25.86649f, .016666f, -2.389556f));
        Assert.Equal(GameState.Racing, reader.GameState);
    }

    [Fact]
    public void ImmediateAttachmentDoesNotInventCountdown()
    {
        var reader = new Reader();
        var changes = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += changes.Add;
        var first = Packet(23.85f, 0, -2.389556f);
        reader.onNewUdpMessage(null!, first);
        reader.onNewUdpMessage(first, Packet(23.88f, 0, -2.389556f));
        Assert.DoesNotContain(changes, e => e.NewGameState == GameState.CountingDown);
    }

    [Fact]
    public void LeavingGridForMenuDoesNotKeepOldCountdown()
    {
        var reader = new Reader();
        var numbers = new List<int>();
        reader.onGameStateChanged += e =>
        {
            if (e.Parameters?.TryGetValue("number", out var number) == true)
                numbers.Add((int)number);
        };
        var grid = Packet(90, 0, -2);
        reader.onNewUdpMessage(null!, grid);
        var menu = new byte[264];
        reader.onNewUdpMessage(grid, menu);
        var newGrid = Packet(20.830814f, 0, -2);
        reader.onNewUdpMessage(menu, newGrid);
        reader.onNewUdpMessage(newGrid, Packet(20.848988f, 0, -2));
        Assert.Equal(new[] { 5 }, numbers);
    }

    [Fact]
    public void EmptyOrTruncatedPacketsDoNotFinishARaceOrPublishData()
    {
        var reader = new Reader();
        var count = 0;
        reader.onNewGameData += (_, _) => count++;
        reader.onNewUdpMessage(null!, new byte[12]);
        Assert.Equal(0, count);
        reader.onNewUdpMessage(null!, new byte[264]);
        Assert.Equal(GameState.Unknown, reader.GameState);
    }

    private static byte[] Packet(float time, float lap, float distance)
    {
        var bytes = new byte[264];
        void Put(int offset, float value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        Put(0, time); Put(4, lap); Put(8, distance); Put(244, 6681.61f);
        return bytes;
    }
}
