#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Core;

public sealed class CoDriverPackageUpdateDefinition
{
    public string id { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    public string gender { get; set; } = string.Empty;
    public string language { get; set; } = string.Empty;
    public string homepage { get; set; } = string.Empty;
    public string version { get; set; } = string.Empty;
    public string url { get; set; } = string.Empty;
    public bool urlRedirected { get; set; }
    public string changelog { get; set; } = string.Empty;
    public float integrity_simple { get; set; }
    public float integrity_normal { get; set; }
    public float integrity_complex { get; set; }
}

public sealed record CoDriverPackageUpdateStatus(
    CoDriverPackageUpdateDefinition Definition,
    bool NeedsUpdate,
    bool NeedsDownload,
    string ExistingPackagePath,
    string OriginalVersion);

public enum CoDriverPackageUpdatePhase
{
    Downloading,
    Installing
}

public sealed record CoDriverPackageUpdateProgress(
    CoDriverPackageUpdatePhase Phase,
    FileDownloadProgress? Download = null);

public sealed class CoDriverPackageUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TemporaryFileDownloadService _downloads;
    private readonly CoDriverPackageCatalogService _packages;
    public CoDriverUpdateNotifications Notifications { get; } = new();

    public CoDriverPackageUpdateService(
        HttpClient httpClient,
        TemporaryFileDownloadService downloads,
        CoDriverPackageCatalogService packages)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
    }

    public async Task<IReadOnlyList<CoDriverPackageUpdateStatus>> CheckForUpdatesAsync(
        string catalogUrl,
        IReadOnlyCollection<CoDriverPackageInfo> installedPackages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogUrl);
        ArgumentNullException.ThrowIfNull(installedPackages);
        var revision = Notifications.Revision;

        using var response = await _httpClient.GetAsync(
            catalogUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var definitions = await JsonSerializer.DeserializeAsync<List<CoDriverPackageUpdateDefinition>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];

        var statuses = definitions.Select(definition =>
        {
            var installed = installedPackages.FirstOrDefault(package =>
                string.Equals(package.id, definition.id, StringComparison.Ordinal));
            if (installed == null)
            {
                return new CoDriverPackageUpdateStatus(
                    definition,
                    NeedsUpdate: false,
                    NeedsDownload: true,
                    ExistingPackagePath: string.Empty,
                    OriginalVersion: string.Empty);
            }

            var needsUpdate = ParseVersion(installed.version, "installed") <
                              ParseVersion(definition.version, "catalog");
            return new CoDriverPackageUpdateStatus(
                definition,
                needsUpdate,
                NeedsDownload: false,
                installed.Path ?? string.Empty,
                installed.version ?? string.Empty);
        }).ToList();
        Notifications.Replace(statuses, revision);
        return statuses;
    }

    public async Task<CoDriverPackage> DownloadAndInstallAsync(
        string url,
        string? existingPackagePath,
        IProgress<CoDriverPackageUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<FileDownloadProgress>? downloadProgress = progress == null
            ? null
            : new ForwardingProgress<FileDownloadProgress>(value => progress.Report(
                new CoDriverPackageUpdateProgress(
                    CoDriverPackageUpdatePhase.Downloading,
                    value)));
        var archivePath = await _downloads.DownloadAsync(
            url,
            "ztmz-codriver",
            ".zpak",
            downloadProgress,
            cancellationToken);
        try
        {
            progress?.Report(new CoDriverPackageUpdateProgress(
                CoDriverPackageUpdatePhase.Installing));
            var package = await _packages.ImportAsync(archivePath, existingPackagePath);
            Notifications.Installed(package.Info);
            return package;
        }
        finally
        {
            TemporaryFileDownloadService.TryDelete(archivePath);
        }
    }

    private static Version ParseVersion(string? value, string source)
    {
        if (!Version.TryParse(value, out var version))
        {
            throw new InvalidDataException($"The {source} co-driver package version '{value}' is invalid.");
        }

        return version;
    }

    private sealed class ForwardingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
