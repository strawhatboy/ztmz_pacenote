using System.Net;
using System.Net.Sockets;
using ZTMZ.PacenoteTool.Base.UI;

namespace ZTMZ.PacenoteTool.Tests.Base.UI;

public class WebDashboardCommandCaptureTest
{
    [Fact]
    public void DashboardUrlsIncludeLocalhostAndUseTheConfiguredPort()
    {
        const int port = 18080;

        var urls = WebDashboardServer.GetDashboardUrls(port);

        Assert.NotEmpty(urls);
        Assert.Contains($"http://localhost:{port}", urls);
        Assert.All(urls, url => Assert.EndsWith($":{port}", url));
        Assert.Equal(urls[0], WebDashboardServer.GetDashboardUrl(port));
    }

    [Fact]
    public async Task ServerStartsWithoutCreatingANativeOverlay()
    {
        var port = GetAvailablePort();
        using var server = new WebDashboardServer { Port = port };
        server.Initialize(new List<Dashboard>());
        server.Start();

        using var client = new HttpClient();
        var endpoint = $"http://127.0.0.1:{port}/api/dashboards";
        var response = await GetWhenReady(client, endpoint);
        var page = await GetWhenReady(client, $"http://127.0.0.1:{port}/");

        Assert.True(server.IsRunning);
        Assert.Equal("[]", response);
        Assert.Contains("fullscreen-button", page);
        Assert.Contains("orientation-button", page);
    }

    [Fact]
    public void ProxyCapturesStandaloneDashboardCommands()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        var graphics = new GraphicsCommandProxy(capture);
        var brush = graphics.CreateSolidBrush(10, 20, 30, 255);
        var image = graphics.CreateImage("background", "not-a-real-file.png");

        capture.RegisterImage("background", image);
        graphics.ClearScene();
        graphics.FillRectangle(brush, 10, 20, 110, 220);
        graphics.FillRoundedRectangle(brush, 20, 30, 120, 130, 8);
        graphics.FillEllipse(brush, 50, 60, 10, 12);
        graphics.ClipRegionStart(5, 6, 50, 60);
        graphics.DrawImage(image, 10, 20, 110, 220);
        graphics.ClipRegionEnd();

        var commands = capture.GetCommands();

        Assert.Contains(commands, command => command is ClearCommand);
        Assert.Contains(commands, command => command is FillRectangleCommand);
        Assert.Contains(commands, command => command is FillRoundedRectangleCommand);
        Assert.Contains(commands, command => command is FillEllipseCommand);
        Assert.Contains(commands, command => command is SetClipCommand clip
            && clip.X == 5 && clip.Y == 6 && clip.Width == 45 && clip.Height == 54);
        Assert.Contains(commands, command => command is DrawImageCommand imageCommand
            && imageCommand.ImageKey == "background");
        Assert.Contains(commands, command => command is ReleaseClipCommand);
    }

    private static async Task<string> GetWhenReady(HttpClient client, string endpoint)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return await client.GetStringAsync(endpoint);
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
                await Task.Delay(100);
            }
        }

        throw new InvalidOperationException("WebDashboard server did not become available.", lastException);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
