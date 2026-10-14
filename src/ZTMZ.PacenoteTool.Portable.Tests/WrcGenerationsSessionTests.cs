using System.Buffers.Binary;
using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class WrcGenerationsSessionTests
{
    [Fact]
    public void ShiftWarningUsesSelectedCarHudConfigAndClearsWhenUnavailable()
    {
        var memory = new Memory();
        memory.Blocks[0x601f8] = new byte[8];
        memory.Put32(0x601f8, 0, 8500);
        memory.Put32(0x601f8, 4, 7500);
        var snapshot = WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog());
        Assert.Equal(7500f, snapshot!.ShiftWarningRpm);
        long now = 0;
        var source = new Source { Value = snapshot };
        using var reader = new WrcGenerationsGameDataReader(source, () => now);
        Send(reader, 10);
        Assert.True(reader.CurrentGameData.ShiftLightsRPMValid);
        Assert.Equal(7500, reader.CurrentGameData.ShiftLightsRPMEnd);
        source.Value = null;
        now = 1000;
        Send(reader, 11);
        Assert.False(reader.CurrentGameData.ShiftLightsRPMValid);
        memory.Put32(0x601f8, 4, 9000);
        Assert.Null(WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog())!.ShiftWarningRpm);
    }

    [Fact]
    public async Task MetadataFailureRetriesAfterBackoffAndRecoversWithoutGameRestart()
    {
        long now = 0;
        using var reader = new WrcGenerationsGameDataReader(() => now);
        var attempts = 0;
        IWrcGenerationsSessionSource Create()
        {
            if (++attempts == 1) throw new IOException("Scene still loading");
            return new Source();
        }
        await reader.DiscoverMetadataAsync(Create);
        await reader.DiscoverMetadataAsync(Create);
        Assert.Equal(1, attempts);
        Send(reader, 10); // Failure still permits UDP.
        Assert.Equal(10, reader.CurrentGameData.LapTime);
        now = 5000;
        await reader.DiscoverMetadataAsync(Create);
        Send(reader, 11);
        Assert.Equal(2, attempts);
        Assert.NotEmpty(reader.TrackName);
        await reader.DiscoverMetadataAsync(Create);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void HandBrakeIsFreshPerPacketAndNeverRetainedAfterUnavailableRead()
    {
        var source = new Source { HandBrake = 0 };
        using var reader = new WrcGenerationsGameDataReader(source, () => 0);
        Send(reader, 10);
        Assert.True(reader.CurrentGameData.HandBrakeValid);
        source.HandBrake = 0.75f;
        Send(reader, 10.01f);
        Assert.Equal(0.75f, reader.CurrentGameData.HandBrake);
        source.HandBrake = null;
        Send(reader, 10.02f);
        Assert.False(reader.CurrentGameData.HandBrakeValid);
        Assert.Equal(0, reader.CurrentGameData.HandBrake);
    }

    [Fact]
    public void HandBrakeMemoryRequiresMatchingPlayerAndReciprocalOwnership()
    {
        var memory = new Memory();
        foreach (var (address, size) in new (ulong, int)[] {
            (0x11d61938, 8), (0x70000, 0x900), (0x80000, 0xe0),
            (0x90000, 0x80), (0xa0000, 0xb0), (0xb0000, 0x190) })
            memory.Blocks[address] = new byte[size];
        memory.Put64(0x20000, 0x48, 0x70000);
        memory.Put64(0x11d61938, 0, 0x70000);
        memory.Put32(0x20000, 0x12d0, 7);
        // Player mapping in the fixture is 1; timer 0 is deliberately missing.
        memory.Put64(0x70000, 0x4f0, 0x80000);
        memory.Put64(0x80000, 0xa0, 0x90000);
        memory.Put64(0x90000, 0x38, 0x80000);
        memory.Put64(0x90000, 0x70, 0xa0000);
        memory.Put64(0xa0000, 0xa0, 0xb0000);
        memory.Put32(0xb0000, 0x178, BitConverter.SingleToInt32Bits(0.8f));
        Assert.Equal(0.8f, WrcGenerationsSessionMemory.ReadHandBrake(memory.Read, 0x10000000));
        memory.Put32(0xb0000, 0x178, BitConverter.SingleToInt32Bits(float.NaN));
        Assert.Null(WrcGenerationsSessionMemory.ReadHandBrake(memory.Read, 0x10000000));
        memory.Put32(0xb0000, 0x178, 0);
        memory.Put64(0x90000, 0x38, 0x80010);
        Assert.Null(WrcGenerationsSessionMemory.ReadHandBrake(memory.Read, 0x10000000));
        memory.Put64(0x90000, 0x38, 0x80000);
        byte[] Torn(ulong address, int size)
        {
            var bytes = memory.Read(address, size);
            if (address == 0xb0178) memory.Put64(0x70000, 0x4f0, 0);
            return bytes;
        }
        Assert.Null(WrcGenerationsSessionMemory.ReadHandBrake(Torn, 0x10000000));
    }
    private static WrcGenerationsSessionCatalog Catalog() => new(
        new byte[4].Concat(Encoding.Unicode.GetBytes("Finland\0Harju\0Ford Puma Rally1\0Reverse\0")).ToArray(),
        "LocalesId = {\n CAR_FORD = 2,\n}",
        "Ford =\n{\n Name = LocalesId.CAR_FORD,\n Category = WrcChampionships.WRC,\n Wheels = { \"ignore\" },\n}");

    [Fact]
    public void NativeFailureIsDecodedDespiteZeroResultTimesAndRequiresOwnership()
    {
        var memory = ResultMemory();
        var result = WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog());
        Assert.True(result!.NativeResult!.Retired);
        Assert.False(result.ResultReady);
        Assert.Equal(0, result.RaceMilliseconds);
        memory.Put64(0x90000, 0x38, 0x80010);
        Assert.Null(WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog())!.NativeResult);
    }

    [Fact]
    public void NativeFailureRejectsTornFlagsAndUnverifiedPlayerSelection()
    {
        var memory = ResultMemory();
        byte[] Torn(ulong address, int size)
        {
            var data = memory.Read(address, size);
            if (address == 0x8009c) memory.Blocks[0x80000][0x9f] = 0;
            return data;
        }
        Assert.Null(WrcGenerationsSessionMemory.Decode(Torn, 0x10000000, Catalog())!.NativeResult);
        memory = ResultMemory();
        memory.Put32(0x20000, 0x12d0, 5);
        Assert.Null(WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog())!.NativeResult);
    }

    [Fact]
    public void FailedProfileIsNotNormalEvenIfItContainsPositiveTimes()
    {
        var memory = ResultMemory();
        memory.Put32(0x30000, 0x210 + 0x19c, 10000);
        memory.Put32(0x30000, 0x210 + 0x1a0, 10000);
        Assert.False(WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog())!.ResultReady);
    }

    [Fact]
    public void NativeFailureUsesPlayerMappedTimerInModeSeven()
    {
        var memory = ResultMemory();
        memory.Put32(0x20000, 0x12d0, 7);
        memory.Put64(0x70000, 0x4e8, 0);
        memory.Put64(0x70000, 0x4f0, 0x80000);
        Assert.True(WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog())!.NativeResult!.Retired);
    }

    private static Memory ResultMemory()
    {
        var memory = new Memory();
        foreach (var (address, size) in new (ulong, int)[] {
            (0x11d61938, 8), (0x70000, 0x900), (0x80000, 0xe0), (0x90000, 0x80) })
            memory.Blocks[address] = new byte[size];
        memory.Put64(0x20000, 0x48, 0x70000);
        memory.Put64(0x11d61938, 0, 0x70000);
        memory.Put64(0x70000, 0x4e8, 0x80000);
        memory.Put64(0x80000, 0xa0, 0x90000);
        memory.Put64(0x90000, 0x38, 0x80000);
        memory.Blocks[0x80000][0x9c] = memory.Blocks[0x80000][0x9d] = memory.Blocks[0x80000][0x9f] = 1;
        memory.Put32(0x30000, 0x210 + 0x19c, 0);
        memory.Put32(0x30000, 0x210 + 0x1a0, 0);
        memory.Blocks[0x30000][0x210 + 0x1a5] = 1;
        return memory;
    }

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
    public void DecodesLivePenaltyAndRejectsReplacedRace()
    {
        var memory = new Memory();
        foreach (var (address, size) in new (ulong, int)[] {
            (0x11d61938, 8), (0x11bbf030, 8), (0x70000, 0x900),
            (0x80000, 0xe0), (0x90000, 0x250), (0xa0000, 0x300), (0xb0000, 128) })
            memory.Blocks[address] = new byte[size];
        memory.Put64(0x20000, 0x48, 0x70000);
        memory.Put64(0x11d61938, 0, 0x70000);
        memory.Put64(0x11bbf030, 0, 0xb0000);
        memory.Put64(0x70000, 0x4e8, 0x80000);
        memory.Put64(0x70000, 0x898, 0xa0000);
        memory.Put32(0x80000, 0x3c, 1);
        memory.Put64(0x80000, 0x40, 0x90000);
        void Protected(ulong block, int offset, uint index, int value)
        {
            memory.Put32(block, offset + 8, (int)index);
            var mask = unchecked(((uint)(block + (ulong)offset + 8) + index) * 0x159a55e5u + 0x159ae628u);
            memory.Put32(0xb0000, (int)index * 4, unchecked((int)((uint)value ^ (mask == 0 ? 0xb10be924u : mask))));
        }
        Protected(0x90000, 0x10, 1, 1000);
        Protected(0x90000, 0x20, 2, 0);
        Protected(0xa0000, 0x2f0, 3, 49530);
        Protected(0x80000, 0xc8, 4, 10000);
        var session = WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog());
        Assert.Equal(new WrcGenerationsTimingSnapshot(0x80000, 48530, 10000), session!.Timing);
        memory.Put64(0x11d61938, 0, 0);
        session = WrcGenerationsSessionMemory.Decode(memory.Read, 0x10000000, Catalog());
        Assert.NotNull(session);
        Assert.Null(session.Timing);
    }

    [Fact]
    public void LivePenaltyIsCumulativeAndDoesNotDriveLifecycle()
    {
        long now = 0;
        var source = new Source();
        source.Value = source.Value! with { Timing = new(1, 10000, 10000) };
        var reader = new WrcGenerationsGameDataReader(source, () => now);
        var reported = new List<float>();
        reader.onNewGameData += (_, current) => reported.Add(current.LapTime);
        Send(reader, 10);
        Assert.Equal(20f, reader.CurrentGameData.LapTime);
        Assert.Equal(20f, ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool.CreateReplayDetailsPerTime(reader.CurrentGameData, true).time);
        now = 250;
        source.Value = source.Value! with { Timing = new(1, 10250, 10000) };
        Send(reader, 10.25f);
        Assert.Equal(20.25f, reader.CurrentGameData.LapTime);
        Assert.Equal(GameState.Racing, reader.GameState);
        now = 1000;
        source.Value = source.Value! with { Timing = new(1, 10250, 20000) };
        Send(reader, 10.25f);
        Assert.Equal(GameState.Paused, reader.GameState);
        Assert.Equal(30.25f, reported[^1]);
        now = 1250;
        source.Value = source.Value! with { Timing = null };
        Send(reader, 10.5f);
        Assert.Equal(30.5f, reported[^1]);
        Send(reader, 0);
        Send(reader, 0.25f);
        Assert.Equal(0.25f, reported[^1]);
    }

    [Fact]
    public void ReplaySamplingUsesElapsedTimeRatherThanSampleCountAfterPenaltyJump()
    {
        Assert.True(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool.IsReplaySampleDue(30, 10, 100));
        Assert.False(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool.IsReplaySampleDue(30.01f, 30, 100));
        Assert.True(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool.IsReplaySampleDue(30.11f, 30, 100));
    }

    [Fact]
    public void OfficialFinishDoesNotAddLivePenaltyTwice()
    {
        var source = new Source();
        source.Value = source.Value! with { Timing = new(1, 379000, 20000) };
        var reader = new WrcGenerationsGameDataReader(source);
        Send(reader, 379);
        source.Value = source.Value! with { ResultReady = true, RaceMilliseconds = 379457, ResultMilliseconds = 399457 };
        Send(reader, 379.457f, true);
        Assert.Equal(GameState.RaceEnd, reader.GameState);
        Assert.Equal(399.457f, reader.CurrentGameData.LapTime);
        Send(reader, 379.457f, true);
        Assert.Equal(399.457f, reader.CurrentGameData.LapTime);
    }

    [Fact]
    public void LongUdpSilencePreservesSessionAndPenaltyUntilResume()
    {
        var source = new Source();
        source.Value = source.Value! with { Timing = new(1, 10000, 10000) };
        var reader = new WrcGenerationsGameDataReader(source);
        var transitions = new List<GameState>();
        reader.onGameStateChanged += e => transitions.Add(e.NewGameState);
        Send(reader, 10); Send(reader, 10.1f);
        for (var second = 0; second < 120; second++) reader.CheckTelemetrySilence();
        Assert.Equal(GameState.Paused, reader.GameState);
        Assert.Equal(20.1f, reader.CurrentGameData.LapTime);
        Assert.Equal("Finland - Harju", reader.TrackName);
        Send(reader, 10.2f);
        Assert.Equal(GameState.Racing, reader.GameState);
        Assert.Equal(20.2f, reader.CurrentGameData.LapTime);
        Assert.Equal(new[] { GameState.AdHocRaceBegin, GameState.Racing, GameState.Paused, GameState.Racing }, transitions);
        Send(reader, 0);
        Assert.Equal(GameState.RaceBegin, reader.GameState);
        Assert.Equal(0f, reader.CurrentGameData.LapTime);
    }

    [Fact]
    public void UdpSilenceDoesNotOverwriteConfirmedFinish()
    {
        var source = new Source();
        var reader = new WrcGenerationsGameDataReader(source);
        Send(reader, 100);
        source.Value = source.Value! with { ResultReady = true, RaceMilliseconds = 379457, ResultMilliseconds = 399457 };
        Send(reader, 379.457f, true);
        for (var second = 0; second < 120; second++) reader.CheckTelemetrySilence();
        Assert.Equal(GameState.RaceEnd, reader.GameState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExitingSilentSessionNeverPublishesNormalFinish(bool changedScene)
    {
        var source = new Source();
        source.Value = source.Value! with { Timing = new(1, 10000, 10000) };
        var reader = new WrcGenerationsGameDataReader(source);
        var finishes = 0;
        reader.onGameStateChanged += e => { if (e.NewGameState == GameState.RaceEnd) finishes++; };
        Send(reader, 10); Send(reader, 10.1f);
        reader.CheckTelemetrySilence(); reader.CheckTelemetrySilence();
        Assert.Equal(GameState.Paused, reader.GameState);
        source.Value = changedScene ? source.Value with { Identity = "other-stage" } : null;
        for (var second = 0; second < 12; second++) reader.CheckTelemetrySilence();
        Assert.Equal(GameState.Unknown, reader.GameState);
        Assert.Equal(0, finishes);
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
        internal float? HandBrake;
        public float? ReadHandBrake() => HandBrake;
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
        Assert.Equal(399.457f, reader.CurrentGameData.LapTime);
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
