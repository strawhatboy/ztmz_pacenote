using System.Buffers.Binary;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class WrcGenerationsDamageTests
{
    private sealed class Source : IWrcGenerationsSessionSource
    {
        public WrcGenerationsSessionSnapshot? Value = Active(10000);
        public CarDamageSnapshot? Damage = Condition(0);
        public int Reads;
        public WrcGenerationsSessionSnapshot? Read() => Value;
        public CarDamageSnapshot? ReadDamage(ulong timer) { Reads++; return timer == Value?.Timing?.TimerAddress ? Damage : null; }
        public void Dispose() { }
    }

    private static WrcGenerationsSessionSnapshot Active(int time) =>
        new("stage:car", "track", "car", "WRC", false, 0, 0, new(0x50000, time, 0))
        { NativeResult = new(0x40000, 0x50000, time > 0, false, false, false, false) };

    private static CarDamageSnapshot Condition(float body, float engine = 0) => KtMechanicalDamage.Create([
        new("Body", 100, body, .75f, .25f), new("Engine", 100, engine, .7f, .3f),
        new("Hybrid", 100, 0, .7f, .1f)], "wrcg");

    private static void Send(WrcGenerationsGameDataReader reader, float time, bool finish = false)
    {
        var bytes = new byte[280];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), time);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), time > 0 ? 100 : -1);
        if (finish) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(248), time * 1000);
        reader.onNewUdpMessage([], bytes);
    }

    [Fact]
    public void DamageIsFreshPerPacketWithNativeEngineThresholdsAndHybridPreserved()
    {
        var source = new Source();
        using var reader = new WrcGenerationsGameDataReader(source, () => 0);
        var events = new List<CarDamageEvent>(); reader.onCarDamaged += events.Add;
        Send(reader, 10); Send(reader, 10.05f);
        source.Damage = Condition(6, 31); Send(reader, 10.1f);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.DamageType == CarDamage.Collision);
        Assert.Contains(events, e => e.DamageType == CarDamage.Engine);
        Assert.Equal(3, source.Reads);
        Assert.Equal("wrcg", reader.CurrentGameData.CarDamage.Source);
        Assert.Contains(reader.CurrentGameData.CarDamage.Components, p => p.Kind == "hybrid");
        Assert.False(reader.CurrentGameData.CarDamage.Dashboard.FrontLeftTyreAvailable);
        source.Damage = null; Send(reader, 10.15f);
        Assert.Null(reader.CurrentGameData.CarDamage);
        source.Damage = Condition(40, 80); Send(reader, 10.2f);
        Assert.Equal(2, events.Count); // Reattachment is a baseline.
    }

    [Theory]
    [InlineData("pause")] [InlineData("restart")] [InlineData("missing")]
    [InlineData("finish")] [InlineData("identity")] [InlineData("timer")] [InlineData("repair")]
    public void LifecycleChangesDoNotAnnounceAccumulatedDamage(string scenario)
    {
        long now = 0;
        var source = new Source { Damage = Condition(10, 10) };
        using var reader = new WrcGenerationsGameDataReader(source, () => now);
        var events = new List<CarDamageEvent>(); reader.onCarDamaged += events.Add;
        Send(reader, 10); Send(reader, 10.05f);
        switch (scenario)
        {
            case "pause": reader.CheckTelemetrySilence(); reader.CheckTelemetrySilence(); break;
            case "restart": source.Value = Active(0); Send(reader, 0); break;
            case "missing": source.Damage = null; Send(reader, 10.06f); break;
            case "finish": Send(reader, 10.06f, true); break;
            case "identity": source.Value = Active(10100) with { Identity = "other" }; now = 1000; break;
            case "timer": source.Value = Active(10100) with { Timing = new(0x60000, 10100, 0) }; now = 1000; break;
        }
        source.Damage = Condition(40, scenario == "repair" ? 0 : 80);
        Send(reader, scenario == "restart" ? .1f : 10.1f);
        Assert.Empty(events);
    }

    [Fact]
    public void ProductionRecordingReplaysThroughUdpReaderWithoutOldDamageCalls()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "wrcg-damage-restart-20261017.json")));
        long now = 0; float lastTime = 0;
        var source = new Source();
        using var reader = new WrcGenerationsGameDataReader(source, () => now);
        var events = new List<CarDamageEvent>(); reader.onCarDamaged += events.Add;
        var reset = false; var sawDamage = false;
        foreach (var row in fixture.RootElement.EnumerateArray())
        {
            now = row.GetProperty("elapsedMs").GetInt64();
            var time = row.GetProperty("milliseconds");
            if (time.ValueKind == JsonValueKind.Null)
            {
                source.Value = null; source.Damage = null; Send(reader, lastTime); continue;
            }
            var milliseconds = time.GetInt32();
            source.Value = Active(milliseconds);
            source.Damage = KtMechanicalDamage.Create(row.GetProperty("parts").EnumerateArray().Select(p =>
            {
                var v = p.GetProperty("values").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                return new KtMechanicalDamagePart(p.GetProperty("name").GetString()!, v[0], v[1], v[2], v[3]);
            }).ToArray(), "wrcg");
            var body = source.Damage.Components.Single(p => p.Kind == CarDamageIds.Bodywork).Magnitude;
            if (body > 0) sawDamage = true;
            if (sawDamage && body == 0) reset = true;
            var before = events.Count;
            lastTime = milliseconds / 1000f; Send(reader, lastTime);
            if (!reset) Assert.Equal(before, events.Count);
            if (reader.CurrentGameData.CarDamage is { } damage)
            {
                Assert.Equal(15, damage.Components.Count);
                Assert.True(damage.Dashboard.FrontRightBrakeAvailable);
                Assert.False(damage.Dashboard.FrontRightTyreAvailable);
                Assert.False(damage.Dashboard.RadiatorAvailable);
            }
        }
        Assert.True(reset);
        Assert.Equal(CarDamage.Collision, Assert.Single(events).DamageType);
    }
}
