using Xunit;

namespace ZTMZ.PacenoteTool.Avalonia.Tests;

public class ProgramDisplayBackendTest
{
    [Theory]
    [InlineData(true, "wayland-0", null)]
    [InlineData(true, "wayland-0", "")]
    [InlineData(true, "wayland-0", "x11")]
    [InlineData(false, "wayland-0", "wayland")]
    [InlineData(true, null, "wayland")]
    public void X11IsTheDefaultBackend(
        bool isLinux,
        string? waylandDisplay,
        string? requestedBackend)
    {
        Assert.False(Program.ShouldUseWayland(
            isLinux,
            waylandDisplay,
            requestedBackend));
    }

    [Theory]
    [InlineData("wayland")]
    [InlineData("WAYLAND")]
    [InlineData(" wayland ")]
    public void NativeWaylandRequiresExplicitOptIn(string requestedBackend)
    {
        Assert.True(Program.ShouldUseWayland(
            true,
            "wayland-0",
            requestedBackend));
    }
}
