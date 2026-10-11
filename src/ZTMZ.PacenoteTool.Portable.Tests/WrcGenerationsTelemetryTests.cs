using System.Buffers.Binary;
using Xunit;
using ZTMZ.PacenoteTool.KT;

public class WrcGenerationsTelemetryTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1133.14165f, 0.5f)]
    [InlineData(2266.2833f, 1)]
    [InlineData(2400, 1)]
    [InlineData(-10, 0)]
    public void HarjuLengthUsesKilometresWhileDistanceUsesMetres(float distance, float completion)
    {
        var packet = new byte[280];
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(8), distance);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(12), 2.2662833f);
        Assert.True(WrcGenerationsTelemetry.TryDecode(packet, out var data));
        Assert.Equal(2266.2833f, data.TrackLength, 3);
        Assert.Equal(distance, data.LapDistance);
        Assert.Equal(completion, data.CompletionRate, 5);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0.5f, -0.5f)]
    [InlineData(2, -1)]
    public void CorrectsWrcgSteeringSign(float input, float expected)
    {
        var packet = new byte[280];
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(120), input);
        Assert.True(WrcGenerationsTelemetry.TryDecode(packet, out var data));
        Assert.Equal(expected, data.Steering);
        Assert.Equal(0, data.CompletionRate); // Unavailable length is not divided by zero.
    }

    [Fact]
    public void ConvertsUnitsAndPublishesCurrentDataBeforeCallback()
    {
        var packet = new byte[280];
        void Set(int index, float value) => BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(index * 4, 4), value);
        Set(7, 20); Set(33, -1); Set(37, 650); Set(63, 890); Set(64, 80); Set(1, 42);
        var reader = new WrcGenerationsGameDataReader();
        var received = false;
        reader.onNewGameData += (_, data) =>
        {
            received = true;
            Assert.Equal(72, data.Speed); Assert.Equal(-1, data.Gear);
            Assert.Equal(6500, data.RPM); Assert.Equal(8900, data.MaxRPM); Assert.Equal(800, data.IdleRPM);
            Assert.Equal(42, reader.CurrentGameData.LapTime);
            Assert.False(data.HandBrakeValid);
        };
        reader.onNewUdpMessage([], packet);
        Assert.True(received);
    }

    [Fact]
    public void RejectsMalformedPacketsAndFractionalGears()
    {
        Assert.False(WrcGenerationsTelemetry.TryDecode(new byte[264], out _));
        var packet = new byte[280];
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(33 * 4, 4), 1.5f);
        Assert.False(WrcGenerationsTelemetry.TryDecode(packet, out _));
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(33 * 4, 4), 1);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(7 * 4, 4), float.NaN);
        Assert.False(WrcGenerationsTelemetry.TryDecode(packet, out _));
    }

    private const string Settings = """
        // WRC.Telemetry.EnableTelemetry = false;
        WRC.Telemetry.TelemetryRate = 60;
        WRC.Telemetry.TelemetryAdress = "127.0.1.1";
        WRC.Telemetry.EnableTelemetry = false;
        WRC.Telemetry.TelemetryPort = 20777;
        Unrelated = "preserve me";
        """;

    [Fact]
    public void ConfiguresOnlyTelemetryAndIsIdempotent()
    {
        var result = WrcGenerationsTelemetrySettings.Configure(Settings, "127.0.0.1", 20778);
        Assert.True(WrcGenerationsTelemetrySettings.Matches(result, "127.0.0.1", 20778));
        Assert.Contains("Unrelated = \"preserve me\";", result);
        Assert.Contains("// WRC.Telemetry.EnableTelemetry = false;", result);
        Assert.Equal(result, WrcGenerationsTelemetrySettings.Configure(result, "127.0.0.1", 20778));
    }

    [Fact]
    public void RefusesDuplicateSettings() => Assert.Throws<InvalidDataException>(() =>
        WrcGenerationsTelemetrySettings.Configure(Settings + "\nWRC.Telemetry.TelemetryPort = 22888;", "127.0.0.1", 20778));
}
