using System.Globalization;
using Newtonsoft.Json.Linq;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class BuiltInLocalizationParityTest
{
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
