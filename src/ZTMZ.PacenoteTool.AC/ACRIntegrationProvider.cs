using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

public sealed class ACRIntegrationProvider : IGameIntegrationProvider
{
    public const string ControlsEnabledFieldId = "controlsEnabled";
    public const string InjectBridgeActionId = "injectBridge";

    private static readonly IReadOnlySet<string> NoActionsEnabled =
        new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> InjectActionEnabled =
        new HashSet<string>(StringComparer.Ordinal) { InjectBridgeActionId };

    public Type ConfigurationType => typeof(ACRExperimentalBridgeConfig);

    public IReadOnlyList<GameSettingField> Fields { get; } =
    [
        new(
            ControlsEnabledFieldId,
            "game.acr.experimental.enable",
            GameSettingFieldKind.Boolean,
            Confirmation: new GameSettingConfirmation(
                "game.acr.experimental.warningTitle",
                "game.acr.experimental.enableWarning",
                "game.acr.experimental.confirm",
                WhenEnablingOnly: true))
    ];

    public IReadOnlyList<string> InformationKeys { get; } =
    [
        "game.acr.experimental.privilegeWarning"
    ];

    public IReadOnlyList<GameIntegrationAction> Actions { get; } =
    [
        new(
            InjectBridgeActionId,
            "game.acr.experimental.inject",
            Confirmation: new GameSettingConfirmation(
                "game.acr.experimental.warningTitle",
                "game.acr.experimental.injectWarning",
                "game.acr.experimental.inject"),
            SuccessTitleKey: "game.acr.experimental.warningTitle",
            FailureTitleKey: "game.acr.experimental.warningTitle")
    ];

    public TimeSpan StatusRefreshInterval => TimeSpan.FromSeconds(1);

    public object? GetFieldValue(IGameConfig configuration, string fieldId)
    {
        var config = GetConfiguration(configuration);
        return fieldId switch
        {
            ControlsEnabledFieldId => config.ControlsEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(fieldId), fieldId, null)
        };
    }

    public void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game)
    {
        if (!string.Equals(fieldId, ControlsEnabledFieldId, StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(fieldId), fieldId, null);
        }

        var config = GetConfiguration(configuration);
        config.ControlsEnabled = Convert.ToBoolean(value);
        if (config.ControlsEnabled)
        {
            GameRuntimeNotifications.NotifyLocalBridgeControlsEnabled(game);
        }
    }

    public GameIntegrationStatus GetStatus(IGameConfig configuration, IGame game)
    {
        if (!GetConfiguration(configuration).ControlsEnabled)
        {
            return new GameIntegrationStatus(
                "game.acr.experimental.disabled",
                EnabledActionIds: NoActionsEnabled);
        }

        var status = ACRExperimentalBridgeController.Inspect();
        var key = status.Kind switch
        {
            ACRExperimentalBridgeStatusKind.BundleUnavailable => "game.acr.experimental.statusBundleUnavailable",
            ACRExperimentalBridgeStatusKind.GameNotRunning => "game.acr.experimental.statusGameNotRunning",
            ACRExperimentalBridgeStatusKind.AlreadyInjected => "game.acr.experimental.statusInjected",
            _ => "game.acr.experimental.statusReady"
        };
        return new GameIntegrationStatus(
            key,
            EnabledActionIds: status.BundleAvailable && status.GameRunning && !status.AlreadyInjected
                ? InjectActionEnabled
                : NoActionsEnabled);
    }

    public async Task<GameIntegrationActionResult> ExecuteActionAsync(
        string actionId,
        IGameConfig configuration,
        IGame game,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(actionId, InjectBridgeActionId, StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(actionId), actionId, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!GetConfiguration(configuration).ControlsEnabled)
        {
            return new GameIntegrationActionResult(
                false,
                "game.acr.experimental.disabled",
                MessageIsResourceKey: true);
        }

        var result = await ACRExperimentalBridgeController.InjectAsync();
        if (result.Succeeded)
        {
            GameRuntimeNotifications.NotifyLocalBridgeInjected(game);
        }
        return new GameIntegrationActionResult(result.Succeeded, result.Message);
    }

    private static ACRExperimentalBridgeConfig GetConfiguration(IGameConfig configuration)
    {
        return configuration as ACRExperimentalBridgeConfig
            ?? throw new ArgumentException("Unexpected ACR bridge configuration type.", nameof(configuration));
    }
}
