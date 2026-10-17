using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;
using ZTMZ.PacenoteTool.AC;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class ForceFeedbackTelemetryTests
{
    [Theory]
    [InlineData(-1f)]
    [InlineData(-.2879168f)]
    [InlineData(0f)]
    [InlineData(.47672224f)]
    [InlineData(1f)]
    public void VerifiedSharedMemoryOutputPreservesSignAndZero(float output)
    {
        var data = ACRGameDataConverter.Convert(new()
        {
            PhysicsDataValid = true,
            PhysicsData = new() { ForceFeedbackValue = output, SteeringAngle = .3f }
        });
        Assert.NotNull(data.ForceFeedback);
        Assert.Equal("ACR", data.ForceFeedback.Source);
        Assert.Equal(output, data.ForceFeedback.OutputFraction);
        Assert.Equal(.3f, data.Steering);
        Assert.NotEqual(default, data.ForceFeedback.CapturedAtUtc);
        Assert.Equal(308, Marshal.OffsetOf<ACRPhysicsData>(nameof(ACRPhysicsData.ForceFeedbackValue)).ToInt32());
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1.01f)]
    [InlineData(1.01f)]
    public void CorruptOutputIsUnavailableWithoutSuppressingOtherTelemetry(float output)
    {
        var data = ACRGameDataConverter.Convert(new()
        {
            PhysicsDataValid = true,
            PhysicsData = new() { ForceFeedbackValue = output, SpeedKilometersPerHour = 60 }
        });
        Assert.Null(data.ForceFeedback);
        Assert.Equal(60, data.Speed);
    }

    [Fact]
    public void FailedPhysicsReadCannotLookLikeValidZeroOrRetainLastOutput()
    {
        var raw = new ACRGameRawData { PhysicsDataValid = true, PhysicsData = new() { ForceFeedbackValue = .5f } };
        Assert.NotNull(ACRGameDataConverter.Convert(raw).ForceFeedback);
        raw.PhysicsDataValid = false;
        Assert.Null(ACRGameDataConverter.Convert(raw).ForceFeedback);
        Assert.Null(ACRGameDataConverter.Convert(new()).ForceFeedback);
        Assert.Null(new ZTMZ.PacenoteTool.Base.Game.GameData().ForceFeedback);
    }

    [Fact]
    public void ProtonUsesSamePackedFieldAndClearsOutputOnDisconnect()
    {
        var reader = new ACRProtonGameDataReader();
        var process = typeof(ACRProtonGameDataReader).GetMethod("ProcessTelemetry", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var availability = typeof(ACRProtonGameDataReader).GetMethod("SetDataAvailable", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var payload = new byte[ACRRelayProtocol.TelemetrySize];
        var offset = ACRRelayProtocol.GraphicsSize + Marshal.OffsetOf<ACRPhysicsData>(nameof(ACRPhysicsData.ForceFeedbackValue)).ToInt32();
        foreach (var output in new[] { -.25f, 0f, 1f })
        {
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset, 4), output);
            process.Invoke(reader, [payload]);
            Assert.Equal(output, reader.CurrentGameData.ForceFeedback.OutputFraction);
        }
        availability.Invoke(reader, [false]);
        Assert.Null(reader.CurrentGameData.ForceFeedback);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset, 4), float.NaN);
        process.Invoke(reader, [payload]);
        Assert.Null(reader.CurrentGameData.ForceFeedback);
    }
}
