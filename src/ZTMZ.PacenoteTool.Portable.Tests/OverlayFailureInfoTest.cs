using System.ComponentModel;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public class OverlayFailureInfoTest
{
    [Fact]
    public void MissingFileRetainsFilenameWithoutClaimingAntivirusDeletedIt()
    {
        var failure = OverlayFailureInfo.FromException(new FileNotFoundException("Missing", "overlay.dll"));
        Assert.Equal("overlayBackend.componentMissing", failure.StatusKey);
        Assert.Equal("overlay.dll", failure.ComponentPath);
        Assert.Contains("FileNotFoundException", failure.Details);
    }

    [Theory]
    [InlineData(2, "overlayBackend.componentMissing")]
    [InlineData(3, "overlayBackend.componentMissing")]
    [InlineData(5, "overlayBackend.componentBlocked")]
    [InlineData(225, "overlayBackend.componentBlocked")]
    [InlineData(226, "overlayBackend.componentBlocked")]
    [InlineData(577, "overlayBackend.componentBlocked")]
    [InlineData(193, "overlayBackend.componentInvalid")]
    [InlineData(299, "overlayBackend.loadFailed")]
    public void WindowsFailureCodesSurviveComponentContext(int code, string expected)
    {
        var error = new IOException("Cannot use component", new Win32Exception(code));
        var failure = OverlayFailureInfo.FromException(error, "injector.exe");
        Assert.Equal(expected, failure.StatusKey);
        Assert.Equal("injector.exe", failure.ComponentPath);
        Assert.Contains("Win32Exception", failure.Details);
    }

    [Fact]
    public void MissingDirectoryAndAccessDeniedAreDistinct()
    {
        Assert.Equal("overlayBackend.componentMissing", OverlayFailureInfo.FromException(new DirectoryNotFoundException()).StatusKey);
        Assert.Equal("overlayBackend.componentBlocked", OverlayFailureInfo.FromException(new UnauthorizedAccessException()).StatusKey);
    }

    [Fact]
    public void InvalidBinaryIsNotReportedAsAntivirusBlock()
    {
        Assert.Equal("overlayBackend.componentInvalid", OverlayFailureInfo.FromException(new BadImageFormatException()).StatusKey);
        Assert.Equal("overlayBackend.componentInvalid", OverlayFailureInfo.FromException(new InvalidDataException()).StatusKey);
    }

    [Fact]
    public void AccessToGameIsNotMisreportedAsAQuarantinedDll()
    {
        var failure = OverlayFailureInfo.FromException(new Win32Exception(5, "Cannot access game for overlay loading"));
        Assert.Equal("overlayBackend.gameAccessDenied", failure.StatusKey);
        Assert.Null(failure.ComponentPath);
    }

    [Fact]
    public void UnknownLoadOutcomeRetainsRestartGuidanceAndOriginalDetails()
    {
        var failure = OverlayFailureInfo.FromException(new TimeoutException("Remote loader has not returned"));
        Assert.Equal("overlayBackend.loadFailed", failure.StatusKey);
        Assert.Contains("Remote loader has not returned", failure.Details);
    }
}
