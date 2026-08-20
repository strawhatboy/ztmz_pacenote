#nullable enable

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal sealed record AvaloniaFontChoice(string Id, string DisplayName, bool IsSystemDefault);

internal sealed class AvaloniaAppearanceService
{
    public const string SystemDefaultFontId = "system-default";
    public const string SuggestedFontPrefix = SuggestedFontCatalogService.SuggestedFontPrefix;
    public const int MinimumFontSizePercent = 85;
    public const int MaximumFontSizePercent = 130;
    public const int DefaultFontSizePercent = 100;

    private const double DefaultFontSize = 14d;
    private readonly SuggestedFontCatalogService _fontCatalog = new();

    public IReadOnlyList<SuggestedFont> SuggestedFonts => _fontCatalog.Fonts;

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

    public SuggestedFont? GetConfiguredSuggestedFont() =>
        _fontCatalog.GetConfiguredFont(Config.Instance.UIFontFamily);

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
        application.Resources["AppSmallFontSize"] = 11d * scale;
        application.Resources["AppSettingDescriptionFontSize"] = 12d * scale;
        application.Resources["AppBodyStrongFontSize"] = 15d * scale;
        application.Resources["AppTitleFontSize"] = 28d * scale;
    }

    public bool IsInstalled(SuggestedFont font) => _fontCatalog.IsInstalled(font);

    public bool TryApplySuggestedFont(SuggestedFont font)
    {
        if (!_fontCatalog.TryGetFontPath(font, out var fontPath) || Application.Current == null)
        {
            return false;
        }

        try
        {
            var key = new Uri($"fonts:ztmz-suggested-{Uri.EscapeDataString(font.Id)}", UriKind.Absolute);
            var collection = new RuntimeFontCollection(key);
            using var stream = File.OpenRead(fontPath);
            if (!collection.TryAddGlyphTypeface(stream, out _))
            {
                return false;
            }

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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or ArgumentException or UriFormatException)
        {
            return false;
        }
    }

    public async Task DownloadSuggestedFontAsync(
        SuggestedFont font,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        await _fontCatalog.DownloadAsync(font, progress, cancellationToken);

    public Task<IReadOnlyList<SuggestedFont>> RefreshSuggestedFontsAsync(
        CancellationToken cancellationToken = default) =>
        _fontCatalog.RefreshAsync(cancellationToken);

    private static void ApplyFont(Application application, FontFamily fontFamily) =>
        application.Resources["AppFontFamily"] = fontFamily;

    private sealed class RuntimeFontCollection(Uri key) : FontCollectionBase
    {
        public override Uri Key { get; } = key;
    }
}
