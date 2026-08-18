
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Xps.Serialization;
using GameOverlay.Windows;
using GameOverlay.Drawing;
using SharpDX;
using SharpDX.Direct2D1;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WebDashboard;
using Geometry = GameOverlay.Drawing.Geometry;
using Image = GameOverlay.Drawing.Image;
using Point = GameOverlay.Drawing.Point;
using Rectangle = GameOverlay.Drawing.Rectangle;
using Color = GameOverlay.Drawing.Color;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.UI;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Security.Principal;

namespace ZTMZ.PacenoteTool.Base.UI
{

    public class GameOverlayManager
    {
        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        public static string GAME_PROCESS = "dirtrally2";
        public static string GAME_WIN_TITLE = "Dirt Rally 2.0";
        private StickyWindow _window;

        private readonly Dictionary<string, SolidBrush> _brushes = new();
        private readonly Dictionary<string, Font> _fonts = new();
        private readonly Dictionary<string, Image> _images = new();
        private BackgroundWorker _bgw;
        private volatile bool _isRunning;
        

        private Random _random;

        public DashboardScriptArguments DashboardScriptArguments { set; get; } = new DashboardScriptArguments() {
            Config = Config.Instance,
            I18NLoader = I18NLoader.Instance,
            GameData = new GameData(),
            GameContext = new GameContext(),
            GameOverlayDrawingHelper = new GameOverlayDrawingHelper()
        };

        private volatile bool _timeToShowTelemetry = false;
        private readonly object _nativeOverlayStatusLock = new();
        private NativeOverlayStatus _nativeOverlayStatus = new(NativeOverlayState.Unavailable, false);

        public bool TimeToShowTelemetry
        {
            set
            {
                SetStageActive(value);
            }
            get
            {
                return _timeToShowTelemetry;
            }
        }

        public NativeOverlayStatus CurrentNativeOverlayStatus
        {
            get
            {
                lock (_nativeOverlayStatusLock)
                {
                    return _nativeOverlayStatus;
                }
            }
        }

        public void SetStageActive(bool isStageActive)
        {
            _timeToShowTelemetry = isStageActive;
            maxSpeed = MAX_SPEED;
            maxWheelSpeed = MAX_WHEEL_SPEED;
            maxWheelTemp = MAX_WHEEL_TEMP;
            maxGForce = MAX_G_FORCE;
            maxSuspension = MAX_SUSPENSION;
            minSuspension = MIN_SUSPENSION;
            maxSuspensionSpd = MAX_SUSPENSION_SPD;
            minSuspensionSpd = MIN_SUSPENSION_SPD;
            RefreshNativeOverlayVisibility();
        }

        public bool TimeToShowStatistics { set; get; }

        public GameData GameData { set; get; }

        public static float MAX_SPEED = 200f;
        public static float MAX_WHEEL_SPEED = 220f;
        public static float MAX_WHEEL_TEMP = 800f;
        public static float MAX_G_FORCE = 2.2f;
        public static float MAX_SUSPENSION_SPD = 1000f; // m/s
        public static float MIN_SUSPENSION_SPD = -1000f;
        public static float MAX_SUSPENSION = 200;
        public static float MIN_SUSPENSION = -200;
        
        private float maxSpeed { set; get; }
        private float maxWheelSpeed { set; get; }
        private float maxWheelTemp { set; get; }
        private float maxGForce { set; get; }
        private float maxSuspensionSpd { set; get; }
        private float minSuspensionSpd { set; get; }
        private float maxSuspension { set; get; }
        private float minSuspension { set; get; }

        public List<Dashboard> Dashboards { set; get; } = new List<Dashboard>();
        public List<bool> DashboardLoaded { set; get; } = new List<bool>();
        private readonly List<bool> _dashboardNativeLoadFailed = new();

        private readonly List<Dashboard> _webDashboards = new();
        private readonly List<bool> _webDashboardLoaded = new();

        public List<bool> DashboardErrorEncountered { set; get; } = new List<bool>();

        // Poll for a real game window instead of imposing a fixed delay. Keep the
        // former five-minute retry budget so slow-starting games such as RBR remain supported.
        private static readonly TimeSpan OverlayInitializationTimeout = TimeSpan.FromMinutes(5);
        private const int OverlayInitializationPollIntervalMilliseconds = 250;
        private const int OverlayWindowAttachmentRefreshIntervalMilliseconds = 250;

        private int dashboardLoadRetryCount = 0;
        private int dashboardLoadRetryMax = 10;    // 10 seconds
        private int dashboardLoadRetryInterval = 1000;

        private Window _gameOverlaySeparateWindow = new SeparateWindowForOverlay();

        private Process _gameProcess = null;
        private IntPtr _lastKnownGameWindowHandle = IntPtr.Zero;
        private long _lastGameWindowAttachmentRefreshAt;

        private bool _hudShowInSeparateWindow = false;

        private WebDashboardServer? _webDashboardServer = null;
        private readonly object _webDashboardLock = new();
        private readonly object _webDashboardCaptureLock = new();
        private WebDashboardServerStatus _webDashboardStatus = new(
            WebDashboardServerState.Stopped,
            0,
            string.Empty);
        private readonly object _dashboardRenderLock = new();
        private Timer? _webDashboardRenderTimer = null;
        private int _webDashboardRenderInProgress = 0;
        private int _webDashboardFrameRate = Config.WebDashboardFrameRateDefault;
        private long _webDashboardLastFrameTimestamp = 0;
        private long _webDashboardRenderedFrameCount = 0;
        private long _webDashboardLastRenderedFrameUtcTicks = 0;
        
        // Web dashboard rendering support
        private DrawingCommandCapture? _webCommandCapture = null;
        private GraphicsCommandProxy? _webGraphicsProxy = null;
        private DashboardScriptArguments? _webDashboardArgs = null;
        private readonly Dictionary<int, WebDashboardStaticRenderState> _webDashboardStaticRenderStates = new();
        private readonly Dictionary<int, NativeStaticImageLayer> _nativeStaticImageLayers = new();
        private readonly Dictionary<int, NativeStaticImageLayerState> _nativeStaticImageLayerStates = new();
        private readonly Dictionary<int, NativeStaticImageLayerState> _nativeStaticImageLayerFailures = new();

        private readonly record struct WebDashboardStaticRenderState(
            long ConfigurationRevision,
            string ContextSignature);

        private readonly record struct NativeStaticImageLayerState(
            long ConfigurationRevision,
            string ContextSignature,
            int Width,
            int Height);

        public event Action<bool> OnGameOverlayInitializingStateChanged;
        public event Action<WebDashboardServerStatus> OnWebDashboardStatusChanged;
        public event Action<NativeOverlayStatus> OnNativeOverlayStatusChanged;

        public WebDashboardServerStatus WebDashboardStatus
        {
            get
            {
                lock (_webDashboardLock)
                {
                    return _webDashboardStatus;
                }
            }
        }

        public WebDashboardRuntimeDiagnostics WebDashboardDiagnostics
        {
            get
            {
                lock (_webDashboardLock)
                {
                    return new WebDashboardRuntimeDiagnostics(
                        _webDashboardStatus,
                        _webDashboardServer?.ClientCount ?? 0,
                        Volatile.Read(ref _webDashboardFrameRate),
                        Interlocked.Read(ref _webDashboardRenderedFrameCount),
                        ToUtcDateTimeOffset(Interlocked.Read(ref _webDashboardLastRenderedFrameUtcTicks)));
                }
            }
        }

        public DashboardOutputTarget GetDashboardOutputTarget(Dashboard dashboard)
        {
            var dashboardId = GetDashboardOutputTargetId(dashboard);
            return Config.Instance.GetDashboardOutputTarget(dashboardId, dashboard.Descriptor.IsEnabled);
        }

        public void SetDashboardOutputTarget(Dashboard dashboard, DashboardOutputTarget target)
        {
            var dashboardId = GetDashboardOutputTargetId(dashboard);
            Config.Instance.SetDashboardOutputTarget(dashboardId, target);
            Config.Instance.SaveUserConfig();
            RefreshNativeOverlayVisibility();
        }

        public bool IsDashboardEnabledForOverlay(Dashboard dashboard)
        {
            return GetDashboardOutputTarget(dashboard).Includes(DashboardOutputTarget.Overlay);
        }

        public bool IsDashboardEnabledForWeb(Dashboard dashboard)
        {
            return GetDashboardOutputTarget(dashboard).Includes(DashboardOutputTarget.Web);
        }

        private static string GetDashboardOutputTargetId(Dashboard dashboard)
        {
            var path = dashboard.Descriptor.Path;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    return folderName;
                }
            }

            return dashboard.Descriptor.Name ?? string.Empty;
        }

        public GameOverlayManager() {
            var dashboardsPath = AppLevelVariables.Instance.GetPath(Constants.PATH_DASHBOARDS);
            if (System.IO.Directory.Exists(dashboardsPath))
            {
                // loop through subfolders and load all dashboards
                foreach (var dashboardPath in System.IO.Directory.GetDirectories(dashboardsPath))
                {
                    if (!System.IO.File.Exists(System.IO.Path.Combine(dashboardPath, Constants.DASHBOARD_INFO_FILE_NAME)))
                    {
                        _logger.Warn("Dashboard info.json not exists: {0}", dashboardPath);
                        continue;
                    }
                    var dashboardInfoPath = System.IO.Path.Combine(dashboardPath, Constants.DASHBOARD_INFO_FILE_NAME);
                    var dashboard = new Dashboard(dashboardInfoPath);
                    var webDashboard = new Dashboard(dashboardInfoPath)
                    {
                        DashboardConfigurations = dashboard.DashboardConfigurations
                    };
                    webDashboard.Descriptor.IsEnabled = dashboard.Descriptor.IsEnabled;
                    Dashboards.Add(dashboard);
                    _webDashboards.Add(webDashboard);
                }
            }
            else
            {
                _logger.Warn("Dashboard path not exists: {0}", dashboardsPath);
            }

            DashboardLoaded.AddRange(Enumerable.Repeat(false, Dashboards.Count));
            _dashboardNativeLoadFailed.AddRange(Enumerable.Repeat(false, Dashboards.Count));
            DashboardErrorEncountered.AddRange(Enumerable.Repeat(false, Dashboards.Count));
            _webDashboardLoaded.AddRange(Enumerable.Repeat(false, _webDashboards.Count));
            _logger.Info($"Loaded {Dashboards.Count} Dashboards.");
            
            _gameOverlaySeparateWindow.Closing += (sender, args) => {
                _hudShowInSeparateWindow = false;
                // UninitializeOverlayInSeparateWindow();
                (sender as Window)?.Hide();
                args.Cancel = true;

                // Restore the actual game window after closing the preview. MainWindowHandle
                // may temporarily be zero for a full-screen game, so keep the last valid handle.
                if (_window != null && TryGetGameWindowHandle(out var gameWindowHandle)) {
                    _window.ParentWindowHandle = gameWindowHandle;
                } else {
                    _logger.Warn("Unable to restore the game window as the overlay parent.");
                    UninitializeOverlayInSeparateWindow();
                    return;
                }

                RefreshNativeOverlayVisibility();
            };

            if (Config.Instance.WebDashboardEnabled)
            {
                ConfigureWebDashboard(
                    true,
                    Config.Instance.WebDashboardPort,
                    Config.Instance.WebDashboardFrameRate);
            }

            RefreshNativeOverlayVisibility();
        }

        /// <summary>
        /// Starts or stops only the Web HUD server. It deliberately does not create,
        /// show, or re-parent the native overlay window.
        /// </summary>
        public void ConfigureWebDashboard(bool enabled, int port)
        {
            ConfigureWebDashboard(enabled, port, Config.Instance.WebDashboardFrameRate);
        }

        public void ConfigureWebDashboard(bool enabled, int port, int frameRate)
        {
            if (!enabled)
            {
                StopWebDashboard(port);
                return;
            }

            if (port is < 1024 or > 65535)
            {
                _logger.Warn("WebDashboard port is outside the allowed range: {0}", port);
                UpdateWebDashboardStatus(
                    WebDashboardServerState.PortUnavailable,
                    port,
                    "Port must be between 1024 and 65535.");
                return;
            }

            StartWebDashboard(port, frameRate);
        }

        /// <summary>
        /// Switches Web Dashboard browser-Lua execution without restarting the
        /// server or disconnecting dashboard clients.
        /// </summary>
        public void SetWebDashboardBrowserLuaEnabled(bool enabled)
        {
            WebDashboardServer? server;
            lock (_webDashboardLock)
            {
                server = _webDashboardServer;
            }

            if (server != null)
            {
                server.BrowserLuaExecutionEnabled = enabled;
            }
        }

        private void StartWebDashboard(int port, int frameRate)
        {
            WebDashboardServer? previousServer = null;
            Timer? previousTimer = null;
            Timer? activeTimer = null;
            var alreadyRunning = false;
            var normalizedFrameRate = NormalizeWebDashboardFrameRate(frameRate);
            var renderInterval = GetWebDashboardRenderInterval(normalizedFrameRate);

            lock (_webDashboardLock)
            {
                if (_webDashboardServer != null
                    && _webDashboardServer.IsRunning
                    && _webDashboardServer.Port == port
                    && _webDashboardRenderTimer != null)
                {
                    alreadyRunning = true;
                    activeTimer = _webDashboardRenderTimer;
                }
                else
                {
                    previousServer = _webDashboardServer;
                    previousTimer = _webDashboardRenderTimer;
                    _webDashboardServer = null;
                    _webDashboardRenderTimer = null;
                }
            }

            if (alreadyRunning)
            {
                Volatile.Write(ref _webDashboardFrameRate, normalizedFrameRate);
                Interlocked.Exchange(ref _webDashboardLastFrameTimestamp, 0);
                activeTimer!.Change(TimeSpan.Zero, renderInterval);
                _logger.Info("WebDashboard frame rate changed to {0} FPS", normalizedFrameRate);
                UpdateWebDashboardStatus(WebDashboardServerState.Running, port);
                return;
            }

            previousTimer?.Dispose();
            previousServer?.Dispose();
            InvalidateWebDashboardStaticLayers();

            WebDashboardServer? server = null;
            try
            {
                server = new WebDashboardServer
                {
                    Port = port,
                    BrowserLuaExecutionEnabled = Config.Instance.WebDashboardBrowserLuaEnabled
                };
                server.Initialize(Dashboards, GetDashboardOutputTarget);
                server.Start();

                lock (_webDashboardLock)
                {
                    _webDashboardServer = server;
                    Volatile.Write(ref _webDashboardFrameRate, normalizedFrameRate);
                    Interlocked.Exchange(ref _webDashboardLastFrameTimestamp, 0);
                    Interlocked.Exchange(ref _webDashboardRenderedFrameCount, 0);
                    Interlocked.Exchange(ref _webDashboardLastRenderedFrameUtcTicks, 0);
                    _webDashboardRenderTimer = new Timer(
                        RenderWebDashboard,
                        null,
                        TimeSpan.Zero,
                        renderInterval);
                }

                _logger.Info("WebDashboard server started on port {0} at {1} FPS", port, normalizedFrameRate);
                UpdateWebDashboardStatus(WebDashboardServerState.Running, port);
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _logger.Error(ex, "Failed to start WebDashboard server");
                UpdateWebDashboardStatus(GetWebDashboardStartFailureState(ex), port, ex.Message);
            }
        }

        private static TimeSpan GetWebDashboardRenderInterval(int frameRate)
        {
            return TimeSpan.FromSeconds(1d / NormalizeWebDashboardFrameRate(frameRate));
        }

        private static int NormalizeWebDashboardFrameRate(int frameRate)
        {
            return Math.Clamp(
                frameRate,
                Config.WebDashboardFrameRateMinimum,
                Config.WebDashboardFrameRateMaximum);
        }

        private void StopWebDashboard(int port = 0)
        {
            WebDashboardServer? server;
            Timer? timer;
            var statusPort = port;

            lock (_webDashboardLock)
            {
                server = _webDashboardServer;
                timer = _webDashboardRenderTimer;
                _webDashboardServer = null;
                _webDashboardRenderTimer = null;
                if (statusPort == 0)
                {
                    statusPort = server?.Port ?? _webDashboardStatus.Port;
                }
            }

            timer?.Dispose();
            Interlocked.Exchange(ref _webDashboardLastFrameTimestamp, 0);
            InvalidateWebDashboardStaticLayers();
            if (server != null)
            {
                server.Dispose();
                _logger.Info("WebDashboard server stopped");
            }

            UpdateWebDashboardStatus(WebDashboardServerState.Stopped, statusPort);
        }

        private void UpdateWebDashboardStatus(
            WebDashboardServerState state,
            int port,
            string errorMessage = "")
        {
            var status = new WebDashboardServerStatus(
                state,
                port,
                WebDashboardServer.GetDashboardUrl(port),
                errorMessage);

            lock (_webDashboardLock)
            {
                _webDashboardStatus = status;
            }

            try
            {
                OnWebDashboardStatusChanged?.Invoke(status);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "WebDashboard status listener failed");
            }
        }

        private static WebDashboardServerState GetWebDashboardStartFailureState(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException socketException
                    && socketException.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
                {
                    return WebDashboardServerState.PortUnavailable;
                }
            }

            return WebDashboardServerState.Error;
        }

        private void RenderWebDashboard(object? state)
        {
            if (!TimeToShowTelemetry)
            {
                return;
            }

            try
            {
                BroadcastWebDashboardFrame();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to render WebDashboard frame");
            }
        }

        private void BroadcastWebDashboardFrame()
        {
            WebDashboardServer? server;
            lock (_webDashboardLock)
            {
                server = _webDashboardServer;
            }

            if (server == null || !server.IsRunning || server.ClientCount == 0)
            {
                return;
            }

            if (!TryBeginWebDashboardFrame())
            {
                return;
            }

            var startedSend = false;
            try
            {
                WebDashboardDrawFrame frame = null;
                WebDashboardBrowserLuaScene browserLuaScene = null;
                GameData? browserLuaGameData = null;
                GameContext browserLuaGameContext = null;
                lock (_webDashboardCaptureLock)
                {
                    if (!TimeToShowTelemetry)
                    {
                        return;
                    }

                    if (TryCreateBrowserLuaScene(server, out var scene))
                    {
                        browserLuaScene = scene;
                        browserLuaGameData = DashboardScriptArguments.GameData;
                        browserLuaGameContext = DashboardScriptArguments.GameContext;
                        var browserLuaDashboardIds = scene.Dashboards
                            .Select(dashboard => dashboard.Id)
                            .ToHashSet();
                        frame = CaptureWebDashboardCommands(browserLuaDashboardIds);

                        if (HasCapturedWebDashboardLayers(frame)
                            && !WebDashboardServer.CanUseLayeredRendering(frame))
                        {
                            // The browser and server can share only retained layers.
                            // Re-capture every dashboard when a remaining server layer
                            // requires the established complete-frame renderer.
                            browserLuaScene = null;
                            browserLuaGameData = null;
                            browserLuaGameContext = null;
                            frame = CaptureWebDashboardCommands();
                        }
                    }
                    else
                    {
                        frame = CaptureWebDashboardCommands();
                    }
                }

                if (browserLuaScene != null
                    && browserLuaGameData != null
                    && browserLuaGameContext != null)
                {
                    _ = HasCapturedWebDashboardLayers(frame)
                        ? SendWebDashboardHybridFrameAsync(
                            server,
                            frame,
                            browserLuaScene,
                            browserLuaGameData.Value,
                            browserLuaGameContext)
                        : SendWebDashboardBrowserLuaFrameAsync(
                            server,
                            browserLuaScene,
                            browserLuaGameData.Value,
                            browserLuaGameContext);
                    startedSend = true;
                    return;
                }

                if (frame == null || frame.LegacyCommands.Count == 0)
                {
                    return;
                }

                _ = SendWebDashboardFrameAsync(server, frame);
                startedSend = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to capture WebDashboard frame");
            }
            finally
            {
                if (!startedSend)
                {
                    Volatile.Write(ref _webDashboardRenderInProgress, 0);
                }
            }
        }

        private bool TryCreateBrowserLuaScene(
            WebDashboardServer server,
            out WebDashboardBrowserLuaScene scene)
        {
            scene = null;
            if (!server.CanExecuteBrowserLuaDashboards)
            {
                return false;
            }

            var browserLuaDashboards = new List<WebDashboardBrowserLuaDashboard>();
            for (var i = 0; i < Dashboards.Count; i++)
            {
                var dashboard = Dashboards[i];
                if (!IsDashboardEnabledForWeb(dashboard))
                {
                    continue;
                }

                if (!dashboard.Descriptor.SupportsWebDashboardClientLua)
                {
                    continue;
                }

                browserLuaDashboards.Add(new WebDashboardBrowserLuaDashboard(
                    i,
                    i,
                    dashboard.ConfigurationRevision,
                    GetBrowserLuaSettings(dashboard)));
            }

            if (browserLuaDashboards.Count == 0)
            {
                return false;
            }

            scene = new WebDashboardBrowserLuaScene(
                browserLuaDashboards,
                new WebDashboardBrowserLuaConfig(
                    Config.Instance.HudChromaKeyMode,
                    Config.Instance.HudShiftGreenZone,
                    Config.Instance.HudShiftYellowZone,
                    Config.Instance.HudShiftRedZone,
                    Volatile.Read(ref _webDashboardFrameRate)));
            return true;
        }

        private static bool HasCapturedWebDashboardLayers(WebDashboardDrawFrame frame)
        {
            return frame != null
                && frame.Layers.Any(layer => layer.Id.StartsWith("dashboard-", StringComparison.Ordinal));
        }

        private static IReadOnlyDictionary<string, object> GetBrowserLuaSettings(Dashboard dashboard)
        {
            var settings = new SortedDictionary<string, object>(StringComparer.Ordinal);
            var configurations = dashboard.DashboardConfigurations;
            if (configurations == null)
            {
                return settings;
            }

            var index = 0;
            foreach (var configuration in configurations.PropertyName)
            {
                if (index >= configurations.PropertyValue.Count)
                {
                    break;
                }

                settings[configuration.Key] = configurations.PropertyValue[index];
                index++;
            }

            return settings;
        }

        private bool TryBeginWebDashboardFrame()
        {
            if (Interlocked.CompareExchange(ref _webDashboardRenderInProgress, 1, 0) != 0)
            {
                return false;
            }

            var frameRate = Volatile.Read(ref _webDashboardFrameRate);
            var minimumFrameInterval = Stopwatch.Frequency / (double)frameRate;
            var timestamp = Stopwatch.GetTimestamp();

            while (true)
            {
                var previousTimestamp = Volatile.Read(ref _webDashboardLastFrameTimestamp);
                if (previousTimestamp != 0 && timestamp - previousTimestamp < minimumFrameInterval)
                {
                    Volatile.Write(ref _webDashboardRenderInProgress, 0);
                    return false;
                }

                if (Interlocked.CompareExchange(
                        ref _webDashboardLastFrameTimestamp,
                        timestamp,
                        previousTimestamp) == previousTimestamp)
                {
                    return true;
                }
            }
        }

        private async Task SendWebDashboardFrameAsync(
            WebDashboardServer server,
            WebDashboardDrawFrame frame)
        {
            try
            {
                await server.BroadcastDrawFrameAsync(frame);
                Interlocked.Increment(ref _webDashboardRenderedFrameCount);
                Interlocked.Exchange(ref _webDashboardLastRenderedFrameUtcTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to broadcast WebDashboard frame");
            }
            finally
            {
                Volatile.Write(ref _webDashboardRenderInProgress, 0);
            }
        }

        private async Task SendWebDashboardBrowserLuaFrameAsync(
            WebDashboardServer server,
            WebDashboardBrowserLuaScene scene,
            GameData gameData,
            GameContext gameContext)
        {
            try
            {
                await server.BroadcastBrowserLuaTelemetryAsync(scene, gameData, gameContext);
                Interlocked.Increment(ref _webDashboardRenderedFrameCount);
                Interlocked.Exchange(ref _webDashboardLastRenderedFrameUtcTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to broadcast browser-Lua WebDashboard frame");
            }
            finally
            {
                Volatile.Write(ref _webDashboardRenderInProgress, 0);
            }
        }

        private async Task SendWebDashboardHybridFrameAsync(
            WebDashboardServer server,
            WebDashboardDrawFrame commandFrame,
            WebDashboardBrowserLuaScene browserLuaScene,
            GameData gameData,
            GameContext gameContext)
        {
            try
            {
                var browserLuaDashboardIds = browserLuaScene.Dashboards
                    .Select(dashboard => dashboard.Id)
                    .ToArray();
                await server.BroadcastDrawFrameAsync(commandFrame, browserLuaDashboardIds);
                await server.BroadcastBrowserLuaTelemetryAsync(
                    browserLuaScene,
                    gameData,
                    gameContext,
                    isHybrid: true);
                Interlocked.Increment(ref _webDashboardRenderedFrameCount);
                Interlocked.Exchange(ref _webDashboardLastRenderedFrameUtcTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to broadcast hybrid WebDashboard frame");
            }
            finally
            {
                Volatile.Write(ref _webDashboardRenderInProgress, 0);
            }
        }

        private static DateTimeOffset? ToUtcDateTimeOffset(long ticks)
        {
            return ticks == 0
                ? null
                : new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
        }

        public void InitializeOverlayInSeparateWindow() {
            if (_hudShowInSeparateWindow) {
                // do nothing
                _logger.Warn("Hud is already shown in separate window, do nothing.");
                return;
            }
            _logger.Info("Initializing overlay in separate window....");
            _hudShowInSeparateWindow = true;
            _gameOverlaySeparateWindow.Title = "PacenoteTool GameOverlay Separate Window";
            _gameOverlaySeparateWindow.Width = 1280;
            _gameOverlaySeparateWindow.Height = 720;
            _gameOverlaySeparateWindow.Show();
            if (_isRunning && _window != null) {
                RememberGameWindowHandle(_window.ParentWindowHandle);
                _window.ParentWindowHandle = new WindowInteropHelper(_gameOverlaySeparateWindow).Handle;
                RefreshNativeOverlayVisibility();
            } else {
                InitializeOverlay(new WindowInteropHelper(_gameOverlaySeparateWindow).Handle);
            }
        }

        public void InitializeOverlay(IntPtr windowHandle) {
            OnGameOverlayInitializingStateChanged?.Invoke(true);
            _logger.Info("Initializing overlay for window handle: {0}", windowHandle.ToString("X8"));

            var gfx = new Graphics()
            {
                MeasureFPS = true,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true
            };

            // set graphics to dashboard arguments
            DashboardScriptArguments.Graphics = gfx;
            try {
                _window = new StickyWindow(windowHandle, gfx);
            } catch (Exception ex) {
                _logger.Error(ex, "Failed to initialize overlay using window handle");
                RefreshNativeOverlayVisibility();
                return;
            }
            if (_window != null) {
                initializeOverlayIfWindowReady();
            }
        }

        public void InitializeOverlay(Process p)
        {
            OnGameOverlayInitializingStateChanged?.Invoke(true);
            _gameProcess = p;
            _lastKnownGameWindowHandle = IntPtr.Zero;
            Interlocked.Exchange(ref _lastGameWindowAttachmentRefreshAt, 0);
            if (_hudShowInSeparateWindow) {
                // do nothing
                _logger.Warn("Hud is already shown in separate window, do nothing.");
                RefreshNativeOverlayVisibility();
                return;
            }
            _logger.Info("Initializing overlay for process: {0}", p.ProcessName);
            var gfx = new Graphics()
            {
                MeasureFPS = true,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true
            };

            // set graphics to dashboard arguments
            DashboardScriptArguments.Graphics = gfx;

            Task.Run(() => {
                var stopwatch = Stopwatch.StartNew();
                var retryCount = 0;
                _logger.Info("Waiting for a usable game window before initializing GameOverlay.");

                while (IsCurrentOverlayProcess(p) && stopwatch.Elapsed < OverlayInitializationTimeout)
                {
                    try
                    {
                        if (p.HasExited)
                        {
                            _logger.Info("Game process exited before GameOverlay initialization completed.");
                            OnGameOverlayInitializingStateChanged?.Invoke(false);
                            return;
                        }

                        if (TryGetGameWindowHandle(out var gameWindowHandle))
                        {
                            var overlayWindow = new StickyWindow(gameWindowHandle, gfx);
                            if (!IsCurrentOverlayProcess(p))
                            {
                                overlayWindow.Dispose();
                                return;
                            }

                            _window = overlayWindow;
                            RememberGameWindowHandle(gameWindowHandle);
                            initializeOverlayIfWindowReady();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount == 1 || retryCount % 20 == 0)
                        {
                            _logger.Debug(ex, "Game window is not ready for GameOverlay initialization yet.");
                        }
                    }

                    Thread.Sleep(OverlayInitializationPollIntervalMilliseconds);
                }

                if (IsCurrentOverlayProcess(p))
                {
                    _logger.Error(
                        "Failed to initialize GameOverlay after waiting {0:F1} seconds for the game window.",
                        stopwatch.Elapsed.TotalSeconds);
                    OnGameOverlayInitializingStateChanged?.Invoke(false);
                }
            });
        }

        private bool IsCurrentOverlayProcess(Process process)
        {
            return !_hudShowInSeparateWindow && ReferenceEquals(_gameProcess, process);
        }

        private void initializeOverlayIfWindowReady() {
            _window.Title = Constants.HUD_WINDOW_NAME;
            if (Config.Instance.HudLockFPS) {
                _window.FPS = Config.Instance.HudFPS;   // 60 fps by default
            } else {
                _window.FPS = 0;    // no limit
            }
            _window.AttachToClientArea = true;
            _window.IsVisible = false;


            _window.DestroyGraphics += _window_DestroyGraphics;
            _window.DrawGraphics += _window_DrawGraphics;
            _window.SetupGraphics += _window_SetupGraphics;

            this.Run();
            RefreshNativeOverlayVisibility();
            _logger.Info("GameOverlay initialized.!");
            OnGameOverlayInitializingStateChanged?.Invoke(false);
        }

        public void UninitializeOverlay()
        {
            SetStageActive(false);
            if (_hudShowInSeparateWindow) {
                // do nothing
                return;
            }
            _isRunning = false;
            _gameProcess = null;
            _lastKnownGameWindowHandle = IntPtr.Zero;
            Interlocked.Exchange(ref _lastGameWindowAttachmentRefreshAt, 0);
            _window?.Dispose();
            _window = null;
            RefreshNativeOverlayVisibility();
            _logger.Info("GameOverlay uninitialized.!");
        }

        public void UninitializeOverlayInSeparateWindow() {
            SetStageActive(false);
            _isRunning = false;
            _hudShowInSeparateWindow = false;
            _window?.Dispose();
            _window = null;
            RefreshNativeOverlayVisibility();
            _logger.Info("GameOverlay uninitialized in separate window.!");
        }

        public void SetFPS(int fps)
        {
            if (_window != null)
                _window.FPS = fps;  
        }

        public void RefreshNativeOverlayVisibility()
        {
            var window = _window;
            var status = NativeOverlayVisibilityPolicy.Resolve(
                isOverlayReady: _isRunning && window?.IsInitialized == true,
                isPreviewActive: _hudShowInSeparateWindow,
                isNativeHudEnabled: Config.Instance.UI_ShowHud,
                isStageActive: TimeToShowTelemetry,
                hasNativeDashboard: HasNativeOverlayDashboard());

            if (window != null && window.IsInitialized)
            {
                try
                {
                    if (status.IsVisible)
                    {
                        RefreshGameWindowAttachment(force: true);
                        if (!window.IsVisible)
                        {
                            window.IsVisible = true;
                        }

                        RefreshOverlayLayering(placeAbove: true);
                    }
                    else if (window.IsVisible)
                    {
                        window.IsVisible = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to update native overlay visibility.");
                    status = new NativeOverlayStatus(NativeOverlayState.Unavailable, false);
                }
            }

            UpdateNativeOverlayStatus(status);
        }

        private bool HasNativeOverlayDashboard()
        {
            return Dashboards.Any(IsDashboardEnabledForOverlay);
        }

        private void UpdateNativeOverlayStatus(NativeOverlayStatus status)
        {
            Action<NativeOverlayStatus> handler;
            NativeOverlayStatus previousStatus;

            lock (_nativeOverlayStatusLock)
            {
                previousStatus = _nativeOverlayStatus;
                if (previousStatus == status)
                {
                    return;
                }

                _nativeOverlayStatus = status;
                handler = OnNativeOverlayStatusChanged;
            }

            _logger.Debug(
                "Native overlay status changed from {0} to {1}; visible: {2}",
                previousStatus.State,
                status.State,
                status.IsVisible);

            try
            {
                handler?.Invoke(status);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Native overlay status listener failed.");
            }
        }

        public void SetTopMost(bool topMost)
        {
            ApplyOverlayLayering(topMost || ShouldUseForegroundTopMost(), placeAbove: true);
        }

        private void RefreshOverlayLayering(bool placeAbove = false)
        {
            RefreshGameWindowAttachment();
            ApplyOverlayLayering(Config.Instance.HudTopMost || ShouldUseForegroundTopMost(), placeAbove);
        }

        private void ApplyOverlayLayering(bool topMost, bool placeAbove)
        {
            var window = _window;
            if (window == null || !window.IsInitialized || !window.IsVisible) {
                return;
            }

            var bypassTopmost = !topMost;
            var layeringChanged = window.BypassTopmost != bypassTopmost || window.IsTopmost != topMost;
            if (window.BypassTopmost != bypassTopmost) {
                window.BypassTopmost = bypassTopmost;
            }
            if (window.IsTopmost != topMost) {
                window.IsTopmost = topMost;
            }

            if (!topMost && (placeAbove || layeringChanged) && window.IsInitialized && window.ParentWindowHandle != IntPtr.Zero) {
                window.PlaceAbove(window.ParentWindowHandle);
            }
        }

        private bool ShouldUseForegroundTopMost()
        {
            // ACR drops this external Direct2D overlay when a focus change promotes it to TOPMOST.
            return !string.Equals(_gameProcess?.ProcessName, "acr", StringComparison.OrdinalIgnoreCase)
                && IsGameWindowForeground();
        }

        private bool IsGameWindowForeground()
        {
            if (_hudShowInSeparateWindow || _gameProcess == null) {
                return false;
            }

            try {
                if (_gameProcess.HasExited) {
                    return false;
                }

                var foregroundWindow = WindowHelper.GetForegroundWindow();
                return (_lastKnownGameWindowHandle != IntPtr.Zero
                        && foregroundWindow == _lastKnownGameWindowHandle)
                    || (foregroundWindow != IntPtr.Zero
                        && WindowHelper.GetProcessIdFromWindow(foregroundWindow) == _gameProcess.Id);
            } catch (Exception ex) {
                _logger.Debug(ex, "Failed to inspect the foreground game window.");
                return false;
            }
        }

        private void RememberGameWindowHandle(IntPtr windowHandle)
        {
            if (windowHandle != IntPtr.Zero && WindowHelper.IsWindow(windowHandle)) {
                _lastKnownGameWindowHandle = windowHandle;
            }
        }

        private bool TryGetGameWindowHandle(out IntPtr gameWindowHandle)
        {
            gameWindowHandle = IntPtr.Zero;
            var gameProcess = _gameProcess;
            if (gameProcess == null)
            {
                return false;
            }

            try {
                if (gameProcess.HasExited) {
                    return false;
                }

                gameProcess.Refresh();
                var processId = gameProcess.Id;
                var foregroundWindow = WindowHelper.GetForegroundWindow();
                var mainWindow = gameProcess.MainWindowHandle;
                var targetWindow = GameOverlayWindowTargetPolicy.SelectTarget(
                    foregroundWindow,
                    IsWindowOwnedByProcess(foregroundWindow, processId),
                    mainWindow,
                    IsWindowOwnedByProcess(mainWindow, processId),
                    _lastKnownGameWindowHandle,
                    IsWindowOwnedByProcess(_lastKnownGameWindowHandle, processId));

                if (targetWindow == IntPtr.Zero) {
                    return false;
                }

                RememberGameWindowHandle(targetWindow);
                gameWindowHandle = targetWindow;
                return true;
            } catch (Exception ex) {
                _logger.Debug(ex, "Failed to refresh the game window handle.");
                return false;
            }
        }

        private void RefreshGameWindowAttachment(bool force = false)
        {
            if (_hudShowInSeparateWindow) {
                return;
            }

            var now = Environment.TickCount64;
            var lastRefresh = Interlocked.Read(ref _lastGameWindowAttachmentRefreshAt);
            if (!force && lastRefresh != 0 && now - lastRefresh < OverlayWindowAttachmentRefreshIntervalMilliseconds) {
                return;
            }

            Interlocked.Exchange(ref _lastGameWindowAttachmentRefreshAt, now);

            var window = _window;
            if (window == null || !window.IsInitialized || !TryGetGameWindowHandle(out var targetWindow)) {
                return;
            }

            var attachedWindow = window.ParentWindowHandle;
            if (!GameOverlayWindowTargetPolicy.NeedsReattachment(attachedWindow, targetWindow)) {
                return;
            }

            try {
                window.ParentWindowHandle = targetWindow;
                window.FitTo(targetWindow, window.AttachToClientArea);
                _logger.Info(
                    "Reattached GameOverlay from window handle {0} to {1}; overlay size is now {2}x{3}.",
                    attachedWindow.ToString("X8"),
                    targetWindow.ToString("X8"),
                    window.Width,
                    window.Height);
            } catch (Exception ex) {
                _logger.Warn(ex, "Failed to reattach GameOverlay to the current game window.");
            }
        }

        private static bool IsWindowOwnedByProcess(IntPtr windowHandle, int processId)
        {
            return windowHandle != IntPtr.Zero
                && processId > 0
                && WindowHelper.IsWindow(windowHandle)
                && WindowHelper.GetProcessIdFromWindow(windowHandle) == processId;
        }

        private void _window_SetupGraphics(object sender, SetupGraphicsEventArgs e) {
            lock (_dashboardRenderLock) {
                _isRunning = true;
                var gfx = e.Graphics;
                DisposeNativeStaticImageLayers();

                if (e.RecreateResources) {
                    foreach (var pair in _brushes) pair.Value.Dispose();
                    foreach (var pair in _fonts) pair.Value.Dispose();
                    foreach (var pair in _images) pair.Value.Dispose();
                }

                // _logger.Debug("Setting up graphics..., the factory is: {0}, factory in LuaArgs is {1}", gfx.GetFactory().NativePointer, DashboardScriptArguments.Graphics.GetFactory().NativePointer);
                // _logger.Debug("Setting up graphics..., the fontFactory is: {0}, fontfactory in LuaArgs is {1}", gfx.GetFontFactory().NativePointer, DashboardScriptArguments.Graphics.GetFontFactory().NativePointer);
                // load custom fonts
                gfx.LoadCustomFont(AppLevelVariables.Instance.GetPath(Constants.PATH_FONTS));
                _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
                for (int i = 0; i < Dashboards.Count; i++) {
                    var dashboard = Dashboards[i];
                    DashboardErrorEncountered[i] = false;
                    _dashboardNativeLoadFailed[i] = false;
                    if (IsDashboardEnabledForOverlay(dashboard)) {

                        while (dashboardLoadRetryCount < dashboardLoadRetryMax)
                        {
                            try
                            {
                                dashboard.Load(DashboardScriptArguments, true); // force reload resources
                                DashboardLoaded[i] = true;
                                dashboardLoadRetryCount = 0;
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to load dashboard: {0}, retrying...", dashboard.Descriptor.Name);
                                dashboardLoadRetryCount++;
                                Thread.Sleep(dashboardLoadRetryInterval);
                            }
                        }
                        if (dashboardLoadRetryCount >= dashboardLoadRetryMax)
                        {
                            _logger.Error("Failed to load dashboard: {0}, retry count: {1}", dashboard.Descriptor.Name, dashboardLoadRetryCount);
                            DashboardLoaded[i] = false;
                            _dashboardNativeLoadFailed[i] = true;
                        }
                        dashboardLoadRetryCount = 0;
                    } else {
                        if (DashboardLoaded[i]) {
                            dashboard.Unload();
                        }
                        DashboardLoaded[i] = false;
                    }
                }
            }

            RefreshNativeOverlayVisibility();
        }

        private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e) {
            RefreshOverlayLayering();

            lock (_dashboardRenderLock) {
                var gfx = e.Graphics;
                gfx.ClearScene(_brushes["clear"]);
                DashboardScriptArguments.NativeStaticLayerEnabled = false;
                DashboardScriptArguments.NativeStaticLayerRenderOnly = false;
                DashboardScriptArguments.NativeStaticLayerGraphics = null;

                var renderNativeHud = Config.Instance.UI_ShowHud || _hudShowInSeparateWindow;
                if (TimeToShowTelemetry || _hudShowInSeparateWindow) {
                    for (var i = 0; i < Dashboards.Count; i++) {
                        var dashboard = Dashboards[i];
                        if (!IsDashboardEnabledForOverlay(dashboard)) {
                            DisposeNativeStaticImageLayer(i);
                            if (DashboardLoaded[i]) {
                                dashboard.Unload();
                                DashboardLoaded[i] = false;
                            }
                            continue;
                        }

                        if (!DashboardLoaded[i] && !_dashboardNativeLoadFailed[i]) {
                            DisposeNativeStaticImageLayer(i);
                            try {
                                dashboard.Load(DashboardScriptArguments);
                                DashboardLoaded[i] = true;
                            } catch (Exception ex) {
                                _dashboardNativeLoadFailed[i] = true;
                                _logger.Error(ex, "Failed to load dashboard for native overlay: {0}", dashboard.Descriptor.Name);
                                continue;
                            }
                        }

                        if (renderNativeHud && DashboardLoaded[i]) {
                            try {
                                RenderNativeDashboard(i, dashboard, gfx);
                            } catch (Exception ex) {
                                // log error for the first time for this dashboard
                                if (!DashboardErrorEncountered[i]) {
                                    _logger.Error(ex, "Error when rendering dashboard: {0}", dashboard.Descriptor.Name);
                                    DashboardErrorEncountered[i] = true;
                                }
                            }
                        }
                    }
                }

            }
        }

        private void RenderNativeDashboard(int dashboardIndex, Dashboard dashboard, Graphics graphics)
        {
            var useCachedStaticLayer = TryDrawNativeStaticImageLayer(dashboardIndex, dashboard, graphics);
            var previousStaticLayerEnabled = DashboardScriptArguments.NativeStaticLayerEnabled;
            var previousStaticLayerRenderOnly = DashboardScriptArguments.NativeStaticLayerRenderOnly;
            var previousStaticLayerGraphics = DashboardScriptArguments.NativeStaticLayerGraphics;
            DashboardScriptArguments.NativeStaticLayerEnabled = useCachedStaticLayer;
            DashboardScriptArguments.NativeStaticLayerRenderOnly = false;
            DashboardScriptArguments.NativeStaticLayerGraphics = null;

            try
            {
                dashboard.Render(DashboardScriptArguments);
            }
            finally
            {
                DashboardScriptArguments.NativeStaticLayerEnabled = previousStaticLayerEnabled;
                DashboardScriptArguments.NativeStaticLayerRenderOnly = previousStaticLayerRenderOnly;
                DashboardScriptArguments.NativeStaticLayerGraphics = previousStaticLayerGraphics;
            }
        }

        private bool TryDrawNativeStaticImageLayer(int dashboardIndex, Dashboard dashboard, Graphics graphics)
        {
            if (!dashboard.Descriptor.SupportsNativeStaticImageCache)
            {
                return false;
            }

            var state = new NativeStaticImageLayerState(
                dashboard.ConfigurationRevision,
                GetDashboardStaticContextSignature(DashboardScriptArguments.GameContext),
                graphics.Width,
                graphics.Height);
            if (_nativeStaticImageLayerFailures.TryGetValue(dashboardIndex, out var failedState)
                && failedState == state)
            {
                return false;
            }

            if (!_nativeStaticImageLayers.TryGetValue(dashboardIndex, out var staticLayer)
                || !_nativeStaticImageLayerStates.TryGetValue(dashboardIndex, out var cachedState)
                || cachedState != state)
            {
                DisposeNativeStaticImageLayer(dashboardIndex);
                staticLayer = new NativeStaticImageLayer();
                try
                {
                    if (!staticLayer.TryBuild(
                        graphics,
                        dashboard,
                        staticGraphics => RenderNativeStaticLayerOnly(dashboard, staticGraphics)))
                    {
                        staticLayer.Dispose();
                        _nativeStaticImageLayerFailures[dashboardIndex] = state;
                        _logger.Warn(
                            "Native static image cache is unavailable for dashboard {0}; using full-frame rendering.",
                            dashboard.Descriptor.Name);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    staticLayer.Dispose();
                    _nativeStaticImageLayerFailures[dashboardIndex] = state;
                    _logger.Warn(
                        ex,
                        "Failed to build native static image cache for dashboard {0}; using full-frame rendering.",
                        dashboard.Descriptor.Name);
                    return false;
                }

                _nativeStaticImageLayers[dashboardIndex] = staticLayer;
                _nativeStaticImageLayerStates[dashboardIndex] = state;
                _nativeStaticImageLayerFailures.Remove(dashboardIndex);
                _logger.Debug("Built native static image cache for dashboard {0}.", dashboard.Descriptor.Name);
            }

            if (staticLayer.TryDraw(graphics))
            {
                return true;
            }

            DisposeNativeStaticImageLayer(dashboardIndex, clearFailure: false);
            _nativeStaticImageLayerFailures[dashboardIndex] = state;
            _logger.Warn(
                "Failed to draw native static image cache for dashboard {0}; using full-frame rendering.",
                dashboard.Descriptor.Name);
            return false;
        }

        private void RenderNativeStaticLayerOnly(Dashboard dashboard, object staticGraphics)
        {
            var previousStaticLayerEnabled = DashboardScriptArguments.NativeStaticLayerEnabled;
            var previousStaticLayerRenderOnly = DashboardScriptArguments.NativeStaticLayerRenderOnly;
            var previousStaticLayerGraphics = DashboardScriptArguments.NativeStaticLayerGraphics;
            DashboardScriptArguments.NativeStaticLayerEnabled = false;
            DashboardScriptArguments.NativeStaticLayerRenderOnly = true;
            DashboardScriptArguments.NativeStaticLayerGraphics = staticGraphics;

            try
            {
                dashboard.Render(DashboardScriptArguments);
            }
            finally
            {
                DashboardScriptArguments.NativeStaticLayerEnabled = previousStaticLayerEnabled;
                DashboardScriptArguments.NativeStaticLayerRenderOnly = previousStaticLayerRenderOnly;
                DashboardScriptArguments.NativeStaticLayerGraphics = previousStaticLayerGraphics;
            }
        }

        private void DisposeNativeStaticImageLayer(int dashboardIndex, bool clearFailure = true)
        {
            if (_nativeStaticImageLayers.Remove(dashboardIndex, out var staticLayer))
            {
                staticLayer.Dispose();
            }

            _nativeStaticImageLayerStates.Remove(dashboardIndex);
            if (clearFailure)
            {
                _nativeStaticImageLayerFailures.Remove(dashboardIndex);
            }
        }

        private void DisposeNativeStaticImageLayers()
        {
            foreach (var staticLayer in _nativeStaticImageLayers.Values)
            {
                staticLayer.Dispose();
            }

            _nativeStaticImageLayers.Clear();
            _nativeStaticImageLayerStates.Clear();
            _nativeStaticImageLayerFailures.Clear();
        }
        
        /// <summary>
        /// Render dashboards using command-proxy-only dashboard instances so Web
        /// rendering does not access the native Direct2D render thread or resources.
        /// </summary>
        private WebDashboardDrawFrame CaptureWebDashboardCommands(
            ISet<int> excludedDashboardIndexes = null)
        {
            try
            {
                InitializeWebDashboardRenderer();
                _webGraphicsProxy.FPS = Volatile.Read(ref _webDashboardFrameRate);
                EnsureWebDashboardProxyResources();
                
                _webDashboardArgs.GameData = DashboardScriptArguments.GameData;
                _webDashboardArgs.GameContext = DashboardScriptArguments.GameContext;

                _webCommandCapture.ClearRegisteredImages();
                for (var i = 0; i < _webDashboards.Count; i++) {
                    var dashboard = _webDashboards[i];
                    if ((excludedDashboardIndexes != null && excludedDashboardIndexes.Contains(i))
                        || !dashboard.Descriptor.IsEnabled
                        || !_webDashboardLoaded[i]
                        || dashboard.ImageResources == null) {
                        continue;
                    }

                    foreach (var imageResource in dashboard.ImageResources) {
                        DashboardImageResourceSource source = null;
                        dashboard.ImageResourceSources?.TryGetValue(imageResource.Key, out source);
                        var imageUrl = source?.WebPath;
                        if (imageUrl == null && imageResource.Value is GraphicsCommandProxy.ProxyImage image) {
                            imageUrl = _webDashboardServer?.GetDashboardImageUrl(image.Path);
                        }
                        _webCommandCapture.RegisterImage(
                            WebDashboardServer.GetDashboardImageCacheKey(i, imageResource.Key),
                            imageResource.Value,
                            imageUrl);
                    }
                }

                _webCommandCapture.ClearCommands();
                _webGraphicsProxy.ClearScene();

                for (var i = 0; i < _webDashboards.Count; i++) {
                    var dashboard = _webDashboards[i];
                    if (excludedDashboardIndexes != null && excludedDashboardIndexes.Contains(i))
                    {
                        // The retained server scene no longer owns this dashboard.
                        // A later compatibility fallback must redraw its static layer.
                        _webDashboardStaticRenderStates.Remove(i);
                        continue;
                    }

                    if (!dashboard.Descriptor.IsEnabled || !_webDashboardLoaded[i]) {
                        continue;
                    }

                    var nativeDashboard = Dashboards[i];
                    var staticRenderState = new WebDashboardStaticRenderState(
                        nativeDashboard.ConfigurationRevision,
                        GetDashboardStaticContextSignature(_webDashboardArgs.GameContext));
                    var staticLayerRefreshRequired = !_webDashboardStaticRenderStates.TryGetValue(
                        i,
                        out var renderedStaticState)
                        || renderedStaticState != staticRenderState;
                    try {
                        _webDashboardArgs.Self = dashboard;
                        _webDashboardArgs.WebDashboardStaticLayerDirty = staticLayerRefreshRequired;
                        _webGraphicsProxy.BeginDashboardCapture($"dashboard-{i}", staticLayerRefreshRequired);
                        dashboard.Render(_webDashboardArgs);
                        if (staticLayerRefreshRequired)
                        {
                            _webDashboardStaticRenderStates[i] = staticRenderState;
                        }
                    }
                    catch (Exception ex) {
                        _logger.Error(ex, "Error capturing WebDashboard commands: {0}", dashboard.Descriptor.Name);
                    }
                    finally
                    {
                        _webGraphicsProxy.EndDashboardCapture();
                    }
                }
                
                return _webCommandCapture.GetWebDashboardDrawFrame();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in CaptureWebDashboardCommands");
                return new WebDashboardDrawFrame(
                    Array.Empty<WebDashboardDrawLayer>(),
                    Array.Empty<DrawingCommand>(),
                    false);
            }
        }

        private void InitializeWebDashboardRenderer()
        {
            if (_webCommandCapture != null) {
                return;
            }

            _webCommandCapture = new DrawingCommandCapture {
                Width = 1920,
                Height = 1080
            };
            _webGraphicsProxy = new GraphicsCommandProxy(
                _webCommandCapture,
                AppLevelVariables.Instance.GetPath(Constants.PATH_FONTS));
            _webDashboardArgs = new DashboardScriptArguments {
                Graphics = _webGraphicsProxy,
                Config = DashboardScriptArguments.Config,
                I18NLoader = DashboardScriptArguments.I18NLoader,
                GameData = DashboardScriptArguments.GameData,
                GameContext = DashboardScriptArguments.GameContext,
                GameOverlayDrawingHelper = DashboardScriptArguments.GameOverlayDrawingHelper,
                IsWebDashboard = true
            };

            _logger.Info("Web rendering initialized - proxy has {0}x{1}", _webGraphicsProxy.Width, _webGraphicsProxy.Height);
        }

        private void EnsureWebDashboardProxyResources()
        {
            for (var i = 0; i < _webDashboards.Count; i++) {
                var dashboard = _webDashboards[i];
                var nativeDashboard = Dashboards[i];
                dashboard.DashboardConfigurations = nativeDashboard.DashboardConfigurations;
                dashboard.Descriptor.IsEnabled = IsDashboardEnabledForWeb(nativeDashboard);

                if (!dashboard.Descriptor.IsEnabled) {
                    if (_webDashboardLoaded[i]) {
                        dashboard.Unload();
                    }
                    _webDashboardLoaded[i] = false;
                    _webDashboardStaticRenderStates.Remove(i);
                    continue;
                }

                if (_webDashboardLoaded[i]) {
                    continue;
                }

                try {
                    dashboard.Load(_webDashboardArgs, true);
                    _webDashboardLoaded[i] = true;
                    _webDashboardStaticRenderStates.Remove(i);
                }
                catch (Exception ex) {
                    _webDashboardLoaded[i] = false;
                    _logger.Error(ex, "Failed to load dashboard for WebDashboard: {0}", dashboard.Descriptor.Name);
                }
            }
        }

        private void InvalidateWebDashboardStaticLayers()
        {
            lock (_webDashboardCaptureLock)
            {
                _webDashboardStaticRenderStates.Clear();
            }
        }

        private static string GetDashboardStaticContextSignature(GameContext context)
        {
            if (context == null)
            {
                return string.Empty;
            }

            return string.Join(
                "\u001f",
                context.TrackName ?? string.Empty,
                context.CarName ?? string.Empty,
                context.CarClass ?? string.Empty,
                context.AudioPackage ?? string.Empty,
                context.PacenoteType ?? string.Empty,
                context.ScriptAuthor ?? string.Empty,
                context.GameState.ToString());
        }

        // private void _window_SetupGraphics(object sender, SetupGraphicsEventArgs e)
        // {
        //     var gfx = e.Graphics;

        //     if (e.RecreateResources)
        //     {
        //         foreach (var pair in _brushes) pair.Value.Dispose();
        //         foreach (var pair in _images) pair.Value.Dispose();
        //     }

        //     _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
        //     _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
        //     _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0);
        //     _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
        // if (Config.Instance.HudChromaKeyMode)
        //     {
        //         // change green to blue, blue to purple, clear to green
        //         _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
        //         _brushes["blue"] = gfx.CreateSolidBrush(255, 0, 255);
        //         _brushes["clear"] = gfx.CreateSolidBrush(0, 255, 0);
        //     }
        //     else
        //     {
        //         _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
        //         _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
        //         _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
        //     }
        //     _brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 100);
            

        //     _brushes["grid"] = gfx.CreateSolidBrush(255, 255, 255, 0.2f);
        //     _brushes["random"] = gfx.CreateSolidBrush(0, 0, 0);
            
        //     _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x3, 0x6, 0xF, 255);

        //     if (e.RecreateResources) return;

        //     _fonts["arial"] = gfx.CreateFont("Arial", 12);
        //     _fonts["consolas"] = gfx.CreateFont("Consolas", 14);

        //     _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
        // }

        private void _window_DestroyGraphics(object sender, DestroyGraphicsEventArgs e)
        {
            lock (_dashboardRenderLock) {
                DisposeNativeStaticImageLayers();
                foreach (var pair in _brushes) pair.Value.Dispose();
                foreach (var pair in _fonts) pair.Value.Dispose();
                foreach (var pair in _images) pair.Value.Dispose();
                for (int i = 0; i < Dashboards.Count; i++) {
                    var dashboard = Dashboards[i];
                    if (DashboardLoaded[i]) {
                        dashboard.Unload();
                        DashboardLoaded[i] = false;
                    }
                }

                _isRunning = false;
            }

            RefreshNativeOverlayVisibility();
        }

        // private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e)
        // {
        //     var gfx = e.Graphics;
        //     gfx.ClearScene(_brushes["clear"]);
        //     //_gridBounds = new Rectangle(20, 60, gfx.Width - 600, gfx.Height - 20);

        //     try
        //     {
        //         if (gfx.Height == 0 || gfx.Width == 0) 
        //         {
        //             // the window is not yet visible
        //             return;
        //         }

        //         drawBasicInfo(gfx);
        //         if (Config.Instance.HudShowTelemetry && TimeToShowTelemetry)
        //         {
        //             drawTelemetry(gfx);
        //         }

        //         if (Config.Instance.HudShowDebugTelemetry && TimeToShowTelemetry)
        //         {
        //             drawDebugTelemetry(gfx);
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.Error("We got exception when drawing hud: {0}", ex.ToString());
        //         GoogleAnalyticsHelper.Instance.TrackExceptionEvent("We got exception when drawing hud", ex.ToString());
        //     }
        // }

        private void drawBasicInfo(Graphics gfx)
        {
            var padding = 200;
            // var infoText = new StringBuilder()
            //     .Append(I18NLoader.Instance["overlay.track"].PadRight(16)).Append(GameContext.TrackName.PadRight(padding)).AppendLine()
            //     .Append(I18NLoader.Instance["overlay.audioPackage"].PadRight(16)).Append(GameContext.AudioPackage.PadRight(padding)).AppendLine()
            //     .Append(I18NLoader.Instance["overlay.scriptAuthor"].PadRight(16)).Append(GameContext.ScriptAuthor.PadRight(padding)).AppendLine()
            //     .Append(I18NLoader.Instance["overlay.dyanmic"].PadRight(16)).Append(GameContext.PacenoteType.PadRight(padding))
            //     .ToString();
            // var size = gfx.MeasureString(_fonts["consolas"], _fonts["consolas"].FontSize, infoText);
            // gfx.DrawTextWithBackground(_fonts["consolas"], _brushes["green"], _brushes["background"], gfx.Width - size.X, 0, infoText);
        }

        private void drawDebugTelemetry(Graphics gfx)
        {
            // gfx.DrawTextWithBackground(_fonts["consolas"], _brushes["green"], _brushes["background"], 0, 0, GameData.ToString());
        }

        private void drawTelemetry(Graphics gfx)
        {
            List<Action<Graphics, float, float, float, float>> drawFuncs = new();
            if (Config.Instance.HudTelemetryShowGBall) drawFuncs.Add(drawGBall);
            if (Config.Instance.HudTelemetryShowSpdSector) drawFuncs.Add(drawSpdSector);
            if (Config.Instance.HudTelemetryShowRPMSector) drawFuncs.Add(drawRPMSector);
            if (Config.Instance.HudTelemetryShowPedals) drawFuncs.Add(drawPedals);
            if (Config.Instance.HudTelemetryShowGear) drawFuncs.Add(drawGear);
            if (Config.Instance.HudTelemetryShowSteering) drawFuncs.Add(drawSteering);
            if (Config.Instance.HudTelemetryShowSuspensionBars) drawFuncs.Add(drawSuspensionBars);
            
            // calculate the margin, padding, pos of each element
            var telemetryHeight = gfx.Height * Config.Instance.HudSizePercentage;
            var telemetryWidth = telemetryHeight * drawFuncs.Count; // elements are squre?
            var telemetryStartPosX = 0.5f * (gfx.Width - telemetryWidth);
            var telemetryStartPosY = gfx.Height - telemetryHeight;

            var telemetryPaddingH = telemetryHeight * Config.Instance.HudPaddingH;
            var telemetryPaddingV = telemetryHeight * Config.Instance.HudPaddingV;

            var telemetrySpacing = telemetryHeight * Config.Instance.HudElementSpacing;
            
            // drawBackground
            _brushes["telemetryBackground"].Color = new Color(
                _brushes["telemetryBackground"].Color.R,
                _brushes["telemetryBackground"].Color.G,
                _brushes["telemetryBackground"].Color.B,
                255 * Config.Instance.HudBackgroundOpacity);
            gfx.FillRectangle(_brushes["telemetryBackground"], 
                telemetryStartPosX,
                telemetryStartPosY,
                telemetryStartPosX + telemetryWidth,
                telemetryStartPosY + telemetryHeight);

            var elementStartX = telemetryStartPosX + telemetryPaddingH;
            var elementStartY = telemetryStartPosY + telemetryPaddingV;
            var elementHeight = telemetryHeight - telemetryPaddingV * 2f;
            var elementWidth = ((telemetryWidth - telemetryPaddingH * 2f) - (drawFuncs.Count-1) * telemetrySpacing) /
                               drawFuncs.Count;

            foreach (var t in drawFuncs)
            {

                try
                {
                    //TODO: suppress unknown ex for now, I have no env for testing...
                    t(gfx, elementStartX, elementStartY, elementWidth, elementHeight);
                }
                catch (Exception ex)
                {
                    _logger.Error("We got exception when drawing elements: {0}, {1}", ex.ToString(), GameData.ToString());
                    // GoogleAnalyticsHelper.Instance.TrackExceptionEvent($"We got exception when drawing elements with func: {t.ToString()}", ex.Message + UdpMessage.ToString());
                }

                elementStartX += elementWidth + telemetrySpacing;
            }
            
            // draw the finish rate
            gfx.FillRectangle(_brushes["green"], 
                0, 
                telemetryStartPosY + telemetryHeight - 5, 
                GameData.CompletionRate * gfx.Width,
                telemetryStartPosY + telemetryHeight);
        }

        private void drawGBall(Graphics gfx, float x, float y, float width, float height)
        {
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radius = MathF.Min(width, height) * 0.5f;
            gfx.FillCircle(_brushes["grey"], centerX, centerY, radius);
            gfx.DrawLine(_brushes["grid"], centerX - radius, centerY, centerX + radius, centerY, 1);
            gfx.DrawLine(_brushes["grid"], centerX, centerY - radius, centerX, centerY + radius, 1);
            gfx.DrawCircle(_brushes["white"], centerX, centerY, radius -1 , 1);
            gfx.DrawCircle(_brushes["black"], centerX, centerY, radius , 1);
            // the ball
            var ballX = centerX + GameData.G_lat * radius / MAX_G_FORCE;
            var ballY = centerY + GameData.G_long * radius / MAX_G_FORCE;
            gfx.FillCircle(_brushes["red"], ballX, ballY, radius * 0.1f);
        }
        private void drawSpdSector(Graphics gfx, float x, float y, float width, float height)
        {
            maxSpeed = MathF.Max(maxSpeed, GameData.Speed); 
            drawSector(gfx, "SPD (KM/h)", x, y, width, height, GameData.Speed, maxSpeed, Config.Instance.HudSectorThicknessRatio);
        }
        private void drawPedals(Graphics gfx, float x, float y, float width, float height)
        {
            // 3 pedals
            var pedalWidth = 1f / 3.6f * width;
            var spacing = 0.3f / 3.6f * width;
            gfx.FillRectangle(_brushes["grey"], x, y, x + pedalWidth, y + height);
            gfx.FillRectangle(_brushes["grey"], x + pedalWidth + spacing, y, x + 2 * pedalWidth + spacing, y + height);
            gfx.FillRectangle(_brushes["grey"], x + 2 * pedalWidth + 2 * spacing, y, x + width, y + height);
            gfx.DrawRectangle(_brushes["white"], x, y, x + pedalWidth, y + height, 1);
            gfx.DrawRectangle(_brushes["white"], x + pedalWidth + spacing, y, x + 2 * pedalWidth + spacing, y + height, 1);
            gfx.DrawRectangle(_brushes["white"], x + 2 * pedalWidth + 2 * spacing, y, x + width, y + height, 1);
            gfx.DrawRectangle(_brushes["black"], x-1, y-1, x + pedalWidth+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x + pedalWidth + spacing-1, y-1, x + 2 * pedalWidth + spacing+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x + 2 * pedalWidth + 2 * spacing-1, y-1, x + width+1, y + height+1, 1);

            gfx.FillRectangle(_brushes["blue"], 1 + x, 1 + y + height * (1-GameData.Clutch), x + pedalWidth - 1, y + height - 1);
            gfx.FillRectangle(_brushes["red"], 1 + x + pedalWidth + spacing, 1 + y + height * (1-GameData.Brake), x + 2 * pedalWidth + spacing - 1, y + height - 1);
            gfx.FillRectangle(_brushes["green"], 1 + x + 2 * pedalWidth + 2 * spacing, 1 + y + height * (1-GameData.Throttle), x + width - 1, y + height - 1);

        }
        private void drawGear(Graphics gfx, float x, float y, float width, float height)
        {
            // var font = gfx.CreateFont("consolas", width);
            // var actualSize = MathF.Min(width, height);
            // gfx.DrawText(_fonts["consolas"], actualSize, _brushes["white"], x, y, getGearText(Convert.ToInt32(UdpMessage.Gear)));
            var columns = Convert.ToInt32(MathF.Ceiling(GameData.MaxGears * 0.5f)) + 1;

            var barWidth = width / (columns + (columns-1) * Config.Instance.HudSectorThicknessRatio);
            var spacingH = Config.Instance.HudSectorThicknessRatio * barWidth;
            var barHeight = height / (2 + Config.Instance.HudSectorThicknessRatio);
            var spacingV = barHeight * Config.Instance.HudSectorThicknessRatio;

            List<Rectangle> rectangles = new()
            {
                // R
                new Rectangle(x, y, x + barWidth, y + barHeight),
            };

            for (var i = 1; i <= GameData.MaxGears; i++)
            {
                var row = (i + 1) % 2;
                var column = (i + 1) / 2;
                rectangles.Add(new Rectangle(
                    x + column * (spacingH + barWidth),
                    y + row * (spacingV + barHeight),
                    x + barWidth + column * (spacingH + barWidth),
                    y + barHeight + row * (spacingV + barHeight)
                    ));
            }

            foreach (var r in rectangles)
            {
                gfx.FillRectangle(_brushes["white"], r);
                gfx.DrawRectangle(_brushes["black"], r, 1);
            }

            int gear = Convert.ToInt32(GameData.Gear);
            string gearText = "";
            bool isNGear = false;
            Rectangle rect;
            switch (gear) 
            {
                case -1:
                case 10:
                    rect = rectangles[0];
                    gearText = "R";
                    break;
                case 0:
                    rect = rectangles[0];
                    isNGear = true;
                    gearText = "N";
                    break;
                default:
                    rect = rectangles[gear];
                    gearText = gear.ToString();
                    break;
            }
            if (!isNGear)
            {
                gfx.FillRectangle(_brushes["red"], rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Bottom - 1);
            }
            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                barWidth * 1.5f,
                _brushes["white"],
                _brushes["black"],
                x + 0.5f * barWidth,
                y + spacingV + 1.5f * barHeight,
                gearText);
        }

        private void drawSteering(Graphics gfx, float x, float y, float width, float height)
        {
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radiusOuter = MathF.Min(width, height) * 0.5f;
            var radiusInner = radiusOuter * (1 - Config.Instance.HudSectorThicknessRatio);
            var radiusWidth = radiusOuter - radiusInner;

            var rawSteeringAngle = GameData.Steering * Config.Instance.HudTelemetrySteeringDegree * 0.5f;
            // bg
            IBrush pathBrush;
            IBrush bgBrush;
            if (MathF.Abs(rawSteeringAngle) >= 360)
            {
                pathBrush = _brushes["blue"];
                bgBrush = _brushes["white"];
            }
            else
            {
                pathBrush = _brushes["white"];
                bgBrush = _brushes["grey"];
            }
            gfx.FillCircle(bgBrush, centerX, centerY, radiusOuter);
            
            
            var steeringAngle = 90 - rawSteeringAngle;
            steeringAngle = steeringAngle / 180 * MathF.PI; // to radian
            var middle = 0.5f * (radiusInner + radiusOuter);

            
            
            gfx.FillCircle(_brushes["black"], centerX, centerY, radiusInner);
            gfx.DrawCircle(_brushes["black"], centerX, centerY, radiusOuter, 1);

            var anchorLeft = new Point(centerX + middle * MathF.Cos(steeringAngle + MathF.PI * 0.5f),
                centerY - middle * MathF.Sin(steeringAngle + MathF.PI * 0.5f));
            var anchorRight = new Point(centerX + middle * MathF.Cos(steeringAngle - MathF.PI * 0.5f),
                centerY - middle * MathF.Sin(steeringAngle - MathF.PI * 0.5f));
            var anchorBottom = new Point(centerX + middle * MathF.Cos(steeringAngle + MathF.PI),
                centerY - middle * MathF.Sin(steeringAngle + MathF.PI));
            
            // cross
            gfx.DrawLine(bgBrush, anchorLeft, anchorRight, radiusWidth);
            gfx.DrawLine(bgBrush, centerX, centerY, anchorBottom.X, anchorBottom.Y, radiusWidth);

            // cursor
            var alpha = MathF.PI / 30f;
            radiusOuter -= 1;
            
            // path
            var arcSize = MathF.Abs(rawSteeringAngle) % 360 >= 180 ? ArcSize.Large : ArcSize.Small;
            var sweepDirection = rawSteeringAngle > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
            var backsDirection = rawSteeringAngle < 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
            var geo_path = gfx.CreateGeometry();
            geo_path.BeginFigure(new Point(centerX,
                centerY - radiusOuter), true);
            geo_path.AddCurve(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle),
                centerY - radiusOuter * MathF.Sin(steeringAngle)), radiusOuter, arcSize, sweepDirection);
            geo_path.AddPoint(new Point(centerX + radiusInner * MathF.Cos(steeringAngle),
                centerY - radiusInner * MathF.Sin(steeringAngle)));
            geo_path.AddCurve(new Point(centerX,
                centerY - radiusInner), radiusInner, arcSize, backsDirection);
            geo_path.EndFigure();
            geo_path.Close();

            gfx.FillGeometry(geo_path, pathBrush);
            geo_path.Dispose();
            
            var geo_cur = gfx.CreateGeometry();
            geo_cur.BeginFigure(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle + alpha),
                centerY - radiusOuter * MathF.Sin(steeringAngle + alpha)), true);
            geo_cur.AddCurve(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle - alpha),
                centerY - radiusOuter * MathF.Sin(steeringAngle - alpha)), radiusOuter, ArcSize.Small, SweepDirection.Clockwise);
            geo_cur.AddPoint(new Point(centerX + radiusInner * MathF.Cos(steeringAngle - alpha),
                centerY - radiusInner * MathF.Sin(steeringAngle - alpha)));
            geo_cur.AddCurve(new Point(centerX + radiusInner * MathF.Cos(steeringAngle + alpha),
                centerY - radiusInner * MathF.Sin(steeringAngle + alpha)), radiusInner, ArcSize.Small, SweepDirection.CounterClockwise);
            geo_cur.EndFigure();
            geo_cur.Close();
            
            gfx.FillGeometry(geo_cur, _brushes["red"]);
            geo_cur.Dispose();
        }
        
        private string getGearText(int g)
        {
            return g switch
            {
                -1 => "R",
                10 => "R",
                0 => "N",
                _ => g.ToString()
            };
        }
        private void drawRPMSector(Graphics gfx, float x, float y, float width, float height)
        {
            var t = drawSector(gfx, "RPM", x, y, width, height, GameData.RPM, GameData.MaxRPM, Config.Instance.HudSectorThicknessRatio);

        }
        private void drawSuspensionBars(Graphics gfx, float x, float y, float width, float height)
        {
            // wheel spd, suspension, wheel temp
            var bgWidth = 0.3f * width;
            var bgHeight = 0.45f * height;
            var spacingH = 0.4f * width;
            var spacingV = 0.1f * height;
            var barWidth = bgWidth / 4f;

            // background
            gfx.FillRectangle(_brushes["grey"], x, y, x + bgWidth, y + bgHeight);
            gfx.FillRectangle(_brushes["grey"], x + bgWidth + spacingH, y, x + width, y + bgHeight);
            gfx.FillRectangle(_brushes["grey"], x, y + bgHeight + spacingV, x + bgWidth, y + height);
            gfx.FillRectangle(_brushes["grey"], x + bgWidth + spacingH, y + bgHeight + spacingV, x + width, y + height);
            // gfx.DrawRectangle(_brushes["white"], x-1, y-1, x + width+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x-1, y-1, x + width+1, y + height+1, 1);
            // gfx.DrawRectangle(_brushes["black"], x + bgWidth + spacingH, y, x + width, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["black"], x, y + bgHeight + spacingV, x + bgWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["black"], x + bgWidth + spacingH, y + bgHeight + spacingV, x + width, y + height, 1);
            
            
            // wheel spd
            // gfx.DrawRectangle(_brushes["green"], x, y + bgHeight, x + barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["green"], x + width - barWidth, y + bgHeight, x + width, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["green"], x, y + bgHeight + spacingV + bgHeight, x + barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["green"], x + width - barWidth, y + bgHeight + spacingV + bgHeight, x + width, y + height, 1);
            // x += 1;
            // y += 1;
            // width -= 2;
            // height -= 2;
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedFrontLeft);
            gfx.FillRectangle(_brushes["green"], x, y + (1-GameData.SpeedFrontLeft / maxWheelSpeed) * bgHeight, x + barWidth, y + bgHeight);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedFrontRight);
            gfx.FillRectangle(_brushes["green"], x + width - barWidth, y + (1-GameData.SpeedFrontRight / maxWheelSpeed) * bgHeight, x + width, y + bgHeight);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedRearLeft);
            gfx.FillRectangle(_brushes["green"], x, y + bgHeight + spacingV + (1-GameData.SpeedRearLeft / maxWheelSpeed) * bgHeight, x + barWidth, y + height);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedRearRight);
            gfx.FillRectangle(_brushes["green"], x + width - barWidth, y + bgHeight + spacingV + (1-GameData.SpeedRearRight / maxWheelSpeed) * bgHeight, x + width, y + height);
            
            // brake temp
            // gfx.DrawRectangle(_brushes["red"], x + barWidth, y + bgHeight, x + 2 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight, x + width - barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["red"], x + barWidth, y + bgHeight + spacingV + bgHeight, x + 2 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - barWidth, y + height, 1);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempFrontLeft);
            gfx.FillRectangle(_brushes["red"], x + barWidth, y + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempFrontLeft)) * bgHeight, x + 2 * barWidth, y + bgHeight);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempFrontRight);
            gfx.FillRectangle(_brushes["red"], x + width - 2 * barWidth, y + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempFrontRight)) * bgHeight, x + width - barWidth, y + bgHeight);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempRearLeft);
            gfx.FillRectangle(_brushes["red"], x + barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempRearLeft)) * bgHeight, x + 2 * barWidth, y + height);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempRearRight);
            gfx.FillRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempRearRight)) * bgHeight, x + width - barWidth, y + height);

            // suspension
            // gfx.DrawRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight, x + 3 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight, x + width - 2 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight + spacingV + bgHeight, x + 3 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - 2 * barWidth, y + height, 1);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionFrontLeft);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionFrontLeft);
            gfx.FillRectangle(_brushes["white"], x + 2 * barWidth, y + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionFrontLeft)) * bgHeight, x + 3 * barWidth, y + bgHeight);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionFrontRight);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionFrontRight);
            gfx.FillRectangle(_brushes["white"], x + width - 3 * barWidth, y + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionFrontRight)) * bgHeight, x + width - 2 * barWidth, y + bgHeight);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionRearLeft);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionRearLeft);
            gfx.FillRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionRearLeft)) * bgHeight, x + 3 * barWidth, y + height);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionRearRight);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionRearRight);
            gfx.FillRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionRearRight)) * bgHeight, x + width - 2 * barWidth, y + height);
            
            // suspension_speed
            // gfx.DrawRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight, x + 4 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight, x + width - 3 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight + spacingV + bgHeight, x + 4 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - 3 * barWidth, y + height, 1);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedFrontLeft);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedFrontLeft);
            gfx.FillRectangle(_brushes["blue"], x + 3 * barWidth, y + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedFrontLeft)) * bgHeight, x + 4 * barWidth, y + bgHeight);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedFrontRight);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedFrontRight);
            gfx.FillRectangle(_brushes["blue"], x + width - 4 * barWidth, y + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedFrontRight)) * bgHeight, x + width - 3 * barWidth, y + bgHeight);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedRearLeft);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedRearLeft);
            gfx.FillRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedRearLeft)) * bgHeight, x + 4 * barWidth, y + height);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedRearRight);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedRearRight);
            gfx.FillRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedRearRight)) * bgHeight, x + width - 3 * barWidth, y + height);

        }

        private float getMaxMinBarPartition(float max, float min, float value)
        {
            value = value < min ? min : value;
            value = value > max ? max : value;
            return (value - min) / (max - min);
        }

        private Point drawSector(Graphics gfx, 
            string unit,
            float x, 
            float y, 
            float width, 
            float height, 
            float value, 
            float maxValue, 
            float thicknessRatio=0.2f)
        {
            if (value > maxValue)
            {
                value = maxValue;
            }
            if (value < 0)
            {
                value = 0;
            }
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radiusOuter = MathF.Min(width, height) * 0.5f;
            var radiusInner = radiusOuter * (1 - thicknessRatio);
            // draw backgroud geometry
            var geo_bg = gfx.CreateGeometry();
            geo_bg.BeginFigure(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
                    ),
                true
                );
            geo_bg.AddCurve(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.75f),
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.75f)
                ),
                radiusOuter, ArcSize.Large
            );
            geo_bg.AddPoint(
                new Point(
                    centerX + radiusInner * MathF.Cos(MathF.PI * 1.75f),
                    centerY - radiusInner * MathF.Sin(MathF.PI * 1.75f)
                )
            );
            geo_bg.AddCurve(
                new Point(
                    centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                    centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
                ),
                radiusInner, ArcSize.Large, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            gfx.FillGeometry(geo_bg, _brushes["grey"]);
            gfx.DrawGeometry(geo_bg, _brushes["black"], 1);

            geo_bg.Dispose();
            
            // draw value
            var angle = MathF.PI * 1.25f - value / maxValue * MathF.PI * 1.5f;
            var arcSize = value / maxValue * 270f >= 180f ? ArcSize.Large : ArcSize.Small;
            radiusOuter -= 1;
            radiusInner += 1;
            geo_bg = gfx.CreateGeometry();
            geo_bg.BeginFigure(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
                ),
                true
            );
            geo_bg.AddCurve(
                new Point(
                    centerX + radiusOuter * MathF.Cos(angle),
                    centerY - radiusOuter * MathF.Sin(angle)
                ),
                radiusOuter, arcSize
            );
            geo_bg.AddPoint(
                new Point(
                    centerX + radiusInner * MathF.Cos(angle),
                    centerY - radiusInner * MathF.Sin(angle)
                )
            );
            var endPoint = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
            );
            geo_bg.AddCurve(
                endPoint,
                radiusInner, arcSize, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            gfx.FillGeometry(geo_bg, _brushes["white"]);

            geo_bg.Dispose();

            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                0.2f * radiusOuter,
                _brushes["white"],
                _brushes["black"],
                centerX,
                centerY + radiusInner,
                unit);
            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                0.5f * radiusOuter,
                _brushes["white"],
                _brushes["black"],
                centerX,
                centerY,
                Convert.ToInt32(value).ToString());

            return endPoint;
        }

        private SolidBrush GetRandomColor()
        {
            var brush = _brushes["random"];

            brush.Color = new Color(_random.Next(0, 256), _random.Next(0, 256), _random.Next(0, 256));

            return brush;
        }

        public void Run()
        {
            _window.Create();
            // _window.Join();
        }

        ~GameOverlayManager()
        {
            Dispose(false);
        }

        #region IDisposable Support

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                StopWebDashboard();
                lock (_webDashboardCaptureLock)
                {
                    foreach (var dashboard in _webDashboards)
                    {
                        dashboard.Unload();
                    }

                    _webGraphicsProxy?.Dispose();
                    _webGraphicsProxy = null;
                    _webCommandCapture = null;
                    _webDashboardArgs = null;
                }
                _window?.Dispose();
                _window = null;
                _isRunning = false;
                UpdateNativeOverlayStatus(new NativeOverlayStatus(NativeOverlayState.Unavailable, false));

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region deprecated
        /// <summary>
        /// deprecated
        /// </summary>
        [Obsolete("deprecated")]
        public void StartLoop()
        {
            _isRunning = true;
            _bgw = new BackgroundWorker();
            _bgw.DoWork += (sender, args) =>
            {
                while (_isRunning)
                {
                    // 1. find process   
                    Thread.Sleep(5000);
                    var processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);

                    if (processes.Length > 0 && _window == null)
                    {
                        // dr2 has 2 windows during launching...shit
                        processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);
                        var process = processes.First();
                        try {
                            if (process.MainWindowTitle.StartsWith(GAME_WIN_TITLE, StringComparison.OrdinalIgnoreCase))
                            // only when the game window available.
                                this.InitializeOverlay(process);
                        } catch (Exception ex) {
                            _logger.Trace("Waiting for game window: {0}", GAME_WIN_TITLE);
                        }
                    }

                    if (processes.Length == 0 && _window != null)
                    {
                        // destroy the window
                        _window.Dispose();
                        _window = null;
                    }

                    //if (processes.Length > 0 && _window != null)
                    //{
                    //    // running
                    //    // check full screen ?
                    //    if (WindowHelper.GetForegroundWindow() != _window.Handle)
                    //    {
                    //        try
                    //        {
                    //            Thread.Sleep(3000);
                    //            processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);
                    //            var process = processes.First();
                    //            WindowHelper.EnableBlurBehind(process.MainWindowHandle);
                    //            _window.IsTopmost = true;
                    //            _window.Recreate();
                    //        }
                    //        catch (Exception e)
                    //        {
                    //            MessageBox.Show("BOOOOOOM");
                    //        }
                    //    }
                    //}
                }
            };
            _bgw.RunWorkerAsync();
        }

        /// <summary>
        /// deprecated
        /// </summary>
        [Obsolete("deprecated")]
        public void StopLoop()
        {
            _bgw?.Dispose();
            _bgw = null;
            _window?.Dispose();
            _window = null;
            _isRunning = false;
        }

        #endregion

    }


    public class GameOverlayDrawingHelper {

        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        public Point getPoint(float x, float y)
        {
            return new Point(x, y);
        }

        public Rectangle getRectangle(float x, float y, float right, float bottom)
        {
            return new Rectangle(x, y, right, bottom);
        }

        public Color getColor(byte r, byte g, byte b, byte a)
        {
            return new Color(r, g, b, a);
        }

        public Point drawSector(object gfx, 
            string unit,
            float x, 
            float y, 
            float width, 
            float height, 
            float value, 
            float maxValue, 
            object bgBrush,
            object valueBrush,
            object emptyBrush,
            object textFont,
            float thicknessRatio=0.2f)
        {
            if (value > maxValue)
            {
                value = maxValue;
            }
            if (value < 0)
            {
                value = 0;
            }
            // Use dynamic to support both real Graphics and GraphicsCommandProxy
            dynamic g = gfx;
            dynamic empty = emptyBrush;
            dynamic bg = bgBrush;
            dynamic val = valueBrush;
            dynamic font = textFont;
            
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radiusOuter = MathF.Min(width, height) * 0.5f;
            var radiusInner = radiusOuter * (1 - thicknessRatio);
            // draw backgroud geometry
            var geo_bg = g.CreateGeometry();
            var pointOuterStart = new Point(
                centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
            );
            var pointOuterEnd = new Point(
                centerX + radiusOuter * MathF.Cos(MathF.PI * 1.75f),
                centerY - radiusOuter * MathF.Sin(MathF.PI * 1.75f)
            );
            var pointInnerEnd = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.75f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.75f)
            );
            var pointInnerStart = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
            );
            
            geo_bg.BeginFigure(
                pointOuterStart,
                true
                );
            geo_bg.AddCurve(
                pointOuterEnd,
                radiusOuter, ArcSize.Large
            );
            geo_bg.AddPoint(
                pointInnerEnd
            );
            geo_bg.AddCurve(
                pointInnerStart,
                radiusInner, ArcSize.Large, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            g.FillGeometry(geo_bg, empty);
            g.DrawGeometry(geo_bg, bg, 1);
            geo_bg.Dispose();
            
            // draw value
            var angle = MathF.PI * 1.25f - value / maxValue * MathF.PI * 1.5f;
            var arcSize = value / maxValue * 270f >= 180f ? ArcSize.Large : ArcSize.Small;
            radiusOuter -= 1;
            radiusInner += 1;
            geo_bg = g.CreateGeometry();
            geo_bg.BeginFigure(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
                ),
                true
            );
            geo_bg.AddCurve(
                new Point(
                    centerX + radiusOuter * MathF.Cos(angle),
                    centerY - radiusOuter * MathF.Sin(angle)
                ),
                radiusOuter, arcSize
            );
            geo_bg.AddPoint(
                new Point(
                    centerX + radiusInner * MathF.Cos(angle),
                    centerY - radiusInner * MathF.Sin(angle)
                )
            );
            var endPoint = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
            );
            geo_bg.AddCurve(
                endPoint,
                radiusInner, arcSize, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            g.FillGeometry(geo_bg, val);
            geo_bg.Dispose();

            g.drawTextWithBackgroundCentered(font,
                0.2f * radiusOuter,
                val,
                bg,
                centerX,
                centerY + radiusInner,
                unit);
            g.drawTextWithBackgroundCentered(font,
                0.45f * radiusOuter,
                val,
                bg,
                centerX,
                centerY,
                Convert.ToInt32(value).ToString());

            return endPoint;
        }
    }
}
