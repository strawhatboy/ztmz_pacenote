using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

[GameConfig(Name)]
public sealed class RBRGameInstallationConfig : IGameConfig
{
    public const string Name = "rbr_installation";

    /// <summary>The RBR executable selected by the player for a portable installation.</summary>
    public string ExecutablePath { get; set; } = string.Empty;
}
