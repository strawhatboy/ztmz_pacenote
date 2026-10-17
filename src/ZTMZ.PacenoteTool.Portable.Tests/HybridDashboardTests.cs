using Neo.IronLua;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;

public class HybridDashboardTests
{
    [Theory]
    [InlineData(true, true, true, "boost")]
    [InlineData(false, true, true, "ready")]
    [InlineData(false, false, true, "regen")]
    [InlineData(false, false, false, "standby")]
    public void SharedLuaReadsClrSnapshotAndPrioritizesState(bool active, bool ready, bool regen, string state)
    {
        using var lua = new Lua(); var env = lua.CreateEnvironment();
        env.DoChunk(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "commonfunc.lua")), "commonfunc.lua");
        var data = new GameData { Hybrid = new("wrcg", DateTimeOffset.UtcNow) {
            BatteryChargeFraction = .8f, BoostProgressFraction = .35f, BoostMap = 2,
            BoostActive = active, BoostReady = ready, RegenerationActive = regen } };
        var view = Assert.IsType<LuaTable>(env.CallMember("getHybridDisplayState", data)[0]);
        Assert.Equal(.8, Convert.ToDouble(view["battery"]), 5);
        Assert.Equal(.35, Convert.ToDouble(view["progress"]), 5);
        Assert.Equal(2, Convert.ToInt32(view["map"])); Assert.Equal(state, view["state"]);
        Assert.Null(env.CallMember("getHybridDisplayState", default(GameData))[0]);
    }

    [Fact]
    public void MissingAndInvalidValuesAreNotDisplayedAsZeroOrReady()
    {
        using var lua = new Lua(); var env = lua.CreateEnvironment();
        env.DoChunk(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "commonfunc.lua")), "commonfunc.lua");
        var data = new GameData { Hybrid = new("test", default) { BatteryChargeFraction = float.NaN,
            BoostProgressFraction = float.PositiveInfinity, BoostMap = 0 } };
        Assert.Null(env.CallMember("getHybridDisplayState", data)[0]);
        data.Hybrid = data.Hybrid with { BatteryChargeFraction = 0 };
        var view = Assert.IsType<LuaTable>(env.CallMember("getHybridDisplayState", data)[0]);
        Assert.Equal(0, Convert.ToDouble(view["battery"])); Assert.Null(view["progress"]);
        Assert.Null(view["map"]); Assert.Equal("hybrid", view["state"]);
    }
}
