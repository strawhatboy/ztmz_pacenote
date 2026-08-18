#nullable enable

using System.Net;
using System.Net.Http;
using System.Text;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class CoDriverPackageUpdateServiceTest
{
    [Fact]
    public async Task CheckForUpdatesAsyncClassifiesInstalledAndNewPackages()
    {
        const string catalog = """
            [
              { "id": "old", "name": "Old", "version": "2.0", "url": "https://packages/old" },
              { "id": "current", "name": "Current", "version": "2.0", "url": "https://packages/current" },
              { "id": "new", "name": "New", "version": "1.0", "url": "https://packages/new" }
            ]
            """;
        using var client = new HttpClient(new StaticResponseHandler(catalog));
        var downloads = new TemporaryFileDownloadService(client);
        var packages = new CoDriverPackageCatalogService(new TestApplicationPaths());
        var service = new CoDriverPackageUpdateService(client, downloads, packages);
        var installed = new List<CoDriverPackageInfo>
        {
            new() { id = "old", version = "1.0", Path = "old-path" },
            new() { id = "current", version = "2.0", Path = "current-path" }
        };

        var statuses = await service.CheckForUpdatesAsync(
            "https://packages/catalog",
            installed);

        var oldPackage = Assert.Single(statuses, status => status.Definition.id == "old");
        Assert.True(oldPackage.NeedsUpdate);
        Assert.False(oldPackage.NeedsDownload);
        Assert.Equal("old-path", oldPackage.ExistingPackagePath);

        var currentPackage = Assert.Single(statuses, status => status.Definition.id == "current");
        Assert.False(currentPackage.NeedsUpdate);
        Assert.False(currentPackage.NeedsDownload);

        var newPackage = Assert.Single(statuses, status => status.Definition.id == "new");
        Assert.False(newPackage.NeedsUpdate);
        Assert.True(newPackage.NeedsDownload);
    }

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestApplicationPaths : IApplicationPaths
    {
        public string DataRoot => Path.GetTempPath();
        public string GetPath(string path) => Path.Combine(DataRoot, path);
        public bool IsPortable => false;
        public void SetDataRootForNextLaunch(string folderPath) { }
    }
}
