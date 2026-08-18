using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
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

    [Fact]
    public void ControllerOwnsRuntimeBridgeAndSupportsLiveFrameRateChanges()
    {
        using var files = new TemporaryDashboardFiles();
        files.CreateDashboard($"portable-controller-{Guid.NewGuid():N}");
        using var tool = new ZTMZPacenoteTool();
        using var controller = new WebDashboardRuntimeController(
            tool,
            new WebDashboardRuntimeControllerOptions(
                files.DashboardsPath,
                files.RootPath));
        var statusChanges = 0;
        controller.StatusChanged += (_, _) => statusChanges++;
        var port = GetAvailablePort();

        controller.Configure(true, port, 30);

        Assert.True(controller.Status.IsRunning);
        Assert.Equal(port, controller.Status.Port);
        Assert.Equal(30, controller.Diagnostics.TargetFrameRate);

        controller.Configure(true, port, 75);

        Assert.True(controller.Status.IsRunning);
        Assert.Equal(75, controller.Diagnostics.TargetFrameRate);

        controller.Configure(false, port, 75);

        Assert.False(controller.Status.IsRunning);
        Assert.Equal(WebDashboardServerState.Stopped.ToString(), controller.Status.State);
        Assert.Equal(3, statusChanges);
    }

    [Fact]
    public void ControllerReportsInvalidPortWithoutStartingTheRuntime()
    {
        using var files = new TemporaryDashboardFiles();
        using var tool = new ZTMZPacenoteTool();
        using var controller = new WebDashboardRuntimeController(
            tool,
            new WebDashboardRuntimeControllerOptions(
                files.DashboardsPath,
                files.RootPath));

        controller.Configure(true, 80, 60);

        Assert.False(controller.Status.IsRunning);
        Assert.Equal(
            WebDashboardServerState.PortUnavailable.ToString(),
            controller.Status.State);
        Assert.Contains("1024", controller.Status.ErrorMessage);
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
