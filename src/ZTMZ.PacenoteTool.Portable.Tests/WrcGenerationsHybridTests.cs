using System.Buffers.Binary;
using Neo.IronLua;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class WrcGenerationsHybridTests
{
    private sealed class Memory
    {
        internal const ulong Image = 0x140000000, Participant = 0x10000, Vehicle = 0x20000,
            Controller = 0x30000, Drive = 0x40000, Manager = 0x90000, Race = 0xa0000, Timer = 0xb0000;
        private readonly Dictionary<ulong, byte> bytes = new();
        internal void Put(ulong a, byte[] b) { for (var i = 0; i < b.Length; i++) bytes[a + (ulong)i] = b[i]; }
        internal void Pointer(ulong a, ulong v) => Put(a, BitConverter.GetBytes(v));
        internal void Float(int offset, float v) => Put(Controller + (ulong)offset, BitConverter.GetBytes(v));
        internal void Energy(double v) => Put(Drive + 0x688, BitConverter.GetBytes(v));
        internal byte[] Read(ulong a, int n) => Enumerable.Range(0, n).Select(i => bytes.TryGetValue(a + (ulong)i, out var b) ? b : throw new IOException("Unavailable")).ToArray();
        internal Memory()
        {
            Pointer(Image + 0x1c61660, Manager); Pointer(Manager + 0x48, Race); Pointer(Image + 0x1d61938, Race);
            Put(Manager + 0x12d0, BitConverter.GetBytes(7)); Put(Manager + 0x12e0, BitConverter.GetBytes(0)); Put(Manager + 0x1380, [1]);
            Pointer(Race + 0x4f0, Timer); Put(Timer + 0x9f, [0]); Pointer(Timer + 0xa0, Participant);
            Pointer(Participant + 0x38, Timer); Pointer(Participant + 0x70, Vehicle);
            Pointer(Vehicle, Image + 0x1744518); Pointer(Vehicle + 0x80, Participant); Pointer(Vehicle + 0x2178, Participant);
            Pointer(Participant + 0xa08, Controller); Pointer(Vehicle + 0xa0, Drive);
            Put(Vehicle + 0x1118, BitConverter.GetBytes(1)); Put(Vehicle + 0x1120, BitConverter.GetBytes(3.9d));
            Put(Controller, new byte[0x80]); Pointer(Controller, Participant); Pointer(Controller + 8, Vehicle);
            Put(Controller + 0x18, BitConverter.GetBytes(1)); Float(0x30, 3.9f); Float(0x5c, 300); Energy(3.12);
        }
        internal HybridTelemetrySnapshot? Hybrid(Func<ulong, int, byte[]>? read = null, ulong timer = Timer) =>
            WrcGenerationsSessionMemory.ReadHybrid(read ?? Read, Image, timer);
    }

    [Fact]
    public void ChargeAndRechargeProgressHaveDifferentUnits()
    {
        var m = new Memory(); m.Float(0x3c, 15); m.Put(Memory.Controller + 0x67, [1]);
        var h = m.Hybrid()!;
        Assert.Equal(.8f, h.BatteryChargeFraction!.Value, 5);
        Assert.Equal(3.12f, h.BatteryEnergyKWh!.Value, 5);
        Assert.Equal(3.9f, h.BatteryCapacityKWh);
        Assert.Equal(.5f, h.BoostProgressFraction);
        Assert.False(h.BoostReady); Assert.False(h.BoostActive); Assert.True(h.RegenerationActive); Assert.Equal(2, h.BoostMap);
        m.Float(0x3c, 30); Assert.True(m.Hybrid()!.BoostReady);
        m.Float(0x3c, 0); m.Put(Memory.Controller + 0x66, [1]); Assert.True(m.Hybrid()!.BoostReady);
    }

    [Fact]
    public void BoostUsesRemainingKilojouleBudgetAndCannotAlsoBeReady()
    {
        var m = new Memory(); m.Put(Memory.Controller + 0x65, [1]); m.Put(Memory.Controller + 0x66, [1]);
        m.Float(0x40, 3); m.Energy(3 - 150d / 3600);
        var h = m.Hybrid()!;
        Assert.True(h.BoostActive); Assert.False(h.BoostReady); Assert.Equal(.5f, h.BoostProgressFraction!.Value, 4);
        m.Energy(2); Assert.Equal(0f, m.Hybrid()!.BoostProgressFraction);
        m.Float(0x40, 0); Assert.Equal(1f, m.Hybrid()!.BoostProgressFraction); // Launch boost getter special case.
    }

    [Theory]
    [InlineData("non-hybrid")] [InlineData("controller")] [InlineData("drive")]
    [InlineData("owner")] [InlineData("reciprocal")] [InlineData("map")]
    [InlineData("flag")] [InlineData("nan")] [InlineData("energy-negative")]
    [InlineData("energy-overflow")] [InlineData("capacity")] [InlineData("budget")]
    [InlineData("recovery")] [InlineData("boost-start")] [InlineData("retired")]
    public void InvalidOrAbsentHybridDoesNotInventReadings(string fault)
    {
        var m = new Memory();
        switch (fault)
        {
            case "non-hybrid": m.Put(Memory.Vehicle + 0x1118, BitConverter.GetBytes(0)); break;
            case "controller": m.Pointer(Memory.Participant + 0xa08, 0); break;
            case "drive": m.Pointer(Memory.Vehicle + 0xa0, 0); break;
            case "owner": m.Pointer(Memory.Vehicle + 0x2178, 0); break;
            case "reciprocal": m.Pointer(Memory.Controller + 8, 0); break;
            case "map": m.Put(Memory.Controller + 0x18, BitConverter.GetBytes(3)); break;
            case "flag": m.Put(Memory.Controller + 0x65, [2]); break;
            case "nan": m.Energy(double.NaN); break;
            case "energy-negative": m.Energy(-1); break;
            case "energy-overflow": m.Energy(4); break;
            case "capacity": m.Float(0x30, 0); break;
            case "budget": m.Float(0x5c, 0); break;
            case "recovery": m.Float(0x3c, float.PositiveInfinity); break;
            case "boost-start": m.Float(0x40, -1); break;
            case "retired": m.Put(Memory.Timer + 0x9f, [1]); break;
        }
        Assert.Null(m.Hybrid());
    }

    [Fact]
    public void StaleSessionAndTornOwnershipAreRejected()
    {
        var m = new Memory(); Assert.Null(m.Hybrid(timer: Memory.Timer + 8));
        var reads = 0;
        byte[] Torn(ulong a, int n) => a == Memory.Participant + 0xa08 && ++reads == 2 ? BitConverter.GetBytes(0UL) : m.Read(a, n);
        Assert.Null(m.Hybrid(Torn));
        Assert.Null(m.Hybrid((_, _) => throw new IOException("Unloading")));
    }

    private sealed class Source : IWrcGenerationsSessionSource
    {
        internal HybridTelemetrySnapshot? Hybrid;
        internal int Reads;
        internal WrcGenerationsSessionSnapshot? Session = new("stage", "track", "car", "WRC", false, 0, 0, new(Memory.Timer, 10000, 0));
        public WrcGenerationsSessionSnapshot? Read() => Session;
        public HybridTelemetrySnapshot? ReadHybrid(ulong timer) { Reads++; Assert.Equal(Memory.Timer, timer); return Hybrid; }
        public void Dispose() { }
    }

    [Fact]
    public void DesktopLuaCanReadOptionalHybridSnapshot()
    {
        using var lua = new Lua(); var env = lua.CreateEnvironment();
        env.DoChunk("function charge(data) if data.Hybrid == nil then return -1 end return data.Hybrid.BatteryChargeFraction end", "hybrid.lua");
        var data = new GameData { Hybrid = new Memory().Hybrid() };
        Assert.Equal(.8, Convert.ToDouble(env.CallMember("charge", data)[0]), 5);
        Assert.Equal(-1, Convert.ToInt32(env.CallMember("charge", default(GameData))[0]));
    }

    [Fact]
    public void CountdownUsesVerifiedPendingTimerWithoutRequiringLapTime()
    {
        var source = new Source { Hybrid = new Memory().Hybrid() };
        source.Session = source.Session! with { Timing = null,
            NativeResult = new(Memory.Race, Memory.Timer, false, false, false, false, false) };
        using var reader = new WrcGenerationsGameDataReader(source, () => 0);
        reader.onNewUdpMessage([], new byte[280]);
        Assert.Equal(1, source.Reads);
        Assert.Same(source.Hybrid, reader.CurrentGameData.Hybrid);
        // Results without a valid lap must never supply the fallback timer.
        Assert.Null((source.Session with { NativeResult = source.Session.NativeResult! with { TimerEnded = true } }).VehicleTimerAddress);
        Assert.Null((source.Session with { NativeResult = source.Session.NativeResult! with { TimerFailed = true } }).VehicleTimerAddress);
        Assert.Null((source.Session with { NativeResult = null }).VehicleTimerAddress);
    }

    [Fact]
    public void UdpSamplesEveryPacketAndClearsMissingHybridWithoutLosingDrivingData()
    {
        var m = new Memory(); var source = new Source { Hybrid = m.Hybrid() }; long clock = 0;
        using var reader = new WrcGenerationsGameDataReader(source, () => clock);
        void Send(float time)
        {
            var packet = new byte[280]; BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(4), time);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(8), 100); reader.onNewUdpMessage([], packet);
        }
        Send(10); Assert.Same(source.Hybrid, reader.CurrentGameData.Hybrid);
        m.Energy(2); source.Hybrid = m.Hybrid(); Send(10.05f);
        Assert.Equal(2, source.Reads); Assert.Equal(2, reader.CurrentGameData.Hybrid.BatteryEnergyKWh);
        source.Hybrid = null; Send(10.1f); Assert.Null(reader.CurrentGameData.Hybrid); Assert.Equal(100, reader.CurrentGameData.LapDistance);
        source.Hybrid = m.Hybrid(); Send(0); Assert.NotNull(reader.CurrentGameData.Hybrid);
        source.Session = null; clock = 1000; Send(.1f); Assert.Null(reader.CurrentGameData.Hybrid);
    }
}
