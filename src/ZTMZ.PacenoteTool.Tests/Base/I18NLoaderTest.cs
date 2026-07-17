using System.Reflection;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class I18NLoaderTest
{
    [Fact]
    public void UsesSelectedCultureThenFallsBackToEnglishPerKey()
    {
        using var testDirectory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(testDirectory.Path, "en-us.json"),
            """
            {
              "common": {
                "translated": "English",
                "fallback": "English fallback"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(testDirectory.Path, "de-de.json"),
            """
            {
              "common": {
                "translated": "Deutsch"
              }
            }
            """);

        var loader = CreateLoader();
        loader.Initialize(new[] { testDirectory.Path });
        loader.SetCulture("de-DE");

        Assert.Equal("de-de", loader.CurrentCultureName);
        Assert.Contains("de-de", loader.cultures);
        Assert.Equal("Deutsch", loader["common.translated"]);
        Assert.Equal("English fallback", loader["common.fallback"]);
        Assert.Equal("English fallback", loader.CurrentCulture["common.fallback"]);
        Assert.Equal("English fallback", loader.ResolveByKeyAndCulture("common.fallback", "de-DE"));
        Assert.Equal("common.unknown", loader["common.unknown"]);
    }

    [Fact]
    public void UsesEnglishWhenTheRequestedCultureIsUnavailable()
    {
        using var testDirectory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(testDirectory.Path, "en-us.json"), "{ \"common\": { \"value\": \"English\" } }");

        var loader = CreateLoader();
        loader.Initialize(new[] { testDirectory.Path });
        loader.SetCulture("fr-FR");

        Assert.Equal(I18NLoader.FallbackCultureName, loader.CurrentCultureName);
        Assert.Equal("English", loader["common.value"]);
    }

    [Fact]
    public void UsesAnInstalledRegionalVariantWhenTheExactCultureIsUnavailable()
    {
        using var testDirectory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(testDirectory.Path, "en-us.json"), "{ \"common\": { \"value\": \"English\" } }");
        File.WriteAllText(Path.Combine(testDirectory.Path, "de-de.json"), "{ \"common\": { \"value\": \"Deutsch\" } }");

        var loader = CreateLoader();
        loader.Initialize(new[] { testDirectory.Path });
        loader.SetCulture("de-AT");

        Assert.Equal("de-de", loader.CurrentCultureName);
        Assert.Equal("Deutsch", loader["common.value"]);
    }

    private static I18NLoader CreateLoader()
    {
        var constructor = typeof(I18NLoader).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(bool) },
            modifiers: null);

        if (constructor == null)
        {
            throw new InvalidOperationException("I18NLoader constructor was not found.");
        }

        return (I18NLoader)constructor.Invoke(new object[] { true });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ZTMZPacenoteI18NTests",
            Guid.NewGuid().ToString("N"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
