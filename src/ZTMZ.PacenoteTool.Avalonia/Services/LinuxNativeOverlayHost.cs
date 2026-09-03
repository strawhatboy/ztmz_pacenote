#if !WINDOWS
#nullable enable

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

/// <summary>
/// Hosts Overlay-routed browser dashboards in a transparent X11 window. Gamescope
/// recognizes the same external-overlay atom used by mangoapp.
/// </summary>
internal sealed class LinuxNativeOverlayHost : IDesktopNativeOverlayHost
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();
    private readonly ZTMZPacenoteTool _tool;
    private readonly string _dashboardPackagesPath;
    private readonly string _staticAssetsRoot;
    private WebDashboardRuntimeController? _runtime;
    private LinuxOverlayWindow? _window;
    private IGame? _attachedGame;
    private Task? _startTask;
    private string _state = "Unavailable";
    private int _isInitializing;
    private int _stageActive;
    private int _windowReady;
    private int _windowVisible;
    private int _hasOverlayDashboards;
    private long _startGeneration;
    private bool _disposed;

    public LinuxNativeOverlayHost(
        ZTMZPacenoteTool tool,
        string dashboardPackagesPath,
        string staticAssetsRoot)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _dashboardPackagesPath = dashboardPackagesPath ??
            throw new ArgumentNullException(nameof(dashboardPackagesPath));
        _staticAssetsRoot = staticAssetsRoot ?? throw new ArgumentNullException(nameof(staticAssetsRoot));

        _tool.onRaceBegin += OnRaceBegin;
        _tool.onRaceEnd += OnRaceEnd;
        _tool.onGameEnded += OnGameEnded;
        _tool.onGameUninitialized += OnGameUninitialized;
        _tool.onGameStateChanged += OnGameStateChanged;

        RefreshDashboardAvailability();
        if (Config.Instance.UI_ShowHud)
        {
            EnsureStarted();
        }
        else
        {
            _state = "Disabled";
        }
    }

    public static bool IsSupportedEnvironment => Program.SupportsLinuxX11Overlay();

    public event EventHandler? Changed;

    public DesktopNativeOverlaySnapshot GetSnapshot()
    {
        var supported = IsSupportedEnvironment;
        var enabled = supported && Config.Instance.UI_ShowHud;
        var state = supported ? _state : "Unavailable";
        if (supported && !enabled)
        {
            state = "Disabled";
        }
        else if (supported && Volatile.Read(ref _hasOverlayDashboards) == 0)
        {
            state = "NoDashboard";
        }

        return new DesktopNativeOverlaySnapshot(
            supported,
            enabled,
            Volatile.Read(ref _isInitializing) != 0,
            Config.Instance.HudFPS,
            Config.Instance.HudLockFPS,
            Config.Instance.HudTopMost,
            state,
            Volatile.Read(ref _windowVisible) != 0);
    }

    public bool SetEnabled(bool enabled)
    {
        if (!IsSupportedEnvironment)
        {
            return false;
        }

        Config.Instance.UI_ShowHud = enabled;
        if (!SaveConfiguration())
        {
            return false;
        }

        if (enabled)
        {
            EnsureStarted();
        }
        else
        {
            _state = "Disabled";
            StopInfrastructure();
        }

        RaiseChanged();
        return true;
    }

    public bool SetFrameRateLock(bool enabled)
    {
        Config.Instance.HudLockFPS = enabled;
        if (!SaveConfiguration())
        {
            return false;
        }

        ReconfigureFrameRate();
        RaiseChanged();
        return true;
    }

    public bool SetFrameRate(int frameRate)
    {
        Config.Instance.HudFPS = Math.Clamp(frameRate, 30, 240);
        if (!SaveConfiguration())
        {
            return false;
        }

        ReconfigureFrameRate();
        RaiseChanged();
        return true;
    }

    public bool SetTopMost(bool enabled)
    {
        Config.Instance.HudTopMost = enabled;
        if (!SaveConfiguration())
        {
            return false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                _window.Topmost = enabled;
            }
        });
        RaiseChanged();
        return true;
    }

    public void ReloadDashboardConfigurations()
    {
        RefreshDashboardAvailability();
        if (Config.Instance.UI_ShowHud)
        {
            Restart();
        }
        RaiseChanged();
    }

    public void HandleActivityChanged(GameActivitySessionChange change)
    {
        if (_disposed)
        {
            return;
        }

        if (change.Kind == GameActivityChangeKind.Started &&
            change.SessionActivated &&
            ReferenceEquals(change.CurrentGame, change.Activity.Game))
        {
            _attachedGame = change.Activity.Game;
            SetStageActive(GameStateStageActivity.IsActive(_attachedGame.GameDataReader.GameState));
            return;
        }

        if (change.Kind == GameActivityChangeKind.Stopped && change.SessionActivated)
        {
            if (change.CurrentActivity is { } fallback && change.CurrentGame != null)
            {
                _attachedGame = fallback.Game;
                SetStageActive(GameStateStageActivity.IsActive(_attachedGame.GameDataReader.GameState));
            }
            else
            {
                _attachedGame = null;
                SetStageActive(false);
            }
        }
    }

    private void EnsureStarted()
    {
        if (_disposed || !IsSupportedEnvironment || Volatile.Read(ref _hasOverlayDashboards) == 0)
        {
            return;
        }

        lock (_sync)
        {
            if (_runtime != null || (_startTask != null && !_startTask.IsCompleted))
            {
                ApplyWindowVisibility();
                return;
            }

            Volatile.Write(ref _isInitializing, 1);
            _state = "Ready";
            var generation = ++_startGeneration;
            _startTask = Task.Run(() => StartAsync(generation));
        }
        RaiseChanged();
    }

    private async Task StartAsync(long generation)
    {
        WebDashboardRuntimeController? runtime = null;
        try
        {
            var port = FindAvailableLoopbackPort();
            runtime = new WebDashboardRuntimeController(
                _tool,
                new WebDashboardRuntimeControllerOptions(
                    _dashboardPackagesPath,
                    _staticAssetsRoot,
                    Config.Instance.WebDashboardBrowserLuaEnabled,
                    SupportsNativeOverlay: true,
                    OutputTarget: DashboardOutputTarget.Overlay,
                    LoopbackOnly: true));
            runtime.Configure(true, port, GetEffectiveFrameRate());
            if (!runtime.Status.IsRunning)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(runtime.Status.ErrorMessage)
                        ? "The internal overlay server did not start."
                        : runtime.Status.ErrorMessage);
            }

            lock (_sync)
            {
                if (_disposed || generation != _startGeneration)
                {
                    runtime.Dispose();
                    return;
                }
                _runtime = runtime;
                runtime = null;
            }

            var overlayUrl = new Uri($"{WebDashboardServer.GetLoopbackDashboardUrl(port)}/?mode=overlay");
            await Dispatcher.UIThread.InvokeAsync(() => CreateWindow(overlayUrl, generation));
        }
        catch (Exception exception)
        {
            runtime?.Dispose();
            WebDashboardRuntimeController? assignedRuntime = null;
            if (generation == Volatile.Read(ref _startGeneration))
            {
                lock (_sync)
                {
                    if (generation == _startGeneration)
                    {
                        assignedRuntime = _runtime;
                        _runtime = null;
                        _startTask = null;
                        _state = "Unavailable";
                        Volatile.Write(ref _isInitializing, 0);
                    }
                }
            }
            assignedRuntime?.Dispose();
            Logger.Error(exception, "Failed to initialize the Linux native overlay.");
            RaiseChanged();
        }
    }

    private void CreateWindow(Uri overlayUrl, long generation)
    {
        if (_disposed || generation != Volatile.Read(ref _startGeneration) || _window != null)
        {
            return;
        }

        var window = new LinuxOverlayWindow(
            overlayUrl,
            (succeeded, error) => OnWindowNavigationCompleted(generation, succeeded, error))
        {
            Topmost = Config.Instance.HudTopMost
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                Volatile.Write(ref _windowReady, 0);
                RaiseChanged();
            }
        };
        _window = window;

        // Showing once initializes WPE WebView before the race starts. The fully
        // transparent window is hidden again as soon as navigation completes.
        window.Opacity = 0;
        window.Show();
    }

    private void OnWindowNavigationCompleted(long generation, bool succeeded, string error)
    {
        if (_disposed || generation != Volatile.Read(ref _startGeneration))
        {
            return;
        }

        Volatile.Write(ref _isInitializing, 0);
        Volatile.Write(ref _windowReady, succeeded ? 1 : 0);
        _state = succeeded ? "Ready" : "Unavailable";
        if (!succeeded)
        {
            Logger.Warn("Linux overlay browser initialization failed: {0}", error);
            StopInfrastructure();
            _state = "Unavailable";
            RaiseChanged();
            return;
        }
        ApplyWindowVisibility();
        RaiseChanged();
    }

    private void ApplyWindowVisibility()
    {
        var visible = !_disposed &&
            Config.Instance.UI_ShowHud &&
            Volatile.Read(ref _stageActive) != 0 &&
            Volatile.Read(ref _windowReady) != 0 &&
            Volatile.Read(ref _hasOverlayDashboards) != 0;
        SetWindowVisible(visible);
        if (visible)
        {
            _state = "Active";
        }
        else if (Config.Instance.UI_ShowHud && Volatile.Read(ref _windowReady) != 0)
        {
            _state = "Ready";
        }
        RaiseChanged();
    }

    private void SetWindowVisible(bool visible)
    {
        Volatile.Write(ref _windowVisible, visible ? 1 : 0);
        Dispatcher.UIThread.Post(() =>
        {
            var window = _window;
            if (window == null)
            {
                return;
            }

            if (visible)
            {
                window.FitPrimaryScreen();
                window.Opacity = 1;
                if (!window.IsVisible)
                {
                    window.Show();
                }
            }
            else
            {
                window.Opacity = 0;
                if (window.IsVisible)
                {
                    window.Hide();
                }
            }
        });
    }

    private void SetStageActive(bool active)
    {
        Volatile.Write(ref _stageActive, active ? 1 : 0);
        if (active)
        {
            EnsureStarted();
        }
        ApplyWindowVisibility();
    }

    private void OnRaceBegin(IGame game)
    {
        if (ReferenceEquals(_attachedGame, game))
        {
            SetStageActive(true);
        }
    }

    private void OnRaceEnd(IGame game)
    {
        if (ReferenceEquals(_attachedGame, game))
        {
            SetStageActive(false);
        }
    }

    private void OnGameEnded(IGame game)
    {
        if (ReferenceEquals(_attachedGame, game))
        {
            _attachedGame = null;
            SetStageActive(false);
        }
    }

    private void OnGameUninitialized(IGame game)
    {
        if (ReferenceEquals(_attachedGame, game))
        {
            SetStageActive(false);
        }
    }

    private void OnGameStateChanged(IGame game, GameStateChangeEvent state)
    {
        if (ReferenceEquals(_attachedGame, game))
        {
            SetStageActive(GameStateStageActivity.IsActive(state.NewGameState));
        }
    }

    private void RefreshDashboardAvailability()
    {
        var available = new WebDashboardCatalog(Logger)
            .Load(_dashboardPackagesPath, supportsNativeOverlay: true)
            .Any(dashboard => dashboard.GetOutputTarget().Includes(DashboardOutputTarget.Overlay));
        Volatile.Write(ref _hasOverlayDashboards, available ? 1 : 0);
        if (!available)
        {
            _state = "NoDashboard";
            SetWindowVisible(false);
        }
    }

    private void ReconfigureFrameRate()
    {
        lock (_sync)
        {
            if (_runtime is { Status.IsRunning: true } runtime)
            {
                runtime.Configure(true, runtime.Status.Port, GetEffectiveFrameRate());
            }
        }
    }

    private static int GetEffectiveFrameRate() =>
        Config.Instance.HudLockFPS ? Config.Instance.HudFPS : Config.WebDashboardFrameRateMaximum;

    private void Restart()
    {
        StopInfrastructure();
        EnsureStarted();
    }

    private void StopInfrastructure()
    {
        WebDashboardRuntimeController? runtime;
        lock (_sync)
        {
            _startGeneration++;
            runtime = _runtime;
            _runtime = null;
            _startTask = null;
        }
        runtime?.Dispose();
        Volatile.Write(ref _windowReady, 0);
        Volatile.Write(ref _windowVisible, 0);
        Volatile.Write(ref _isInitializing, 0);
        Dispatcher.UIThread.Post(() =>
        {
            var window = _window;
            _window = null;
            window?.Close();
        });
    }

    private static int FindAvailableLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool SaveConfiguration()
    {
        try
        {
            Config.Instance.SaveUserConfig();
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist Linux native-overlay settings.");
            return false;
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tool.onRaceBegin -= OnRaceBegin;
        _tool.onRaceEnd -= OnRaceEnd;
        _tool.onGameEnded -= OnGameEnded;
        _tool.onGameUninitialized -= OnGameUninitialized;
        _tool.onGameStateChanged -= OnGameStateChanged;
        StopInfrastructure();
    }
}

internal sealed class LinuxOverlayWindow : Window
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly Action<bool, string> _navigationCompleted;

    public LinuxOverlayWindow(Uri source, Action<bool, string> navigationCompleted)
    {
        _navigationCompleted = navigationCompleted;
        Title = "ZTMZ Pacenote Overlay";
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = WebDashboardCanvasResolution.DefaultWidth;
        Height = WebDashboardCanvasResolution.DefaultHeight;

        var webView = new NativeWebView
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        webView.NavigationCompleted += (_, args) =>
            _navigationCompleted(args.IsSuccess, args.IsSuccess ? string.Empty : "Navigation failed.");
        Content = webView;
        webView.Source = source;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitPrimaryScreen();
        var handle = TryGetPlatformHandle();
        if (!LinuxX11OverlayInterop.Configure(handle))
        {
            Logger.Warn("The Linux overlay window could not be marked as click-through/Gamescope overlay.");
        }
    }

    public void FitPrimaryScreen()
    {
        var screen = Screens.Primary;
        if (screen == null)
        {
            return;
        }

        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;
    }
}

internal static class LinuxX11OverlayInterop
{
    private const int PropModeReplace = 0;
    private const int ShapeSet = 0;
    private const int ShapeInput = 2;
    private const int Unsorted = 0;
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static bool Configure(IPlatformHandle? platformHandle)
    {
        if (platformHandle == null ||
            !string.Equals(platformHandle.HandleDescriptor, "XID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            return false;
        }

        var overlayMarked = false;
        var inputPassthrough = false;
        try
        {
            var overlayAtom = XInternAtom(display, "GAMESCOPE_EXTERNAL_OVERLAY", false);
            var cardinalAtom = XInternAtom(display, "CARDINAL", false);
            if (overlayAtom != IntPtr.Zero && cardinalAtom != IntPtr.Zero)
            {
                uint enabled = 1;
                XChangeProperty(
                    display,
                    platformHandle.Handle,
                    overlayAtom,
                    cardinalAtom,
                    32,
                    PropModeReplace,
                    ref enabled,
                    1);
                overlayMarked = true;
            }

            try
            {
                XShapeCombineRectangles(
                    display,
                    platformHandle.Handle,
                    ShapeInput,
                    0,
                    0,
                    IntPtr.Zero,
                    0,
                    ShapeSet,
                    Unsorted);
                inputPassthrough = true;
            }
            catch (DllNotFoundException exception)
            {
                Logger.Warn(exception, "libXext is unavailable; the overlay may intercept pointer input.");
            }
            catch (EntryPointNotFoundException exception)
            {
                Logger.Warn(exception, "XShape is unavailable; the overlay may intercept pointer input.");
            }

            XFlush(display);
            return overlayMarked && inputPassthrough;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(string? displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern int XChangeProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        ref uint data,
        int elementCount);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libXext.so.6")]
    private static extern void XShapeCombineRectangles(
        IntPtr display,
        IntPtr window,
        int destinationKind,
        int xOffset,
        int yOffset,
        IntPtr rectangles,
        int rectangleCount,
        int operation,
        int ordering);
}
#endif
