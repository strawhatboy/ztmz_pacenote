#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public interface IGameSessionActivator
{
    IGame? CurrentGame { get; }

    bool ActivateGame(IGame game);

    void DeactivateCurrentGame();
}

public sealed record GameActivitySessionChange(
    GameActivityChangeKind Kind,
    GameActivity Activity,
    bool SessionActivated,
    IGame? CurrentGame);

public sealed class GameActivitySessionCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IGameActivityMonitor _monitor;
    private readonly IGameSessionActivator _sessions;
    private readonly IReadOnlyList<IGame> _games;
    private bool _started;
    private bool _disposed;

    public GameActivitySessionCoordinator(
        IGameActivityMonitor monitor,
        IGameSessionActivator sessions,
        IReadOnlyList<IGame> games)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _games = games ?? throw new ArgumentNullException(nameof(games));
    }

    public event EventHandler<GameActivitySessionChange>? ActivityChanged;

    public IReadOnlyCollection<GameActivity> ActiveGames => _monitor.ActiveGames;

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _monitor.GameStarted += OnGameStarted;
            _monitor.GameStopped += OnGameStopped;
            _started = true;
            try
            {
                _monitor.Start(_games);
            }
            catch
            {
                _started = false;
                _monitor.GameStarted -= OnGameStarted;
                _monitor.GameStopped -= OnGameStopped;
                throw;
            }
        }
    }

    public async Task StopAsync()
    {
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _monitor.GameStarted -= OnGameStarted;
            _monitor.GameStopped -= OnGameStopped;
        }

        await _monitor.StopAsync().ConfigureAwait(false);
        lock (_sync)
        {
            foreach (var game in _games)
            {
                game.IsRunning = false;
            }

            _sessions.DeactivateCurrentGame();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        await _monitor.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnGameStarted(GameActivity activity)
    {
        GameActivitySessionChange change;
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            activity.Game.IsRunning = true;
            var activated = _sessions.ActivateGame(activity.Game);
            change = new GameActivitySessionChange(
                GameActivityChangeKind.Started,
                activity,
                activated,
                _sessions.CurrentGame);
        }

        ActivityChanged?.Invoke(this, change);
    }

    private void OnGameStopped(GameActivity activity)
    {
        GameActivitySessionChange change;
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            activity.Game.IsRunning = false;
            var activated = false;
            if (ReferenceEquals(_sessions.CurrentGame, activity.Game))
            {
                _sessions.DeactivateCurrentGame();
                var fallback = _monitor.ActiveGames.FirstOrDefault();
                if (fallback != null)
                {
                    fallback.Game.IsRunning = true;
                    activated = _sessions.ActivateGame(fallback.Game);
                }
            }

            change = new GameActivitySessionChange(
                GameActivityChangeKind.Stopped,
                activity,
                activated,
                _sessions.CurrentGame);
        }

        ActivityChanged?.Invoke(this, change);
    }
}
