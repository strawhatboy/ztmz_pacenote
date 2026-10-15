using System.Buffers.Binary;
using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc9MemoryTests
{
    // Synthetic memory with separate global/TLS tables: a clock decoded through
    // the global table would produce a plausible but wrong time.
    private sealed class Memory
    {
        public const ulong Image = 0x140000000, Manager = 0x20000, Race = 0x30000, Profile = 0x40000,
            Stage = 0x50000, Rally = 0x60000, Car = 0x70000, Timer = 0x80000,
            Participant = 0x90000, Laps = 0xa0000, Clock = 0xb0000,
            Teb = 0xc0000, Tls = 0xd0000, Slot = 0xe0000, GlobalTable = 0xf0000, ClockTable = 0x100000;
        private readonly Dictionary<ulong, byte> bytes = new();
        public readonly WrcGenerationsSessionCatalog Names = new(
            new byte[4].Concat(Encoding.Unicode.GetBytes("Stage\0Rally\0Car\0")).ToArray(),
            "LocalesId = {\n STAGE = 0,\n RALLY = 1,\n CAR = 2,\n}", "Cars = {\n}");
        public Memory()
        {
            U64(Image + 0x1a4e820, Manager); U64(Manager, Image + 0x13f3398);
            U64(Manager + 0x48, Race); U64(Image + 0x1b024e8, Race); U64(Race, Image + 0x13f2510);
            I32(Manager + 0x1124, 1); Put(Manager + 0x1130, [0]); U64(Manager + 0x1128, Profile);
            U64(Manager + 0xbc8, Stage); U64(Manager + 0xbd0, Rally); U64(Profile + 0x30, Car);
            I32(Stage + 12, 0); I32(Rally + 16, 1); I32(Car + 8, 2);
            U64(Race + 0x4e8, Timer); U64(Timer + 0xa0, Participant); U64(Participant + 0x38, Timer);
            Put(Participant + 0x44, [0]); U64(Participant + 0x70, 0);
            Put(Timer + 0x9c, [1, 0, 0, 0]); Put(Profile + 0x194, new byte[14]);
            I32(Timer + 0x3c, 1); U64(Timer + 0x40, Laps); U64(Race + 0x898, Clock);
            U64(Image + 0x19e9140, GlobalTable);
            U64(Teb + 0x58, Tls); I32(Image + 0x1b0eb38, 3); U64(Tls + 3 * 8, Slot); U64(Slot + 0x40, ClockTable);
            Protected(Laps + 0x10, 1, 11477, GlobalTable); Protected(Laps + 0x20, 2, 0, GlobalTable);
            Protected(Timer + 0xc8, 3, 0, GlobalTable);
            Protected(Clock + 0x2e0, 4, 12000, GlobalTable);
            Protected(Clock + 0x2e0, 4, 59084, ClockTable);
        }
        public void Put(ulong address, byte[] value) { for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i]; }
        public void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        public void I32(ulong address, int value) => Put(address, BitConverter.GetBytes(value));
        public byte[] Read(ulong address, int count) => Enumerable.Range(0, count).Select(i =>
            bytes.TryGetValue(address + (ulong)i, out var b) ? b : throw new IOException($"Unmapped {address:x}")).ToArray();
        public void Protected(ulong address, uint slot, int value, ulong table)
        {
            I32(address + 8, (int)slot);
            var mask = unchecked(((uint)(address + 8) + slot) * 0x159a55e5u + 0x159ae628u);
            I32(table + slot * 4, unchecked(value ^ (int)(mask == 0 ? 0xb10be924u : mask)));
        }
        public Wrc9Snapshot? Decode(Func<ulong, int, byte[]>? read = null) => Wrc9SessionMemory.Decode(read ?? Read, Image, Teb, 0, Names);
    }

    [Fact]
    public void RunningClockUsesMainThreadTlsWhileLapStartUsesGlobalTable()
    {
        var snapshot = new Memory().Decode();
        Assert.NotNull(snapshot);
        Assert.Equal(47607, snapshot.RawMilliseconds);
        Assert.Equal("Rally - Stage", snapshot.Track);
        Assert.Null(snapshot.ResultMilliseconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountdownDoesNotRequireAnAllocatedLapOrClock(bool timerStarted)
    {
        var memory = new Memory();
        memory.Put(Memory.Timer + 0x9c, [timerStarted ? (byte)1 : (byte)0, 0, 0, 0]);
        memory.I32(Memory.Timer + 0x3c, 0); memory.U64(Memory.Timer + 0x40, 0); memory.U64(Memory.Race + 0x898, 0);
        Assert.Equal(0, memory.Decode()!.RawMilliseconds);
    }

    [Fact]
    public void CapturedStartedFlagBeforeLapAllocationStaysInCountdown()
    {
        var memory = new Memory();
        var lifecycle = new Wrc9RaceSession();
        memory.Put(Memory.Timer + 0x9c, [0, 0, 0, 0]);
        memory.I32(Memory.Timer + 0x3c, 0); memory.U64(Memory.Timer + 0x40, 0);
        Assert.Equal(ZTMZ.PacenoteTool.Base.Game.GameState.CountingDown, lifecycle.Update(memory.Decode(), 48836750));
        memory.Put(Memory.Timer + 0x9c, [1, 0, 0, 0]);
        Assert.Equal(ZTMZ.PacenoteTool.Base.Game.GameState.CountingDown, lifecycle.Update(memory.Decode(), 48845046));
        Assert.False(lifecycle.IsNewSession);
        memory.I32(Memory.Timer + 0x3c, 1); memory.U64(Memory.Timer + 0x40, Memory.Laps);
        memory.Protected(Memory.Laps + 0x10, 1, 9725, Memory.GlobalTable);
        memory.Protected(Memory.Clock + 0x2e0, 4, 9753, Memory.ClockTable);
        Assert.Equal(ZTMZ.PacenoteTool.Base.Game.GameState.Racing, lifecycle.Update(memory.Decode(), 48846453));
        Assert.False(lifecycle.IsNewSession);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinishRequiresMatchingProcessedResultAndTimer(bool mismatch)
    {
        var memory = new Memory();
        memory.Put(Memory.Timer + 0x9c, [1, 1, 0, 0]);
        memory.Put(Memory.Profile + 0x1a0, [1, 0]);
        memory.Protected(Memory.Laps + 0x20, 2, 59084, Memory.GlobalTable);
        memory.Protected(Memory.Timer + 0xc8, 3, 18000, Memory.GlobalTable);
        memory.I32(Memory.Profile + 0x194, 47607);
        memory.I32(Memory.Profile + 0x198, mismatch ? 65608 : 65607);
        var result = memory.Decode()!;
        Assert.Equal(mismatch ? null : (int?)65607, result.ResultMilliseconds);
        Assert.Equal(18000, result.PenaltyMilliseconds);
    }

    [Fact]
    public void RetiredTimerDoesNotReadDiscardedLapStorage()
    {
        var memory = new Memory();
        memory.Put(Memory.Timer + 0x9c, [1, 1, 0, 1]); memory.Put(Memory.Profile + 0x1a0, [1, 1]);
        memory.U64(Memory.Timer + 0x40, 0);
        var result = memory.Decode()!;
        Assert.True(result.NativeResult!.Retired);
        Assert.Null(result.ResultMilliseconds);
        Assert.Equal(0, result.RawMilliseconds);
    }

    [Fact]
    public void ChangedOwnerDuringReadDiscardsTheSample()
    {
        var memory = new Memory();
        var reads = 0;
        byte[] Read(ulong address, int count)
        {
            if (address == Memory.Manager + 0x1128 && ++reads == 2) return BitConverter.GetBytes(0x99999UL);
            return memory.Read(address, count);
        }
        Assert.Null(memory.Decode(Read));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnverifiedProfileCountsAreRejected(int count)
    {
        var memory = new Memory(); memory.I32(Memory.Manager + 0x1124, count);
        Assert.Null(memory.Decode());
    }
}
