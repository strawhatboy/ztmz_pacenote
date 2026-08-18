#nullable enable

using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public sealed class GameTelemetrySession : IDisposable
{
    private readonly object _sync = new();
    private readonly NLog.Logger _logger;
    private IGame? _currentGame;

    public GameTelemetrySession(NLog.Logger? logger = null)
    {
        _logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
    }

    public event Action<IGame>? GameActivated;
    public event Action<IGame>? GameDeactivated;
    public event Action<IGame, GameData>? TelemetryReceived;

    public IGame? CurrentGame
    {
        get
        {
            lock (_sync)
            {
                return _currentGame;
            }
        }
    }

    public bool Activate(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        lock (_sync)
        {
            if (ReferenceEquals(_currentGame, game) && game.IsInitialized)
            {
                return true;
            }
        }

        Deactivate();
        var reader = game.GameDataReader;
        try
        {
            reader.onNewGameData += OnNewGameData;
            if (!reader.Initialize(game))
            {
                reader.onNewGameData -= OnNewGameData;
                TryUninitialize(game);
                return false;
            }

            lock (_sync)
            {
                _currentGame = game;
                game.IsInitialized = true;
            }

            _logger.Info("Game telemetry session activated for {0}", game.Name);
            InvokeHandlers(GameActivated, game, "activation");
            return true;
        }
        catch (Exception exception)
        {
            reader.onNewGameData -= OnNewGameData;
            TryUninitialize(game);
            game.IsInitialized = false;
            _logger.Error(exception, "Failed to activate game telemetry for {0}", game.Name);
            return false;
        }
    }

    public void Deactivate()
    {
        IGame? game;
        lock (_sync)
        {
            game = _currentGame;
            _currentGame = null;
        }

        if (game == null)
        {
            return;
        }

        try
        {
            game.GameDataReader.onNewGameData -= OnNewGameData;
            game.GameDataReader.Uninitialize(game);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to deactivate game telemetry for {0}", game.Name);
        }
        finally
        {
            game.IsInitialized = false;
            _logger.Info("Game telemetry session deactivated for {0}", game.Name);
            InvokeHandlers(GameDeactivated, game, "deactivation");
        }
    }

    public void Dispose()
    {
        Deactivate();
        GC.SuppressFinalize(this);
    }

    private void OnNewGameData(GameData previous, GameData current)
    {
        IGame? game;
        lock (_sync)
        {
            game = _currentGame;
        }

        if (game != null)
        {
            var handlers = TelemetryReceived;
            if (handlers == null)
            {
                return;
            }

            foreach (Action<IGame, GameData> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(game, current);
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Telemetry handler failed for {0}", game.Name);
                }
            }
        }
    }

    private void TryUninitialize(IGame game)
    {
        try
        {
            game.GameDataReader.Uninitialize(game);
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Failed to clean up telemetry reader for {0}", game.Name);
        }
    }

    private void InvokeHandlers(Action<IGame>? handlers, IGame game, string operation)
    {
        if (handlers == null)
        {
            return;
        }

        foreach (Action<IGame> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(game);
            }
            catch (Exception exception)
            {
                _logger.Error(
                    exception,
                    "Game telemetry {0} handler failed for {1}",
                    operation,
                    game.Name);
            }
        }
    }
}
