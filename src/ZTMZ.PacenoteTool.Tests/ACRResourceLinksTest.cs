using ZTMZ.PacenoteTool.AC;
using ZTMZ.PacenoteTool.Base.Game;
using Xunit;

namespace ZTMZ.PacenoteTool.Tests;

public sealed class ACRResourceLinksTest
{
    [Fact]
    public void ACRally_DeclaresReplacementChinesePacenotesWithoutGuiProjectReferences()
    {
        var assembly = typeof(ACRally).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name is "ZTMZ.PacenoteTool.UI.Wpf" or "ZTMZ.PacenoteTool.Avalonia");

        var resources = Assert.IsAssignableFrom<IGameWithResourceLinks>(new ACRally());
        var link = Assert.Single(resources.ResourceLinks);

        Assert.Equal("replacement-chinese-pacenotes", link.Id);
        Assert.Equal("game.acr.resources.replacementChinesePacenotes", link.LabelKey);
        Assert.Equal(
            "game.acr.resources.replacementChinesePacenotesDescription",
            link.DescriptionKey);
        Assert.Equal(GameResourceKind.Pacenotes, link.Kind);
        Assert.Equal(
            "https://gitee.com/ztmz/acr_pacenotes/releases/tag/0.0.1",
            link.Uri.AbsoluteUri);
    }
}
