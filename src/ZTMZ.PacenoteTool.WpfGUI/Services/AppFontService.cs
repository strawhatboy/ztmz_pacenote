using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

public sealed class AppFontService
{
    public const string SystemDefaultFontId = "system-default";
    private const string SuggestedFontPrefix = "suggested:";
    private const string FontCatalogUrl = "https://gitee.com/ztmz/fonts/raw/master/repo.json";
    private const string FontCatalogCacheFileName = "repo.json";
    private static readonly JsonSerializerOptions CatalogSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IReadOnlyList<SuggestedFont> _suggestedFonts;

    public AppFontService()
    {
        _suggestedFonts = LoadCachedSuggestedFonts();
    }

    public IReadOnlyList<SuggestedFont> SuggestedFonts => _suggestedFonts;

    public IReadOnlyList<FontChoice> GetSystemFontChoices()
    {
        var choices = new List<FontChoice>
        {
            new(SystemDefaultFontId, I18NLoader.Instance["font.systemDefault"], true)
        };

        choices.AddRange(Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new FontChoice(name, name, false)));

        return choices;
    }

    public FontChoice? GetConfiguredSystemFontChoice(IList<FontChoice> choices)
    {
        var configured = Config.Instance.UIFontFamily ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configured) || configured.StartsWith(SuggestedFontPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return configured.StartsWith(SuggestedFontPrefix, StringComparison.OrdinalIgnoreCase) ? null : choices[0];
        }

        return choices.FirstOrDefault(choice => choice.Id.Equals(configured, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
    }

    public SuggestedFont? GetConfiguredSuggestedFont()
    {
        var configured = Config.Instance.UIFontFamily ?? string.Empty;
        if (!configured.StartsWith(SuggestedFontPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = configured.Substring(SuggestedFontPrefix.Length);
        return SuggestedFonts.FirstOrDefault(font => font.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyConfiguredFont()
    {
        var suggestedFont = GetConfiguredSuggestedFont();
        if (suggestedFont != null && TryApplySuggestedFont(suggestedFont))
        {
            return;
        }

        var configured = Config.Instance.UIFontFamily ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configured) && !configured.StartsWith(SuggestedFontPrefix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ApplyFont(new FontFamily(configured));
                return;
            }
            catch (ArgumentException)
            {
                Config.Instance.UIFontFamily = string.Empty;
                Config.Instance.SaveUserConfig();
            }
        }

        ApplySystemDefaultFont();
    }

    public void ApplySystemDefaultFont()
    {
        ApplyFont(SystemFonts.MessageFontFamily);
    }

    public void ApplySystemFont(string familyName)
    {
        ApplyFont(new FontFamily(familyName));
    }

    public bool IsInstalled(SuggestedFont font)
    {
        var path = GetSuggestedFontPath(font);
        return File.Exists(path) && IsHashMatch(path, font.Sha256);
    }

    public bool TryApplySuggestedFont(SuggestedFont font)
    {
        var fontPath = GetSuggestedFontPath(font);
        if (!File.Exists(fontPath) || !IsHashMatch(fontPath, font.Sha256))
        {
            return false;
        }

        try
        {
            var fontDirectory = Path.GetDirectoryName(fontPath)! + Path.DirectorySeparatorChar;
            ApplyFont(new FontFamily(new Uri(fontDirectory), $"./#{font.FamilyName}"));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public async Task DownloadSuggestedFontAsync(
        SuggestedFont font,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destination = GetSuggestedFontPath(font);
        var tempPath = destination + ".download";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        try
        {
            progress?.Report(0);
            using var client = new HttpClient();
            using var response = await client.GetAsync(font.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
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
            }

            if (!IsHashMatch(tempPath, font.Sha256))
            {
                throw new InvalidDataException("Downloaded font did not match its expected SHA-256 hash.");
            }

            File.Move(tempPath, destination, true);
            progress?.Report(1);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task<IReadOnlyList<SuggestedFont>> RefreshSuggestedFontsAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();
        var catalogJson = await client.GetStringAsync(FontCatalogUrl, cancellationToken);
        var fonts = ParseSuggestedFonts(catalogJson);
        SaveSuggestedFontCache(catalogJson);
        _suggestedFonts = fonts;
        return SuggestedFonts;
    }

    private static bool IsHashMatch(string path, string expectedHash)
    {
        using var stream = File.OpenRead(path);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSuggestedFontPath(SuggestedFont font)
    {
        var safeFileName = Path.GetFileName(font.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName)
            || !safeFileName.Equals(font.FileName, StringComparison.Ordinal)
            || (!safeFileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !safeFileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The font catalog contains an invalid font file name.");
        }

        return Path.Combine(GetFontDirectory(), safeFileName);
    }

    private static void ApplyFont(FontFamily fontFamily)
    {
        Application.Current.Resources["AppFontFamily"] = fontFamily;
    }

    private static IReadOnlyList<SuggestedFont> LoadCachedSuggestedFonts()
    {
        var cachePath = Path.Combine(GetFontDirectory(), FontCatalogCacheFileName);
        if (!File.Exists(cachePath))
        {
            return Array.Empty<SuggestedFont>();
        }

        try
        {
            return ParseSuggestedFonts(File.ReadAllText(cachePath));
        }
        catch (IOException)
        {
            return Array.Empty<SuggestedFont>();
        }
        catch (JsonException)
        {
            return Array.Empty<SuggestedFont>();
        }
        catch (InvalidDataException)
        {
            return Array.Empty<SuggestedFont>();
        }
    }

    private static IReadOnlyList<SuggestedFont> ParseSuggestedFonts(string catalogJson)
    {
        var fonts = JsonSerializer.Deserialize<List<SuggestedFont>>(catalogJson, CatalogSerializerOptions)
            ?? throw new InvalidDataException("The font catalog is empty or invalid.");

        var validFonts = fonts
            .Where(IsValidSuggestedFont)
            .GroupBy(font => font.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (validFonts.Count == 0)
        {
            throw new InvalidDataException("The font catalog did not contain any valid fonts.");
        }

        return validFonts;
    }

    private static bool IsValidSuggestedFont(SuggestedFont font)
    {
        return !string.IsNullOrWhiteSpace(font.Id)
            && !string.IsNullOrWhiteSpace(font.Name)
            && !string.IsNullOrWhiteSpace(font.FamilyName)
            && Uri.TryCreate(font.Url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && font.Sha256.Length == 64
            && font.Sha256.All(Uri.IsHexDigit)
            && font.Size > 0
            && !string.IsNullOrWhiteSpace(font.FileName)
            && font.FileName.Equals(Path.GetFileName(font.FileName), StringComparison.Ordinal)
            && (font.FileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                || font.FileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));
    }

    private static void SaveSuggestedFontCache(string catalogJson)
    {
        var directory = GetFontDirectory();
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, FontCatalogCacheFileName);
        var tempPath = targetPath + ".download";
        File.WriteAllText(tempPath, catalogJson);
        File.Move(tempPath, targetPath, true);
    }

    private static string GetFontDirectory()
    {
        return Path.Combine(AppLevelVariables.Instance.AppConfigFolder, "fonts");
    }
}

public sealed record FontChoice(string Id, string DisplayName, bool IsSystemDefault);

public sealed record SuggestedFont(
    string Id,
    string Name,
    string Description,
    string FamilyName,
    string FileName,
    string Url,
    string Sha256,
    long Size);
