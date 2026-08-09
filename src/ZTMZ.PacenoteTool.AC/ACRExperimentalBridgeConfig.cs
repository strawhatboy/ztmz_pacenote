using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

[GameConfig(Name)]
public sealed class ACRExperimentalBridgeConfig : IGameConfig
{
    public const string Name = "acr_experimental_live_bridge";

    // Enables the experimental bridge and its per-game-process injection confirmation.
    public bool ControlsEnabled { get; set; }
}
