#nullable enable

using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal sealed record AvaloniaFontChoice(string Id, string DisplayName, bool IsSystemDefault);

internal sealed record AvaloniaSuggestedFont(
    string Id,
    string Name,
    string Description,
    string FamilyName,
    string FileName,
    string Url,
    string Sha256,
    long Size);

internal sealed class AvaloniaAppearanceService
{
    public const string SystemDefaultFontId = "system-default";
    public const string SuggestedFontPrefix = "suggested:";
    public const int MinimumFontSizePercent = 85;
    public const int MaximumFontSizePercent = 130;
    public const int DefaultFontSizePercent = 100;

    private const double DefaultFontSize = 14d;
    private const string FontCatalogUrl = "https://gitee.com/ztmz/fonts/raw/master/repo.json";
    private const string FontCatalogCacheFileName = "repo.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IReadOnlyList<AvaloniaSuggestedFont> _suggestedFonts = LoadCachedSuggestedFonts();

    public IReadOnlyList<AvaloniaSuggestedFont> SuggestedFonts => _suggestedFonts;

    public static void ApplyConfiguredAppearance(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        ApplyTheme(application, Config.Instance.UseSystemTheme
            ? 2
            : Config.Instance.IsDarkTheme ? 1 : 0);
        ApplyAccentColor(application);
        ApplyFontSize(application, Config.Instance.UIFontSizePercent);

        var configuredFont = Config.Instance.UIFontFamily ?? string.Empty;
        if (configuredFont.StartsWith(SuggestedFontPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var service = new AvaloniaAppearanceService();
            var configuredSuggestedFont = service.GetConfiguredSuggestedFont();
            if (configuredSuggestedFont != null && service.TryApplySuggestedFont(configuredSuggestedFont))
            {
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(configuredFont))
        {
            ApplyFont(application, new FontFamily(configuredFont));
            return;
        }

        ApplyFont(application, FontManager.Current.DefaultFontFamily);
    }

    public IReadOnlyList<AvaloniaFontChoice> GetSystemFontChoices()
    {
        var choices = new List<AvaloniaFontChoice>
        {
            new(SystemDefaultFontId, AvaloniaLocalization.Get("font.systemDefault"), true)
        };
        choices.AddRange(FontManager.Current.SystemFonts
            .Where(font => !string.IsNullOrWhiteSpace(font.Name))
            .DistinctBy(font => font.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(font => font.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(font => new AvaloniaFontChoice(font.Name, font.Name, false)));
        return choices;
    }

    public AvaloniaFontChoice? GetConfiguredSystemFontChoice(IReadOnlyList<AvaloniaFontChoice> choices)
    {
        if (choices.Count == 0)
        {
            return null;
        }

        var configured = Config.Instance.UIFontFamily ?? string.Empty;
        if (configured.StartsWith(SuggestedFontPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(configured)
            ? choices[0]
            : choices.FirstOrDefault(choice => string.Equals(
                  choice.Id,
                  configured,
                  StringComparison.OrdinalIgnoreCase)) ?? choices[0];
    }

    public AvaloniaSuggestedFont? GetConfiguredSuggestedFont()
    {
        var configured = Config.Instance.UIFontFamily ?? string.Empty;
        if (!configured.StartsWith(SuggestedFontPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = configured[SuggestedFontPrefix.Length..];
        return SuggestedFonts.FirstOrDefault(font => string.Equals(
            font.Id,
            id,
            StringComparison.OrdinalIgnoreCase));
    }

    public void ApplySystemFont(AvaloniaFontChoice choice)
    {
        var application = Application.Current ?? throw new InvalidOperationException("Avalonia is not initialized.");
        ApplyFont(application, choice.IsSystemDefault
            ? FontManager.Current.DefaultFontFamily
            : new FontFamily(choice.Id));
    }

    public static void ApplyTheme(Application application, int selection)
    {
        application.RequestedThemeVariant = selection switch
        {
            0 => ThemeVariant.Light,
            1 => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public static void ApplyAccentColor(Application application)
    {
        var theme = application.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        if (theme == null)
        {
            return;
        }

        theme.PreferUserAccentColor = Config.Instance.UseSystemTheme;
        theme.CustomAccentColor = Config.Instance.UseSystemTheme
            ? null
            : Color.FromRgb(
                (byte)Math.Clamp(Config.Instance.AccentColorR, 0, 255),
                (byte)Math.Clamp(Config.Instance.AccentColorG, 0, 255),
                (byte)Math.Clamp(Config.Instance.AccentColorB, 0, 255));
    }

    public static void ApplyFontSize(Application application, int percentage)
    {
        var bounded = Math.Clamp(percentage, MinimumFontSizePercent, MaximumFontSizePercent);
        var scale = bounded / (double)DefaultFontSizePercent;
        application.Resources["AppFontSize"] = DefaultFontSize * scale;
        application.Resources["AppBodyStrongFontSize"] = 15d * scale;
        application.Resources["AppTitleFontSize"] = 28d * scale;
    }

    public bool IsInstalled(AvaloniaSuggestedFont font) =>
        IsExpectedFontFile(GetSuggestedFontPath(font), font.Sha256)
        || IsExpectedFontFile(GetLegacySuggestedFontPath(font), font.Sha256);

    public bool TryApplySuggestedFont(AvaloniaSuggestedFont font)
    {
        if (!TryGetSuggestedFontPath(font, out var fontPath) || Application.Current == null)
        {
            return false;
        }

        try
        {
            // Avalonia 12 cannot load a font from a bare directory/file URI passed
            // to FontFamily. Register the font file as a font collection keyed by a
            // fonts:// URI, then reference it through the parsed "fonts://key#family"
            // form so the FontManager actually resolves the custom glyph typeface.
            // See https://github.com/AvaloniaUI/Avalonia/issues/16793
            var key = new Uri($"fonts://ztmz/suggested/{Uri.EscapeDataString(font.Id)}/", UriKind.Absolute);
            var source = new Uri(fontPath, UriKind.Absolute);
            var collection = new EmbeddedFontCollection(key, source);
            if (!collection.TryGetGlyphTypeface(
                    font.FamilyName,
                    FontStyle.Normal,
                    FontWeight.Normal,
                    FontStretch.Normal,
                    out _))
            {
                return false;
            }

            FontManager.Current.RemoveFontCollection(key);
            FontManager.Current.AddFontCollection(collection);
            ApplyFont(Application.Current, FontFamily.Parse($"{key}#{font.FamilyName}"));
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    public async Task DownloadSuggestedFontAsync(
        AvaloniaSuggestedFont font,
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
            using var response = await client.GetAsync(
                font.Url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
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

    public async Task<IReadOnlyList<AvaloniaSuggestedFont>> RefreshSuggestedFontsAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();
        var json = await client.GetStringAsync(FontCatalogUrl, cancellationToken);
        var fonts = ParseSuggestedFonts(json);
        SaveSuggestedFontCache(json);
        _suggestedFonts = fonts;
        return fonts;
    }

    private static void ApplyFont(Application application, FontFamily fontFamily) =>
        application.Resources["AppFontFamily"] = fontFamily;

    private static IReadOnlyList<AvaloniaSuggestedFont> LoadCachedSuggestedFonts()
    {
        var cachePath = Path.Combine(GetFontDirectory(), FontCatalogCacheFileName);
        if (!File.Exists(cachePath))
        {
            return [];
        }

        try
        {
            return ParseSuggestedFonts(File.ReadAllText(cachePath));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return [];
        }
    }

    private static IReadOnlyList<AvaloniaSuggestedFont> ParseSuggestedFonts(string json)
    {
        var fonts = JsonSerializer.Deserialize<List<AvaloniaSuggestedFont>>(json, SerializerOptions)
                    ?? throw new InvalidDataException("The font catalog is empty or invalid.");
        var validFonts = fonts
            .Where(IsValidSuggestedFont)
            .GroupBy(font => font.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return validFonts.Length > 0
            ? validFonts
            : throw new InvalidDataException("The font catalog did not contain any valid fonts.");
    }

    private static bool IsValidSuggestedFont(AvaloniaSuggestedFont font) =>
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

    private static void SaveSuggestedFontCache(string json)
    {
        var directory = GetFontDirectory();
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, FontCatalogCacheFileName);
        var tempPath = targetPath + ".download";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, targetPath, true);
    }

    private static string GetSuggestedFontPath(AvaloniaSuggestedFont font) => Path.Combine(
        GetFontDirectory(),
        "suggested",
        font.Sha256.ToUpperInvariant(),
        GetSafeFontFileName(font));

    private static string GetLegacySuggestedFontPath(AvaloniaSuggestedFont font) =>
        Path.Combine(GetFontDirectory(), GetSafeFontFileName(font));

    private static string GetSafeFontFileName(AvaloniaSuggestedFont font)
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

    private static bool TryGetSuggestedFontPath(AvaloniaSuggestedFont font, out string path)
    {
        path = GetSuggestedFontPath(font);
        if (IsExpectedFontFile(path, font.Sha256))
        {
            return true;
        }

        var legacyPath = GetLegacySuggestedFontPath(font);
        if (!IsExpectedFontFile(legacyPath, font.Sha256))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(legacyPath, path, true);
            return IsExpectedFontFile(path, font.Sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsExpectedFontFile(string path, string hash)
    {
        try
        {
            return File.Exists(path) && IsHashMatch(path, hash);
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
