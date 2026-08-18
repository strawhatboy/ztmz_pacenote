#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Audio.PortAudio;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal enum DesktopGameConnectionState
{
    Waiting,
    Connected,
    Failed
}

internal sealed record DesktopGameSnapshot(
    string Name,
    string Description,
    string ImageFileName,
    DesktopGameConnectionState ConnectionState);

internal sealed record DesktopRuntimeSnapshot(
    string Status,
    DesktopGameSnapshot? ActiveGame,
    string CurrentTrack,
    int ActiveGameCount,
    int GameCount,
    int CoDriverCount,
    int OutputDeviceCount,
    IReadOnlyList<WebDashboardDefinition> Dashboards,
    SharedRuntimeControlsSnapshot? Controls,
    bool IsReady,
    string? ErrorMessage);

internal sealed class DesktopRuntime : IAsyncDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private ZTMZPacenoteTool? _tool;
    private WebDashboardRuntimeController? _dashboard;
    private SharedRuntimeControlsService? _controls;
    private GameActivitySessionCoordinator? _gameSessions;
    private ZtmzRuntimeState? _state;
    private IReadOnlyList<WebDashboardDefinition> _dashboards = [];
    private string _currentTrack = string.Empty;
    private string _status = "Initializing runtime...";
    private string? _errorMessage;
    private bool _disposed;

    public event EventHandler? SnapshotChanged;

    public SharedRuntimeControlsService? Controls => _controls;

    public DesktopRuntimeSnapshot GetSnapshot()
    {
        var currentGame = _tool?.CurrentGame;
        var activeGame = currentGame?.IsRunning == true
            ? new DesktopGameSnapshot(
                currentGame.Name,
                currentGame.Description,
                GetImageFileName(currentGame.ImageUri),
                currentGame.IsInitialized
                    ? DesktopGameConnectionState.Connected
                    : DesktopGameConnectionState.Failed)
            : null;

        return new DesktopRuntimeSnapshot(
            _status,
            activeGame,
            _currentTrack,
            _gameSessions?.ActiveGames.Count ?? (activeGame == null ? 0 : 1),
            _state?.Games.Count ?? 0,
            _state?.CoDrivers.Count ?? 0,
            _tool?.OutputDevices.Count ?? 0,
            _dashboards,
            _controls?.GetSnapshot(),
            _state != null && _errorMessage == null,
            _errorMessage);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_tool != null)
        {
            return;
        }

        try
        {
            AudioDecodingServices.Decoder = new SoundFileAudioFileDecoder();
            var paths = AppLevelVariables.Instance;
            var resourceRoot = AvaloniaLocalization.ResourceRoot;
            var gamesDirectory = Path.Combine(AppContext.BaseDirectory, Constants.PATH_GAMES);
            _tool = new ZTMZPacenoteTool(
                new PortAudioAudioOutputDeviceEnumerator(),
                new PortAudioAudioPlaybackEngineFactory());
            _tool.onStatusReport += SetStatus;
            _tool.onGameInitialized += _ => RaiseSnapshotChanged();
            _tool.onGameUninitialized += _ =>
            {
                _currentTrack = string.Empty;
                RaiseSnapshotChanged();
            };
            _tool.onGameInitializeFailed += (_, _, _) => RaiseSnapshotChanged();
            _tool.onGameStateChanged += (_, _) => RaiseSnapshotChanged();
            _tool.onTrackAndCarChanged += (_, track, _, _) =>
            {
                _currentTrack = track ?? string.Empty;
                RaiseSnapshotChanged();
            };

            var dashboardPackagesPath = ResolveDashboardPackagesPath(resourceRoot);
            _dashboards = new WebDashboardCatalog(Logger).Load(
                dashboardPackagesPath,
                supportsNativeOverlay: false);
            _dashboard = new WebDashboardRuntimeController(
                _tool,
                new WebDashboardRuntimeControllerOptions(
                    dashboardPackagesPath,
                    AppContext.BaseDirectory,
                    Config.Instance.WebDashboardBrowserLuaEnabled));
            _controls = new SharedRuntimeControlsService(_tool, _dashboard);
            _controls.StateChanged += (_, _) => RaiseSnapshotChanged();

            _state = await _tool.InitAsync(
                new ZtmzPacenoteToolInitializationOptions(
                    new ZtmzRuntimeOptions(
                        gamesDirectory,
                        new ResourceBootstrapOptions(
                            Path.Combine(
                                resourceRoot,
                                ZTMZ.PacenoteTool.Base.Script.ScriptResource.DBNAME),
                            AvaloniaLocalization.GetLocalizationRoots(resourceRoot, paths),
                            Config.Instance.Language,
                            InitializeLocalization: false,
                            PackagedDefaultCoDriverDirectory: GetExistingDirectory(Path.Combine(
                                resourceRoot,
                                Constants.PATH_CODRIVERS,
                                "default")),
                            PackagedDefaultProfileDirectory: GetExistingDirectory(Path.Combine(
                                resourceRoot,
                                Constants.PATH_PROFILES,
                                Constants.DEFAULT_PROFILE))),
                        CheckPrerequisites: true),
                    InitializeProcessWatcher: false,
                    EnableObsIntegration: false,
                    EnableReplayIntegration: false),
                cancellationToken);
            _tool.SetFromConfiguration();
            _controls.SetWebDashboardEnabled(Config.Instance.WebDashboardEnabled);

            if (_tool.Games.Count > 0)
            {
                var monitor = new PollingGameActivityMonitor(
                    CreateProcessSnapshotProvider(),
                    TimeSpan.FromMilliseconds(
                        Math.Max(100, Config.Instance.ProcessWatcherInterval)));
                _gameSessions = new GameActivitySessionCoordinator(
                    monitor,
                    _tool,
                    _tool.Games);
                _gameSessions.ActivityChanged += (_, _) => RaiseSnapshotChanged();
                _gameSessions.Start();
            }

            SetStatus("Ready");
            Logger.Info(
                "Avalonia desktop runtime initialized. Games={0}; CoDrivers={1}; OutputDevices={2}.",
                _state.Games.Count,
                _state.CoDrivers.Count,
                _state.OutputDevices.Count);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Avalonia desktop runtime initialization failed.");
            _errorMessage = exception.Message;
            SetStatus("Initialization failed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_gameSessions != null)
        {
            await _gameSessions.DisposeAsync().ConfigureAwait(false);
        }

        _dashboard?.Dispose();
        _tool?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SetStatus(string status)
    {
        _status = status;
        RaiseSnapshotChanged();
    }

    private void RaiseSnapshotChanged() => SnapshotChanged?.Invoke(this, EventArgs.Empty);

    private static IGameProcessSnapshotProvider CreateProcessSnapshotProvider() =>
        OperatingSystem.IsWindows()
            ? new SystemGameProcessSnapshotProvider()
            : new LinuxProcGameProcessSnapshotProvider();

    private static string GetImageFileName(string imageUri)
    {
        if (string.IsNullOrWhiteSpace(imageUri))
        {
            return string.Empty;
        }

        var normalized = imageUri.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static string ResolveDashboardPackagesPath(string resourceRoot)
    {
        var packaged = Path.Combine(resourceRoot, Constants.PATH_DASHBOARDS);
        return Directory.Exists(packaged)
            ? packaged
            : Path.Combine(AppContext.BaseDirectory, Constants.PATH_DASHBOARDS);
    }

    private static string? GetExistingDirectory(string path) =>
        Directory.Exists(path) ? path : null;
}
