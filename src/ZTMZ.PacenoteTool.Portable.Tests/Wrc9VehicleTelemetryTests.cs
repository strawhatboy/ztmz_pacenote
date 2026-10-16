using System.Buffers.Binary;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc9VehicleTelemetryTests
{
    private static void Float(byte[] b, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(offset), value);
    private static void Int(byte[] b, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offset), value);

    [Fact]
    public void CapturedControlsWheelsAndDamageSurviveUnitConversion()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "wrc9-vehicle-damage-20261016.json")));
        foreach (var sample in fixture.RootElement.EnumerateArray())
        {
            var label = sample.GetProperty("label").GetString();
            var drive = new byte[0x538]; var controls = sample.GetProperty("controls").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            for (var i = 0; i < controls.Length; i++) Float(drive, 0xe0 + i * 4, controls[i]);
            Int(drive, 0x128, sample.GetProperty("gear").GetInt32()); Int(drive, 0x534, sample.GetProperty("gearCount").GetInt32());
            Float(drive, 0x134, sample.GetProperty("omega").GetSingle());
            Float(drive, 0x448, sample.GetProperty("maximumOmega").GetSingle());
            Float(drive, 0x44c, sample.GetProperty("idleOmega").GetSingle());
            var acceleration = sample.GetProperty("acceleration").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            Float(drive, 0x29c, acceleration[0]); Float(drive, 0x2a0, acceleration[1]);
            var data = Wrc9VehicleTelemetry.DecodeDrive(drive, new() { LapTime = 42 });
            Assert.Equal(42, data.LapTime); Assert.Equal(6, data.MaxGears);
            Assert.Equal(controls[0], data.Throttle); Assert.Equal(controls[1], data.Brake); Assert.Equal(controls[5], data.Clutch);
            Assert.True(data.HandBrakeValid); Assert.Equal(8021.409f, data.MaxRPM, 2); Assert.Equal(2244.085f, data.IdleRPM, 2);
            if (label == "full-left") Assert.Equal(-1, data.Steering);
            if (label == "full-right") Assert.Equal(1, data.Steering);
            if (label == "accelerating") Assert.True(data.G_long > 0);
            if (label == "braking") Assert.True(data.G_long < 0);
            if (sample.TryGetProperty("wheels", out var wheels))
            {
                var vehicle = new byte[0x1208]; var query = new byte[0x280]; var config = new byte[0xc0]; var dynamics = new byte[0x20];
                var expectedSpeed = new List<float>(); var expectedTravel = new List<float>(); var expectedRate = new List<float>(); var expectedTemp = new List<float>();
                var i = 0;
                foreach (var wheel in wheels.EnumerateArray())
                {
                    float Value(string key) => wheel.GetProperty(key).GetSingle();
                    Float(config, i * 0x30, Value("radius")); Float(dynamics, 0x10 + i * 4, Value("omega"));
                    Float(query, i * 0xa0 + 0x5c, Value("jounce")); Float(drive, 0x1e0 + i * 4, Value("rest"));
                    Float(drive, 0x200 + i * 4, Value("velocity"));
                    var tempOffset = 0x118c + i * 0x24; var flag = wheel.GetProperty("temperatureFlag").GetByte();
                    vehicle[tempOffset + 12] = flag; Float(vehicle, tempOffset - (flag == 1 ? 20 : 0), Value("temperature"));
                    expectedSpeed.Add(Value("radius") * Value("omega") * 3.6f); expectedTravel.Add(Value("jounce") - Value("rest"));
                    expectedRate.Add(Value("velocity")); expectedTemp.Add(Value("temperature")); i++;
                }
                data = Wrc9VehicleTelemetry.DecodeWheels(vehicle, drive, query, config, dynamics, data);
                Assert.Equal(expectedSpeed, [data.SpeedFrontLeft, data.SpeedFrontRight, data.SpeedRearLeft, data.SpeedRearRight]);
                Assert.Equal(expectedTravel, [data.SuspensionFrontLeft, data.SuspensionFrontRight, data.SuspensionRearLeft, data.SuspensionRearRight]);
                Assert.Equal(expectedRate, [data.SuspensionSpeedFrontLeft, data.SuspensionSpeedFrontRight, data.SuspensionSpeedRearLeft, data.SuspensionSpeedRearRight]);
                Assert.Equal(expectedTemp, [data.BrakeTempFrontLeft, data.BrakeTempFrontRight, data.BrakeTempRearLeft, data.BrakeTempRearRight]);
            }
            if (sample.TryGetProperty("parts", out var parts))
            {
                var damage = Wrc9Damage.Create(parts.EnumerateArray().Select(p => new Wrc9DamagePart(p.GetProperty("name").GetString()!,
                    p.GetProperty("maximum").GetSingle(), p.GetProperty("damage").GetSingle(), p.GetProperty("warning").GetSingle(), p.GetProperty("critical").GetSingle())).ToArray());
                Assert.Equal(14, damage.Components.Count); Assert.False(damage.Dashboard.FrontLeftTyreAvailable);
                Assert.True(damage.Dashboard.FrontLeftBrakeAvailable); Assert.True(damage.Dashboard.BodyworkAvailable);
                if (label == "healthy-after-restart") Assert.All(damage.Components, p => { Assert.Equal(0, p.Magnitude); Assert.False(p.IsActive); });
                if (label == "damaged-after-collisions")
                {
                    Assert.Equal(.3322f, damage.Components.Single(p => p.Kind == CarDamageIds.Bodywork).Magnitude, 4);
                    Assert.Equal(1, damage.Dashboard.EngineSeverity); Assert.Equal(1, damage.Dashboard.BodyworkSeverity);
                    Assert.Equal(1, damage.Dashboard.FrontLeftSeverity);
                }
            }
        }
    }

    [Theory]
    [InlineData(0, -1)] [InlineData(1, 0)] [InlineData(2, 1)] [InlineData(7, 6)]
    public void GearTableIncludesReverseAndNeutral(int raw, int expected)
    {
        var bytes = new byte[0x538]; Int(bytes, 0x534, 8); Int(bytes, 0x128, raw);
        Assert.Equal(expected, Wrc9VehicleTelemetry.DecodeDrive(bytes, new()).Gear);
    }

    [Fact]
    public void InvalidChannelsCannotPoisonOtherTelemetry()
    {
        var drive = new byte[0x538]; Float(drive, 0xe0, .8f); Float(drive, 0xe8, float.NaN);
        Float(drive, 0xf0, 2); Float(drive, 0x2a0, float.PositiveInfinity);
        var data = Wrc9VehicleTelemetry.DecodeDrive(drive, new());
        Assert.Equal(.8f, data.Throttle); Assert.False(data.HandBrakeValid); Assert.Equal(0, data.Steering); Assert.Equal(0, data.G_long);
        Assert.Equal(12, Wrc9VehicleTelemetry.DecodeDrive([], new() { LapTime = 12 }).LapTime);
        Assert.Equal(12, Wrc9VehicleTelemetry.DecodeWheels(new byte[0x1204], drive, new byte[0x280], new byte[0xc0], new byte[0x20], new() { LapTime = 12 }).LapTime);
    }

    [Fact]
    public void TemperatureAlternateBufferAndLastWheelAreReadSafely()
    {
        var vehicle = new byte[0x1208]; var drive = new byte[0x538]; var query = new byte[0x280]; var config = new byte[0xc0]; var dynamics = new byte[0x20];
        for (var i = 0; i < 4; i++)
        {
            Float(config, i * 0x30, .5f); var offset = 0x118c + i * 0x24;
            Float(vehicle, offset, 100 + i); Float(vehicle, offset - 20, 200 + i);
            vehicle[offset + 12] = (byte)(i % 2);
        }
        var data = Wrc9VehicleTelemetry.DecodeWheels(vehicle, drive, query, config, dynamics, new());
        Assert.Equal(new float[] { 100, 201, 102, 203 }, new float[] { data.BrakeTempFrontLeft, data.BrakeTempFrontRight, data.BrakeTempRearLeft, data.BrakeTempRearRight });
        vehicle[0x1204] = 2;
        Assert.Equal(0, Wrc9VehicleTelemetry.DecodeWheels(vehicle, drive, query, config, dynamics, new()).BrakeTempRearRight);
    }

    [Theory]
    [InlineData(6499, 0)] [InlineData(7000, .5f)] [InlineData(7500, 1)] [InlineData(8021, 1)]
    public void NativeHudRedRpmControlsWarningNotEngineMaximum(float rpm, float expected)
    {
        var data = KtShiftLights.ApplyWarning(new() { RPM = rpm, MaxRPM = 8021.409f }, 7500);
        Assert.True(data.ShiftLightsRPMValid); Assert.Equal(6500, data.ShiftLightsRPMStart); Assert.Equal(7500, data.ShiftLightsRPMEnd);
        Assert.Equal(expected, data.ShiftLightsFraction);
    }
}
