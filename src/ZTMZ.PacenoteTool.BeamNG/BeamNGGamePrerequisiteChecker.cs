using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

/// <summary>
/// OutGauge is enabled by the player in BeamNG's Protocols settings. The Rally
/// bridge is a required local dependency for the BeamNG provider.
/// </summary>
public sealed class BeamNGGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    public const string MissingUserFolderReason = "missing-user-folder";
    public const string MissingBridgeReason = "missing-rally-bridge";
    public const string OutdatedBridgeReason = "outdated-rally-bridge";

    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!TryGetBridgeConfig(game, out var bridgeConfig))
        {
            return Failure(PrerequisitesCheckResultCode.UNKNOWN);
        }

        if (!BeamNGUserFolderLocator.IsValidUserFolder(bridgeConfig.UserFolderPath))
        {
            return Failure(PrerequisitesCheckResultCode.SETUP_REQUIRED, MissingUserFolderReason);
        }

        var bridgeInstallation = BeamNGBridgeInstaller.GetInstallationInfo(bridgeConfig);
        if (!bridgeInstallation.IsCurrent)
        {
            return Failure(
                PrerequisitesCheckResultCode.SETUP_REQUIRED,
                bridgeInstallation.State == BeamNGBridgeInstallationState.Missing
                    ? MissingBridgeReason
                    : OutdatedBridgeReason);
        }

        if (!CanListenForBridge(game, out var socketError))
        {
            return Failure(
                socketError == SocketError.AddressAlreadyInUse
                    ? PrerequisitesCheckResultCode.PORT_ALREADY_IN_USE
                    : socketError == SocketError.AccessDenied
                        ? PrerequisitesCheckResultCode.PORT_ACCESS_DENIED
                        : PrerequisitesCheckResultCode.UNKNOWN,
                BeamNGRallyBridgeConfig.DefaultPort);
        }

        return Success();
    }

    public void ForceFix(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!TryGetBridgeConfig(game, out var bridgeConfig))
        {
            throw new InvalidOperationException("BeamNG Rally bridge settings are unavailable.");
        }

        if (!BeamNGUserFolderLocator.IsValidUserFolder(bridgeConfig.UserFolderPath))
        {
            var detectedUserFolder = BeamNGUserFolderLocator.FindDefaultUserFolder();
            if (!BeamNGUserFolderLocator.IsValidUserFolder(detectedUserFolder))
            {
                throw new DirectoryNotFoundException(
                    "Select the BeamNG.drive user folder before installing the Rally bridge.");
            }

            bridgeConfig.UserFolderPath = detectedUserFolder;
        }

        BeamNGBridgeInstaller.Install(bridgeConfig);
    }

    private static bool TryGetBridgeConfig(IGame game, out BeamNGRallyBridgeConfig bridgeConfig)
    {
        if (game.GameConfigurations.TryGetValue(BeamNGRallyBridgeConfig.Name, out var config) &&
            config is BeamNGRallyBridgeConfig configuredBridge)
        {
            bridgeConfig = configuredBridge;
            return true;
        }

        bridgeConfig = null!;
        return false;
    }

    private static bool CanListenForBridge(IGame game, out SocketError? socketError)
    {
        if (game.IsInitialized && game.GameDataReader is BeamNGGameDataReader reader)
        {
            socketError = reader.BridgeListenerSocketError;
            return reader.IsBridgeListening;
        }

        var canBind = UdpPortFinder.CanBind(
            IPAddress.Loopback,
            BeamNGRallyBridgeConfig.DefaultPort,
            out var bindError);
        socketError = canBind ? null : bindError;
        return canBind;
    }

    private static PrerequisitesCheckResult Success()
    {
        return new PrerequisitesCheckResult
        {
            Code = PrerequisitesCheckResultCode.OK,
            IsOK = true,
            Params = new List<object>()
        };
    }

    private static PrerequisitesCheckResult Failure(PrerequisitesCheckResultCode code, params object[] parameters)
    {
        return new PrerequisitesCheckResult
        {
            Code = code,
            IsOK = false,
            Params = new List<object>(parameters)
        };
    }
}
