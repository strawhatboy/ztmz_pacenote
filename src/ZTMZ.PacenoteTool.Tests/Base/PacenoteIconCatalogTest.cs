using System.Buffers.Binary;
using System.Text.Json;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class PacenoteIconCatalogTest
{
    [Fact]
    public void ShippedCatalogResolvesCanonicalAndAliasTokens()
    {
        var catalog = PacenoteIconCatalog.Load();

        Assert.Equal(1, catalog.Version);
        Assert.Equal(128, catalog.IconSize);
        Assert.Equal(346, catalog.IconCount);
        Assert.Equal(710, catalog.AliasCount);

        Assert.True(catalog.TryResolve("rough", out var canonical));
        Assert.Equal("rough", canonical!.CanonicalToken);
        Assert.EndsWith(Path.Combine("icons", "rough.png"), canonical.FilePath);
        Assert.Equal("/pacenote-icons/v1/icons/rough.png", canonical.WebPath);

        Assert.True(catalog.TryResolve("DETAIL_ROUGH.PNG", out var alias));
        Assert.Equal(canonical, alias);
        Assert.True(catalog.TryResolve("system_collision_severe", out var systemAlias));
        Assert.Equal("collision_severe", systemAlias!.CanonicalToken);
        Assert.False(catalog.TryResolve("../../rough", out _));
        Assert.False(catalog.TryResolve("unknown_token", out _));

        var canonicalIcons = catalog.GetCanonicalIcons();
        Assert.Equal(catalog.IconCount, canonicalIcons.Count);
        Assert.Equal(catalog.IconCount, canonicalIcons.Select(icon => icon.CanonicalToken).Distinct().Count());
        Assert.Equal("rough", catalog.TokenMap["detail_rough"]);
        Assert.All(canonicalIcons, icon => Assert.StartsWith(PacenoteIconCatalog.WebRequestPath, icon.WebPath));
    }

    [Fact]
    public void EveryManifestIconIsA128PixelPng()
    {
        var assetRoot = PacenoteIconCatalog.GetAssetRoot(AppContext.BaseDirectory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetRoot, "manifest.json")));
        var icons = manifest.RootElement.GetProperty("icons");

        Assert.Equal(346, icons.EnumerateObject().Count());
        foreach (var icon in icons.EnumerateObject())
        {
            var relativePath = icon.Value.GetProperty("file").GetString();
            Assert.NotNull(relativePath);
            var filePath = Path.Combine(assetRoot, relativePath!.Replace('/', Path.DirectorySeparatorChar));
            var header = File.ReadAllBytes(filePath).AsSpan(0, 24);

            Assert.True(header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            Assert.Equal(128, BinaryPrimitives.ReadInt32BigEndian(header[16..20]));
            Assert.Equal(128, BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
        }
    }
}
