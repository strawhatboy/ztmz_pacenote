using System.Buffers.Binary;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.BeamNG;
using ZTMZ.PacenoteTool.AC;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class EngineTurboTelemetryTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static byte[] Packet(float coolant = 91, float oil = 104, float pressure = .8f,
        ushort flags = 8192, uint dash = 256, uint lights = 0)
    {
        var bytes = new byte[92];
        "beam"u8.CopyTo(bytes.AsSpan(4));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), flags);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(20), pressure);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(24), coolant);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(36), oil);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), dash);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), lights);
        return bytes;
    }

    [Theory]
    [InlineData(8192, 0f)]
    [InlineData(40960, 1.25f)]
    [InlineData(8192, -.2f)]
    public void BeamNgPreservesBarReadingsRegardlessOfDisplayUnitPreference(int flags, float pressure)
    {
        var clock = new Clock();
        using var reader = new BeamNGGameDataReader(clock);
        reader.onNewUdpMessage([], Packet(pressure: pressure, flags: (ushort)flags, lights: 256));
        var data = reader.CurrentGameData;
        Assert.Equal(91, data.Engine.CoolantTemperatureCelsius);
        Assert.Equal(104, data.Engine.OilTemperatureCelsius);
        Assert.Null(data.Engine.OilPressureWarning);
        Assert.Null(data.Engine.CoolantTemperatureWarning);
        Assert.Null(data.Engine.EngineWarning);
        Assert.Equal(pressure, data.Turbo.PressureBar);
        Assert.Equal(TurboPressureReference.Gauge, data.Turbo.PressureReference);
        Assert.Equal("beamng", data.Engine.Source);
        Assert.Equal(clock.Now, data.Engine.CapturedAtUtc);
        Assert.Equal(clock.Now, data.Turbo.CapturedAtUtc);
    }

    [Fact]
    public void BeamNgInvalidOrUnsupportedFieldsDoNotBecomeZeroOrHealthy()
    {
        using var reader = new BeamNGGameDataReader();
        reader.onNewUdpMessage([], Packet());
        reader.onNewUdpMessage([], Packet(coolant: float.NaN, oil: 0, flags: 0, dash: 0, lights: 256));
        Assert.Null(reader.CurrentGameData.Engine);
        Assert.Null(reader.CurrentGameData.Turbo);
        reader.onNewUdpMessage([], Packet(coolant: 95, oil: float.PositiveInfinity, pressure: float.NaN));
        Assert.Equal(95, reader.CurrentGameData.Engine.CoolantTemperatureCelsius);
        Assert.Null(reader.CurrentGameData.Engine.OilTemperatureCelsius);
        Assert.Null(reader.CurrentGameData.Engine.OilPressureWarning);
        Assert.Null(reader.CurrentGameData.Turbo);
        reader.onNewUdpMessage([], Packet(coolant: 0, oil: 0, flags: 0));
        Assert.Null(reader.CurrentGameData.Engine); // e.g. EV/default electrics
    }

    [Fact]
    public void BeamNgBridgePacketsCannotRefreshStaleOutGaugeReadings()
    {
        var clock = new Clock();
        using var reader = new BeamNGGameDataReader(clock);
        GameData published = default;
        reader.onNewGameData += (_, next) => published = next;
        reader.onNewUdpMessage([], Packet());
        var capturedAt = reader.CurrentGameData.Engine.CapturedAtUtc;
        clock.Now += TimeSpan.FromSeconds(1);
        reader.ProcessBridgePacket(new());
        Assert.Equal(capturedAt, published.Engine.CapturedAtUtc);
        clock.Now += TimeSpan.FromSeconds(2);
        Assert.Null(reader.CurrentGameData.Engine);
        Assert.Null(reader.CurrentGameData.Turbo);
        reader.onNewUdpMessage([], new byte[12]); // malformed packets cannot renew freshness
        reader.ProcessBridgePacket(new());
        Assert.Null(published.Engine);
        Assert.Null(published.Turbo);
        reader.onNewUdpMessage([], Packet());
        Assert.Equal(clock.Now, published.Engine.CapturedAtUtc);
    }

    [Fact]
    public void BeamNgVehicleChangeAndDisconnectClearOptionalReadings()
    {
        using var reader = new BeamNGGameDataReader();
        reader.onNewUdpMessage([], Packet());
        reader.ProcessBridgePacket(new() { Telemetry = new() { VehicleId = 2 } });
        Assert.Null(reader.CurrentGameData.Engine);
        Assert.Null(reader.CurrentGameData.Turbo);
        reader.onNewUdpMessage([], Packet(flags: 0));
        Assert.NotNull(reader.CurrentGameData.Engine);
        Assert.Null(reader.CurrentGameData.Turbo);
        reader.Uninitialize(new BeamNGRally());
        Assert.Null(reader.CurrentGameData.Engine);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(350, 1.2f)]
    public void AcrUnverifiedAbiFieldsRemainUnavailable(float water, float pressure)
    {
        var data = ACRGameDataConverter.Convert(new()
        {
            PhysicsData = new() { WaterTemperature = water, TurboBoostPressure = pressure },
            StaticInfo = new() { TurboBoostMax = 3 }
        });
        Assert.Null(data.Engine);
        Assert.Null(data.Turbo);
    }
}
