#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public sealed class GameCatalogService
{
    private readonly NLog.Logger _logger;

    public GameCatalogService(NLog.Logger? logger = null)
    {
        _logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
    }

    public IReadOnlyList<IGame> Load(string gamesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamesDirectory);

        if (!Directory.Exists(gamesDirectory))
        {
            _logger.Warn("Game plugin directory does not exist: {0}", gamesDirectory);
            return [];
        }

        var games = GamePluginLoader.Load(
                Directory.EnumerateFiles(gamesDirectory, "*.dll"),
                _logger)
            .OrderBy(game => game.Order)
            .ToList();

        foreach (var game in games)
        {
            game.GameConfigurations = Config.Instance.LoadGameConfig(game);
            if (game is IGameConfigurationMigrator migrator &&
                migrator.MigrateGameConfiguration())
            {
                Config.Instance.SaveGameConfig(game);
                _logger.Info("Migrated game configuration for {0}.", game.Name);
            }
        }

        return games;
    }
}
