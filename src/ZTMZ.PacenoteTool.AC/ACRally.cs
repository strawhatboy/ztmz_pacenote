using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

public class ACRally : IGame, ILocalGameBridgeDiagnostics
{
    public string WindowTitle => "Assetto Corsa Rally";

    public string Name => "Assetto Corsa Rally";

    public string Description { get; private set; } = "";

    public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.AC;component/acr.jpg";

    public string Executable => "acr";

    private readonly ACRGameDataReader _gameDataReader = new();

    public IGamePacenoteReader GamePacenoteReader { get; }

    public IGameDataReader GameDataReader => _gameDataReader;

    public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new ACRGamePrerequisiteChecker();

    public Dictionary<string, IGameConfig> GameConfigurations { set; get; } = new Dictionary<string, IGameConfig>();

    public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>
    {
        { MemoryGameConfig.Name, new MemoryGameConfig() { RefreshRate = ACRGameDataReader.TARGET_FPS } },
        { ACRExperimentalBridgeConfig.Name, new ACRExperimentalBridgeConfig() }
    };

    public bool IsRunning { get; set; }
    public bool IsInitialized { get; set; }

    public int Order => 4000;


    public event Action<int, string>? onCustomMessage;

    public void OnCustomMessage(int level, string message)
    {
        onCustomMessage?.Invoke(level, message);
    }

    public ACRally()
    {
        this.Description = I18NLoader.Instance["game.acr.description"];
        this.GameConfigurations = DefaultGameConfigurations;
        this.GamePacenoteReader = new ACRGamePacenoteReader(_gameDataReader);
    }

    public LocalBridgeDiagnosticsInfo GetLocalBridgeDiagnostics()
    {
        var enabled = GameConfigurations.TryGetValue(ACRExperimentalBridgeConfig.Name, out var config) &&
            config is ACRExperimentalBridgeConfig bridgeConfig && bridgeConfig.ControlsEnabled;
        var status = ACRExperimentalBridgeController.Inspect();
        return new LocalBridgeDiagnosticsInfo(
            "ACR live bridge",
            enabled,
            status.BundleAvailable,
            status.AlreadyInjected,
            status.Message);
    }
}
