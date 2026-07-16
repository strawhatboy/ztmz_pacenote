using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using GameOverlay.Drawing;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
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
        using var dashboardListResponse = await client.GetAsync(endpoint);
        using var pageResponse = await client.GetAsync($"http://127.0.0.1:{port}/");
        using var fontResponse = await client.GetAsync($"http://127.0.0.1:{port}/fonts/WRCCleanRoman.ttf");
        dashboardListResponse.EnsureSuccessStatusCode();
        pageResponse.EnsureSuccessStatusCode();
        fontResponse.EnsureSuccessStatusCode();
        var page = await pageResponse.Content.ReadAsStringAsync();

        Assert.True(server.IsRunning);
        Assert.Equal("[]", response);
        Assert.Contains("fullscreen-button", page);
        Assert.Contains("orientation-button", page);
        Assert.Contains("frame-rate", page);
        Assert.Contains("connection-status-text", page);
        Assert.Contains("WRCCleanRoman.ttf", page);
        Assert.Contains("dashboard-client.js?v=1", page);
        Assert.DoesNotContain("fengari", page.ToLowerInvariant());
        Assert.True(dashboardListResponse.Headers.TryGetValues("Cache-Control", out var dashboardCacheControlValues));
        Assert.Contains("no-store", string.Join(",", dashboardCacheControlValues));
        Assert.True(pageResponse.Headers.TryGetValues("Cache-Control", out var cacheControlValues));
        Assert.Contains("no-store", string.Join(",", cacheControlValues));
        Assert.Equal("font/ttf", fontResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DashboardListUsesEffectiveOutputTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ZTMZWebDashboardRouteTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var dashboardPath = Path.Combine(root, "test_dashboard");
            Directory.CreateDirectory(dashboardPath);
            File.WriteAllText(
                Path.Combine(dashboardPath, Constants.FILE_SETTINGS),
                """
                {
                  "PropertyName": { "dashboards.settings.enabled": "dashboards.settings.tooltip.enabled" },
                  "PropertyValue": [ true ],
                  "ValueRange": [ [] ],
                  "PropertyType": [ "System.Boolean" ]
                }
                """);

            var dashboard = new Dashboard(new DashboardDescriptor
            {
                Path = dashboardPath,
                Name = "test.dashboard",
                Description = "test.dashboard.description",
                Author = "test",
                Version = "1.0"
            });
            var port = GetAvailablePort();
            using var server = new WebDashboardServer { Port = port };
            server.Initialize(new List<Dashboard> { dashboard }, _ => DashboardOutputTarget.Web);
            server.Start();

            using var client = new HttpClient();
            var response = await GetWhenReady(client, $"http://127.0.0.1:{port}/api/dashboards");

            Assert.Contains("\"enabled\":true", response);
            Assert.Contains("\"overlayEnabled\":false", response);
            Assert.Contains("\"webEnabled\":true", response);
            Assert.Contains("\"outputTarget\":\"web\"", response);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProxyCapturesStandaloneDashboardCommands()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        var graphics = new GraphicsCommandProxy(capture);
        var brush = graphics.CreateSolidBrush(10, 20, 30, 255);
        var image = graphics.CreateImage("background", "not-a-real-file.png");

        capture.RegisterImage("background", image, "/dashboards/example/background.png");
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
            && imageCommand.ImageKey == "background"
            && imageCommand.ImageUrl == "/dashboards/example/background.png");
        Assert.Contains(commands, command => command is ReleaseClipCommand);
    }

    [Fact]
    public async Task CommandFramesOmitNullProperties()
    {
        var port = GetAvailablePort();
        using var server = new WebDashboardServer { Port = port };
        server.Initialize(new List<Dashboard>());
        server.Start();

        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
        await WaitForClient(server);

        var receiveTask = ReceiveTextAsync(socket);
        await server.BroadcastGameData(
            new GameData(),
            new GameContext(),
            new List<DrawingCommand>
            {
                new FillCircleCommand
                {
                    X = 20,
                    Y = 30,
                    Radius = 10,
                    Color = 0xFF112233
                },
                new DrawImageCommand
                {
                    ImageKey = "background"
                }
            });

        var message = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"type\":\"drawCommands\"", message);
        Assert.DoesNotContain("\"gradient\":null", message);
        Assert.DoesNotContain("\"imageUrl\"", message);
        Assert.DoesNotContain("\"width\":null", message);
        Assert.DoesNotContain("\"height\":null", message);
        Assert.True(socket.HttpResponseHeaders!.TryGetValue(
            "Sec-WebSocket-Extensions",
            out var extensionValues));
        Assert.Contains("permessage-deflate", string.Join(",", extensionValues));
    }

    [Fact]
    public void ProxyCapturesGradientFilledArcGeometry()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        var graphics = new GraphicsCommandProxy(capture);
        var green = graphics.CreateSolidBrush(49, 210, 27, 255);
        var yellow = graphics.CreateSolidBrush(211, 236, 70, 255);
        var red = graphics.CreateSolidBrush(251, 33, 13, 255);
        dynamic gradient = graphics.CreateLinearGradientBrush(green, yellow, red);
        gradient.SetRange(0, 0, 200, 0);

        var geometry = (ProxyGeometry)graphics.CreateGeometry();
        geometry.BeginFigure(new Point(100, 0), true);
        geometry.AddCurveWithArcSegmentArgs(new Point(0, 100), 100, 0, 1);
        geometry.AddPoint(new Point(0, 80));
        geometry.AddCurveWithArcSegmentArgs(new Point(80, 0), 80, 0, 0);
        geometry.EndFigure();
        geometry.Close();
        graphics.FillGeometry(geometry, gradient);

        var command = Assert.IsType<FillGeometryCommand>(Assert.Single(capture.GetCommands()));

        Assert.True(command.Points.Length > 8);
        Assert.NotNull(command.Gradient);
        Assert.Equal("linear", command.Gradient.Type);
        Assert.Equal(3, command.Gradient.Colors.Count);
        Assert.Equal(0, command.Gradient.X0);
        Assert.Equal(200, command.Gradient.X1);
    }

    [Fact]
    public void ProxyCapturesRadialGradientFilledCircleAndEllipse()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        var graphics = new GraphicsCommandProxy(capture);
        dynamic gradient = graphics.CreateRadialGradientBrush(
            new Color(44, 51, 60, 150),
            new Color(44, 51, 60, 0));
        gradient.SetCenter(50, 60);
        gradient.SetRadius(100, 120);

        graphics.FillCircle(gradient, 50, 60, 100);
        graphics.FillEllipse(gradient, 50, 60, 100, 120);

        var commands = capture.GetCommands();
        var circle = Assert.IsType<FillCircleCommand>(commands[0]);
        var ellipse = Assert.IsType<FillEllipseCommand>(commands[1]);

        Assert.Equal("radial", circle.Gradient?.Type);
        Assert.Equal(50, circle.Gradient?.X0);
        Assert.Equal(60, circle.Gradient?.Y0);
        Assert.Equal(120, circle.Gradient?.R1);
        Assert.Equal(0x962C333Cu, circle.Gradient?.Colors[0]);
        Assert.Equal(0x002C333Cu, circle.Gradient?.Colors[1]);
        Assert.Equal("radial", ellipse.Gradient?.Type);
        Assert.Equal(2, ellipse.Gradient?.Colors.Count);
    }

    [Fact]
    public void ProxyFontExposesTheGameOverlayFontSizeApi()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        var graphics = new GraphicsCommandProxy(capture);
        dynamic font = graphics.CreateCustomFont("WRC Clean Roman", 14);

        var size = graphics.MeasureString(font, font.FontSize, "Map information");

        Assert.Equal(14f, (float)font.FontSize);
        Assert.IsType<ProxyPoint>(size);
    }

    [Fact]
    public void ProxySolidBrushSupportsColorMutation()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        var graphics = new GraphicsCommandProxy(capture);
        dynamic brush = graphics.CreateSolidBrush(3, 6, 15, 255);

        brush.Color = new Color(3, 6, 15, 100);
        graphics.FillRectangle(brush, 0, 0, 100, 100);

        var command = Assert.IsType<FillRectangleCommand>(Assert.Single(capture.GetCommands()));
        Assert.Equal(0x6403060Fu, command.Color);
    }

    [Fact]
    public void CaptureKeepsImagesWithSharedResourceNamesDistinct()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        var graphics = new GraphicsCommandProxy(capture);
        var raceLogicImage = graphics.CreateImage("images@background", "race-logic.png");
        var wrcImage = graphics.CreateImage("images@background", "wrc.png");
        var raceLogicKey = WebDashboardServer.GetDashboardImageCacheKey(1, "images@background");
        var wrcKey = WebDashboardServer.GetDashboardImageCacheKey(2, "images@background");

        capture.RegisterImage(raceLogicKey, raceLogicImage, "/dashboards/modern_raceLogic/images/background.png");
        capture.RegisterImage(wrcKey, wrcImage, "/dashboards/modern_wrc_2/images/background.png");
        graphics.DrawImage(raceLogicImage, 0, 0, 100, 100);
        graphics.DrawImage(wrcImage, 100, 0, 200, 100);

        var commands = capture.GetCommands();
        Assert.Equal(raceLogicKey, Assert.IsType<DrawImageCommand>(commands[0]).ImageKey);
        Assert.Equal(wrcKey, Assert.IsType<DrawImageCommand>(commands[1]).ImageKey);
        Assert.Equal("/dashboards/modern_raceLogic/images/background.png", Assert.IsType<DrawImageCommand>(commands[0]).ImageUrl);
        Assert.Equal("/dashboards/modern_wrc_2/images/background.png", Assert.IsType<DrawImageCommand>(commands[1]).ImageUrl);
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

    private static async Task WaitForClient(WebDashboardServer server)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (server.ClientCount == 1)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException("WebDashboard client did not connect.");
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket socket)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
