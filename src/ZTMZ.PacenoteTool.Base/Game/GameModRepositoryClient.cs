#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.Game;

public sealed record GameModCatalogLoadResult(
    IReadOnlyList<GameModDefinition> Mods,
    bool IsCached,
    string? Error = null);

public sealed class GameModRepositoryClient
{
    private const int MaximumCatalogBytes = 1024 * 1024;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _cacheRoot;

    public GameModRepositoryClient(string? cacheRoot = null)
    {
        _cacheRoot = cacheRoot ?? AppLevelVariables.Instance.GetPath(Path.Combine("cache", "game-mods"));
    }

    public async Task<GameModCatalogLoadResult> LoadAsync(
        Uri repositoryUri,
        string gameId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoryUri);
        ValidateIdentifier(gameId, nameof(gameId));
        var cachePath = Path.Combine(_cacheRoot, gameId + ".json");

        try
        {
            if (!repositoryUri.IsAbsoluteUri || repositoryUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("The Mod repository must use HTTPS.");

            using var response = await HttpClient.GetAsync(
                repositoryUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumCatalogBytes)
                throw new InvalidDataException("The Mod repository is too large.");

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            await responseStream.CopyToAsync(memory, cancellationToken);
            if (memory.Length > MaximumCatalogBytes)
                throw new InvalidDataException("The Mod repository is too large.");

            var json = memory.ToArray();
            var mods = Parse(json, gameId);
            try
            {
                Directory.CreateDirectory(_cacheRoot);
                var temporaryCachePath = cachePath + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporaryCachePath, json, cancellationToken);
                    File.Move(temporaryCachePath, cachePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryCachePath))
                        File.Delete(temporaryCachePath);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A valid online catalog remains usable even if the offline cache cannot be updated.
            }
            return new GameModCatalogLoadResult(mods, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                if (File.Exists(cachePath))
                {
                    return new GameModCatalogLoadResult(
                        Parse(await File.ReadAllBytesAsync(cachePath, cancellationToken), gameId),
                        true,
                        exception.Message);
                }
            }
            catch (Exception cacheException) when (cacheException is not OperationCanceledException)
            {
                return new GameModCatalogLoadResult([], false, $"{exception.Message} {cacheException.Message}");
            }
            return new GameModCatalogLoadResult([], false, exception.Message);
        }
    }

    public static IReadOnlyList<GameModDefinition> Parse(byte[] json, string gameId)
    {
        var repository = JsonSerializer.Deserialize<GameModRepositoryDto>(json, JsonOptions)
                         ?? throw new InvalidDataException("The Mod repository is empty.");
        if (repository.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported Mod repository schema: {repository.SchemaVersion}.");

        var game = repository.Games.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, gameId, StringComparison.OrdinalIgnoreCase));
        if (game is null)
            return [];

        var duplicate = game.Mods.GroupBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Duplicate Mod id: {duplicate.Key}.");

        return game.Mods.Select(ToDefinition).ToList();
    }

    private static GameModDefinition ToDefinition(GameModDto dto)
    {
        ValidateIdentifier(dto.Id, "Mod id");
        if (dto.Names.Count == 0 ||
            !Regex.IsMatch(dto.Version, "^[0-9A-Za-z][0-9A-Za-z._+-]{0,63}$"))
            throw new InvalidDataException($"Mod '{dto.Id}' has incomplete name or version metadata.");
        if (!Uri.TryCreate(dto.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps ||
            !Uri.TryCreate(dto.ReleasePageUrl, UriKind.Absolute, out var releaseUri) ||
            releaseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"Mod '{dto.Id}' has an invalid HTTPS URL.");
        if (!Regex.IsMatch(dto.Sha256, "^[0-9a-fA-F]{64}$"))
            throw new InvalidDataException($"Mod '{dto.Id}' has an invalid SHA256 value.");

        ValidateArchiveRelativePath(dto.PackageRootDirectory, true);
        if (!string.IsNullOrWhiteSpace(dto.ExternalFolderName))
            ValidateArchiveRelativePath(dto.ExternalFolderName, false);
        foreach (var path in dto.PreserveExistingPaths)
            ValidateArchiveRelativePath(path, false);
        foreach (var credit in dto.Credits)
        {
            if (credit.Roles.Count == 0 ||
                credit.Roles.Values.Any(string.IsNullOrWhiteSpace) ||
                credit.Names.Count == 0 ||
                credit.Names.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Mod '{dto.Id}' has invalid credit metadata.");
        }

        return new GameModDefinition(
            dto.Id,
            new Dictionary<string, string>(dto.Names, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(dto.Descriptions, StringComparer.OrdinalIgnoreCase),
            dto.Author,
            dto.Version,
            downloadUri,
            releaseUri,
            dto.Sha256.ToUpperInvariant(),
            dto.PackageRootDirectory,
            dto.ExternalFolderName,
            dto.ExternalFolderPrefix,
            dto.ConflictGroup,
            dto.ArchiveEntryCodePage,
            dto.PreserveExistingPaths,
            dto.Credits.Select(credit => new GameModCredit(
                new Dictionary<string, string>(credit.Roles, StringComparer.OrdinalIgnoreCase),
                credit.Names.ToList())).ToList());
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, "^[a-z0-9][a-z0-9._-]*$"))
            throw new InvalidDataException($"Invalid {parameterName}: '{value}'.");
    }

    private static void ValidateArchiveRelativePath(string value, bool allowEmpty)
    {
        var normalized = value.Replace('\\', '/').Trim('/');
        if ((allowEmpty && normalized.Length == 0) ||
            (!Path.IsPathRooted(value) && !value.Contains(':') &&
             normalized.Split('/').All(part => part is not "" and not "." and not "..")))
            return;
        throw new InvalidDataException($"Unsafe Mod package path: '{value}'.");
    }

    private sealed class GameModRepositoryDto
    {
        public int SchemaVersion { get; set; }
        public List<GameDto> Games { get; set; } = [];
    }

    private sealed class GameDto
    {
        public string Id { get; set; } = string.Empty;
        public List<GameModDto> Mods { get; set; } = [];
    }

    private sealed class GameModDto
    {
        public string Id { get; set; } = string.Empty;
        public Dictionary<string, string> Names { get; set; } = [];
        public Dictionary<string, string> Descriptions { get; set; } = [];
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleasePageUrl { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string PackageRootDirectory { get; set; } = string.Empty;
        public string? ExternalFolderName { get; set; }
        public string? ExternalFolderPrefix { get; set; }
        public string? ConflictGroup { get; set; }
        public int? ArchiveEntryCodePage { get; set; }
        public List<string> PreserveExistingPaths { get; set; } = [];
        public List<GameModCreditDto> Credits { get; set; } = [];
    }

    private sealed class GameModCreditDto
    {
        public Dictionary<string, string> Roles { get; set; } = [];
        public List<string> Names { get; set; } = [];
    }
}
