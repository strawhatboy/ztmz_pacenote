#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Audio.PortAudio;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WebDashboard;
#if !WINDOWS
using ZTMZ.PacenoteTool.Platform.Linux;
#endif

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
    DesktopNativeOverlaySnapshot NativeOverlay,
    bool IsReady,
    string? ErrorMessage);

internal sealed record VoicePackageSnapshot(
    string Id,
    string Name,
    string Description,
    string Gender,
    string Language,
    string Homepage,
    string Version,
    string OriginalVersion,
    string PackagePath,
    string DownloadUrl,
    bool NeedsUpdate,
    bool NeedsDownload,
    bool IsInstalled,
    float IntegritySimple,
    float IntegrityNormal,
    float IntegrityComplex);

internal sealed record VoicePackageTokenSnapshot(
    int Id,
    string Token,
    string Description,
    string Type,
    IReadOnlyList<string> AudioFiles);

internal interface IDesktopRuntime : IAsyncDisposable
{
    event EventHandler? SnapshotChanged;
    IGame? CurrentGame { get; }
    IReadOnlyList<IGame> Games { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    DesktopRuntimeSnapshot GetSnapshot();
    void ReloadDashboards();
    bool SetNativeOverlayEnabled(bool enabled);
    bool SetNativeOverlayFrameRateLock(bool enabled);
    bool SetNativeOverlayFrameRate(int frameRate);
    bool SetNativeOverlayTopMost(bool enabled);
    bool SetMuted(bool muted);
    bool SetWebDashboardEnabled(bool enabled);
    bool ConfigureWebDashboard(int port, int targetFrameRate);
    bool TrySelectCodriver(string identity);
    bool TrySelectOutputDevice(string id);
    bool SetPlaybackVolume(double volume);
    bool SetPlaybackAdjustSeconds(double seconds);
    bool SetPlaybackSpeed(double speed);
    bool SetSilenceRemovalLevel(int level);
    bool SetIntercomEffect(int effect);
    bool TryPlayExample();
    Task<IReadOnlyList<VoicePackageSnapshot>> GetVoicePackagesAsync(
        CancellationToken cancellationToken = default);
    Task InstallVoicePackageAsync(
        VoicePackageSnapshot package,
        IProgress<CoDriverPackageUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task ImportVoicePackageAsync(string archivePath, string? existingPackagePath = null);
    Task<CoDriverPackageImportPlan> InspectVoicePackageImportAsync(string archivePath);
    Task ExportVoicePackageAsync(string id, string archivePath);
    Task<IReadOnlyList<VoicePackageTokenSnapshot>> GetVoicePackageDetailsAsync(string id);
    void PlayVoicePackageFile(string path);
}

internal sealed class DesktopRuntime : IDesktopRuntime
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private ZTMZPacenoteTool? _tool;
    private WebDashboardRuntimeController? _dashboard;
    private SharedRuntimeControlsService? _controls;
    private HttpClient? _voicePackageHttpClient;
    private CoDriverPackageCatalogService? _voicePackageCatalog;
    private CoDriverPackageUpdateService? _voicePackageUpdates;
    private GameActivitySessionCoordinator? _gameSessions;
    private ZtmzRuntimeState? _state;
#if WINDOWS
    private WindowsNativeOverlayHost? _nativeOverlay;
#endif
    private IReadOnlyList<WebDashboardDefinition> _dashboards = [];
    private string _currentTrack = string.Empty;
    private string _status = "Initializing runtime...";
    private string? _errorMessage;
    private bool _disposed;

    public event EventHandler? SnapshotChanged;

    /// <summary>Gets the currently active game (may be null/not running).</summary>
    public IGame? CurrentGame => _tool?.CurrentGame;

    /// <summary>Gets every game integration loaded by the desktop runtime.</summary>
    public IReadOnlyList<IGame> Games => _tool is null
        ? []
        : _tool.Games.OrderBy(game => game.Order).ToArray();

    public bool SetMuted(bool muted) => _controls?.SetMuted(muted) ?? false;
    public bool SetWebDashboardEnabled(bool enabled) =>
        _controls?.SetWebDashboardEnabled(enabled) ?? false;
    public bool ConfigureWebDashboard(int port, int targetFrameRate) =>
        _controls?.ConfigureWebDashboard(port, targetFrameRate) ?? false;
    public bool TrySelectCodriver(string identity) =>
        _controls?.TrySelectCodriver(identity) ?? false;
    public bool TrySelectOutputDevice(string id) =>
        _controls?.TrySelectOutputDevice(id) ?? false;
    public bool SetPlaybackVolume(double volume) => _controls?.SetPlaybackVolume(volume) ?? false;
    public bool SetPlaybackAdjustSeconds(double seconds) =>
        _controls?.SetPlaybackAdjustSeconds(seconds) ?? false;
    public bool SetPlaybackSpeed(double speed) => _controls?.SetPlaybackSpeed(speed) ?? false;
    public bool SetSilenceRemovalLevel(int level) =>
        _controls?.SetSilenceRemovalLevel(level) ?? false;
    public bool SetIntercomEffect(int effect) => _controls?.SetIntercomEffect(effect) ?? false;
    public bool TryPlayExample() => _controls?.TryPlayExample() ?? false;

    public void ReloadDashboards()
    {
        var resourceRoot = AvaloniaLocalization.ResourceRoot;
        _dashboards = new WebDashboardCatalog(Logger).Load(
            ResolveDashboardPackagesPath(resourceRoot),
            supportsNativeOverlay: OperatingSystem.IsWindows());
        _dashboard?.Reload();
#if WINDOWS
        _nativeOverlay?.ReloadDashboardConfigurations();
#endif
        RaiseSnapshotChanged();
    }

    public bool SetNativeOverlayEnabled(bool enabled)
    {
#if WINDOWS
        return _nativeOverlay?.SetEnabled(enabled) ?? false;
#else
        return false;
#endif
    }

    public bool SetNativeOverlayFrameRateLock(bool enabled)
    {
#if WINDOWS
        return _nativeOverlay?.SetFrameRateLock(enabled) ?? false;
#else
        return false;
#endif
    }

    public bool SetNativeOverlayFrameRate(int frameRate)
    {
#if WINDOWS
        return _nativeOverlay?.SetFrameRate(frameRate) ?? false;
#else
        return false;
#endif
    }

    public bool SetNativeOverlayTopMost(bool enabled)
    {
#if WINDOWS
        return _nativeOverlay?.SetTopMost(enabled) ?? false;
#else
        return false;
#endif
    }

    public async Task<IReadOnlyList<VoicePackageSnapshot>> GetVoicePackagesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_tool == null || _voicePackageUpdates == null)
        {
            return [];
        }

        var localPackages = _tool.CoDriverPackages.Select(package => package.Info).ToArray();
        var updateStatuses = await _voicePackageUpdates.CheckForUpdatesAsync(
            Config.Instance.UpdateDefinitionUrl_AudioPkg,
            localPackages,
            cancellationToken);
        var snapshots = updateStatuses.Select(status => new VoicePackageSnapshot(
            status.Definition.id,
            status.Definition.name,
            status.Definition.description,
            status.Definition.gender,
            status.Definition.language,
            status.Definition.homepage,
            status.Definition.version,
            status.OriginalVersion,
            status.ExistingPackagePath,
            status.Definition.url,
            status.NeedsUpdate,
            status.NeedsDownload,
            !status.NeedsDownload,
            status.Definition.integrity_simple,
            status.Definition.integrity_normal,
            status.Definition.integrity_complex)).ToList();

        foreach (var local in localPackages.Where(local => snapshots.All(item =>
                     !string.Equals(item.Id, local.id, StringComparison.Ordinal))))
        {
            snapshots.Add(new VoicePackageSnapshot(
                local.id ?? string.Empty,
                local.name ?? string.Empty,
                local.description ?? string.Empty,
                local.gender ?? string.Empty,
                local.language ?? string.Empty,
                local.homepage ?? string.Empty,
                local.version ?? string.Empty,
                local.version ?? string.Empty,
                local.Path ?? string.Empty,
                string.Empty,
                false,
                false,
                true,
                local.integrity_simple,
                local.integrity_normal,
                local.integrity_complex));
        }

        return snapshots;
    }

    public async Task InstallVoicePackageAsync(
        VoicePackageSnapshot package,
        IProgress<CoDriverPackageUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_tool == null || _voicePackageUpdates == null)
            throw new InvalidOperationException("The runtime is not ready.");

        await _voicePackageUpdates.DownloadAndInstallAsync(
            package.DownloadUrl,
            package.PackagePath,
            progress,
            cancellationToken);
        await _tool.RefreshCodriversAsync();
        RaiseSnapshotChanged();
    }

    public async Task ImportVoicePackageAsync(string archivePath, string? existingPackagePath = null)
    {
        if (_tool == null || _voicePackageCatalog == null)
            throw new InvalidOperationException("The runtime is not ready.");

        await _voicePackageCatalog.ImportAsync(archivePath, existingPackagePath);
        await _tool.RefreshCodriversAsync();
        RaiseSnapshotChanged();
    }

    public Task<CoDriverPackageImportPlan> InspectVoicePackageImportAsync(string archivePath) =>
        Task.FromResult(_voicePackageCatalog?.InspectImport(archivePath)
            ?? throw new InvalidOperationException("The runtime is not ready."));

    public Task ExportVoicePackageAsync(string id, string archivePath)
    {
        if (_tool == null || _voicePackageCatalog == null)
            throw new InvalidOperationException("The runtime is not ready.");
        var package = _tool.CoDriverPackages.FirstOrDefault(item =>
            string.Equals(item.Info.id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The co-driver package is no longer installed.");
        return _voicePackageCatalog.ExportAsync(package, archivePath);
    }

    public Task<IReadOnlyList<VoicePackageTokenSnapshot>> GetVoicePackageDetailsAsync(string id)
    {
        if (_tool == null)
            throw new InvalidOperationException("The runtime is not ready.");

        var package = _tool.CoDriverPackages.FirstOrDefault(item =>
            string.Equals(item.Info.id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The co-driver package is no longer installed.");
        var resource = ZTMZ.PacenoteTool.Base.Script.ScriptResource.LoadedInstance;
        if (resource == null)
            return Task.FromResult<IReadOnlyList<VoicePackageTokenSnapshot>>([]);

        var locale = I18NLoader.Instance.CurrentCultureName;
        // Voice packages can contain several hundred token entries. Build the snapshots away
        // from the UI thread; the host adds them to its bound collection in small batches.
        return Task.Run<IReadOnlyList<VoicePackageTokenSnapshot>>(() => resource.Pacenotes.Select(pacenote =>
        {
            var token = resource.FilenameDict.TryGetValue(pacenote.id, out var names)
                ? names.FirstOrDefault() ?? pacenote.id.ToString()
                : pacenote.id.ToString();
            var files = package.id2tokensPath.TryGetValue(pacenote.id, out var paths)
                ? paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            var type = resource.TypeDict.TryGetValue(pacenote.type, out var typeDefinition)
                ? typeDefinition.name
                : string.Empty;
            return new VoicePackageTokenSnapshot(
                pacenote.id,
                token,
                resource.ResolvePacenoteDisplayName(token, locale),
                type,
                files);
        }).ToArray());
    }

    public void PlayVoicePackageFile(string path)
    {
        if (_tool == null)
            throw new InvalidOperationException("The runtime is not ready.");
        _tool.PlaySound(path);
    }

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
            GetNativeOverlaySnapshot(),
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
            _voicePackageHttpClient = new HttpClient();
            _voicePackageCatalog = new CoDriverPackageCatalogService(paths);
            _voicePackageUpdates = new CoDriverPackageUpdateService(
                _voicePackageHttpClient,
                new TemporaryFileDownloadService(_voicePackageHttpClient),
                _voicePackageCatalog);
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
                supportsNativeOverlay: OperatingSystem.IsWindows());
            _dashboard = new WebDashboardRuntimeController(
                _tool,
                new WebDashboardRuntimeControllerOptions(
                    dashboardPackagesPath,
                    AppContext.BaseDirectory,
                    Config.Instance.WebDashboardBrowserLuaEnabled,
                    SupportsNativeOverlay: OperatingSystem.IsWindows()));
            _controls = new SharedRuntimeControlsService(_tool, _dashboard);
            _controls.StateChanged += (_, _) => RaiseSnapshotChanged();
#if WINDOWS
            _nativeOverlay = new WindowsNativeOverlayHost(
                _tool,
                () => _controls?.GetSnapshot().SelectedCodriver);
            _nativeOverlay.Changed += (_, _) => RaiseSnapshotChanged();
#endif

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
                _gameSessions.ActivityChanged += (_, change) =>
                {
#if WINDOWS
                    _nativeOverlay?.HandleActivityChanged(change);
#endif
                    RaiseSnapshotChanged();
                };
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
#if WINDOWS
        _nativeOverlay?.Dispose();
#endif
        _tool?.Dispose();
        _voicePackageHttpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SetStatus(string status)
    {
        _status = status;
        RaiseSnapshotChanged();
    }

    private void RaiseSnapshotChanged() => SnapshotChanged?.Invoke(this, EventArgs.Empty);

    private DesktopNativeOverlaySnapshot GetNativeOverlaySnapshot()
    {
#if WINDOWS
        return _nativeOverlay?.GetSnapshot()
            ?? new DesktopNativeOverlaySnapshot(true, Config.Instance.UI_ShowHud, false,
                Config.Instance.HudFPS, Config.Instance.HudLockFPS, Config.Instance.HudTopMost,
                "Unavailable", false);
#else
        return new DesktopNativeOverlaySnapshot(false, false, false, 0, false, false, "Unavailable", false);
#endif
    }

    private static IGameProcessSnapshotProvider CreateProcessSnapshotProvider()
    {
#if WINDOWS
        return new SystemGameProcessSnapshotProvider();
#else
        return new LinuxProcGameProcessSnapshotProvider();
#endif
    }

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
