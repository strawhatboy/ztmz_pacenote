using System.Buffers.Binary;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public class KtShiftLightsTests
{
    [Theory]
    [InlineData(7499, 0)]
    [InlineData(7500, 1)]
    [InlineData(8000, 1)]
    public void SingleWarningPointDoesNotInventProgressiveLedThresholds(float rpm, float fraction)
    {
        var data = KtShiftLights.ApplyWarning(new GameData { RPM = rpm }, 7500);
        Assert.True(data.ShiftLightsRPMValid);
        Assert.Equal(7500, data.ShiftLightsRPMStart);
        Assert.Equal(7500, data.ShiftLightsRPMEnd);
        Assert.Equal(fraction, data.ShiftLightsFraction);
        data = KtShiftLights.ApplyWarning(data, float.NaN);
        Assert.False(data.ShiftLightsRPMValid);
        Assert.Equal(0, data.ShiftLightsFraction);
        Assert.Equal(0, data.ShiftLightsRPMEnd);
    }

    [Theory]
    [InlineData(898, 0)]
    [InlineData(899, 1)]
    [InlineData(900, 1)]
    public void Wrc7WarningUsesNativeHudOmegaLimitMinusOne(float omega, float fraction)
    {
        var engine = new byte[0xb0];
        var limits = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(engine.AsSpan(8), omega);
        BinaryPrimitives.WriteSingleLittleEndian(limits, 900);
        BinaryPrimitives.WriteSingleLittleEndian(limits.AsSpan(4), 80);
        var data = Wrc7VehicleTelemetry.Decode(engine, limits, new());
        Assert.True(data.ShiftLightsRPMValid);
        Assert.Equal(899 * 60 / (2 * MathF.PI), data.ShiftLightsRPMEnd, 2);
        Assert.Equal(fraction, data.ShiftLightsFraction);
    }
}
