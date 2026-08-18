using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

public sealed class BeamNGRally : IGame, IGameConfigurationMigrator, IGameWithIntegrationProviders
{
    public const string GameName = "BeamNG.drive Rally Mode";

    public string WindowTitle => "BeamNG.drive";

    public string Name => GameName;

    public string Description { get; } = I18NLoader.Instance["game.beamng.description"];

    public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.BeamNG;component/beamng.jpg";

    public string Executable => "BeamNG.drive.x64";

    public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();

    public IGameDataReader GameDataReader { get; } = new BeamNGGameDataReader();

    public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new BeamNGGamePrerequisiteChecker();

    public Dictionary<string, IGameConfig> GameConfigurations { get; set; }

    public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new()
    {
        [UdpGameConfig.Name] = new UdpGameConfig
        {
            IPAddress = System.Net.IPAddress.Loopback.ToString(),
            Port = BeamNGGameDataReader.DefaultOutGaugePort
        },
        [BeamNGRallyBridgeConfig.Name] = new BeamNGRallyBridgeConfig()
    };

    public IReadOnlyList<IGameIntegrationProvider> IntegrationProviders { get; } =
    [
        new BeamNGIntegrationProvider()
    ];

    public event Action<int, string>? onCustomMessage;

    public bool IsRunning { get; set; }

    public bool IsInitialized { get; set; }

    public int Order => 4000;

    public BeamNGRally()
    {
        GameConfigurations = DefaultGameConfigurations;
    }

    public void OnCustomMessage(int level, string message)
    {
        onCustomMessage?.Invoke(level, message);
    }

    public bool MigrateGameConfiguration()
    {
        var changed = false;

        if (GameConfigurations.TryGetValue(UdpGameConfig.Name, out var udpGameConfig) &&
            udpGameConfig is UdpGameConfig udpConfig &&
            udpConfig.Port == BeamNGGameDataReader.LegacyIncorrectOutGaugePort)
        {
            udpConfig.Port = BeamNGGameDataReader.DefaultOutGaugePort;
            changed = true;
        }

        if (GameConfigurations.TryGetValue(BeamNGRallyBridgeConfig.Name, out var bridgeGameConfig) &&
            bridgeGameConfig is BeamNGRallyBridgeConfig bridgeConfig &&
            !BeamNGUserFolderLocator.IsValidUserFolder(bridgeConfig.UserFolderPath))
        {
            var detectedUserFolder = BeamNGUserFolderLocator.FindDefaultUserFolder();
            if (BeamNGUserFolderLocator.IsValidUserFolder(detectedUserFolder) &&
                !string.Equals(bridgeConfig.UserFolderPath, detectedUserFolder, StringComparison.OrdinalIgnoreCase))
            {
                bridgeConfig.UserFolderPath = detectedUserFolder;
                changed = true;
            }
        }

        return changed;
    }
}
