#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.Game;

public enum GameSettingFieldKind
{
    Text,
    Number,
    Boolean,
    DirectoryPath,
    FilePath
}

public sealed record GameSettingConfirmation(
    string TitleKey,
    string MessageKey,
    string PrimaryButtonKey,
    bool WhenEnablingOnly = false);

public sealed record GameSettingField(
    string Id,
    string LabelKey,
    GameSettingFieldKind Kind,
    string? ToolTipKey = null,
    bool RequiresRestart = false,
    GameSettingConfirmation? Confirmation = null,
    double? Minimum = null,
    double? Maximum = null,
    string? FileExtension = null);

public sealed record GameIntegrationAction(
    string Id,
    string LabelKey,
    string? ToolTipKey = null,
    GameSettingConfirmation? Confirmation = null,
    string? SuccessTitleKey = null,
    string? FailureTitleKey = null,
    bool ShowSuccessMessage = false);

public sealed record GameIntegrationStatus(
    string MessageKey,
    IReadOnlyList<object>? MessageArguments = null,
    IReadOnlySet<string>? EnabledActionIds = null);

public sealed record GameIntegrationActionResult(
    bool Succeeded,
    string Message,
    bool MessageIsResourceKey = false,
    IReadOnlyList<object>? MessageArguments = null);

/// <summary>
/// Describes a game's configuration fields and setup commands without depending on a UI framework.
/// </summary>
public interface IGameSettingsProvider
{
    Type ConfigurationType { get; }

    IReadOnlyList<GameSettingField> GetFields(IGameConfig configuration);

    object? GetFieldValue(IGameConfig configuration, string fieldId);

    void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game);
}

public interface IGameIntegrationProvider : IGameSettingsProvider
{
    IReadOnlyList<GameSettingField> Fields { get; }

    IReadOnlyList<GameSettingField> IGameSettingsProvider.GetFields(IGameConfig configuration) => Fields;

    IReadOnlyList<string> InformationKeys { get; }

    IReadOnlyList<GameIntegrationAction> Actions { get; }

    TimeSpan StatusRefreshInterval { get; }

    GameIntegrationStatus GetStatus(IGameConfig configuration, IGame game);

    Task<GameIntegrationActionResult> ExecuteActionAsync(
        string actionId,
        IGameConfig configuration,
        IGame game,
        CancellationToken cancellationToken);
}

/// <summary>Optional capability for games with data-driven setup or bridge controls.</summary>
public interface IGameWithIntegrationProviders
{
    IReadOnlyList<IGameIntegrationProvider> IntegrationProviders { get; }
}
