using NLog;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.BeamNG;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class BeamNGIntegrationProviderTest
{
    [Fact]
    public void PortablePlugin_LoadsWithoutBaseUiAndExposesGenericIntegrationSettings()
    {
        var assembly = typeof(BeamNGRally).Assembly;
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => string.Equals(
                reference.Name,
                "ZTMZ.PacenoteTool.Base.UI",
                StringComparison.Ordinal));

        var game = Assert.Single(GamePluginLoader.Load(
            [assembly.Location],
            LogManager.GetCurrentClassLogger()));
        var integrations = Assert.IsAssignableFrom<IGameWithIntegrationProviders>(game);
        var provider = Assert.Single(integrations.IntegrationProviders);
        var configuration = Assert.IsType<BeamNGRallyBridgeConfig>(
            game.GameConfigurations[BeamNGRallyBridgeConfig.Name]);
        configuration.UserFolderPath = string.Empty;

        Assert.Equal(typeof(BeamNGRallyBridgeConfig), provider.ConfigurationType);
        Assert.Contains(
            provider.Fields,
            field => field.Id == BeamNGIntegrationProvider.UserFolderFieldId &&
                     field.Kind == GameSettingFieldKind.DirectoryPath);
        Assert.Contains(
            provider.Actions,
            action => action.Id == BeamNGIntegrationProvider.InstallBridgeActionId);
        Assert.Equal(
            "game.beamng.bridge.statusUserFolderRequired",
            provider.GetStatus(configuration, game).MessageKey);
    }
}
