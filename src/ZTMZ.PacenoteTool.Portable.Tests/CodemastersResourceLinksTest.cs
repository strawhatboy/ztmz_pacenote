using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class CodemastersResourceLinksTest
{
    [Fact]
    public void CodemastersPlugin_DeclaresResourceLinksWithoutGuiDependencies()
    {
        var assembly = typeof(WRC).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name is "ZTMZ.PacenoteTool.UI.Wpf" or "ZTMZ.PacenoteTool.Avalonia");

        var wrc = Assert.IsAssignableFrom<IGameWithResourceLinks>(new WRC());
        Assert.Collection(
            wrc.ResourceLinks,
            link =>
            {
                Assert.Equal("replacement-chinese-pacenotes", link.Id);
                Assert.Equal("game.resources.replacementChinesePacenotes", link.LabelKey);
                Assert.Equal("game.resources.wrcReplacementChinesePacenotesDescription", link.DescriptionKey);
                Assert.Equal(GameResourceKind.Pacenotes, link.Kind);
                Assert.Equal("https://gitee.com/ztmz/ea_wrc_chinese_codrivers/releases", link.Uri.AbsoluteUri);
            },
            link =>
            {
                Assert.Equal("chinese-localization", link.Id);
                Assert.Equal(GameResourceKind.Localization, link.Kind);
                Assert.Equal("https://gitee.com/ztmz/ea_wrc_chinese_translation/releases", link.Uri.AbsoluteUri);
            });

        var dirtRally2 = Assert.IsAssignableFrom<IGameWithResourceLinks>(new DirtRally2());
        var localization = Assert.Single(dirtRally2.ResourceLinks);
        Assert.Equal("chinese-localization", localization.Id);
        Assert.Equal(GameResourceKind.Localization, localization.Kind);
        Assert.Equal("game.resources.dr2ChineseLocalizationDescription", localization.DescriptionKey);
        Assert.Equal(
            "https://gitee.com/ztmz/dr2_-chinese_localization/releases",
            localization.Uri.AbsoluteUri);
    }

    [Fact]
    public void ResourceLinks_AreUniqueAbsoluteHttpUris()
    {
        IGameWithResourceLinks[] games = [new WRC(), new DirtRally2()];
        foreach (var game in games)
        {
            Assert.Equal(
                game.ResourceLinks.Count,
                game.ResourceLinks.Select(link => link.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.All(game.ResourceLinks, link =>
            {
                Assert.True(link.Uri.IsAbsoluteUri);
                Assert.True(link.Uri.Scheme == Uri.UriSchemeHttps ||
                    link.Uri.Scheme == Uri.UriSchemeHttp);
                Assert.False(string.IsNullOrWhiteSpace(link.LabelKey));
            });
        }
    }
}
