#nullable enable

using System;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WebDashboard;

public sealed class WebDashboardGameSessionBridge : IDisposable
{
    private readonly ZTMZPacenoteTool _tool;
    private readonly WebDashboardRuntime _dashboard;
    private bool _disposed;

    public WebDashboardGameSessionBridge(
        ZTMZPacenoteTool tool,
        WebDashboardRuntime dashboard)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _tool.onTrackAndCarChanged += OnTrackAndCarChanged;
        _tool.onGameStateChanged += OnGameStateChanged;
        _tool.onPacenoteDispatched += OnPacenoteDispatched;
        _tool.onRaceBegined += OnRaceBegined;
        _tool.onRaceEnd += OnRaceEnd;
        _tool.onNewGameData += OnNewGameData;
    }

    public GameContext Context { get; } = new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tool.onTrackAndCarChanged -= OnTrackAndCarChanged;
        _tool.onGameStateChanged -= OnGameStateChanged;
        _tool.onPacenoteDispatched -= OnPacenoteDispatched;
        _tool.onRaceBegined -= OnRaceBegined;
        _tool.onRaceEnd -= OnRaceEnd;
        _tool.onNewGameData -= OnNewGameData;
    }

    private void OnTrackAndCarChanged(
        IGame game,
        string trackName,
        string carName,
        string carClass)
    {
        Context.TrackName = trackName;
        Context.CarName = carName;
        Context.CarClass = carClass;
        Context.AudioPackage = _tool.ProfileManager?
            .CurrentCoDriverSoundPackageInfo?
            .name ?? string.Empty;
    }

    private void OnGameStateChanged(IGame game, GameStateChangeEvent state)
    {
        Context.LastGameState = (int)state.LastGameState;
        Context.GameState = (int)state.NewGameState;
        Context.GameStateChangedParameters = state.Parameters ?? [];
        if (!GameStateStageActivity.IsActive(state.NewGameState))
        {
            Context.CurrentPacenote = PacenotePlaybackSnapshot.Empty;
            Context.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
        }
    }

    private void OnPacenoteDispatched(PacenotePlaybackSnapshot snapshot) =>
        Context.CurrentPacenote = snapshot;

    private void OnRaceBegined(IGame game) =>
        Context.ScriptAuthor = _tool.CurrentScriptAuthor;

    private void OnRaceEnd(IGame game)
    {
        Context.CurrentPacenote = PacenotePlaybackSnapshot.Empty;
        Context.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
    }

    private void OnNewGameData(IGame game, GameData data) =>
        _dashboard.PublishTelemetry(data, Context);
}
