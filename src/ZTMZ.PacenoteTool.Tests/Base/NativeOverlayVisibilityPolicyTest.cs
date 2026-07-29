using ZTMZ.PacenoteTool.Base.UI;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class NativeOverlayVisibilityPolicyTest
{
    [Fact]
    public void ResolveKeepsAnUninitializedOverlayHidden()
    {
        var status = NativeOverlayVisibilityPolicy.Resolve(
            isOverlayReady: false,
            isPreviewActive: false,
            isNativeHudEnabled: true,
            isStageActive: true,
            hasNativeDashboard: true);

        Assert.Equal(NativeOverlayState.Unavailable, status.State);
        Assert.False(status.IsVisible);
    }

    [Fact]
    public void ResolveHidesTheOverlayWhenTheNativeHudIsDisabled()
    {
        var status = NativeOverlayVisibilityPolicy.Resolve(
            isOverlayReady: true,
            isPreviewActive: false,
            isNativeHudEnabled: false,
            isStageActive: true,
            hasNativeDashboard: true);

        Assert.Equal(NativeOverlayState.Disabled, status.State);
        Assert.False(status.IsVisible);
    }

    [Fact]
    public void ResolveHidesTheOverlayWhenNoDashboardTargetsIt()
    {
        var status = NativeOverlayVisibilityPolicy.Resolve(
            isOverlayReady: true,
            isPreviewActive: false,
            isNativeHudEnabled: true,
            isStageActive: true,
            hasNativeDashboard: false);

        Assert.Equal(NativeOverlayState.NoDashboard, status.State);
        Assert.False(status.IsVisible);
    }

    [Fact]
    public void ResolveKeepsAnAttachedOverlayHiddenBetweenStages()
    {
        var status = NativeOverlayVisibilityPolicy.Resolve(
            isOverlayReady: true,
            isPreviewActive: false,
            isNativeHudEnabled: true,
            isStageActive: false,
            hasNativeDashboard: true);

        Assert.Equal(NativeOverlayState.Ready, status.State);
        Assert.False(status.IsVisible);
    }

    [Fact]
    public void ResolveShowsTheOverlayDuringAnActiveStage()
    {
        var status = NativeOverlayVisibilityPolicy.Resolve(
            isOverlayReady: true,
            isPreviewActive: false,
            isNativeHudEnabled: true,
            isStageActive: true,
            hasNativeDashboard: true);

        Assert.Equal(NativeOverlayState.Active, status.State);
        Assert.True(status.IsVisible);
    }

    [Fact]
    public void ResolveLetsTheExplicitPreviewOverrideTheNativeHudSwitch()
    {
        var status = NativeOverlayVisibilityPolicy.Resolve(
            isOverlayReady: true,
            isPreviewActive: true,
            isNativeHudEnabled: false,
            isStageActive: false,
            hasNativeDashboard: true);

        Assert.Equal(NativeOverlayState.Preview, status.State);
        Assert.True(status.IsVisible);
    }
}
