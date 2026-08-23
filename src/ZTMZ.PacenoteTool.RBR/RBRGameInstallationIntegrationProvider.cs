using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public sealed class RBRGameInstallationIntegrationProvider : IGameIntegrationProvider
{
    public const string ExecutablePathFieldId = "executablePath";

    public Type ConfigurationType => typeof(RBRGameInstallationConfig);

    public IReadOnlyList<GameSettingField> Fields { get; } =
    [
        new(
            ExecutablePathFieldId,
            "game.rbr.installation.executable",
            GameSettingFieldKind.FilePath,
            "game.rbr.installation.tooltip.executable",
            RequiresRestart: true,
            FileExtension: "exe")
    ];

    public IReadOnlyList<string> InformationKeys { get; } = [];

    public IReadOnlyList<GameIntegrationAction> Actions { get; } = [];

    public TimeSpan StatusRefreshInterval => TimeSpan.Zero;

    public object? GetFieldValue(IGameConfig configuration, string fieldId)
    {
        return fieldId == ExecutablePathFieldId
            ? GetConfiguration(configuration).ExecutablePath
            : throw new ArgumentOutOfRangeException(nameof(fieldId), fieldId, null);
    }

    public void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game)
    {
        if (fieldId != ExecutablePathFieldId)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldId), fieldId, null);
        }

        var installConfig = GetConfiguration(configuration);
        installConfig.ExecutablePath = Convert.ToString(value)?.Trim() ?? string.Empty;
        if (game.GamePacenoteReader is RBRGamePacenoteReader reader)
        {
            reader.Configure(installConfig);
        }
    }

    public GameIntegrationStatus GetStatus(IGameConfig configuration, IGame game)
    {
        var installConfig = GetConfiguration(configuration);
        if (RBRGameInstallPathResolver.TryResolveRoot(installConfig, out var rootDirectory, out var source))
        {
            return source == RBRInstallationPathSource.ConfiguredExecutable
                ? new GameIntegrationStatus("game.rbr.installation.status.configured", [rootDirectory])
                : new GameIntegrationStatus("game.rbr.installation.status.registry", [rootDirectory]);
        }

        return new GameIntegrationStatus("game.rbr.installation.status.missing");
    }

    public Task<GameIntegrationActionResult> ExecuteActionAsync(
        string actionId,
        IGameConfig configuration,
        IGame game,
        CancellationToken cancellationToken)
    {
        throw new ArgumentOutOfRangeException(nameof(actionId), actionId, null);
    }

    private static RBRGameInstallationConfig GetConfiguration(IGameConfig configuration)
    {
        return configuration as RBRGameInstallationConfig
            ?? throw new ArgumentException("Unexpected RBR installation configuration type.", nameof(configuration));
    }
}
