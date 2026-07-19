using Neo.IronLua;
using ZTMZ.PacenoteTool.Base.UI;

namespace ZTMZ.PacenoteTool.Tests.Base.UI;

public class NativeStaticImageLayerTest
{
    [Fact]
    public void BoundsCaptureUnionsAndClipsStaticImageBounds()
    {
        var capture = new NativeStaticImageBoundsCapture(1920, 1080);

        capture.Include(100, 120, 700, 460);
        capture.Include(-50, 300, 250, 1200);

        Assert.True(capture.TryGetBounds(out var bounds));
        Assert.Equal(0, bounds.Left);
        Assert.Equal(120, bounds.Top);
        Assert.Equal(700, bounds.Right);
        Assert.Equal(1080, bounds.Bottom);
    }

    [Fact]
    public void BoundsCaptureRejectsArtworkOutsideTheOverlay()
    {
        var capture = new NativeStaticImageBoundsCapture(1920, 1080);

        capture.Include(-100, -100, -10, -10);

        Assert.False(capture.TryGetBounds(out _));
    }

    [Fact]
    public void NativeStaticImageCachingRequiresExplicitDashboardOptIn()
    {
        Assert.False(new DashboardDescriptor().SupportsNativeStaticImageCache);
        Assert.True(new DashboardDescriptor { SupportsNativeStaticImageCache = true }.SupportsNativeStaticImageCache);
    }

    [Fact]
    public void LuaCanInvokeTheNativeStaticLayerProxy()
    {
        using var lua = new Lua();
        var environment = lua.CreateEnvironment();
        environment.DefaultCompileOptions = new LuaCompileOptions { ClrEnabled = false };
        environment.DoChunk(
            "function render(args) args.NativeStaticLayerGraphics.Include(20, 30, 120, 230) end",
            "native-static-layer-test.lua");
        var capture = new NativeStaticImageBoundsCapture(1920, 1080);

        environment.CallMember(
            "render",
            new DashboardScriptArguments { NativeStaticLayerGraphics = capture });

        Assert.True(capture.TryGetBounds(out var bounds));
        Assert.Equal(new NativeStaticImageLayerBounds(20, 30, 120, 230), bounds);
    }
}
