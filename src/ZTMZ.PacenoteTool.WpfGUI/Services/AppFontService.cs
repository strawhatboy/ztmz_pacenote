using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private const string SuggestedFontPrefix = SuggestedFontCatalogService.SuggestedFontPrefix;
    private readonly SuggestedFontCatalogService _fontCatalog = new();
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

    public IReadOnlyList<SuggestedFont> SuggestedFonts => _fontCatalog.Fonts;

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

    public SuggestedFont? GetConfiguredSuggestedFont() =>
        _fontCatalog.GetConfiguredFont(Config.Instance.UIFontFamily);

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

    public bool IsInstalled(SuggestedFont font) => _fontCatalog.IsInstalled(font);

    public bool TryApplySuggestedFont(SuggestedFont font)
    {
        if (!_fontCatalog.TryGetFontPath(font, out var fontPath))
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
        CancellationToken cancellationToken = default) =>
        await _fontCatalog.DownloadAsync(font, progress, cancellationToken);

    public Task<IReadOnlyList<SuggestedFont>> RefreshSuggestedFontsAsync(CancellationToken cancellationToken = default) =>
        _fontCatalog.RefreshAsync(cancellationToken);

    private static void ApplyFont(FontFamily fontFamily)
    {
        Application.Current.Resources["AppFontFamily"] = fontFamily;
    }

}

public sealed record FontChoice(string Id, string DisplayName, bool IsSystemDefault);
