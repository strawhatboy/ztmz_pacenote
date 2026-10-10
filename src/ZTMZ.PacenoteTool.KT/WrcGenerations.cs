using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

public sealed class WrcGenerations : IGame, ISteamGameIdentity, IGameWithIntegrationProviders, IGamePacenoteSourcePolicy, ILocalGameBridgeControl
{
    public string Name => "WRC Generations";
    public string WindowTitle => "WRCG";
    public string Description => I18NLoader.Instance["game.wrcg.description"];
    public string ImageUri => new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Games", "wrcg.jpg")).AbsoluteUri;
    public string Executable => "WRCG";
    public uint SteamApplicationId => 1953520;
    public int Order => 4100;
    public bool UseLivePacenotesOnly => true;
    public bool AllowLivePacenotesBeforeGo => true;
    public bool UseTelemetryGoSound => false;
    public bool IsRunning { get; set; }
    public bool IsInitialized { get; set; }
    public IGameDataReader GameDataReader { get; } = new WrcGenerationsGameDataReader();
    public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();
    public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new WrcGenerationsPrerequisiteChecker();
    public Dictionary<string, IGameConfig> DefaultGameConfigurations => new()
    {
        [UdpGameConfig.Name] = new UdpGameConfig { IPAddress = "127.0.0.1", Port = 20778 },
        [WrcGenerationsConfig.Name] = new WrcGenerationsConfig()
    };
    public Dictionary<string, IGameConfig> GameConfigurations { get; set; }
    public IReadOnlyList<IGameIntegrationProvider> IntegrationProviders { get; } = [new WrcGenerationsIntegrationProvider()];
    public event Action<int, string>? onCustomMessage;
    public void OnCustomMessage(int level, string message) => onCustomMessage?.Invoke(level, message);
    public WrcGenerations() => GameConfigurations = DefaultGameConfigurations;

    public string InjectionTitleKey => "game.wrcg.title";
    public string InjectionWarningKey => "game.wrcg.warning";
    public string InjectionPrivilegeWarningKey => "game.wrcg.privilegeWarning";
    internal static string BridgeDirectory => Path.Combine(Path.GetDirectoryName(typeof(WrcGenerations).Assembly.Location)!, "WRCGBridge");
    public LocalBridgeDiagnosticsInfo GetLocalBridgeDiagnostics()
    {
        var enabled = ((WrcGenerationsConfig)GameConfigurations[WrcGenerationsConfig.Name]).ControlsEnabled;
        var connected = ((WrcGenerationsGameDataReader)GameDataReader).BridgeConnected;
        var available = OperatingSystem.IsWindows() && File.Exists(Path.Combine(BridgeDirectory, "WrcGenerationsBridge.dll"))
            && File.Exists(Path.Combine(BridgeDirectory, "WrcGenerationsBridgeInject.exe"));
        return new("WRCG live bridge", enabled, available, connected,
            I18NLoader.Instance[connected ? "game.wrcg.connected" : "game.wrcg.disconnected"]);
    }

    public async Task<LocalBridgeInjectionResult> InjectLocalBridgeAsync()
    {
        var result = await IntegrationProviders[0].ExecuteActionAsync("inject",
            GameConfigurations[WrcGenerationsConfig.Name], this, CancellationToken.None);
        return new(result.Succeeded, result.MessageIsResourceKey ? I18NLoader.Instance[result.Message] : result.Message);
    }
}
