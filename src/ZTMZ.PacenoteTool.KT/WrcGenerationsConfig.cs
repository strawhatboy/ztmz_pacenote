using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

[GameConfig("wrcg_bridge")]
public sealed class WrcGenerationsConfig : IGameConfig
{
    public const string Name = "wrcg_bridge";
    public bool ControlsEnabled { get; set; }
    public bool ReceiveForwardedTelemetry { get; set; }
}
