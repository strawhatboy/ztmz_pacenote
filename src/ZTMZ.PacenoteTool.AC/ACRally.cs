using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

public class ACRally : IGame, ISteamGameIdentity, ILocalGameBridgeControl, IGameWithIntegrationProviders, IGameWithResourceLinks, IGamePacenoteSourcePolicy
{
    // Extracted distance tables were an experiment, not a supported fallback.
    public bool UseLivePacenotesOnly => true;
    // RaceBegin comes from the countdown sound. The welcome belongs to the
    // earlier, verified native stage-entry signal, not the first countdown number.
    public bool UseRaceBeginStartSound => false;

    public string WindowTitle => "Assetto Corsa Rally";

    public string Name => "Assetto Corsa Rally";

    public string Description { get; private set; } = "";

    public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.AC;component/acr.jpg";

    public string Executable => "acr";

    private readonly IGameDataReader _gameDataReader = OperatingSystem.IsWindows()
        ? new ACRGameDataReader()
        : new ACRProtonGameDataReader();

    public IGamePacenoteReader GamePacenoteReader { get; }

    public IGameDataReader GameDataReader => _gameDataReader;

    public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new ACRGamePrerequisiteChecker();

    public Dictionary<string, IGameConfig> GameConfigurations { set; get; } = new Dictionary<string, IGameConfig>();

    public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>
    {
        { MemoryGameConfig.Name, new MemoryGameConfig() { RefreshRate = ACRGameDataReader.TARGET_FPS } },
        { ACRExperimentalBridgeConfig.Name, new ACRExperimentalBridgeConfig() }
    };

    public IReadOnlyList<IGameIntegrationProvider> IntegrationProviders { get; } =
    [
        new ACRIntegrationProvider()
    ];

    public IReadOnlyList<GameResourceLink> ResourceLinks { get; } =
    [
        new("replacement-chinese-pacenotes", "game.acr.resources.replacementChinesePacenotes",
            new Uri("https://gitee.com/ztmz/acr_pacenotes/releases/tag/0.0.1"),
            GameResourceKind.Pacenotes,
            DescriptionKey: "game.acr.resources.replacementChinesePacenotesDescription")
    ];

    public bool IsRunning { get; set; }
    public bool IsInitialized { get; set; }

    public int Order => 4000;

    public uint SteamApplicationId => 3917090;


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
        var detail = ACRExperimentalBridgeController.FormatStatus(status);
        if (!OperatingSystem.IsWindows())
        {
            var relayStatus = ACRProtonBridgeRuntime.GetStatus();
            if (!string.IsNullOrWhiteSpace(relayStatus.Detail))
            {
                detail = $"{detail} ({relayStatus.Detail})";
            }
        }
        return new LocalBridgeDiagnosticsInfo(
            "ACR live bridge",
            enabled,
            status.BundleAvailable,
            status.AlreadyInjected,
            detail);
    }

    public async Task<LocalBridgeInjectionResult> InjectLocalBridgeAsync()
    {
        var result = await ACRExperimentalBridgeController.InjectAsync();
        return WrapInjectionResult(result);
    }

    private static LocalBridgeInjectionResult WrapInjectionResult(ACRExperimentalBridgeResult result) =>
        new(result.Succeeded, result.Message, result.ConfirmAndRetry is { } retry
            ? async () => WrapInjectionResult(await retry()) : null);
}
