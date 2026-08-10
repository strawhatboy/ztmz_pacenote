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
using System.Windows.Documents;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using WpfUiTextBlock = Wpf.Ui.Controls.TextBlock;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

public sealed class AppFontService
{
    public const string SystemDefaultFontId = "system-default";
    public const int MinimumFontSizePercent = 85;
    public const int MaximumFontSizePercent = 130;
    public const int DefaultFontSizePercent = 100;
    private const double DefaultFontSize = 12d;
    private const double DefaultControlContentFontSize = 14d;
    private const string BodyStrongFontSizeResourceKey = "AppBodyStrongFontSize";
    private const string SuggestedFontPrefix = "suggested:";
    private const string FontCatalogUrl = "https://gitee.com/ztmz/fonts/raw/master/repo.json";
    private const string FontCatalogCacheFileName = "repo.json";
    private static readonly JsonSerializerOptions CatalogSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IReadOnlyList<SuggestedFont> _suggestedFonts;
    private readonly Dictionary<string, Style> _baseTypographyStyles = new();
    private static readonly IReadOnlyDictionary<string, double> TypographyStyleFontSizes =
        new Dictionary<string, double>
        {
            ["CaptionTextBlockStyle"] = 12d,
            ["BodyTextBlockStyle"] = 14d,
            ["BodyStrongTextBlockStyle"] = 14d,
            ["SubtitleTextBlockStyle"] = 20d,
            ["TitleTextBlockStyle"] = 28d,
            ["TitleLargeTextBlockStyle"] = 40d,
            ["DisplayTextBlockStyle"] = 68d
        };

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

    public void ApplyConfiguredFontSize()
    {
        ApplyFontSize(Config.Instance.UIFontSizePercent);
    }

    public void ApplyFontSize(int percentage)
    {
        var boundedPercentage = Math.Clamp(percentage, MinimumFontSizePercent, MaximumFontSizePercent);
        var scale = boundedPercentage / (double)DefaultFontSizePercent;
        var resources = Application.Current.Resources;

        resources["AppFontSize"] = DefaultFontSize * scale;
        resources[BodyStrongFontSizeResourceKey] = TypographyStyleFontSizes["BodyStrongTextBlockStyle"] * scale;
        resources["ContentControlFontSize"] = DefaultControlContentFontSize * scale;
        resources["ControlContentThemeFontSize"] = DefaultControlContentFontSize * scale;

        foreach (var (resourceKey, baseFontSize) in TypographyStyleFontSizes)
        {
            if (!_baseTypographyStyles.TryGetValue(resourceKey, out var baseStyle))
            {
                baseStyle = (Style)resources[resourceKey];
                _baseTypographyStyles[resourceKey] = baseStyle;
            }

            var scaledStyle = new Style(baseStyle.TargetType, baseStyle);
            scaledStyle.Setters.Add(new Setter(TextElement.FontSizeProperty, baseFontSize * scale));
            resources[resourceKey] = scaledStyle;
        }

        RefreshActiveTypographyStyles();
    }

    private static void RefreshActiveTypographyStyles()
    {
        foreach (Window window in Application.Current.Windows)
        {
            RefreshTypographyStyles(window);
        }
    }

    private static void RefreshTypographyStyles(DependencyObject element)
    {
        if (element is WpfUiTextBlock textBlock)
        {
            if (textBlock.FontTypography is { } typography)
            {
                textBlock.FontTypography = typography == FontTypography.Caption
                    ? FontTypography.Body
                    : FontTypography.Caption;
                textBlock.FontTypography = typography;

            }
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            RefreshTypographyStyles(VisualTreeHelper.GetChild(element, index));
        }
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
        return IsExpectedFontFile(GetSuggestedFontPath(font), font.Sha256)
            || IsExpectedFontFile(GetLegacySuggestedFontPath(font), font.Sha256);
    }

    public bool TryApplySuggestedFont(SuggestedFont font)
    {
        if (!TryGetIsolatedSuggestedFontPath(font, out var fontPath))
        {
            return false;
        }

        try
        {
            var fontDirectory = Path.GetDirectoryName(fontPath)! + Path.DirectorySeparatorChar;
            var expectedSource = $"./#{font.FamilyName}";
            var fontFamily = Fonts.GetFontFamilies(new Uri(fontDirectory))
                .FirstOrDefault(candidate =>
                    candidate.Source.Equals(expectedSource, StringComparison.OrdinalIgnoreCase)
                    || candidate.FamilyNames.Values.Any(name =>
                        name.Equals(font.FamilyName, StringComparison.OrdinalIgnoreCase)));
            if (fontFamily is null)
            {
                return false;
            }

            ApplyFont(fontFamily);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
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
        return Path.Combine(
            GetFontDirectory(),
            "suggested",
            font.Sha256.ToUpperInvariant(),
            GetSafeSuggestedFontFileName(font));
    }

    private static string GetLegacySuggestedFontPath(SuggestedFont font)
    {
        return Path.Combine(GetFontDirectory(), GetSafeSuggestedFontFileName(font));
    }

    private static string GetSafeSuggestedFontFileName(SuggestedFont font)
    {
        var safeFileName = Path.GetFileName(font.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName)
            || !safeFileName.Equals(font.FileName, StringComparison.Ordinal)
            || (!safeFileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !safeFileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The font catalog contains an invalid font file name.");
        }

        return safeFileName;
    }

    private static bool TryGetIsolatedSuggestedFontPath(SuggestedFont font, out string fontPath)
    {
        fontPath = GetSuggestedFontPath(font);
        if (IsExpectedFontFile(fontPath, font.Sha256))
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
            Directory.CreateDirectory(Path.GetDirectoryName(fontPath)!);
            File.Copy(legacyPath, fontPath, true);
            return IsExpectedFontFile(fontPath, font.Sha256);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsExpectedFontFile(string path, string expectedHash)
    {
        try
        {
            return File.Exists(path) && IsHashMatch(path, expectedHash);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
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
