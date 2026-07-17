using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class CoDriverSelectionIdentityTest
{
    [Fact]
    public void CreatePrefersPackageIdOverPath()
    {
        var package = Package("id-1", "C:\\codrivers\\Alpha");

        Assert.Equal("id:id-1", CoDriverSelectionIdentity.Create(package));
    }

    [Fact]
    public void CreateFallsBackToPathForLegacyPackage()
    {
        var package = Package(string.Empty, "C:\\codrivers\\Legacy");

        Assert.Equal("path:C:\\codrivers\\Legacy", CoDriverSelectionIdentity.Create(package));
    }

    [Fact]
    public void ResolveIndexPrefersStableIdentityOverLegacyIndex()
    {
        var packages = new[]
        {
            Package("first", "C:\\codrivers\\First"),
            Package("selected", "C:\\codrivers\\Selected")
        };

        var index = CoDriverSelectionIdentity.ResolveIndex(packages, "id:selected", 0);

        Assert.Equal(1, index);
    }

    [Fact]
    public void ResolveIndexUsesLegacyIndexWhenIdentityIsMissing()
    {
        var packages = new[]
        {
            Package("first", "C:\\codrivers\\First"),
            Package("second", "C:\\codrivers\\Second")
        };

        var index = CoDriverSelectionIdentity.ResolveIndex(packages, string.Empty, 1);

        Assert.Equal(1, index);
    }

    [Fact]
    public void ResolveIndexRejectsUnknownIdentityWithoutValidFallback()
    {
        var packages = new[] { Package("first", "C:\\codrivers\\First") };

        var index = CoDriverSelectionIdentity.ResolveIndex(packages, "id:missing", -1);

        Assert.Equal(-1, index);
    }

    private static CoDriverPackageInfo Package(string id, string path)
    {
        return new CoDriverPackageInfo
        {
            id = id,
            name = path.Split('\\').Last(),
            Path = path
        };
    }
}
