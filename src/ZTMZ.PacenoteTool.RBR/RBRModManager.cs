#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

internal sealed class RBRModManager : IGameModManager
{
    private const long MaximumPackageBytes = 1024L * 1024 * 1024;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    public Task<GameModStatus> GetStatusAsync(
        GameModDefinition mod,
        IReadOnlyList<GameModDefinition> catalog,
        IGame game,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return GetStatus(mod, catalog, game);
            }
            catch (Exception exception)
            {
                return new GameModStatus(
                    GameModInstallState.Conflict,
                    "gameMods.status.error",
                    [exception.Message],
                    Actions(GameModActionKind.OpenReleasePage));
            }
        }, cancellationToken);
    }

    public async Task<GameModOperationResult> ExecuteActionAsync(
        GameModDefinition mod,
        IReadOnlyList<GameModDefinition> catalog,
        GameModActionKind action,
        IGame game,
        IProgress<GameModOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (action == GameModActionKind.OpenReleasePage)
                return new GameModOperationResult(false, "gameMods.result.openedByUi");

            if (!RBRGameInstallPathResolver.TryResolveRoot(game, out var rbrRoot, out _))
                return new GameModOperationResult(false, "gameMods.status.gamePathMissing");

            var status = GetStatus(mod, catalog, game);
            if (status.EnabledActions?.Contains(action) != true)
                return new GameModOperationResult(false, "gameMods.result.actionUnavailable");

            if (action == GameModActionKind.OpenExternalManager)
            {
                if (!RBRJsgmeRepositoryLocator.TryLocate(rbrRoot, out var repository))
                    return new GameModOperationResult(false, "gameMods.status.jsgmeMissing");
                Process.Start(new ProcessStartInfo(repository.ExecutablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = rbrRoot
                });
                return new GameModOperationResult(true, "gameMods.result.externalManagerOpened");
            }

            if (action == GameModActionKind.Uninstall)
            {
                progress?.Report(new GameModOperationProgress(null, "gameMods.progress.uninstalling"));
                var retained = RBRModPackageInstaller.UninstallManaged(rbrRoot, mod);
                return retained == 0
                    ? new GameModOperationResult(true, "gameMods.result.uninstalled")
                    : new GameModOperationResult(false, "gameMods.result.modifiedFilesRetained", true, [retained]);
            }

            var operationDirectory = Path.Combine(
                Path.GetTempPath(), $"ztmz-rbr-mod-{Guid.NewGuid():N}");
            Directory.CreateDirectory(operationDirectory);
            try
            {
                var archivePath = Path.Combine(operationDirectory, "package.zip");
                await DownloadAsync(mod.DownloadUri, archivePath, progress, cancellationToken);
                progress?.Report(new GameModOperationProgress(null, "gameMods.progress.verifying"));
                var packageRoot = RBRModPackageInstaller.ExtractValidatedPackage(
                    archivePath, mod, operationDirectory);
                cancellationToken.ThrowIfCancellationRequested();

                if (action == GameModActionKind.AddToExternalManager)
                {
                    if (!RBRJsgmeRepositoryLocator.TryLocate(rbrRoot, out var repository))
                        return new GameModOperationResult(false, "gameMods.status.jsgmeMissing");
                    progress?.Report(new GameModOperationProgress(null, "gameMods.progress.addingToJsgme"));
                    RBRModPackageInstaller.AddToJsgme(packageRoot, repository, mod);
                    return new GameModOperationResult(true, "gameMods.result.addedToJsgme");
                }

                progress?.Report(new GameModOperationProgress(null, "gameMods.progress.installing"));
                RBRModPackageInstaller.InstallManaged(packageRoot, rbrRoot, mod);
                return new GameModOperationResult(true,
                    action == GameModActionKind.Update
                        ? "gameMods.result.updated"
                        : "gameMods.result.installed");
            }
            finally
            {
                TryDeleteDirectory(operationDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            return new GameModOperationResult(false, "gameMods.result.cancelled");
        }
        catch (Exception exception)
        {
            return new GameModOperationResult(false, exception.Message, MessageIsResourceKey: false);
        }
    }

    private static GameModStatus GetStatus(
        GameModDefinition mod,
        IReadOnlyList<GameModDefinition> catalog,
        IGame game)
    {
        if (!RBRGameInstallPathResolver.TryResolveRoot(game, out var rbrRoot, out _))
            return Status(GameModInstallState.Unavailable, "gameMods.status.gamePathMissing",
                GameModActionKind.OpenReleasePage);

        var running = IsRbrRunning(game);
        var managed = RBRModPackageInstaller.InspectManagedInstallation(rbrRoot, mod);
        var hasJsgme = RBRJsgmeRepositoryLocator.TryLocate(rbrRoot, out var repository);
        var exactExternalName = GetExternalFolderName(mod);
        var externalReady = hasJsgme && exactExternalName is not null &&
                            Directory.Exists(Path.Combine(repository!.ModsDirectory, exactExternalName));
        var externalEnabled = hasJsgme && repository!.EnabledMods.Any(name => MatchesExternalFolder(mod, name));

        var conflictingMod = catalog.FirstOrDefault(other =>
            !string.Equals(other.Id, mod.Id, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(mod.ConflictGroup) &&
            string.Equals(other.ConflictGroup, mod.ConflictGroup, StringComparison.OrdinalIgnoreCase) &&
            (RBRModPackageInstaller.HasManagedInstallation(rbrRoot, other) ||
             (hasJsgme && repository!.EnabledMods.Any(name => MatchesExternalFolder(other, name)))));

        if ((managed.Exists && externalEnabled) || conflictingMod is not null)
            return Status(GameModInstallState.Conflict, "gameMods.status.conflict",
                running ? [GameModActionKind.OpenReleasePage] : ExternalActions(hasJsgme));

        if (managed.Exists)
        {
            if (!managed.IsIntact)
                return Status(GameModInstallState.Conflict, "gameMods.status.modified",
                    running ? [GameModActionKind.OpenReleasePage] : ManagedActions(hasJsgme, false));
            var update = managed.Version is null ||
                         CompareVersions(managed.Version, mod.Version) < 0 ||
                         !string.Equals(managed.PackageSha256, mod.Sha256, StringComparison.OrdinalIgnoreCase);
            return Status(update ? GameModInstallState.UpdateAvailable : GameModInstallState.Installed,
                update ? "gameMods.status.updateAvailable" : "gameMods.status.installed",
                running ? [GameModActionKind.OpenReleasePage] : ManagedActions(hasJsgme, update));
        }

        if (externalEnabled)
            return Status(GameModInstallState.ExternalManagerEnabled, "gameMods.status.jsgmeEnabled",
                running ? [GameModActionKind.OpenReleasePage] : ExternalActions(hasJsgme));
        if (externalReady)
            return Status(GameModInstallState.ExternalManagerReady, "gameMods.status.jsgmeReady",
                running ? [GameModActionKind.OpenReleasePage] : ExternalActions(hasJsgme));

        var actions = new HashSet<GameModActionKind>
        {
            GameModActionKind.Install,
            GameModActionKind.OpenReleasePage
        };
        if (hasJsgme)
        {
            actions.Add(GameModActionKind.AddToExternalManager);
            actions.Add(GameModActionKind.OpenExternalManager);
        }
        if (running)
            actions.RemoveWhere(action => action != GameModActionKind.OpenReleasePage);
        return new GameModStatus(GameModInstallState.NotInstalled, "gameMods.status.notInstalled", [], actions);
    }

    private static async Task DownloadAsync(
        Uri uri,
        string destination,
        IProgress<GameModOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new GameModOperationProgress(0, "gameMods.progress.downloading"));
        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        if (total > MaximumPackageBytes)
            throw new InvalidDataException("The Mod package is larger than the supported 1 GiB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (received > MaximumPackageBytes)
                throw new InvalidDataException("The Mod package is larger than the supported 1 GiB limit.");
            progress?.Report(new GameModOperationProgress(
                total > 0 ? (double)received / total.Value : null,
                "gameMods.progress.downloading"));
        }
    }

    private static bool IsRbrRunning(IGame game)
    {
        if (game.IsRunning)
            return true;
        try
        {
            return Process.GetProcessesByName("RichardBurnsRally_SSE").Length > 0 ||
                   Process.GetProcessesByName("RichardBurnsRally").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // Temporary cleanup must not turn a completed install into a reported failure.
        }
    }

    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(left, out var leftVersion) &&
            Version.TryParse(right, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);
        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static bool MatchesExternalFolder(GameModDefinition mod, string name) =>
        string.Equals(name, GetExternalFolderName(mod), StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(mod.ExternalFolderPrefix) &&
         name.StartsWith(mod.ExternalFolderPrefix, StringComparison.OrdinalIgnoreCase));

    private static string? GetExternalFolderName(GameModDefinition mod) =>
        !string.IsNullOrWhiteSpace(mod.ExternalFolderName)
            ? mod.ExternalFolderName
            : !string.IsNullOrWhiteSpace(mod.PackageRootDirectory) ? mod.PackageRootDirectory : null;

    private static HashSet<GameModActionKind> ManagedActions(bool hasJsgme, bool update)
    {
        var actions = Actions(GameModActionKind.Uninstall, GameModActionKind.OpenReleasePage);
        if (update)
            actions.Add(GameModActionKind.Update);
        if (hasJsgme)
            actions.Add(GameModActionKind.OpenExternalManager);
        return actions;
    }

    private static HashSet<GameModActionKind> ExternalActions(bool hasJsgme) =>
        hasJsgme
            ? Actions(GameModActionKind.OpenExternalManager, GameModActionKind.OpenReleasePage)
            : Actions(GameModActionKind.OpenReleasePage);

    private static GameModStatus Status(
        GameModInstallState state,
        string message,
        params GameModActionKind[] actions) => new(state, message, [], Actions(actions));

    private static GameModStatus Status(
        GameModInstallState state,
        string message,
        IReadOnlySet<GameModActionKind> actions) => new(state, message, [], actions);

    private static HashSet<GameModActionKind> Actions(params GameModActionKind[] actions) => [.. actions];
}
