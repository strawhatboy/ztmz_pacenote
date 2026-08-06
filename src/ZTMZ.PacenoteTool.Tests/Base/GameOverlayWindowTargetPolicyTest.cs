using ZTMZ.PacenoteTool.Base.UI;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class GameOverlayWindowTargetPolicyTest
{
    [Fact]
    public void SelectTargetPrefersTheForegroundWindowOwnedByTheGame()
    {
        var selected = GameOverlayWindowTargetPolicy.SelectTarget(
            foregroundWindow: (IntPtr)0x200,
            foregroundWindowBelongsToGame: true,
            mainWindow: (IntPtr)0x100,
            mainWindowBelongsToGame: true,
            lastKnownWindow: (IntPtr)0x080,
            lastKnownWindowBelongsToGame: true);

        Assert.Equal((IntPtr)0x200, selected);
    }

    [Fact]
    public void SelectTargetIgnoresAForegroundWindowFromAnotherProcess()
    {
        var selected = GameOverlayWindowTargetPolicy.SelectTarget(
            foregroundWindow: (IntPtr)0x200,
            foregroundWindowBelongsToGame: false,
            mainWindow: (IntPtr)0x100,
            mainWindowBelongsToGame: true,
            lastKnownWindow: (IntPtr)0x080,
            lastKnownWindowBelongsToGame: true);

        Assert.Equal((IntPtr)0x100, selected);
    }

    [Fact]
    public void SelectTargetUsesOnlyAnOwnedLastKnownWindowAsFallback()
    {
        var selected = GameOverlayWindowTargetPolicy.SelectTarget(
            foregroundWindow: IntPtr.Zero,
            foregroundWindowBelongsToGame: false,
            mainWindow: IntPtr.Zero,
            mainWindowBelongsToGame: false,
            lastKnownWindow: (IntPtr)0x080,
            lastKnownWindowBelongsToGame: true);

        Assert.Equal((IntPtr)0x080, selected);
    }

    [Fact]
    public void SelectTargetRejectsAnUnownedStaleHandle()
    {
        var selected = GameOverlayWindowTargetPolicy.SelectTarget(
            foregroundWindow: IntPtr.Zero,
            foregroundWindowBelongsToGame: false,
            mainWindow: IntPtr.Zero,
            mainWindowBelongsToGame: false,
            lastKnownWindow: (IntPtr)0x080,
            lastKnownWindowBelongsToGame: false);

        Assert.Equal(IntPtr.Zero, selected);
    }

    [Theory]
    [InlineData(0x100, 0x100, false)]
    [InlineData(0x100, 0x200, true)]
    [InlineData(0x100, 0x000, false)]
    public void NeedsReattachmentRequiresANewValidTarget(long attachedWindow, long targetWindow, bool expected)
    {
        var shouldReattach = GameOverlayWindowTargetPolicy.NeedsReattachment(
            (IntPtr)attachedWindow,
            (IntPtr)targetWindow);

        Assert.Equal(expected, shouldReattach);
    }
}
