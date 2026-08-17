using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class BuiltInLocalizationParityTest
{
    private static readonly string[] ProviderCultures =
    [
        "zh-cn", "de-de", "fr-fr", "it-it", "ru-ru",
        "es-es", "pt-br", "ja-jp", "ko-kr"
    ];

    [Theory]
    [InlineData("de-de")]
    [InlineData("fr-fr")]
    [InlineData("it-it")]
    [InlineData("ru-ru")]
    [InlineData("es-es")]
    [InlineData("pt-br")]
    [InlineData("ja-jp")]
    [InlineData("ko-kr")]
    public void CoreLocaleIsShipped(string culture)
    {
        Assert.True(
            LoadBuiltInResourceKeys().ContainsKey(culture),
            $"Expected the built-in {culture} localization pack to be present.");
    }

    [Fact]
    public void CoreLocalesMatchEnglishKeysAndPlaceholders()
    {
        var languageDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ZTMZ.PacenoteTool.I18N",
            "lang");
        var english = LoadResourceValues(Path.Combine(languageDirectory, "en-us.json"));

        foreach (var culture in ProviderCultures)
        {
            var resourcePath = Path.Combine(languageDirectory, $"{culture}.json");
            Assert.True(File.Exists(resourcePath), $"Missing core localization: {culture}.");

            var localized = LoadResourceValues(resourcePath);
            Assert.Equal(
                english.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase),
                localized.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            foreach (var key in english.Keys)
            {
                Assert.Equal(
                    ExtractPlaceholders(english[key]),
                    ExtractPlaceholders(localized[key]));
            }
        }
    }

    [Fact]
    public void BuiltInLocalesOnlyOverrideEnglishResourceKeys()
    {
        var resourcesByCulture = LoadBuiltInResourceKeys();
        var englishKeys = resourcesByCulture["en-us"];

        foreach (var resourceSet in resourcesByCulture.Where(pair => pair.Key != "en-us"))
        {
            var extra = resourceSet.Value.Except(englishKeys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key).ToList();

            Assert.True(
                extra.Count == 0,
                $"{resourceSet.Key} localization contains keys that do not exist in en-us. " +
                $"Unknown: {string.Join(", ", extra.Take(10))}.");
        }
    }

    [Theory]
    [InlineData("ZTMZ.PacenoteTool.AC", "acr")]
    [InlineData("ZTMZ.PacenoteTool.BeamNG", "beamng")]
    [InlineData("ZTMZ.PacenoteTool.Codemasters", "cm")]
    [InlineData("ZTMZ.PacenoteTool.RBR", "rbr")]
    public void GameProviderLocalesMatchEnglishKeysAndPlaceholders(string project, string suffix)
    {
        var languageDirectory = Path.Combine(FindRepositoryRoot(), "src", project, "lang");
        var english = LoadResourceValues(Path.Combine(languageDirectory, $"en-us.{suffix}.json"));

        foreach (var culture in ProviderCultures)
        {
            var resourcePath = Path.Combine(languageDirectory, $"{culture}.{suffix}.json");
            Assert.True(File.Exists(resourcePath), $"Missing {project} localization: {culture}.");

            var localized = LoadResourceValues(resourcePath);
            Assert.Equal(
                english.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase),
                localized.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            foreach (var key in english.Keys)
            {
                Assert.Equal(
                    ExtractPlaceholders(english[key]),
                    ExtractPlaceholders(localized[key]));
            }
        }
    }

    [Fact]
    public void PacenoteRoadbookDashboardIsLocalizedForEveryBuiltInCulture()
    {
        var resourcesByCulture = LoadBuiltInResourceKeys();
        var requiredKeys = new[]
        {
            "dashboards.pacenoteRoadbook.name",
            "dashboards.pacenoteRoadbook.description",
            "dashboards.pacenoteRoadbook.showLabels",
            "dashboards.pacenoteRoadbook.historyRows",
            "dashboards.pacenoteRoadbook.rowGap",
            "dashboards.pacenoteRoadbook.iconLabelGap",
            "dashboards.pacenoteRoadbook.tooltip.showLabels",
            "dashboards.pacenoteRoadbook.tooltip.historyRows",
            "dashboards.pacenoteRoadbook.tooltip.rowGap",
            "dashboards.pacenoteRoadbook.tooltip.iconLabelGap"
        };

        foreach (var culture in new[] { "en-us" }.Concat(ProviderCultures))
        {
            Assert.True(resourcesByCulture.TryGetValue(culture, out var keys), $"Missing {culture} resources.");
            var missing = requiredKeys.Where(key => !keys.Contains(key)).ToList();
            Assert.Empty(missing);
        }
    }

    private static Dictionary<string, HashSet<string>> LoadBuiltInResourceKeys()
    {
        var repositoryRoot = FindRepositoryRoot();
        var resourcesByCulture = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var languageDirectory in Directory.EnumerateDirectories(
                     Path.Combine(repositoryRoot, "src"),
                     "lang",
                     SearchOption.AllDirectories)
                     .Where(directory => IsSourceDirectory(repositoryRoot, directory)))
        {
            foreach (var resourceFile in Directory.EnumerateFiles(languageDirectory, "*.json"))
            {
                var culture = Path.GetFileName(resourceFile).Split('.')[0].ToLowerInvariant();
                if (!IsSupportedCultureName(culture))
                {
                    continue;
                }

                if (!resourcesByCulture.TryGetValue(culture, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    resourcesByCulture.Add(culture, keys);
                }

                var root = JObject.Parse(File.ReadAllText(resourceFile));
                foreach (var value in root.DescendantsAndSelf().OfType<JValue>())
                {
                    keys.Add(value.Path);
                }
            }
        }

        return resourcesByCulture;
    }

    private static bool IsSupportedCultureName(string culture)
    {
        return culture == "zh-cn" || CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Any(candidate => candidate.Name.Equals(culture, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> LoadResourceValues(string path)
    {
        var root = JObject.Parse(File.ReadAllText(path));
        return root.DescendantsAndSelf()
            .OfType<JValue>()
            .ToDictionary(value => value.Path, value => value.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private static string[] ExtractPlaceholders(string value)
    {
        return Regex.Matches(value, @"\{\d+(?::[^{}]+)?\}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsSourceDirectory(string repositoryRoot, string directory)
    {
        return Path.GetRelativePath(repositoryRoot, directory)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .All(segment => !segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                && !segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startPath); directory != null; directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Repository root could not be found.");
    }
}
