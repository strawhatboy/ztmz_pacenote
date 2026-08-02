using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

public sealed class BeamNGRally : IGame, IGamePacenotePlaybackControl
{
    public const string GameName = "BeamNG.drive Rally Mode";

    public string WindowTitle => "BeamNG.drive";

    public string Name => GameName;

    public string Description { get; } = I18NLoader.Instance["game.beamng.description"];

    // The main application icon is used until a dedicated BeamNG thumbnail is available.
    public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.WpfGUI;component/Assets/app.png";

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

    public event Action<int, string>? onCustomMessage;

    public bool IsRunning { get; set; }

    public bool IsInitialized { get; set; }

    public int Order => 4000;

    public bool EnableExternalPacenotes
    {
        get
        {
            return GameConfigurations.TryGetValue(BeamNGRallyBridgeConfig.Name, out var config) &&
                   config is BeamNGRallyBridgeConfig bridgeConfig &&
                   bridgeConfig.EnableExternalPacenotes;
        }
    }

    public BeamNGRally()
    {
        GameConfigurations = DefaultGameConfigurations;
    }

    public void OnCustomMessage(int level, string message)
    {
        onCustomMessage?.Invoke(level, message);
    }
}
