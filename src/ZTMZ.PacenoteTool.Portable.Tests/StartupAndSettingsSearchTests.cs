using System.Net;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public class StartupAndSettingsSearchTests
{
    private const string Catalog = """
        [{"id":"old","version":"2.0"},{"id":"old","version":"2.0"},
         {"id":"current","version":"2.0"},{"id":"new","version":"1.0"}]
        """;
    private static CoDriverPackageInfo[] Installed => [new() { id = "old", version = "1.0" }, new() { id = "current", version = "2.0" }];

    [Fact]
    public async Task StartupChecksOnceAndOnlyReportsInstalledOutdatedPackages()
    {
        using var handler = new Handler(Catalog);
        using var client = new HttpClient(handler);
        var service = Service(client);
        var check = new StartupCoDriverUpdateCheck(service);
        var first = check.CheckOnceAsync(true, "https://test/catalog", Installed);
        var second = check.CheckOnceAsync(true, "https://test/catalog", Installed);
        Assert.Same(first, second);
        Assert.Equal("old", Assert.Single(await first).Definition.id);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(1, service.Notifications.Count);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DisabledOrNoPackagesNeverContactsNetwork(bool enabled, bool hasPackages)
    {
        using var handler = new Handler(Catalog);
        using var client = new HttpClient(handler);
        Assert.Empty(await new StartupCoDriverUpdateCheck(Service(client)).CheckOnceAsync(enabled,
            "https://test/catalog", hasPackages ? Installed : []));
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("invalid json", HttpStatusCode.OK)]
    [InlineData("", HttpStatusCode.BadGateway)]
    public async Task OfflineOrInvalidCatalogDoesNotBlockStartup(string body, HttpStatusCode status)
    {
        using var handler = new Handler(body, status);
        using var client = new HttpClient(handler);
        Assert.Empty(await new StartupCoDriverUpdateCheck(Service(client)).CheckOnceAsync(true, "https://test/catalog", Installed));
    }

    [Fact]
    public void SuccessfulInstallRemovesBadgeAndStaleResponseCannotRestoreIt()
    {
        var notifications = new CoDriverUpdateNotifications();
        var statuses = new[] { new CoDriverPackageUpdateStatus(new() { id = "old", version = "2.0" }, true, false, "", "1.0") };
        notifications.Replace(statuses, notifications.Revision);
        var requestRevision = notifications.Revision;
        notifications.Installed(new() { id = "old", version = "2.0" });
        notifications.Replace(statuses, requestRevision);
        Assert.Equal(0, notifications.Count);
    }

    [Fact]
    public async Task CanceledStartupCheckReturnsWithoutThrowing()
    {
        using var client = new HttpClient(new Handler(Catalog));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Empty(await new StartupCoDriverUpdateCheck(Service(client)).CheckOnceAsync(true,
            "https://test/catalog", Installed, cancellation.Token));
    }

    [Fact]
    public async Task FailedRefreshPreservesKnownUpdateBadge()
    {
        using var client = new HttpClient(new Handler("", HttpStatusCode.BadGateway));
        var service = Service(client);
        service.Notifications.Replace([new(new() { id = "old", version = "2.0" }, true, false, "", "1.0")], 0);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckForUpdatesAsync("https://test/catalog", Installed));
        Assert.Equal(1, service.Notifications.Count);
    }

    [Fact]
    public void ImportingOlderVersionDoesNotClearAvailableUpdate()
    {
        var notifications = new CoDriverUpdateNotifications();
        notifications.Replace([new(new() { id = "old", version = "2.0" }, true, false, "", "1.0")], 0);
        notifications.Installed(new() { id = "old", version = "1.5" });
        Assert.Equal(1, notifications.Count);
    }

    [Fact]
    public void SearchMatchesLocalizedDescriptionsAndEnglishWithoutDuplicateRows()
    {
        SettingSearchEntry[] entries = [new("general", "tabs.general", "voiceStartup.setting", ["voiceStartup.description"]),
            new("hud", "tabs.hud", "settings.hud", ["settings.hud"])];
        var chinese = new Dictionary<string, string> { ["voiceStartup.setting"] = "启动时检查领航员更新", ["voiceStartup.description"] = "后台检查已安装语音包", ["tabs.general"] = "常规" };
        var english = new Dictionary<string, string> { ["voiceStartup.setting"] = "Check voice updates on startup", ["voiceStartup.description"] = "Checks installed voice packages" };
        IReadOnlyList<SettingSearchResult> Search(string query) => SettingsSearchIndex.Search(entries, query,
            key => chinese.GetValueOrDefault(key, key), key => english.GetValueOrDefault(key, key));
        Assert.Equal("general", Assert.Single(Search("已安装 语音包")).Entry.PageId);
        Assert.Equal("启动时检查领航员更新", Assert.Single(Search("ＶＯＩＣＥ startup")).Title);
        Assert.Empty(Search(""));
        Assert.Empty(Search("voice missing"));
    }

    private static CoDriverPackageUpdateService Service(HttpClient client) =>
        new(client, new TemporaryFileDownloadService(client), new CoDriverPackageCatalogService(new Paths()));
    private sealed class Paths : IApplicationPaths
    {
        public string DataRoot => Path.GetTempPath();
        public bool IsPortable => false;
        public string GetPath(string path) => Path.Combine(DataRoot, path);
        public void SetDataRootForNextLaunch(string path) { }
    }
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
