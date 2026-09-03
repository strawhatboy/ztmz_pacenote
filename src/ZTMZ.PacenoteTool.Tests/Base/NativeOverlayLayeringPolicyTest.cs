using ZTMZ.PacenoteTool.UI.Windows;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class NativeOverlayLayeringPolicyTest
{
    [Theory]
    [InlineData("dirtrally2")]
    [InlineData("WRC")]
    [InlineData("BeamNG.drive.x64")]
    [InlineData("RichardBurnsRally_SSE")]
    [InlineData("RichardBurnsRally")]
    public void ForegroundGamesPromoteTheOverlay(string processName)
    {
        Assert.True(NativeOverlayLayeringPolicy.ShouldPromoteToTopMost(processName, true));
    }

    [Theory]
    [InlineData("dirtrally2")]
    [InlineData("RichardBurnsRally_SSE")]
    public void BackgroundGamesDoNotPromoteTheOverlay(string processName)
    {
        Assert.False(NativeOverlayLayeringPolicy.ShouldPromoteToTopMost(processName, false));
    }

    [Theory]
    [InlineData("acr")]
    [InlineData("ACR")]
    public void AcrRetainsItsExistingForegroundOptOut(string processName)
    {
        Assert.False(NativeOverlayLayeringPolicy.ShouldPromoteToTopMost(processName, true));
    }
}
