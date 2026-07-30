using ZTMZ.PacenoteTool.WpfGUI.Models;

namespace ZTMZ.PacenoteTool.Tests.WpfGUI;

public class UpdateFileTest
{
    [Theory]
    [InlineData("en-US", "https://example.invalid/en-us")]
    [InlineData("zh-CN", "https://example.invalid/zh-cn")]
    [InlineData("zh", "https://example.invalid/zh-cn")]
    [InlineData("EN_us", "https://example.invalid/en-us")]
    public void ResolveChangelogUrl_UsesTheClosestLocalizedMatch(string cultureName, string expectedUrl)
    {
        var update = CreateUpdate();

        Assert.Equal(expectedUrl, update.ResolveChangelogUrl(cultureName));
    }

    [Fact]
    public void ResolveChangelogUrl_UsesTheLegacyUrlWhenNoLocalizedMatchExists()
    {
        var update = CreateUpdate();

        Assert.Equal("https://example.invalid/default", update.ResolveChangelogUrl("de-DE"));
    }

    private static UpdateFile CreateUpdate()
    {
        return new UpdateFile
        {
            changelog = "https://example.invalid/default",
            changelogs = new Dictionary<string, string>
            {
                ["en-us"] = "https://example.invalid/en-us",
                ["zh-cn"] = "https://example.invalid/zh-cn"
            }
        };
    }
}
