using ZTMZ.PacenoteTool.UI.Windows;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class NativeOverlayLayeringPolicyTest
{
    [Theory]
    [InlineData("dirtrally2", false, false)]
    [InlineData("dirtrally2", true, true)]
    [InlineData("acr", true, false)]
    [InlineData("ACR", true, false)]
    [InlineData("RichardBurnsRally", true, false)]
    [InlineData("RichardBurnsRally_SSE", true, false)]
    public void ShouldPromoteToTopMostUsesCompatibleLayeringForTheActiveGame(
        string processName,
        bool isGameWindowForeground,
        bool expected)
    {
        Assert.Equal(
            expected,
            NativeOverlayLayeringPolicy.ShouldPromoteToTopMost(
                processName,
                isGameWindowForeground));
    }
}
