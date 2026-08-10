using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

/// <summary>
/// Prerequisite checker for Assetto Corsa Rally.
/// ACR uses shared memory for telemetry which is automatically available when the game runs,
/// so no special configuration or setup is required.
/// </summary>
public class ACRGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        // ACR uses shared memory which is automatically created by the game when it runs.
        // No configuration files or port setup is required.
        // The SharedMemoryGameReader will handle connection attempts automatically.
        return new PrerequisitesCheckResult
        {
            Code = PrerequisitesCheckResultCode.OK,
            IsOK = true,
            Msg = I18NLoader.Instance["game.acr.prerequisite.sharedMemoryReady"]
        };
    }

    public void ForceFix(IGame game)
    {
        // Nothing to fix for ACR since it uses shared memory.
        // If shared memory is not available, it means the game is not running.
    }
}
