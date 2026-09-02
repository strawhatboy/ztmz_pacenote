using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class WebDashboardCanvasResolutionTest
{
    [Theory]
    [InlineData("640x360", 640, 360)]
    [InlineData("960x540", 960, 540)]
    [InlineData("1280x720", 1280, 720)]
    [InlineData("1920x1080", 1920, 1080)]
    public void ParseReturnsSupportedRenderSize(string value, int width, int height)
    {
        Assert.Equal((width, height), WebDashboardCanvasResolution.Parse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("800x600")]
    [InlineData("invalid")]
    public void UnsupportedResolutionUsesBalancedDefault(string? value)
    {
        Assert.Equal(
            WebDashboardCanvasResolution.Default,
            WebDashboardCanvasResolution.Normalize(value));
        Assert.Equal((1280, 720), WebDashboardCanvasResolution.Parse(value));
    }
}
