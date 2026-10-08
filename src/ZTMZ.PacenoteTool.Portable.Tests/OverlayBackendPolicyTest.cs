using Xunit;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class OverlayBackendPolicyTest
{
    [Theory]
    [InlineData(1849250u, "anything")]
    [InlineData(0u, "WRC")]
    [InlineData(0u, "wrc.exe")]
    public void WrcNeverAllowsInjectionEvenWhenIntegrationIsAvailable(uint id, string executable)
    {
        var result = OverlayBackendPolicy.Evaluate(OverlayDisplayBackend.InGame,
            id, executable, true, true, true);
        Assert.False(result.CanAttachInGame);
        Assert.Equal(OverlayDisplayBackend.SeparateWindow, result.Effective);
        Assert.Equal("overlayBackend.antiCheat", result.StatusKey);
    }

    [Fact]
    public void SelectingInGameDoesNotClaimItIsAvailable()
    {
        var result = OverlayBackendPolicy.Evaluate(OverlayDisplayBackend.InGame,
            690790, "dirtrally2", true, true, false);
        Assert.Equal(OverlayDisplayBackend.InGame, result.Requested);
        Assert.Equal(OverlayDisplayBackend.SeparateWindow, result.Effective);
        Assert.False(result.CanAttachInGame);
        Assert.Equal("overlayBackend.integrationPending", result.StatusKey);
    }

    [Fact]
    public void SwitchingGamesReevaluatesCompatibility()
    {
        var dr2 = OverlayBackendPolicy.Evaluate(OverlayDisplayBackend.InGame,
            690790, "dirtrally2", true, true, true);
        var wrc = OverlayBackendPolicy.Evaluate(OverlayDisplayBackend.InGame,
            1849250, "WRC", true, true, true);
        Assert.True(dr2.CanAttachInGame);
        Assert.False(wrc.CanAttachInGame);
        Assert.Equal(OverlayDisplayBackend.InGame, wrc.Requested);
    }

    [Fact]
    public void NonWindowsDoesNotUseTheWindowsDll()
    {
        var result = OverlayBackendPolicy.Evaluate(OverlayDisplayBackend.InGame,
            690790, "dirtrally2", true, false, true);
        Assert.False(result.CanAttachInGame);
        Assert.Equal("overlayBackend.platformUnavailable", result.StatusKey);
    }

    [Fact]
    public void NoGameCannotBeAnInjectionTarget()
    {
        var result = OverlayBackendPolicy.Evaluate(OverlayDisplayBackend.InGame,
            null, null, false, true, true);
        Assert.False(result.CanAttachInGame);
        Assert.Equal("overlayBackend.waitingForGame", result.StatusKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void DefaultAndUnknownPreferencesKeepTheExistingBackend(int preference)
    {
        var result = OverlayBackendPolicy.Evaluate((OverlayDisplayBackend)preference,
            690790, "dirtrally2", true, true, true);
        Assert.Equal(OverlayDisplayBackend.SeparateWindow, result.Effective);
        Assert.False(result.CanAttachInGame);
    }
}
