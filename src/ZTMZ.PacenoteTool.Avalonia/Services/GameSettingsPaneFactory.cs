#nullable enable

using System.Collections.Generic;
using System.Linq;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Avalonia.Models;
using ZTMZ.PacenoteTool.Avalonia.Views.GameSettings;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

public static class GameSettingsPaneFactory
{
    /// <summary>
    /// Create a GameSettingsPane for the given IGameConfig.
    /// Returns null if no matching pane type or integration provider handles it.
    /// </summary>
    public static GameSettingsPane? CreatePane(IGameConfig configuration, IGame? game = null)
    {
        IGameSettingsProvider? provider = BuiltInGameSettingsProviders.Find(configuration);
        if (provider is null && game is IGameWithIntegrationProviders integrationGame)
        {
            provider = integrationGame.IntegrationProviders
                .FirstOrDefault(p => p.ConfigurationType.IsInstanceOfType(configuration));
        }

        if (provider is null)
            return null;

        var pane = new GameIntegrationSettingsPane(configuration, provider);
        if (game is not null)
            pane.InitializeWithGame(game);
        return pane;
    }

    /// <summary>
    /// Create panes for all configurations of the given game.
    /// </summary>
    public static List<GameSettingsPane> CreatePanesForGame(IGame game)
    {
        var panes = new List<GameSettingsPane>();
        foreach (var kv in game.GameConfigurations)
        {
            var pane = CreatePane(kv.Value, game);
            if (pane is not null)
                panes.Add(pane);
        }
        if (game is IGameWithModCatalog modGame)
        {
            var modPane = new GameModCatalogPane(modGame);
            modPane.InitializeWithGame(game);
            panes.Add(modPane);
        }
        if (game is IGameWithResourceLinks { ResourceLinks.Count: > 0 } resourceGame)
        {
            var resourcePane = new GameResourceLinksPane(resourceGame.ResourceLinks);
            resourcePane.InitializeWithGame(game);
            panes.Add(resourcePane);
        }
        return panes;
    }
}
