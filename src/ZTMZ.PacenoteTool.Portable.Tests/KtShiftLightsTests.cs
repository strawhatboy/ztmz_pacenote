using System.Buffers.Binary;
using Neo.IronLua;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public class KtShiftLightsTests
{
    [Theory]
    [InlineData(6499, 0)]
    [InlineData(6500, 0)]
    [InlineData(7000, .5f)]
    [InlineData(7499, .999f)]
    [InlineData(7500, 1)]
    [InlineData(8000, 1)]
    public void NativeWarningGetsTheRbrAndDr2DashboardLeadIn(float rpm, float fraction)
    {
        var data = KtShiftLights.ApplyWarning(new GameData { RPM = rpm }, 7500);
        Assert.True(data.ShiftLightsRPMValid);
        Assert.Equal(6500, data.ShiftLightsRPMStart);
        Assert.Equal(7500, data.ShiftLightsRPMEnd);
        Assert.Equal(fraction, data.ShiftLightsFraction);
        data = KtShiftLights.ApplyWarning(data, float.NaN);
        Assert.False(data.ShiftLightsRPMValid);
        Assert.Equal(0, data.ShiftLightsFraction);
        Assert.Equal(0, data.ShiftLightsRPMEnd);
    }

    [Theory]
    [InlineData(898, .9904507f)]
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
        Assert.Equal(fraction, data.ShiftLightsFraction, 5);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(6499, 0)]
    [InlineData(6500, 1)] [InlineData(6833, 1)]
    [InlineData(6834, 2)] [InlineData(7166, 2)]
    [InlineData(7167, 3)] [InlineData(7499, 3)]
    [InlineData(7500, 4)] [InlineData(8021, 4)]
    public void ActualDashboardLuaRendersAllFiveStatesFromKtTelemetry(float rpm, int level)
    {
        using var lua = new Lua();
        var environment = lua.CreateEnvironment();
        environment.DoChunk(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "commonfunc.lua")), "commonfunc.lua");
        var data = KtShiftLights.ApplyWarning(new() { RPM = rpm, MaxRPM = 8021.409f }, 7500);
        // An empty configuration deliberately catches fallback to percentage
        // thresholds instead of the provider's native warning point.
        Assert.Equal(level, Convert.ToInt32(environment.CallMember("getRPMLevel", data, new LuaTable())[0]));
        // Also exercise Lua numeric fields, as used by browser/native dashboards.
        var table = new LuaTable(); table["RPM"] = (double)data.RPM; table["MaxRPM"] = (double)data.MaxRPM;
        table["ShiftLightsRPMValid"] = true; table["ShiftLightsRPMStart"] = (double)data.ShiftLightsRPMStart;
        table["ShiftLightsRPMEnd"] = (double)data.ShiftLightsRPMEnd;
        Assert.Equal(level, Convert.ToInt32(environment.CallMember("getRPMLevel", table, new LuaTable())[0]));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void InvalidWarningCannotCreateALightRange(float warning)
    {
        var data = KtShiftLights.ApplyWarning(new() { RPM = 7000 }, warning);
        Assert.False(data.ShiftLightsRPMValid); Assert.Equal(0, data.ShiftLightsFraction);
    }
}
