using System.Buffers.Binary;
using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class WrcGenerationsSessionTests
{
    private static WrcGenerationsSessionCatalog Catalog() => new(
        new byte[4].Concat(Encoding.Unicode.GetBytes("Finland\0Harju\0Ford Puma Rally1\0Reverse\0")).ToArray(),
        "LocalesId = {\n CAR_FORD = 2,\n}",
        "Ford =\n{\n Name = LocalesId.CAR_FORD,\n Category = WrcChampionships.WRC,\n Wheels = { \"ignore\" },\n}");

    [Fact]
    public void InstalledNamesAndCategoryAreReadWithoutEvaluatingLua()
    {
        var catalog = Catalog();
        Assert.Equal("Ford Puma Rally1", catalog.Name(2));
        Assert.Equal("WRC", catalog.Class(2));
        Assert.Empty(catalog.Name(-1)); Assert.Empty(catalog.Name(50000));
        Assert.Throws<InvalidDataException>(() => new WrcGenerationsSessionCatalog(new byte[5], "", ""));
    }

    private sealed class Memory
    {
        internal readonly Dictionary<ulong, byte[]> Blocks = new()
        {
            [0x11c61660] = new byte[8], [0x20000] = new byte[0x1381],
            [0x30000] = new byte[0x420], [0x40000] = new byte[0x20],
            [0x50000] = new byte[0x18], [0x60000] = new byte[0x20]
        };
        internal Memory()
        {
            Put64(0x11c61660, 0, 0x20000);
            Put64(0x20000, 0xd50, 0x40000); Put64(0x20000, 0xd58, 0x50000);
            Put64(0x20000, 0x1378, 0x30000); Put32(0x20000, 0x1374, 2);
            Blocks[0x20000][0x1380] = 1; // Player 0 maps to the second profile.
            Put64(0x30000, 0x210 + 0x30, 0x60000);
            Put32(0x40000, 8, 7); Put32(0x40000, 0xc, 1);
            Put32(0x50000, 8, 7); Put32(0x50000, 0x10, 0);
            Put32(0x60000, 0x14, 2);
            Put32(0x30000, 0x210 + 0x19c, 379457); Put32(0x30000, 0x210 + 0x1a0, 399457);
            Blocks[0x30000][0x210 + 0x1a4] = 1;
        }
        internal void Put32(ulong p, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(Blocks[p].AsSpan(offset), value);
        internal void Put64(ulong p, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Blocks[p].AsSpan(offset), value);
        internal byte[] Read(ulong p, int size)
        {
            foreach (var (address, bytes) in Blocks)
                if (p >= address && p - address + (ulong)size <= (ulong)bytes.Length)
                    return bytes.AsSpan((int)(p - address), size).ToArray();
            throw new IOException("unmapped");
        }
    }

    [Fact]
    public void DecodesPlayerMappingAndPenaltyInclusiveResult()
    {
        var memory = new Memory();
        var session = WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog());
        Assert.NotNull(session);
        Assert.Equal("Finland - Harju", session.Track);
        Assert.Equal("Ford Puma Rally1", session.Car);
        Assert.True(session.ResultReady);
        Assert.Equal(20000, session.ResultMilliseconds - session.RaceMilliseconds);
    }

    [Fact]
    public void RejectsInvalidPlayerIndexAndChangingScene()
    {
        var memory = new Memory();
        memory.Blocks[0x20000][0x1380] = 2;
        Assert.Null(WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog()));
        memory.Blocks[0x20000][0x1380] = 1;
        byte[] Changing(ulong p, int size)
        {
            if (p == 0x20000 + 0xd50) return new byte[8];
            return memory.Read(p, size);
        }
        Assert.Null(WrcGenerationsSessionMemory.Decode(Changing, 0x10000000, Catalog()));
    }

    private sealed class Source : IWrcGenerationsSessionSource
    {
        internal WrcGenerationsSessionSnapshot? Value = new("stage:car", "Finland - Harju", "Ford Puma Rally1", "WRC", false, 0, 0);
        public WrcGenerationsSessionSnapshot? Read() => Value;
        public void Dispose() { }
    }

    private static void Send(WrcGenerationsGameDataReader reader, float time, bool finish = false)
    {
        var packet = new byte[280];
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(4), time);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(8), time > 0 ? 2000 : -1);
        if (finish) BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(248), MathF.Round(time * 1000));
        reader.onNewUdpMessage([], packet);
    }

    [Fact]
    public void IdentityIsReadyBeforeLateAttachmentAndChangesReprepare()
    {
        var source = new Source();
        var reader = new WrcGenerationsGameDataReader(source, () => 1000);
        var tracks = new List<string>();
        reader.onGameStateChanged += e => { if (e.NewGameState == GameState.AdHocRaceBegin) tracks.Add(reader.TrackName); };
        Send(reader, 10);
        source.Value = source.Value! with { Identity = "different", Track = "Finland - Reverse" };
        Send(reader, 0); Send(reader, 10);
        Assert.Contains("Finland - Harju", tracks);
        Assert.Equal("Finland - Reverse", reader.TrackName);
    }

    [Fact]
    public void MetadataRecoveryRepreparesAnInitiallyUnnamedRunningSession()
    {
        long now = 0;
        var source = new Source { Value = null };
        var reader = new WrcGenerationsGameDataReader(source, () => now);
        var tracks = new List<string>();
        reader.onGameStateChanged += e => { if (e.NewGameState == GameState.AdHocRaceBegin) tracks.Add(reader.TrackName); };
        Send(reader, 10); Send(reader, 11);
        source.Value = new("new", "Finland - Harju", "Puma", "WRC", false, 0, 0);
        now = 1000;
        Send(reader, 12);
        Assert.Equal(new[] { "", "Finland - Harju" }, tracks);
    }

    [Fact]
    public async Task WaitsForCurrentResultAndPublishesTotalOnlyOnce()
    {
        var source = new Source();
        var reader = new WrcGenerationsGameDataReader(source);
        var completion = new TaskCompletionSource<GameStateChangeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        reader.onGameStateChanged += e => { if (e.NewGameState == GameState.RaceEnd) { count++; completion.TrySetResult(e); } };
        Send(reader, 100);
        Send(reader, 379.457f, true);
        Assert.False(completion.Task.IsCompleted);
        source.Value = source.Value! with { ResultReady = true, RaceMilliseconds = 379457, ResultMilliseconds = 399457 };
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(399.457f, result.Parameters[GameStateRaceEndProperty.FINISH_TIME]);
        Assert.Equal(20f, result.Parameters[GameStateRaceEndProperty.FINISH_TIME_PANALTY]);
        Send(reader, 379.457f, true);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RestartCancelsPendingFinishAndRejectsPreviousResult()
    {
        var source = new Source();
        var reader = new WrcGenerationsGameDataReader(source);
        var finishes = 0;
        reader.onGameStateChanged += e => { if (e.NewGameState == GameState.RaceEnd) finishes++; };
        Send(reader, 100); Send(reader, 379.457f, true);
        source.Value = source.Value! with { ResultReady = true, RaceMilliseconds = 379457, ResultMilliseconds = 399457 };
        Send(reader, 0);
        await Task.Delay(150);
        Assert.Equal(0, finishes);
        Assert.Equal(GameState.RaceBegin, reader.GameState);
    }

    [Fact]
    public async Task HistoricalSameTimeResultIsNotTreatedAsCurrent()
    {
        var source = new Source { Value = new("stage:car", "Harju", "Puma", "WRC", true, 379457, 399457) };
        var reader = new WrcGenerationsGameDataReader(source);
        var completion = new TaskCompletionSource<GameStateChangeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.onGameStateChanged += e => { if (e.NewGameState == GameState.RaceEnd) completion.TrySetResult(e); };
        Send(reader, 100); Send(reader, 379.457f, true);
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(379.457f, result.Parameters[GameStateRaceEndProperty.FINISH_TIME]);
        Assert.False(result.Parameters.ContainsKey(GameStateRaceEndProperty.FINISH_TIME_PANALTY));
    }
}
