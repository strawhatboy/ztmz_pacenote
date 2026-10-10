using System.Buffers.Binary;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public class Wrc7VehicleTelemetryTests
{
    [Fact]
    public void HandbrakeIsIndependentOfBrakeAndSteeringUsesSharedSign()
    {
        var inputs = new byte[24];
        Write(inputs, 0, .75f);
        Write(inputs, 8, 1);
        Write(inputs, 16, -.8f);
        Write(inputs, 20, 1);
        var result = Wrc7VehicleTelemetry.DecodeControls(inputs, 7, new());
        Assert.Equal(.75f, result.Throttle);
        Assert.Equal(0, result.Brake);
        Assert.Equal(1, result.HandBrake);
        Assert.True(result.HandBrakeValid);
        Assert.Equal(.8f, result.Steering);
        Assert.Equal(1, result.Clutch);
        Assert.Equal(5, result.MaxGears);
        Write(inputs, 8, float.NaN);
        result = Wrc7VehicleTelemetry.DecodeControls(inputs, 7, new());
        Assert.False(result.HandBrakeValid);
        Assert.Equal(.75f, result.Throttle);
        Assert.False(Wrc7VehicleTelemetry.DecodeControls([], 7, new()).HandBrakeValid);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(6, 5)]
    public void ConvertsPhysicsUnitsAndKeepsStageTime(int physicsGear, int expectedGear)
    {
        var engine = new byte[0xb0];
        var limits = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(engine, physicsGear);
        Write(engine, 8, 100 * MathF.PI);
        Write(engine, 0x98, 3);
        Write(engine, 0x9c, 4);
        Write(limits, 0, 200 * MathF.PI);
        Write(limits, 4, 50 * MathF.PI);
        var result = Wrc7VehicleTelemetry.Decode(engine, limits, new GameData { LapTime = 12.3f });
        Assert.Equal(expectedGear, result.Gear);
        Assert.Equal(3000, result.RPM, 2);
        Assert.Equal(6000, result.MaxRPM, 2);
        Assert.Equal(1500, result.IdleRPM, 2);
        Assert.Equal(12.3f, result.LapTime);
        Write(engine, 0x98, float.NaN);
        Assert.Equal(0, Wrc7VehicleTelemetry.Decode(engine, limits, new()).Speed);
    }

    [Fact]
    public void MotionUsesBodyVelocityAndRemovesLocalCenterOfMassOffset()
    {
        var body = new byte[40];
        var local = new byte[28];
        Write(body, 12, 1); Write(local, 12, 1);
        Write(body, 16, 100); Write(local, 16, 2);
        Write(body, 28, 3); Write(body, 32, 4);
        var result = Wrc7VehicleTelemetry.DecodeMotion(body, local, new());
        Assert.Equal(18, result.Speed, 2);
        Assert.Equal(98, result.PosX);
        Write(body, 28, float.NaN);
        Assert.Equal(0, Wrc7VehicleTelemetry.DecodeMotion(body, local, new()).Speed);
    }

    [Fact]
    public void WheelSpeedsPreserveOrderAndReverseRotation()
    {
        var wheels = new byte[0xc0]; var angular = new byte[16];
        for (var i = 0; i < 4; i++) { Write(wheels, i * 0x30, .5f); Write(angular, i * 4, i - 1); }
        var result = Wrc7VehicleTelemetry.DecodeWheelSpeeds(wheels, angular, new());
        Assert.Equal(-1.8f, result.SpeedFrontLeft);
        Assert.Equal(0, result.SpeedFrontRight);
        Assert.Equal(1.8f, result.SpeedRearLeft);
        Assert.Equal(3.6f, result.SpeedRearRight);
    }

    [Theory]
    [InlineData(-1f, 1f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, -1f)]
    public void ConvertsWrc7NegatedSteeringChannel(float channel, float expected)
    {
        var inputs = new byte[24];
        Write(inputs, 16, channel);
        Assert.Equal(expected, Wrc7VehicleTelemetry.DecodeControls(inputs, 7, new()).Steering);
    }

    private static void Write(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset), value);

    [Theory]
    [InlineData(5453.6387f, 0f)]
    [InlineData(5324.4893f, 129.1494f)]
    [InlineData(5261.244f, 192.3947f)]
    [InlineData(0f, 5453.6387f)]
    public void RemainingDistanceBecomesIncreasingTravelledDistance(float remaining, float expected)
    {
        var length = new byte[4]; var distance = new byte[4];
        Write(length, 0, 5453.6387f); Write(distance, 0, remaining);
        var result = Wrc7VehicleTelemetry.DecodeProgress(length, distance, new());
        Assert.InRange(Math.Abs(result.LapDistance - expected), 0, .001f);
        Assert.Equal(result.LapDistance / result.TrackLength, result.CompletionRate);
    }
}
