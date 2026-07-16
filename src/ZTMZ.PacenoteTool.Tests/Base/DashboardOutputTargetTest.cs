using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class DashboardOutputTargetTest
{
    [Theory]
    [InlineData(true, DashboardOutputTarget.Both)]
    [InlineData(false, DashboardOutputTarget.None)]
    public void ResolveUsesLegacyEnabledStateWhenThereIsNoOverride(
        bool legacyEnabled,
        DashboardOutputTarget expected)
    {
        var target = DashboardOutputTargetResolver.Resolve(
            new Dictionary<string, DashboardOutputTarget>(),
            "modern_wrc_2",
            legacyEnabled);

        Assert.Equal(expected, target);
    }

    [Theory]
    [InlineData(DashboardOutputTarget.None)]
    [InlineData(DashboardOutputTarget.Overlay)]
    [InlineData(DashboardOutputTarget.Web)]
    [InlineData(DashboardOutputTarget.Both)]
    public void ResolveUsesSavedOutputTarget(DashboardOutputTarget expected)
    {
        var targets = new Dictionary<string, DashboardOutputTarget>(StringComparer.OrdinalIgnoreCase)
        {
            ["modern_wrc_2"] = expected
        };

        var target = DashboardOutputTargetResolver.Resolve(targets, "MODERN_WRC_2", legacyEnabled: false);

        Assert.Equal(expected, target);
    }

    [Fact]
    public void ResolveFallsBackWhenStoredOutputTargetIsInvalid()
    {
        var targets = new Dictionary<string, DashboardOutputTarget>
        {
            ["modern_wrc_2"] = (DashboardOutputTarget)8
        };

        var target = DashboardOutputTargetResolver.Resolve(targets, "modern_wrc_2", legacyEnabled: true);

        Assert.Equal(DashboardOutputTarget.Both, target);
    }

    [Fact]
    public void OutputTargetsExposeTheirExpectedDestinations()
    {
        Assert.False(DashboardOutputTarget.None.Includes(DashboardOutputTarget.Overlay));
        Assert.True(DashboardOutputTarget.Overlay.Includes(DashboardOutputTarget.Overlay));
        Assert.False(DashboardOutputTarget.Overlay.Includes(DashboardOutputTarget.Web));
        Assert.True(DashboardOutputTarget.Web.Includes(DashboardOutputTarget.Web));
        Assert.False(DashboardOutputTarget.Web.Includes(DashboardOutputTarget.Overlay));
        Assert.True(DashboardOutputTarget.Both.Includes(DashboardOutputTarget.Overlay));
        Assert.True(DashboardOutputTarget.Both.Includes(DashboardOutputTarget.Web));
    }
}
