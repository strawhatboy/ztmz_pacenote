using System.Diagnostics;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;

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

    public NativeOverlaySessionController(
        ZTMZPacenoteTool tool,
        Func<CoDriverPackageInfo?> selectedCoDriver,
        GameOverlayManager overlay,
        bool ownsOverlay = false)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _selectedCoDriver = selectedCoDriver ?? throw new ArgumentNullException(nameof(selectedCoDriver));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _ownsOverlay = ownsOverlay;

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
        }

        // A state reset belongs to the accepted new session only. A late duplicate
        // request must never blank a dashboard that is already active.
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
        lock (_initializationLock)
        {
            _attachedGame = null;
            _initializationProcessId = -1;
            _initializationGeneration++;
        }

        SetStageActive(false);
        _overlay.UninitializeOverlay();
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

    private void InitializeOverlayAsync(IGame game, Process process, int processId, long generation)
    {
        try
        {
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
            Logger.Error(exception, "Failed to initialize native overlay for process {0}.", processId);
        }
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
