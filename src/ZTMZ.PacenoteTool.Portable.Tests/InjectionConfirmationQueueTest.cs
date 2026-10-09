using Xunit;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class InjectionConfirmationQueueTest
{
    [Fact]
    public async Task ConfirmingModeDoesNotRequireAGameOrPerGameConfiguration()
    {
        Assert.True(await InGameOverlayConsent.EnsureAsync(() => true, _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task DecliningSelectionDoesNotGrantPermission()
    {
        var shown = false;
        Assert.False(await InGameOverlayConsent.EnsureAsync(() => true, _ =>
        {
            shown = true;
            return Task.FromResult(false);
        }));
        Assert.True(shown);
    }

    [Fact]
    public async Task SelectionChangedWhileDialogOpenDoesNotGrantPermission()
    {
        var current = true;
        Assert.False(await InGameOverlayConsent.EnsureAsync(() => current, _ =>
        {
            current = false;
            return Task.FromResult(true);
        }));
    }

    [Fact]
    public async Task BridgeAndOverlayCannotOwnThePromptAtTheSameTime()
    {
        var bridge = await InjectionConfirmationQueue.EnterAsync();
        var overlay = InjectionConfirmationQueue.EnterAsync();
        try { Assert.False(overlay.IsCompleted); }
        finally { bridge.Dispose(); }
        using var overlayLease = await overlay.WaitAsync(TimeSpan.FromSeconds(5));
        bridge.Dispose(); // Repeated disposal must not release another owner's slot.
        var next = InjectionConfirmationQueue.EnterAsync();
        try { Assert.False(next.IsCompleted); }
        finally { overlayLease.Dispose(); }
        using var nextLease = await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StaleQueuedOverlayDoesNotShowWarningOrGrantConsent()
    {
        var bridge = await InjectionConfirmationQueue.EnterAsync();
        var current = true;
        var shown = false;
        var result = InGameOverlayConsent.EnsureAsync(() => current, _ =>
        {
            shown = true;
            return Task.FromResult(true);
        });
        current = false;
        bridge.Dispose();
        Assert.False(await result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(shown);
    }
}
