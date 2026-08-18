using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class WebDashboardRuntimeTest
{
    [Fact]
    public async Task RuntimeServesPortableDashboardCatalogAndStaticClient()
    {
        using var files = new TemporaryDashboardFiles();
        var dashboardId = $"portable-test-{Guid.NewGuid():N}";
        files.CreateDashboard(dashboardId);

        var catalog = new WebDashboardCatalog().Load(
            files.DashboardsPath,
            supportsNativeOverlay: false);
        var definition = Assert.Single(catalog);
        Assert.Equal(dashboardId, definition.Id);
        Assert.True(definition.SupportsWebDashboardClientLua);
        Assert.Equal(42L, definition.GetSettings()["test.value"]);

        var port = GetAvailablePort();
        using var runtime = new WebDashboardRuntime();
        runtime.Start(new WebDashboardRuntimeOptions(
            files.DashboardsPath,
            files.RootPath,
            port,
            61));

        Assert.True(runtime.Status.IsRunning);
        Assert.Equal(61, runtime.GetDiagnostics().TargetFrameRate);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        Assert.Equal("portable dashboard client", await client.GetStringAsync("/"));

        using var dashboardsResponse = JsonDocument.Parse(
            await client.GetStringAsync("/api/dashboards"));
        var listedDashboard = Assert.Single(dashboardsResponse.RootElement.EnumerateArray());
        Assert.Equal("Portable test", listedDashboard.GetProperty("name").GetString());
        Assert.True(listedDashboard.GetProperty("webEnabled").GetBoolean());
        Assert.False(listedDashboard.GetProperty("overlayEnabled").GetBoolean());
        Assert.Equal("web", listedDashboard.GetProperty("outputTarget").GetString());

        using var packageResponse = JsonDocument.Parse(
            await client.GetStringAsync("/api/dashboard/0"));
        Assert.Contains(
            "drawText",
            packageResponse.RootElement.GetProperty("luaScript").GetString());
        Assert.Equal(
            42,
            packageResponse.RootElement.GetProperty("settings").GetProperty("test.value").GetInt32());

        runtime.Stop();
        Assert.Equal(WebDashboardServerState.Stopped, runtime.Status.State);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class TemporaryDashboardFiles : IDisposable
    {
        public TemporaryDashboardFiles()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"ztmz-web-dashboard-test-{Guid.NewGuid():N}");
            DashboardsPath = Path.Combine(RootPath, "dashboards");
            Directory.CreateDirectory(Path.Combine(RootPath, "wwwroot"));
            Directory.CreateDirectory(DashboardsPath);
            File.WriteAllText(
                Path.Combine(RootPath, "wwwroot", "index.html"),
                "portable dashboard client");
        }

        public string RootPath { get; }

        public string DashboardsPath { get; }

        public void CreateDashboard(string dashboardId)
        {
            var packagePath = Path.Combine(DashboardsPath, dashboardId);
            Directory.CreateDirectory(packagePath);
            File.WriteAllText(
                Path.Combine(packagePath, Constants.DASHBOARD_INFO_FILE_NAME),
                """
                {
                  "Name": "Portable test",
                  "Description": "Cross-platform dashboard test",
                  "Author": "ZTMZ",
                  "Version": "1.0.0",
                  "SupportsWebDashboardClientLua": true,
                  "ImageResources": {},
                  "ImageResourcesInDirectory": {},
                  "IsEnabled": true
                }
                """);
            File.WriteAllText(
                Path.Combine(packagePath, Constants.FILE_SETTINGS),
                """
                {
                  "PropertyName": { "test.value": "test.tooltip" },
                  "PropertyValue": [42],
                  "ValueRange": [[]]
                }
                """);
            File.WriteAllText(
                Path.Combine(packagePath, Constants.FILE_LUA_SCRIPT),
                "function draw(g, data, ctx) g:drawText('portable') end");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
