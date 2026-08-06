using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

[GameConfig(Name)]
public sealed class ACRExperimentalBridgeConfig : IGameConfig
{
    public const string Name = "acr_experimental_live_bridge";

    // This only unlocks the explicit inject command. It never injects automatically.
    public bool ControlsEnabled { get; set; }
}
