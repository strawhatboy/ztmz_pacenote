#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Core;

public class ApplicationUpdateDefinition
{
    public string version { get; set; } = string.Empty;

    public string url { get; set; } = string.Empty;

    public bool urlRedirected { get; set; }

    public string changelog { get; set; } = string.Empty;

    public Dictionary<string, string>? changelogs { get; set; }

    public string minVersionSupported { get; set; } = string.Empty;

    public string ResolveChangelogUrl(string? cultureName)
    {
        if (changelogs == null || changelogs.Count == 0 ||
            string.IsNullOrWhiteSpace(cultureName))
        {
            return changelog;
        }

        var requestedCulture = cultureName.Trim().Replace('_', '-');
        var exact = changelogs.FirstOrDefault(entry =>
            string.Equals(entry.Key, requestedCulture, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exact.Value))
        {
            return exact.Value;
        }

        var language = GetLanguageCode(requestedCulture);
        var match = changelogs.FirstOrDefault(entry =>
            string.Equals(
                GetLanguageCode(entry.Key),
                language,
                StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match.Value) ? changelog : match.Value;
    }

    private static string GetLanguageCode(string cultureName)
    {
        var separator = cultureName.IndexOf('-');
        return separator < 0 ? cultureName : cultureName[..separator];
    }
}

public sealed record ApplicationUpdateQueryOptions(
    string StableDefinitionUrl,
    string BetaDefinitionUrl,
    Version CurrentVersion,
    Version? SkippedVersion,
    bool IncludeBetaChannel);

public sealed class ApplicationUpdateQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public ApplicationUpdateQueryService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ApplicationUpdateDefinition?> FindUpdateAsync(
        ApplicationUpdateQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var stable = await CheckDefinitionAsync(
            options.StableDefinitionUrl,
            options,
            cancellationToken);
        if (stable != null || !options.IncludeBetaChannel)
        {
            return stable;
        }

        return await CheckDefinitionAsync(
            options.BetaDefinitionUrl,
            options,
            cancellationToken);
    }

    private async Task<ApplicationUpdateDefinition?> CheckDefinitionAsync(
        string url,
        ApplicationUpdateQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        await using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        var definition = await JsonSerializer.DeserializeAsync<ApplicationUpdateDefinition>(
            stream,
            JsonOptions,
            cancellationToken);
        if (definition == null ||
            !Version.TryParse(definition.version, out var availableVersion) ||
            !Version.TryParse(definition.minVersionSupported, out var minimumVersion))
        {
            return null;
        }

        if (options.SkippedVersion != null && availableVersion == options.SkippedVersion)
        {
            return null;
        }

        return options.CurrentVersion >= minimumVersion &&
               options.CurrentVersion < availableVersion
            ? definition
            : null;
    }
}
