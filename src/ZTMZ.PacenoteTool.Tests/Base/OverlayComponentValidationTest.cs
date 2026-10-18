using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;
using ZTMZ.PacenoteTool.UI.Windows;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class OverlayComponentValidationTest
{
    [Fact]
    public void MissingThenInvalidComponentIsRecheckedBeforeAnyRemoteLoadAttempt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ztmz-overlay-check-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "ztmz_dashboard_overlay.dll");
        using var process = Process.GetCurrentProcess();
        try
        {
            // Both failures must happen before opening a target or caching a remote load.
            var missing = Assert.Throws<OverlayComponentException>(() => { _ = InGameOverlayLoader.LoadAsync(process, path); });
            Assert.Equal(path, missing.ComponentPath);
            Assert.Equal("overlayBackend.componentMissing", OverlayFailureInfo.FromException(missing).StatusKey);
            File.WriteAllText(path, "damaged component, not executable");
            var invalid = Assert.Throws<OverlayComponentException>(() => { _ = InGameOverlayLoader.LoadAsync(process, path); });
            Assert.Equal(path, invalid.ComponentPath);
            Assert.Equal("overlayBackend.componentInvalid", OverlayFailureInfo.FromException(invalid).StatusKey);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void FileReadFailurePreservesExactComponentAndCause()
    {
        var path = Path.Combine(Path.GetTempPath(), "ztmz-overlay-" + Guid.NewGuid() + ".dll");
        try
        {
            using var locked = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var error = Assert.Throws<OverlayComponentException>(() => OverlayComponentValidation.ValidateBundle(path));
            Assert.Equal(path, error.ComponentPath);
            Assert.IsAssignableFrom<IOException>(error.InnerException);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ControllerExposesFailureInsteadOfClaimingWindowFallbackIsActive()
    {
        // Avoid creating a real overlay, audio devices, or game process.
        var controller = (NativeOverlaySessionController)RuntimeHelpers.GetUninitializedObject(typeof(NativeOverlaySessionController));
        var failure = OverlayFailureInfo.FromException(new FileNotFoundException("Missing", "overlay.dll"));
        typeof(NativeOverlaySessionController).GetField("_backendFailure", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, failure);
        Assert.Same(failure, controller.Failure);
        Assert.Equal(failure.StatusKey, controller.BackendDecision.StatusKey);
        Assert.Equal(OverlayBackendSettings.Requested, controller.BackendDecision.Effective);
        Assert.False(controller.BackendDecision.CanAttachInGame);
        Assert.False(controller.CurrentStatus.IsVisible);
        Assert.Equal(Config.Instance.UI_ShowHud ? NativeOverlayState.Unavailable : NativeOverlayState.Disabled,
            controller.CurrentStatus.State);
    }

    [Fact]
    public void WorkingPreviewRemainsVisibleWhileTheInGameFailureIsAvailable()
    {
        var controller = (NativeOverlaySessionController)RuntimeHelpers.GetUninitializedObject(typeof(NativeOverlaySessionController));
        var overlay = (GameOverlayManager)RuntimeHelpers.GetUninitializedObject(typeof(GameOverlayManager));
        var fields = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(GameOverlayManager).GetField("_nativeOverlayStatusLock", fields)!.SetValue(overlay, new object());
        typeof(GameOverlayManager).GetField("_nativeOverlayStatus", fields)!.SetValue(overlay,
            new NativeOverlayStatus(NativeOverlayState.Preview, true));
        typeof(NativeOverlaySessionController).GetField("_overlay", fields)!.SetValue(controller, overlay);
        var failure = OverlayFailureInfo.FromException(new FileNotFoundException("Missing", "overlay.dll"));
        typeof(NativeOverlaySessionController).GetField("_backendFailure", fields)!.SetValue(controller, failure);
        Assert.Equal(new NativeOverlayStatus(NativeOverlayState.Preview, true), controller.CurrentStatus);
        Assert.Same(failure, controller.Failure);
    }
}
