using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

/// <summary>
/// OutGauge is enabled by the player in BeamNG's Protocols settings. The optional
/// Rally bridge is installed from the dedicated game settings pane.
/// </summary>
public sealed class BeamNGGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        return new PrerequisitesCheckResult
        {
            Code = PrerequisitesCheckResultCode.OK,
            IsOK = true
        };
    }

    public void ForceFix(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!game.GameConfigurations.TryGetValue(BeamNGRallyBridgeConfig.Name, out var config) ||
            config is not BeamNGRallyBridgeConfig bridgeConfig)
        {
            throw new InvalidOperationException("BeamNG Rally bridge settings are unavailable.");
        }

        BeamNGBridgeInstaller.Install(bridgeConfig);
    }
}
