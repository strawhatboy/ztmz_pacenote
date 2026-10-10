using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

[GameConfig("wrc7_bridge")]
public sealed class Wrc7Config : IGameConfig
{
    public const string Name = "wrc7_bridge";
    public bool ControlsEnabled { get; set; }
}

public sealed class Wrc7 : IGame, ISteamGameIdentity, IGameWithIntegrationProviders, IGamePacenoteSourcePolicy, ILocalGameBridgeControl
{
    public string Name => "WRC 7";
    public string Executable => "WRC7";
    public string WindowTitle => "WRC7";
    public uint SteamApplicationId => 621830;
    public string Description => I18NLoader.Instance["game.wrc7.description"];
    public string ImageUri => new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Games", "wrc7.jpg")).AbsoluteUri;
    public int Order => 4007;
    public bool IsRunning { get; set; }
    public bool IsInitialized { get; set; }
    public bool UseLivePacenotesOnly => true;
    public bool AllowLivePacenotesBeforeGo => true;
    public bool UseTelemetryGoSound => false;
    public IGameDataReader GameDataReader { get; } = new Wrc7GameDataReader();
    public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();
    public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new Wrc7Prerequisites();
    public Dictionary<string, IGameConfig> DefaultGameConfigurations => new() { [Wrc7Config.Name] = new Wrc7Config() };
    public Dictionary<string, IGameConfig> GameConfigurations { get; set; }
    public IReadOnlyList<IGameIntegrationProvider> IntegrationProviders { get; } = [new Wrc7IntegrationProvider()];
    public event Action<int, string>? onCustomMessage;
    public void OnCustomMessage(int level, string message) => onCustomMessage?.Invoke(level, message);
    public Wrc7() => GameConfigurations = DefaultGameConfigurations;
    public string InjectionTitleKey => "game.wrc7.title";
    public string InjectionWarningKey => "game.wrc7.warning";
    public string InjectionPrivilegeWarningKey => "game.wrc7.privilegeWarning";
    internal static string BridgeDirectory => Path.Combine(Path.GetDirectoryName(typeof(Wrc7).Assembly.Location)!, "WRC7Bridge");
    public LocalBridgeDiagnosticsInfo GetLocalBridgeDiagnostics() => new("WRC7 live bridge",
        ((Wrc7Config)GameConfigurations[Wrc7Config.Name]).ControlsEnabled,
        OperatingSystem.IsWindows() && File.Exists(Path.Combine(BridgeDirectory, "Wrc7BridgeAbsolute.dll")) && File.Exists(Path.Combine(BridgeDirectory, "Wrc7BridgeAbsoluteInject.exe")),
        ((Wrc7GameDataReader)GameDataReader).BridgeConnected,
        ((Wrc7GameDataReader)GameDataReader).Error ?? I18NLoader.Instance[((Wrc7GameDataReader)GameDataReader).BridgeConnected ? "game.wrc7.connected" : "game.wrc7.disconnected"]);
    public async Task<LocalBridgeInjectionResult> InjectLocalBridgeAsync()
    {
        var result = await IntegrationProviders[0].ExecuteActionAsync("inject", GameConfigurations[Wrc7Config.Name], this, CancellationToken.None);
        return new(result.Succeeded, result.MessageIsResourceKey ? I18NLoader.Instance[result.Message] : result.Message);
    }
    private sealed class Wrc7Prerequisites : IGamePrerequisiteChecker
    {
        public PrerequisitesCheckResult CheckPrerequisites(IGame game) => new()
        {
            Code = OperatingSystem.IsWindows() ? PrerequisitesCheckResultCode.OK : PrerequisitesCheckResultCode.SETUP_REQUIRED,
            IsOK = OperatingSystem.IsWindows(), Msg = OperatingSystem.IsWindows() ? "" : I18NLoader.Instance["game.wrc7.windowsOnly"]
        };
        public void ForceFix(IGame game) { }
    }
}
