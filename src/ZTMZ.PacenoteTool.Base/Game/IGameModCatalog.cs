#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.Game;

public enum GameModInstallState
{
    Unavailable, NotInstalled, Installed, UpdateAvailable,
    ExternalManagerReady, ExternalManagerEnabled, Conflict
}

public enum GameModActionKind
{
    Install, Update, Uninstall, AddToExternalManager,
    OpenExternalManager, OpenReleasePage
}

public sealed record GameModCredit(
    IReadOnlyDictionary<string, string> Roles,
    IReadOnlyList<string> Names)
{
    public string GetRole(string cultureName) =>
        GameModDefinition.GetLocalizedText(Roles, cultureName, string.Empty);
}

public sealed record GameModDefinition(
    string Id,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, string> Descriptions,
    string Author,
    string Version,
    Uri DownloadUri,
    Uri ReleasePageUri,
    string Sha256,
    string PackageRootDirectory,
    string? ExternalFolderName = null,
    string? ExternalFolderPrefix = null,
    string? ConflictGroup = null,
    int? ArchiveEntryCodePage = null,
    IReadOnlyList<string>? PreserveExistingPaths = null,
    IReadOnlyList<GameModCredit>? Credits = null)
{
    public string GetName(string cultureName) => GetLocalizedText(Names, cultureName, Id);

    public string GetDescription(string cultureName) =>
        GetLocalizedText(Descriptions, cultureName, string.Empty);

    internal static string GetLocalizedText(
        IReadOnlyDictionary<string, string> values,
        string cultureName,
        string fallback)
    {
        if (values.TryGetValue(cultureName, out var exact))
            return exact;

        var language = cultureName.Split('-')[0];
        var languageMatch = values.FirstOrDefault(item =>
            item.Key.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(languageMatch.Key))
            return languageMatch.Value;

        return values.TryGetValue("en-us", out var english)
            ? english
            : values.Values.FirstOrDefault() ?? fallback;
    }
}

public sealed record GameModStatus(
    GameModInstallState State,
    string MessageKey,
    IReadOnlyList<object>? MessageArguments = null,
    IReadOnlySet<GameModActionKind>? EnabledActions = null);

public sealed record GameModOperationProgress(
    double? Fraction,
    string MessageKey,
    IReadOnlyList<object>? MessageArguments = null);

public sealed record GameModOperationResult(
    bool Succeeded,
    string Message,
    bool MessageIsResourceKey = true,
    IReadOnlyList<object>? MessageArguments = null);

/// <summary>Runs game-specific Mod operations without depending on a desktop UI framework.</summary>
public interface IGameModManager
{
    Task<GameModStatus> GetStatusAsync(
        GameModDefinition mod,
        IReadOnlyList<GameModDefinition> catalog,
        IGame game,
        CancellationToken cancellationToken);

    Task<GameModOperationResult> ExecuteActionAsync(
        GameModDefinition mod,
        IReadOnlyList<GameModDefinition> catalog,
        GameModActionKind action,
        IGame game,
        IProgress<GameModOperationProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Optional capability implemented by a game plugin that can manage catalog Mods.</summary>
public interface IGameWithModCatalog
{
    string ModCatalogGameId { get; }
    Uri ModRepositoryUri { get; }
    IGameModManager ModManager { get; }
}
