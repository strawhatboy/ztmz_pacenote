#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool.Base;

public sealed class PacenoteIconCatalog
{
    public const int CurrentVersion = 1;
    public const int CurrentIconSize = 128;
    public const string WebRequestPath = "/pacenote-icons/v1";

    private static readonly string[] AssetPathParts =
    {
        "Assets",
        "PacenoteIcons",
        "v1"
    };

    private readonly IReadOnlyDictionary<string, PacenoteIconManifestEntry> _icons;
    private readonly IReadOnlyDictionary<string, string> _aliases;

    private PacenoteIconCatalog(
        string assetRoot,
        PacenoteIconManifest manifest)
    {
        AssetRoot = assetRoot;
        Version = manifest.Version;
        IconSize = manifest.IconSize;
        _icons = new Dictionary<string, PacenoteIconManifestEntry>(
            manifest.Icons,
            StringComparer.OrdinalIgnoreCase);
        _aliases = new Dictionary<string, string>(
            manifest.Aliases,
            StringComparer.OrdinalIgnoreCase);
    }

    public string AssetRoot { get; }
    public int Version { get; }
    public int IconSize { get; }
    public int IconCount => _icons.Count;
    public int AliasCount => _aliases.Count;

    public static string GetAssetRoot(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        return Path.Combine(baseDirectory, AssetPathParts[0], AssetPathParts[1], AssetPathParts[2]);
    }

    public static PacenoteIconCatalog Load(string? baseDirectory = null)
    {
        return LoadFromAssetRoot(GetAssetRoot(baseDirectory ?? AppContext.BaseDirectory));
    }

    public static PacenoteIconCatalog LoadFromAssetRoot(string assetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        var normalizedRoot = Path.GetFullPath(assetRoot);
        var manifestPath = Path.Combine(normalizedRoot, "manifest.json");
        var manifest = JsonConvert.DeserializeObject<PacenoteIconManifest>(
            File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Pacenote icon manifest is empty.");

        if (manifest.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported pacenote icon manifest version {manifest.Version}.");
        }

        if (manifest.IconSize != CurrentIconSize)
        {
            throw new InvalidDataException(
                $"Pacenote icon manifest declares {manifest.IconSize}px icons; expected {CurrentIconSize}px.");
        }

        foreach (var (alias, canonical) in manifest.Aliases)
        {
            if (!manifest.Icons.ContainsKey(canonical))
            {
                throw new InvalidDataException(
                    $"Pacenote icon alias {alias} references missing canonical token {canonical}.");
            }
        }

        foreach (var (canonical, entry) in manifest.Icons)
        {
            var expectedPath = $"icons/{canonical}.png";
            if (!string.Equals(entry.File, expectedPath, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Pacenote icon {canonical} has invalid file path {entry.File}.");
            }
        }

        return new PacenoteIconCatalog(normalizedRoot, manifest);
    }

    public bool TryResolve(string? token, out PacenoteIconReference? icon)
    {
        icon = null;
        var normalizedToken = NormalizeToken(token);
        if (normalizedToken.Length == 0
            || !_aliases.TryGetValue(normalizedToken, out var canonical)
            || !_icons.TryGetValue(canonical, out var entry))
        {
            return false;
        }

        var relativePath = entry.File.Replace('/', Path.DirectorySeparatorChar);
        icon = new PacenoteIconReference(
            canonical,
            Path.Combine(AssetRoot, relativePath),
            $"{WebRequestPath}/{entry.File.Replace('\\', '/')}");
        return true;
    }

    private static string NormalizeToken(string? token)
    {
        var normalized = token?.Trim() ?? string.Empty;
        if (normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }

    private sealed class PacenoteIconManifest
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("iconSize")]
        public int IconSize { get; set; }

        [JsonProperty("icons")]
        public Dictionary<string, PacenoteIconManifestEntry> Icons { get; set; } = new();

        [JsonProperty("aliases")]
        public Dictionary<string, string> Aliases { get; set; } = new();
    }

    private sealed class PacenoteIconManifestEntry
    {
        [JsonProperty("file")]
        public string File { get; set; } = string.Empty;
    }
}

public sealed record PacenoteIconReference(
    string CanonicalToken,
    string FilePath,
    string WebPath);
