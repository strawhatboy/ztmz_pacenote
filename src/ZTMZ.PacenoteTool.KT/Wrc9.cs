using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

[GameConfig("wrc9_bridge")]
public sealed class Wrc9Config : IGameConfig
{
    public const string Name = "wrc9_bridge";
    public bool ControlsEnabled { get; set; }
}

public sealed class Wrc9 : IGame, ISteamGameIdentity, IGameWithIntegrationProviders, IGamePacenoteSourcePolicy, ILocalGameBridgeControl
{
    public string Name => "WRC 9";
    public string Executable => "WRC9";
    public string WindowTitle => "WRC9";
    public uint SteamApplicationId => 1267540;
    public string Description => I18NLoader.Instance["game.wrc9.description"];
    public string ImageUri => new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Games", "wrc9.jpg")).AbsoluteUri;
    public int Order => 4009;
    public bool IsRunning { get; set; }
    public bool IsInitialized { get; set; }
    public bool UseLivePacenotesOnly => true;
    public bool AllowLivePacenotesBeforeGo => true;
    public bool UseTelemetryGoSound => false;
    public IGameDataReader GameDataReader { get; } = new Wrc9GameDataReader();
    public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();
    public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new Wrc9Prerequisites();
    public Dictionary<string, IGameConfig> DefaultGameConfigurations => new() { [Wrc9Config.Name] = new Wrc9Config() };
    public Dictionary<string, IGameConfig> GameConfigurations { get; set; }
    public IReadOnlyList<IGameIntegrationProvider> IntegrationProviders { get; } = [new Wrc9IntegrationProvider()];
    public event Action<int, string>? onCustomMessage;
    public void OnCustomMessage(int level, string message) => onCustomMessage?.Invoke(level, message);
    public Wrc9() => GameConfigurations = DefaultGameConfigurations;
    public string InjectionTitleKey => "game.wrc9.title";
    public string InjectionWarningKey => "game.wrc9.warning";
    public string InjectionPrivilegeWarningKey => "game.wrc9.privilegeWarning";
    internal static string BridgeDirectory => Path.Combine(Path.GetDirectoryName(typeof(Wrc9).Assembly.Location)!, "WRC9Bridge");
    public LocalBridgeDiagnosticsInfo GetLocalBridgeDiagnostics() => new("WRC9 live bridge",
        ((Wrc9Config)GameConfigurations[Wrc9Config.Name]).ControlsEnabled,
        OperatingSystem.IsWindows() && File.Exists(Path.Combine(BridgeDirectory, "Wrc9Bridge.dll")) && File.Exists(Path.Combine(BridgeDirectory, "Wrc9BridgeInject.exe")),
        ((Wrc9GameDataReader)GameDataReader).BridgeConnected,
        ((Wrc9GameDataReader)GameDataReader).Error ?? I18NLoader.Instance[((Wrc9GameDataReader)GameDataReader).BridgeConnected ? "game.wrc9.connected" : "game.wrc9.disconnected"]);
    public async Task<LocalBridgeInjectionResult> InjectLocalBridgeAsync()
    {
        var result = await IntegrationProviders[0].ExecuteActionAsync("inject", GameConfigurations[Wrc9Config.Name], this, CancellationToken.None);
        return new(result.Succeeded, result.MessageIsResourceKey ? I18NLoader.Instance[result.Message] : result.Message);
    }
    private sealed class Wrc9Prerequisites : IGamePrerequisiteChecker
    {
        public PrerequisitesCheckResult CheckPrerequisites(IGame game) => new()
        {
            Code = OperatingSystem.IsWindows() ? PrerequisitesCheckResultCode.OK : PrerequisitesCheckResultCode.SETUP_REQUIRED,
            IsOK = OperatingSystem.IsWindows(), Msg = OperatingSystem.IsWindows() ? "" : I18NLoader.Instance["game.wrc9.windowsOnly"]
        };
        public void ForceFix(IGame game) { }
    }
}
