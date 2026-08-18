using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.BeamNG;

public sealed class BeamNGIntegrationProvider : IGameIntegrationProvider
{
    public const string UserFolderFieldId = "userFolder";
    public const string InstallBridgeActionId = "installBridge";

    private static readonly IReadOnlySet<string> InstallActionEnabled =
        new HashSet<string>(StringComparer.Ordinal) { InstallBridgeActionId };

    public Type ConfigurationType => typeof(BeamNGRallyBridgeConfig);

    public IReadOnlyList<GameSettingField> Fields { get; } =
    [
        new(
            UserFolderFieldId,
            "game.beamng.bridge.userFolder",
            GameSettingFieldKind.DirectoryPath,
            "game.beamng.bridge.browseUserFolder",
            RequiresRestart: true)
    ];

    public IReadOnlyList<string> InformationKeys { get; } =
    [
        "game.beamng.bridge.ports"
    ];

    public IReadOnlyList<GameIntegrationAction> Actions { get; } =
    [
        new(
            InstallBridgeActionId,
            "game.beamng.bridge.install",
            "game.beamng.bridge.installTooltip",
            SuccessTitleKey: "game.beamng.bridge.install",
            FailureTitleKey: "game.beamng.bridge.installFailed",
            ShowSuccessMessage: true)
    ];

    public TimeSpan StatusRefreshInterval => TimeSpan.FromSeconds(1);

    public object? GetFieldValue(IGameConfig configuration, string fieldId)
    {
        var config = GetConfiguration(configuration);
        return fieldId switch
        {
            UserFolderFieldId => config.UserFolderPath,
            _ => throw new ArgumentOutOfRangeException(nameof(fieldId), fieldId, null)
        };
    }

    public void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game)
    {
        var config = GetConfiguration(configuration);
        switch (fieldId)
        {
            case UserFolderFieldId:
                config.UserFolderPath = Convert.ToString(value)?.Trim() ?? string.Empty;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fieldId), fieldId, null);
        }
    }

    public GameIntegrationStatus GetStatus(IGameConfig configuration, IGame game)
    {
        var config = GetConfiguration(configuration);
        if (!BeamNGUserFolderLocator.IsValidUserFolder(config.UserFolderPath))
        {
            return Status("game.beamng.bridge.statusUserFolderRequired");
        }

        var installation = BeamNGBridgeInstaller.GetInstallationInfo(config);
        if (installation.State == BeamNGBridgeInstallationState.Missing)
        {
            return Status("game.beamng.bridge.statusNotInstalled");
        }

        if (!installation.IsCurrent)
        {
            return Status(
                "game.beamng.bridge.statusUpdateRequired",
                installation.InstalledVersion?.ToString() ?? "?",
                installation.BundledVersion?.ToString() ?? "?");
        }

        if (game.IsInitialized && game.GameDataReader is BeamNGGameDataReader reader)
        {
            if (!reader.IsBridgeListening)
            {
                return Status("game.beamng.bridge.statusPortUnavailable", BeamNGRallyBridgeConfig.DefaultPort);
            }

            if (reader.IsBridgeConnected)
            {
                return Status("game.beamng.bridge.statusConnected", BeamNGRallyBridgeConfig.DefaultPort);
            }

            return Status(
                reader.LastBridgePacketUtc is null
                    ? "game.beamng.bridge.statusWaiting"
                    : "game.beamng.bridge.statusNoRecentData",
                BeamNGRallyBridgeConfig.DefaultPort);
        }

        return Status("game.beamng.bridge.statusInstalled");
    }

    public Task<GameIntegrationActionResult> ExecuteActionAsync(
        string actionId,
        IGameConfig configuration,
        IGame game,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(actionId, InstallBridgeActionId, StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(actionId), actionId, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var installedPath = BeamNGBridgeInstaller.Install(GetConfiguration(configuration));
        return Task.FromResult(new GameIntegrationActionResult(
            true,
            "game.beamng.bridge.installed",
            MessageIsResourceKey: true,
            MessageArguments: new object[] { installedPath }));
    }

    private static BeamNGRallyBridgeConfig GetConfiguration(IGameConfig configuration)
    {
        return configuration as BeamNGRallyBridgeConfig
            ?? throw new ArgumentException("Unexpected BeamNG Rally bridge configuration type.", nameof(configuration));
    }

    private static GameIntegrationStatus Status(string key, params object[] arguments)
    {
        return new GameIntegrationStatus(key, arguments, InstallActionEnabled);
    }
}
