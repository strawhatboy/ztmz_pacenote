using ZTMZ.PacenoteTool.Base.Game;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public class CarDamageProjectionTest
{
    [Fact]
    public void PreservesSeparateGearboxAndLocatedBodyDamage()
    {
        var damage = CarDamageSnapshot.Create("test", DateTimeOffset.UtcNow, [],
        [
            new("gearbox", "gearbox", "", true, true, 3, .8f),
            new("bodywork:front", "bodywork", "front", true, true, 2, .4f),
            new("bodywork:rear", "bodywork", "rear", false, true, 4, 1f)
        ], []);
        Assert.True(damage.Dashboard.GearboxAvailable);
        Assert.True(damage.Dashboard.BodyworkAvailable);
        Assert.Equal(3, damage.Dashboard.GearboxSeverity);
        Assert.Equal(2, damage.Dashboard.BodyworkSeverity);
        Assert.Equal(0, damage.Dashboard.EngineSeverity);
        Assert.Equal(3, damage.Dashboard.GeneralSeverity);
    }

    [Fact]
    public void MissingPartsAreNotReportedAsAvailable()
    {
        var damage = CarDamageSnapshot.Create("test", DateTimeOffset.UtcNow, [],
            [new("engine", "engine", "", true, false, 0, 0)], []);
        Assert.False(damage.Dashboard.GearboxAvailable);
        Assert.False(damage.Dashboard.BodyworkAvailable);
    }
}
