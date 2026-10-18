using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.Base.Game;
#if WINDOWS
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.UI.Windows;
#endif

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal sealed record DesktopNativeOverlaySnapshot(
    bool IsSupported,
    bool IsEnabled,
    bool IsInitializing,
    int FrameRate,
    bool IsFrameRateLocked,
    bool IsTopMost,
    string State,
    bool IsVisible,
    string? BackendStatusKey = null,
    OverlayFailureInfo? Failure = null);

internal interface IDesktopNativeOverlayHost : IDisposable
{
    event EventHandler? Changed;
    DesktopNativeOverlaySnapshot GetSnapshot();
    bool SetEnabled(bool enabled);
    bool SetFrameRateLock(bool enabled);
    bool SetFrameRate(int frameRate);
    bool SetTopMost(bool enabled);
    void ReloadDashboardConfigurations();
    void HandleActivityChanged(GameActivitySessionChange change);
}

#if WINDOWS
/// <summary>
/// Avalonia presentation adapter for the shared Windows native-overlay session.
/// </summary>
internal sealed class WindowsNativeOverlayHost : IDesktopNativeOverlayHost
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly NativeOverlaySessionController _session;

    public WindowsNativeOverlayHost(
        ZTMZPacenoteTool tool,
        Func<CoDriverPackageInfo?> selectedCoDriver)
    {
        // Avalonia owns the Web Dashboard server. The native renderer only serves
        // Windows overlay drawing in this host.
        _session = new NativeOverlaySessionController(
            tool,
            selectedCoDriver,
            new GameOverlayManager(manageWebDashboard: false),
            ownsOverlay: true,
            confirmSelection: async isCurrent => await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!isCurrent()) return false;
                var text = I18NLoader.Instance;
                var dialog = FaContentDialogFactory.Create(new FluentAvalonia.UI.Controls.FAContentDialog
                {
                    Title = text["overlayBackend.warningTitle"],
                    Content = new global::Avalonia.Controls.TextBlock
                    {
                        Text = text["overlayBackend.warningBody"],
                        TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                        MaxWidth = 520
                    },
                    PrimaryButtonText = text["overlayBackend.warningEnable"],
                    CloseButtonText = text["overlayBackend.warningKeepWindow"],
                    DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Close
                });
                return await dialog.ShowAsync() == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary;
            }));
        _session.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public DesktopNativeOverlaySnapshot GetSnapshot()
    {
        var status = _session.CurrentStatus;
        return new DesktopNativeOverlaySnapshot(
            IsSupported: true,
            IsEnabled: Config.Instance.UI_ShowHud,
            IsInitializing: _session.IsInitializing,
            FrameRate: Config.Instance.HudFPS,
            IsFrameRateLocked: Config.Instance.HudLockFPS,
            IsTopMost: Config.Instance.HudTopMost,
            State: status.State.ToString(),
            IsVisible: status.IsVisible,
            BackendStatusKey: _session.BackendDecision.StatusKey,
            Failure: _session.Failure);
    }

    public bool SetEnabled(bool enabled)
    {
        if (Config.Instance.UI_ShowHud == enabled)
        {
            _session.Overlay.RefreshNativeOverlayVisibility();
            return true;
        }

        Config.Instance.UI_ShowHud = enabled;
        return SaveConfigurationAndRefresh();
    }

    public bool SetFrameRateLock(bool enabled)
    {
        Config.Instance.HudLockFPS = enabled;
        _session.Overlay.SetFPS(enabled ? Config.Instance.HudFPS : 0);
        return SaveConfigurationAndRefresh();
    }

    public bool SetFrameRate(int frameRate)
    {
        Config.Instance.HudFPS = Math.Clamp(frameRate, 30, 240);
        _session.Overlay.SetFPS(Config.Instance.HudLockFPS ? Config.Instance.HudFPS : 0);
        return SaveConfigurationAndRefresh();
    }

    public bool SetTopMost(bool enabled)
    {
        Config.Instance.HudTopMost = enabled;
        _session.Overlay.SetTopMost(enabled);
        return SaveConfigurationAndRefresh();
    }

    public void ReloadDashboardConfigurations() => _session.Overlay.ReloadNativeDashboardConfigurations();

    public void HandleActivityChanged(GameActivitySessionChange change) => _session.HandleActivityChanged(change);

    private bool SaveConfigurationAndRefresh()
    {
        try
        {
            Config.Instance.SaveUserConfig();
            _session.Overlay.RefreshNativeOverlayVisibility();
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist native overlay settings.");
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    public void Dispose() => _session.Dispose();
}
#endif
