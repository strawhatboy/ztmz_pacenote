using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

[GameConfig(Name)]
public sealed class BeamNGRallyBridgeConfig : IGameConfig
{
    public const string Name = "beamng_rally_bridge";
    public const int DefaultPort = 44445;

    public string UserFolderPath { get; set; } = BeamNGUserFolderLocator.FindDefaultUserFolder();
}
