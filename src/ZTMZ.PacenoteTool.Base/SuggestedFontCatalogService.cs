#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base;

public sealed record SuggestedFont(
    string Id,
    string Name,
    string Description,
    string FamilyName,
    string FileName,
    string Url,
    string Sha256,
    long Size);

public sealed class SuggestedFontCatalogService
{
    public const string SuggestedFontPrefix = "suggested:";
    private const string FontCatalogUrl = "https://gitee.com/ztmz/fonts/raw/master/repo.json";
    private const string FontCatalogCacheFileName = "repo.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new(
        StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<SuggestedFont> _fonts;

    public SuggestedFontCatalogService()
    {
        _fonts = LoadCachedFonts();
    }

    public IReadOnlyList<SuggestedFont> Fonts => _fonts;

    public SuggestedFont? GetConfiguredFont(string? configuredFontFamily)
    {
        var configured = configuredFontFamily ?? string.Empty;
        if (!configured.StartsWith(SuggestedFontPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = configured[SuggestedFontPrefix.Length..];
        return _fonts.FirstOrDefault(font => font.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsInstalled(SuggestedFont font) =>
        IsExpectedFontFile(GetFontPath(font), font.Sha256)
        || IsExpectedFontFile(GetHashFontPath(font), font.Sha256)
        || IsExpectedFontFile(GetLegacyFontPath(font), font.Sha256);

    public bool TryGetFontPath(SuggestedFont font, out string path)
    {
        path = GetFontPath(font);
        if (IsExpectedFontFile(path, font.Sha256))
        {
            return true;
        }

        var existingPath = new[] { GetHashFontPath(font), GetLegacyFontPath(font) }
            .FirstOrDefault(path => IsExpectedFontFile(path, font.Sha256));
        if (existingPath == null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(existingPath, path, true);
            return IsExpectedFontFile(path, font.Sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task DownloadAsync(
        SuggestedFont font,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destination = GetFontPath(font);
        var downloadLock = DownloadLocks.GetOrAdd(font.Sha256, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        string? tempPath = null;

        try
        {
            if (IsExpectedFontFile(destination, font.Sha256))
            {
                progress?.Report(1);
                return;
            }

            tempPath = GetTemporaryDownloadPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            progress?.Report(0);
            using var client = new HttpClient();
            using var response = await client.GetAsync(
                font.Url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                var totalBytesRead = 0L;
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalBytesRead += bytesRead;
                    progress?.Report(Math.Min(1d, totalBytesRead / (double)font.Size));
                }

                await target.FlushAsync(cancellationToken);
            }

            // The exclusive write handle must be closed before the hash verifier reopens the file.
            if (!IsHashMatch(tempPath, font.Sha256))
            {
                throw new InvalidDataException("Downloaded font did not match its expected SHA-256 hash.");
            }

            File.Move(tempPath, destination, true);
            progress?.Report(1);
        }
        finally
        {
            try
            {
                if (tempPath != null && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            finally
            {
                downloadLock.Release();
            }
        }
    }

    public async Task<IReadOnlyList<SuggestedFont>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();
        var json = await client.GetStringAsync(FontCatalogUrl, cancellationToken);
        var fonts = ParseFonts(json);
        SaveCache(json);
        _fonts = fonts;
        return fonts;
    }

    private IReadOnlyList<SuggestedFont> LoadCachedFonts()
    {
        var cachePath = Path.Combine(GetFontDirectory(), FontCatalogCacheFileName);
        if (!File.Exists(cachePath))
        {
            return [];
        }

        try
        {
            return ParseFonts(File.ReadAllText(cachePath));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SuggestedFont> ParseFonts(string json)
    {
        var fonts = JsonSerializer.Deserialize<List<SuggestedFont>>(json, SerializerOptions)
                    ?? throw new InvalidDataException("The font catalog is empty or invalid.");
        var validFonts = fonts
            .Where(IsValid)
            .GroupBy(font => font.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return validFonts.Length > 0
            ? validFonts
            : throw new InvalidDataException("The font catalog did not contain any valid fonts.");
    }

    private static bool IsValid(SuggestedFont font) =>
        !string.IsNullOrWhiteSpace(font.Id)
        && !string.IsNullOrWhiteSpace(font.Name)
        && !string.IsNullOrWhiteSpace(font.FamilyName)
        && Uri.TryCreate(font.Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && font.Sha256.Length == 64
        && font.Sha256.All(Uri.IsHexDigit)
        && font.Size > 0
        && font.FileName.Equals(Path.GetFileName(font.FileName), StringComparison.Ordinal)
        && (font.FileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
            || font.FileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

    private static void SaveCache(string json)
    {
        var directory = GetFontDirectory();
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, FontCatalogCacheFileName);
        var tempPath = GetTemporaryDownloadPath(targetPath);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, targetPath, true);
    }

    // Each download owns its temporary file so repeated clicks or another app instance cannot lock it.
    private static string GetTemporaryDownloadPath(string destination) =>
        destination + "." + Guid.NewGuid().ToString("N") + ".download";

    private static string GetFontPath(SuggestedFont font) => Path.Combine(
        GetFontDirectory(),
        "suggested",
        GetSafeFontId(font),
        GetSafeFileName(font));

    private static string GetHashFontPath(SuggestedFont font) => Path.Combine(
        GetFontDirectory(),
        "suggested",
        font.Sha256.ToUpperInvariant(),
        GetSafeFileName(font));

    private static string GetLegacyFontPath(SuggestedFont font) =>
        Path.Combine(GetFontDirectory(), GetSafeFileName(font));

    private static string GetSafeFontId(SuggestedFont font)
    {
        var id = font.Id;
        if (string.IsNullOrWhiteSpace(id)
            || !id.Equals(Path.GetFileName(id), StringComparison.Ordinal)
            || id.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidDataException("The font catalog contains an invalid font id.");
        }

        return id;
    }

    private static string GetSafeFileName(SuggestedFont font)
    {
        var fileName = Path.GetFileName(font.FileName);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.Equals(font.FileName, StringComparison.Ordinal)
            || (!fileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !fileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The font catalog contains an invalid file name.");
        }

        return fileName;
    }

    private static bool IsExpectedFontFile(string path, string expectedHash)
    {
        try
        {
            return File.Exists(path) && IsHashMatch(path, expectedHash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsHashMatch(string path, string expectedHash)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFontDirectory() =>
        Path.Combine(AppLevelVariables.Instance.AppConfigFolder, "fonts");
}
