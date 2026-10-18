using System.Diagnostics;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

namespace ZTMZ.PacenoteTool.UI.Windows;

/// <summary>
/// Owns the native overlay's game-session lifecycle. Hosts may request an
/// attachment, but only this controller changes the stage-active state.
/// </summary>
public sealed class NativeOverlaySessionController : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly object _initializationLock = new();
    private readonly ZTMZPacenoteTool _tool;
    private readonly Func<CoDriverPackageInfo?> _selectedCoDriver;
    private readonly GameOverlayManager _overlay;
    private readonly bool _ownsOverlay;
    private IGame? _attachedGame;
    private int _initializationProcessId = -1;
    private long _initializationGeneration;
    private int _isInitializing;
    private int _stageActive;
    private bool _disposed;
    private readonly SemaphoreSlim _backendGate = new(1, 1);
    private InGameOverlaySession? _inGame;
    private OverlayFailureInfo? _backendFailure;
    private readonly Func<Func<bool>, Task<bool>>? _confirmSelection;

    public NativeOverlaySessionController(
        ZTMZPacenoteTool tool,
        Func<CoDriverPackageInfo?> selectedCoDriver,
        GameOverlayManager overlay,
        bool ownsOverlay = false,
        Func<Func<bool>, Task<bool>>? confirmSelection = null)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _selectedCoDriver = selectedCoDriver ?? throw new ArgumentNullException(nameof(selectedCoDriver));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _ownsOverlay = ownsOverlay;
        _confirmSelection = confirmSelection;
        OverlayBackendSettings.ConfirmSelection = confirmSelection;
        OverlayBackendSettings.Changed += OnBackendChanged;

        _overlay.OnNativeOverlayStatusChanged += _ => RaiseChanged();
        _overlay.OnGameOverlayInitializingStateChanged += isInitializing =>
        {
            Volatile.Write(ref _isInitializing, isInitializing ? 1 : 0);
            RaiseChanged();
        };
        _tool.onGameEnded += OnGameEnded;
        _tool.onGameUninitialized += OnGameUninitialized;
        _tool.onRaceBegin += OnRaceBegin;
        _tool.onRaceEnd += OnRaceEnd;
        _tool.onRaceBegined += OnRaceBegined;
        _tool.onTrackAndCarChanged += OnTrackAndCarChanged;
        _tool.onLocalReplayLoaded += OnLocalReplayLoaded;
        _tool.onNewGameData += OnNewGameData;
        _tool.onPacenoteDispatched += OnPacenoteDispatched;
        _tool.onGameStateChanged += OnGameStateChanged;
    }

    public event EventHandler? Changed;

    public GameOverlayManager Overlay => _overlay;

    public OverlayFailureInfo? Failure
    {
        get
        {
            if (Volatile.Read(ref _backendFailure) is { } failure) return failure;
            var session = _inGame;
            return session?.Error is { Length: > 0 } error
                ? new OverlayFailureInfo(session.FailureStatusKey, null, error) : null;
        }
    }

    public OverlayBackendDecision BackendDecision => Volatile.Read(ref _backendFailure) is { } failure
        ? new(OverlayBackendSettings.Requested, OverlayBackendSettings.Requested, failure.StatusKey, false)
        : _inGame is { } session
        ? new(OverlayBackendSettings.Requested, OverlayDisplayBackend.InGame,
            session.IsActive ? "overlayBackend.inGameActive"
                : string.IsNullOrEmpty(session.Error) ? "overlayBackend.waitingForRenderer"
                : session.FailureStatusKey, true)
        : OverlayBackendSettings.GetDecision(_tool.CurrentGame);

    public NativeOverlayStatus CurrentStatus => _overlay?.CurrentNativeOverlayStatus is { State: NativeOverlayState.Preview } preview
        ? preview
        : Volatile.Read(ref _backendFailure) != null
        ? new(Config.Instance.UI_ShowHud ? NativeOverlayState.Unavailable : NativeOverlayState.Disabled, false)
        : _inGame is { } session
        ? new(!Config.Instance.UI_ShowHud ? NativeOverlayState.Disabled
            : session.IsActive ? NativeOverlayState.Active
            : string.IsNullOrEmpty(session.Error) ? NativeOverlayState.Ready : NativeOverlayState.Unavailable,
            session.IsActive)
        : _overlay.CurrentNativeOverlayStatus;

    private void OnBackendChanged()
    {
        IGame? game;int processId;
        lock (_initializationLock)
        {
            game = _attachedGame;processId = _initializationProcessId;
            _initializationProcessId = -1; // A backend change is a new attachment generation.
        }
        if (!_disposed && game != null && processId > 0) TryEnsureAttached(game, processId);
        RaiseChanged();
    }

    public bool IsInitializing => Volatile.Read(ref _isInitializing) != 0;

    public bool IsStageActive => Volatile.Read(ref _stageActive) != 0;

    public bool EnsureAttached(IGame game, Process process)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(process);
        if (_disposed)
        {
            return false;
        }

        int processId;
        try
        {
            processId = process.Id;
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Cannot initialize native overlay because the game process is unavailable.");
            return false;
        }

        long generation;
        lock (_initializationLock)
        {
            if (_initializationProcessId == processId && ReferenceEquals(_attachedGame, game))
            {
                Logger.Debug("Ignoring duplicate native-overlay attachment request for process {0}.", processId);
                return false;
            }

            _attachedGame = game;
            _initializationProcessId = processId;
            generation = ++_initializationGeneration;
            Volatile.Write(ref _backendFailure, null);
        }

        // A state reset belongs to the accepted new session only. A late duplicate
        // request must never blank a dashboard that is already active.
        _inGame?.CancelPendingWork();
        SetStageActive(false);
        _ = Task.Run(() => InitializeOverlayAsync(game, process, processId, generation));
        return true;
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
            TryEnsureAttached(change.Activity.Game, change.Activity.ProcessId);
            return;
        }

        if (change.Kind == GameActivityChangeKind.Stopped &&
            change.SessionActivated &&
            change.CurrentActivity is { } fallback &&
            ReferenceEquals(change.CurrentGame, fallback.Game))
        {
            TryEnsureAttached(fallback.Game, fallback.ProcessId);
            return;
        }

        if (change.Kind == GameActivityChangeKind.Stopped && change.CurrentGame == null)
        {
            Detach();
        }
    }

    public void Detach()
    {
        _inGame?.CancelPendingWork();
        lock (_initializationLock)
        {
            _attachedGame = null;
            _initializationProcessId = -1;
            _initializationGeneration++;
            Volatile.Write(ref _backendFailure, null);
        }

        SetStageActive(false);
        _overlay.UninitializeOverlay();
        _ = StopInGameAsync();
    }

    private async Task StopInGameAsync()
    {
        await _backendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A newer attachment owns cleanup if a game has already replaced this one.
            lock (_initializationLock) { if (_attachedGame != null) return; }
            if (_inGame != null) {await _inGame.StopAsync().ConfigureAwait(false);_inGame = null;}
        }
        catch (Exception error) {Logger.Error(error, "Could not confirm in-game overlay shutdown.");}
        finally {_backendGate.Release();RaiseChanged();}
    }

    private void TryEnsureAttached(IGame game, int processId)
    {
        try
        {
            EnsureAttached(game, Process.GetProcessById(processId));
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Cannot initialize native overlay for process {0}.", processId);
        }
    }

    private async Task InitializeOverlayAsync(IGame game, Process process, int processId, long generation)
    {
        string? libraryPath = null;
        await _backendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_initializationLock)
            {
                if (_disposed || !IsCurrentInitialization(game, processId, generation)) return;
            }
            if (_inGame != null) {await _inGame.StopAsync().ConfigureAwait(false);_inGame = null;}
            var decision = OverlayBackendPolicy.Evaluate(OverlayBackendSettings.Requested, game, true, true);
            // The persisted mode is the user's selection, including configurations
            // saved before confirmation moved to the GUI. Never override it using
            // the obsolete per-game consent list when attaching a different game.
            Logger.Info("Overlay backend for {0}, PID {1}: requested={2}, effective={3}, reason={4}.",
                game.Name, processId, decision.Requested, decision.Effective, decision.StatusKey);
            bool loaded = false;
            libraryPath = decision.CanAttachInGame ? InGameOverlayLoader.GetLibraryPath(process) : null;
            var compatible = OverlayBackendPolicy.Evaluate(OverlayDisplayBackend.InGame, game, true, true).CanAttachInGame;
            if (compatible)
            {
                try {loaded = InGameOverlaySession.IsLoaded(process);}
                catch (Exception error) {Logger.Debug(error, "Cannot inspect experimental overlay module; retaining window backend.");}
            }
            if (!loaded && decision.CanAttachInGame && _overlay.CurrentNativeOverlayStatus.State != NativeOverlayState.Preview)
            {
                lock (_initializationLock)
                {
                    if (_disposed || !IsCurrentInitialization(game, processId, generation)) return;
                    _overlay.UninitializeOverlay();
                }
                Logger.Info("Loading in-game overlay DLL into {0}, PID {1}: {2}", game.Name, processId, libraryPath);
                await InGameOverlayLoader.LoadAsync(process, libraryPath!).ConfigureAwait(false);
                Logger.Info("In-game overlay DLL loaded for PID {0}; awaiting renderer frame acknowledgement.", processId);
                loaded = true;
            }
            if (loaded && decision.CanAttachInGame && _overlay.CurrentNativeOverlayStatus.State != NativeOverlayState.Preview)
            {
                lock (_initializationLock)
                {
                    if (_disposed || !IsCurrentInitialization(game, processId, generation)) return;
                    _overlay.UninitializeOverlay();
                    SynchronizeStageState(game, "in-game backend attachment");
                }
                _inGame = new(process, _overlay, () => IsStageActive, RaiseChanged);
                await _inGame.StartAsync().ConfigureAwait(false);
                bool current;
                lock (_initializationLock) {current = !_disposed && IsCurrentInitialization(game, processId, generation);}
                if (!current) {await _inGame.StopAsync().ConfigureAwait(false);_inGame = null;}
                RaiseChanged();
                return;
            }
            // A DLL may outlive a previous ZTMZ process even when today's preference is Window.
            if (loaded) await new InGameOverlayControlClient(process).StopAsync().ConfigureAwait(false);
            lock (_initializationLock)
            {
                if (_disposed || !IsCurrentInitialization(game, processId, generation))
                {
                    return;
                }

                _overlay.UninitializeOverlay();
                _overlay.InitializeOverlay(process);
                SynchronizeStageState(game, "initialization");
            }

            _ = ReconcileStageStateAsync(game, processId, generation);
        }
        catch (Exception exception)
        {
            lock (_initializationLock)
            {
                // A failed obsolete request must not replace a newer session's status.
                if (!_disposed && IsCurrentInitialization(game, processId, generation))
                    Volatile.Write(ref _backendFailure, OverlayFailureInfo.FromException(exception,
                        (exception as OverlayComponentException)?.ComponentPath));
            }
            Logger.Error(exception, "Failed to initialize native overlay for process {0}.", processId);
        }
        finally {_backendGate.Release();RaiseChanged();}
    }

    private async Task ReconcileStageStateAsync(IGame game, int processId, long generation)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        lock (_initializationLock)
        {
            if (_disposed || !IsCurrentInitialization(game, processId, generation))
            {
                return;
            }

            SynchronizeStageState(game, "post-initialization synchronization");
        }
    }

    private void SynchronizeStageState(IGame game, string reason)
    {
        var state = game.GameDataReader.GameState;
        SetStageActive(GameStateStageActivity.IsActive(state));
        if (IsStageActive)
        {
            UpdateTelemetry(game.GameDataReader.CurrentGameData);
        }

        Logger.Info(
            "Synchronized native-overlay stage state after {0}: {1} for game state {2}.",
            reason,
            IsStageActive,
            state);
    }

    private bool IsCurrentInitialization(IGame game, int processId, long generation) =>
        ReferenceEquals(_attachedGame, game) &&
        _initializationProcessId == processId &&
        _initializationGeneration == generation;

    private bool IsAttachedGame(IGame game)
    {
        lock (_initializationLock)
        {
            return ReferenceEquals(_attachedGame, game);
        }
    }

    private void OnGameEnded(IGame game)
    {
        if (IsAttachedGame(game))
        {
            Detach();
        }
    }

    private void OnGameUninitialized(IGame game)
    {
        if (IsAttachedGame(game))
        {
            SetStageActive(false);
        }
    }

    private void OnRaceBegin(IGame game)
    {
        if (!IsAttachedGame(game))
        {
            return;
        }

        SetStageActive(true);
        var context = _overlay.DashboardScriptArguments.GameContext;
        context.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
        context.CurrentPacenote = PacenotePlaybackSnapshot.Empty;
    }

    private void OnRaceEnd(IGame game)
    {
        if (!IsAttachedGame(game))
        {
            return;
        }

        SetStageActive(false);
        var context = _overlay.DashboardScriptArguments.GameContext;
        context.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
        context.CurrentPacenote = PacenotePlaybackSnapshot.Empty;
    }

    private void OnRaceBegined(IGame game)
    {
        if (IsAttachedGame(game))
        {
            _overlay.DashboardScriptArguments.GameContext.ScriptAuthor = _tool.CurrentScriptAuthor;
        }
    }

    private void OnTrackAndCarChanged(IGame game, string trackName, string carName, string carClass)
    {
        if (!IsAttachedGame(game))
        {
            return;
        }

        var context = _overlay.DashboardScriptArguments.GameContext;
        context.TrackName = trackName;
        context.CarName = carName;
        context.CarClass = carClass;
        context.AudioPackage = _selectedCoDriver()?.name ?? string.Empty;
    }

    private void OnLocalReplayLoaded(bool loaded)
    {
        var context = _overlay.DashboardScriptArguments.GameContext;
        context.LocalReplayValid = loaded;
        context.LocalReplay = _tool.BestLocalReplay;
        context.LocalReplayDetailsPerCheckpoints = _tool.BestLocalReplayDetailsPerCheckpoints;
        context.LocalReplayDetailsPerTimes = _tool.BestLocalReplayDetailsPerTimes;
        context.LocalReplayDetailsPerTimesDict = _tool.BestLocalReplayDetailsPerTimes
            .GroupBy(item => item.time)
            .ToDictionary(group => group.Key, group => group.First().distance);
        context.LocalReplayFinishTime = _tool.BestLocalReplay?.finish_time ?? 0;
        context.GetDelta = _tool.GetDelta;
        context.GetDeltaByDistanceAndTime = _tool.GetDeltaByDistanceAndTime;
        context.GetDeltaByCheckpoint = _tool.GetDeltaByCheckpoint;
        context.GetTimeByDistance = _tool.GetTimeByDistance;
        context.GetBestTimeByDistance = _tool.GetBestTimeByDistance;
        context.GetBestDistanceByTime = _tool.GetBestDistanceByTime;
        context.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
    }

    private void OnNewGameData(IGame game, GameData data)
    {
        if (IsAttachedGame(game) && IsStageActive)
        {
            UpdateTelemetry(data);
        }
    }

    private void OnPacenoteDispatched(PacenotePlaybackSnapshot snapshot)
    {
        _overlay.DashboardScriptArguments.GameContext.CurrentPacenote = snapshot;
    }

    private void OnGameStateChanged(IGame game, GameStateChangeEvent state)
    {
        if (!IsAttachedGame(game))
        {
            return;
        }

        var context = _overlay.DashboardScriptArguments.GameContext;
        context.LastGameState = (int)state.LastGameState;
        context.GameState = (int)state.NewGameState;
        context.GameStateChangedParameters = state.Parameters;
        SetStageActive(GameStateStageActivity.IsActive(state.NewGameState));
        if (!IsStageActive)
        {
            context.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
            context.CurrentPacenote = PacenotePlaybackSnapshot.Empty;
        }
    }

    private void UpdateTelemetry(GameData data)
    {
        _overlay.DashboardScriptArguments.GameData = data;
        _overlay.DashboardScriptArguments.GameContext.LocalReplayComparison =
            _tool.GetReplayComparison(data);
    }

    private void SetStageActive(bool active)
    {
        Volatile.Write(ref _stageActive, active ? 1 : 0);
        _overlay.SetStageActive(active);
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OverlayBackendSettings.Changed -= OnBackendChanged;
        if (OverlayBackendSettings.ConfirmSelection == _confirmSelection)
            OverlayBackendSettings.ConfirmSelection = null;
        _tool.onGameEnded -= OnGameEnded;
        _tool.onGameUninitialized -= OnGameUninitialized;
        _tool.onRaceBegin -= OnRaceBegin;
        _tool.onRaceEnd -= OnRaceEnd;
        _tool.onRaceBegined -= OnRaceBegined;
        _tool.onTrackAndCarChanged -= OnTrackAndCarChanged;
        _tool.onLocalReplayLoaded -= OnLocalReplayLoaded;
        _tool.onNewGameData -= OnNewGameData;
        _tool.onPacenoteDispatched -= OnPacenoteDispatched;
        _tool.onGameStateChanged -= OnGameStateChanged;
        Detach();
        if (_ownsOverlay)
        {
            _overlay.Dispose();
        }
    }
}
