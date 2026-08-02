using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

[GameConfig(Name)]
public sealed class BeamNGRallyBridgeConfig : IGameConfig
{
    public const string Name = "beamng_rally_bridge";
    public const int DefaultPort = 44445;

    public string UserFolderPath { get; set; } = BeamNGUserFolderLocator.FindDefaultUserFolder();

    /// <summary>
    /// BeamNG plays its built-in Rally Mode calls by default. Enabling this makes
    /// Pacenote Tool use its own script and audio for this game instead.
    /// </summary>
    public bool EnableExternalPacenotes { get; set; }
}
