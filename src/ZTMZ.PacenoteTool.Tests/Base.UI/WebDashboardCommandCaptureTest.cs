using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
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
        using var clientScriptResponse = await client.GetAsync($"http://127.0.0.1:{port}/dashboard-client.js");
        using var browserLuaScriptResponse = await client.GetAsync($"http://127.0.0.1:{port}/browser-lua-dashboard.js");
        using var fengariResponse = await client.GetAsync($"http://127.0.0.1:{port}/vendor/fengari-web.js");
        using var fontResponse = await client.GetAsync($"http://127.0.0.1:{port}/fonts/WRCCleanRoman.ttf");
        using var boldFontResponse = await client.GetAsync($"http://127.0.0.1:{port}/fonts/WRCCleanBold.ttf");
        dashboardListResponse.EnsureSuccessStatusCode();
        pageResponse.EnsureSuccessStatusCode();
        clientScriptResponse.EnsureSuccessStatusCode();
        browserLuaScriptResponse.EnsureSuccessStatusCode();
        fengariResponse.EnsureSuccessStatusCode();
        fontResponse.EnsureSuccessStatusCode();
        boldFontResponse.EnsureSuccessStatusCode();
        var page = await pageResponse.Content.ReadAsStringAsync();
        var clientScript = await clientScriptResponse.Content.ReadAsStringAsync();
        var browserLuaScript = await browserLuaScriptResponse.Content.ReadAsStringAsync();
        var fengariScript = await fengariResponse.Content.ReadAsStringAsync();

        Assert.True(server.IsRunning);
        Assert.Equal("[]", response);
        Assert.Contains("fullscreen-button", page);
        Assert.Contains("orientation-button", page);
        Assert.Contains("frame-rate", page);
        Assert.Contains("connection-status-text", page);
        Assert.Contains("WRCCleanRoman.ttf", page);
        Assert.Contains("font-weight: 700", page);
        var normalizedPage = page.Replace("\r\n", "\n");
        Assert.Contains(
            "font-family: 'WRC Clean';\n            src: url('/fonts/WRCCleanBold.ttf') format('truetype');\n            font-weight: 400;",
            normalizedPage);
        Assert.Contains(
            "font-family: 'WRC Clean Roman';\n            src: url('/fonts/WRCCleanRoman.ttf') format('truetype');\n            font-weight: 400;",
            normalizedPage);
        Assert.Contains("vendor/fengari-web.js?v=0.1.4", page);
        Assert.Contains("browser-lua-dashboard.js?v=5", page);
        Assert.Contains("dashboard-client.js?v=5", page);
        Assert.Contains("cmd.fontWeight", clientScript);
        Assert.Contains("cmd.baseline", clientScript);
        Assert.Contains("layeredDrawCommands", clientScript);
        Assert.Contains("browserLuaTelemetry", clientScript);
        Assert.Contains("browserLuaDashboardIds", clientScript);
        Assert.Contains("DASHBOARD_LAYER_Z_INDEX_STRIDE", clientScript);
        Assert.Contains("ZtmzBrowserLuaDashboardManager", browserLuaScript);
        Assert.Contains("DASHBOARD_LAYER_Z_INDEX_STRIDE", browserLuaScript);
        Assert.Contains("t.fengari=e()", fengariScript);
        Assert.Contains("dashboard-layer-stack", page);
        Assert.True(dashboardListResponse.Headers.TryGetValues("Cache-Control", out var dashboardCacheControlValues));
        Assert.Contains("no-store", string.Join(",", dashboardCacheControlValues));
        Assert.True(pageResponse.Headers.TryGetValues("Cache-Control", out var cacheControlValues));
        Assert.Contains("no-store", string.Join(",", cacheControlValues));
        Assert.Equal("font/ttf", fontResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("font/ttf", boldFontResponse.Content.Headers.ContentType?.MediaType);
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
    public void CaptureBuildsOrderedStaticAndDynamicLayers()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        var graphics = new GraphicsCommandProxy(capture);
        var brush = graphics.CreateSolidBrush(10, 20, 30, 255);

        graphics.ClearScene();
        capture.BeginDashboardCapture("dashboard-0", staticLayerRefreshRequested: true);
        graphics.BeginStaticLayer();
        graphics.FillRectangle(brush, 10, 20, 110, 120);
        graphics.EndStaticLayer();
        graphics.FillCircle(brush, 40, 50, 20);
        capture.EndDashboardCapture();

        var frame = capture.GetWebDashboardDrawFrame();

        Assert.True(frame.CanUseLayeredRendering);
        Assert.Collection(
            frame.Layers,
            layer =>
            {
                Assert.Equal("scene:static:0", layer.Id);
                Assert.Equal(WebDashboardDrawLayerKind.Static, layer.Kind);
                Assert.IsType<ClearCommand>(Assert.Single(layer.Commands));
            },
            layer =>
            {
                Assert.Equal("dashboard-0:static:1", layer.Id);
                Assert.Equal(WebDashboardDrawLayerKind.Static, layer.Kind);
                Assert.True(layer.StaticContentProvided);
                Assert.IsType<FillRectangleCommand>(Assert.Single(layer.Commands));
            },
            layer =>
            {
                Assert.Equal("dashboard-0:dynamic:2", layer.Id);
                Assert.Equal(WebDashboardDrawLayerKind.Dynamic, layer.Kind);
                Assert.IsType<FillCircleCommand>(Assert.Single(layer.Commands));
            });
    }

    [Fact]
    public async Task LayeredFramesSendSnapshotsForNewScenesAndDeltasForDynamicUpdates()
    {
        var port = GetAvailablePort();
        using var server = new WebDashboardServer { Port = port };
        server.Initialize(new List<Dashboard>());
        server.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
        await WaitForClient(server);

        var initialFrame = CreateLayeredFrame(
            staticContentProvided: true,
            staticColor: 0xFF102030,
            dynamicValue: 10);
        var initialMessageTask = ReceiveTextAsync(socket);
        await server.BroadcastDrawFrameAsync(initialFrame);
        var initialMessage = await initialMessageTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"type\":\"layeredDrawCommands\"", initialMessage);
        Assert.Contains("\"sceneRevision\":1", initialMessage);
        Assert.Contains("\"layers\"", initialMessage);
        Assert.Contains("\"kind\":\"static\"", initialMessage);
        Assert.Contains("\"kind\":\"dynamic\"", initialMessage);

        var dynamicFrame = CreateLayeredFrame(
            staticContentProvided: false,
            staticColor: 0,
            dynamicValue: 20);
        var dynamicMessageTask = ReceiveTextAsync(socket);
        await server.BroadcastDrawFrameAsync(dynamicFrame);
        var dynamicMessage = await dynamicMessageTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"sceneRevision\":1", dynamicMessage);
        Assert.Contains("\"dynamicLayers\"", dynamicMessage);
        Assert.DoesNotContain("\"layers\"", dynamicMessage);
        Assert.Contains("\"text\":\"20\"", dynamicMessage);

        var changedStaticFrame = CreateLayeredFrame(
            staticContentProvided: true,
            staticColor: 0xFF405060,
            dynamicValue: 30);
        var changedStaticMessageTask = ReceiveTextAsync(socket);
        await server.BroadcastDrawFrameAsync(changedStaticFrame);
        var changedStaticMessage = await changedStaticMessageTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"sceneRevision\":2", changedStaticMessage);
        Assert.Contains("\"layers\"", changedStaticMessage);
        Assert.Contains("\"color\":4282404960", changedStaticMessage);
    }

    [Fact]
    public async Task LayeredFrameSendsSnapshotToClientThatConnectsAfterTheSceneExists()
    {
        var port = GetAvailablePort();
        using var server = new WebDashboardServer { Port = port };
        server.Initialize(new List<Dashboard>());
        server.Start();

        using var firstSocket = new ClientWebSocket();
        await firstSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
        await WaitForClient(server);

        var initialMessageTask = ReceiveTextAsync(firstSocket);
        await server.BroadcastDrawFrameAsync(CreateLayeredFrame(true, 0xFF102030, 10));
        await initialMessageTask.WaitAsync(TimeSpan.FromSeconds(5));

        using var secondSocket = new ClientWebSocket();
        await secondSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
        await WaitForClientCount(server, 2);

        var firstDeltaTask = ReceiveTextAsync(firstSocket);
        var secondSnapshotTask = ReceiveTextAsync(secondSocket);
        await server.BroadcastDrawFrameAsync(CreateLayeredFrame(false, 0, 20));

        var firstDelta = await firstDeltaTask.WaitAsync(TimeSpan.FromSeconds(5));
        var secondSnapshot = await secondSnapshotTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"dynamicLayers\"", firstDelta);
        Assert.Contains("\"layers\"", secondSnapshot);
        Assert.Contains("\"sceneRevision\":1", secondSnapshot);
    }

    [Fact]
    public async Task BrowserLuaTelemetryUsesCapabilityHandshakeSnapshotsAndDeltas()
    {
        var port = GetAvailablePort();
        using var server = new WebDashboardServer { Port = port };
        server.Initialize(new List<Dashboard>());
        server.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
        await WaitForClient(server);
        Assert.False(server.CanExecuteBrowserLuaDashboards);

        await SendClientControlMessage(socket, """{"type":"browserLuaCapabilities","supported":true}""");
        await WaitForBrowserLuaCapability(server, expected: true);

        var scene = new WebDashboardBrowserLuaScene(
            new List<WebDashboardBrowserLuaDashboard>
            {
                new(
                    id: 4,
                    order: 2,
                    configurationRevision: 7,
                    settings: new Dictionary<string, object>
                    {
                        ["dashboards.settings.size"] = 0.27,
                        ["dashboards.settings.enabled"] = true
                    })
            },
            new WebDashboardBrowserLuaConfig(
                hudChromaKeyMode: false,
                hudShiftGreenZone: 0.6f,
                hudShiftYellowZone: 0.75f,
                hudShiftRedZone: 0.9f,
                frameRate: 75));
        var context = new GameContext
        {
            TrackName = "Stage",
            CarName = "Car",
            CarClass = "RC1",
            AudioPackage = "Package",
            PacenoteType = "Default",
            ScriptAuthor = "Author",
            LocalReplayValid = true,
            LocalReplayFinishTime = 98.5f,
            LocalReplay = new Replay { checkpoints = 10, track_length = 1000 },
            LocalReplayDetailsPerTimes = new List<ReplayDetailsPerTime>
            {
                new() { time = 0, distance = 0 },
                new() { time = 10, distance = 100 }
            },
            GameState = 3,
            LocalReplayComparison = new ReplayComparisonSnapshot(
                valid: true,
                currentCompletionRate: 0.3f,
                bestTimeAtCurrentProgress: 29.5f,
                deltaAtCurrentProgress: -0.5f,
                bestCompletionRateAtCurrentTime: 0.31f,
                bestDistanceAtCurrentTime: 310),
            GetDeltaByDistanceAndTime = (_, _) => -0.5f
        };
        var initialData = new GameData
        {
            Brake = 0.25f,
            Clutch = 0.1f,
            CompletionRate = 0.3f,
            Gear = 3,
            HandBrakeValid = true,
            LapDistance = 100,
            LapTime = float.NaN,
            MaxGears = 6,
            MaxRPM = 7000,
            RPM = 5200,
            ShiftLightsRPMStart = 6000,
            ShiftLightsRPMEnd = 6800,
            ShiftLightsRPMValid = true,
            Speed = float.PositiveInfinity,
            Steering = 0.2f,
            Throttle = 0.8f,
            TrackLength = float.NegativeInfinity
        };

        var snapshotTask = ReceiveTextAsync(socket);
        await server.BroadcastBrowserLuaTelemetryAsync(scene, initialData, context);
        var snapshot = await snapshotTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"type\":\"browserLuaTelemetry\"", snapshot);
        Assert.Contains("\"sceneRevision\":1", snapshot);
        Assert.Contains("\"dashboards\"", snapshot);
        Assert.Contains("\"id\":4", snapshot);
        Assert.Contains("\"configurationRevision\":7", snapshot);
        Assert.Contains("\"TrackName\":\"Stage\"", snapshot);
        Assert.Contains("\"ShiftLightsRPMValid\":true", snapshot);
        Assert.Contains("\"Clutch\":0.1", snapshot);
        Assert.Contains("\"CompletionRate\":0.3", snapshot);
        Assert.Contains("\"BestReplayTimeline\":[{\"Time\":0.0,\"Distance\":0.0}", snapshot);
        Assert.Contains("\"LocalReplayFinishTime\":98.5", snapshot);
        Assert.Contains("\"checkpoints\":10", snapshot);
        Assert.Contains("\"LocalReplayDelta\":-0.5", snapshot);
        Assert.Contains("\"FrameRate\":75", snapshot);
        Assert.DoesNotContain("\"Id\":4", snapshot);
        using (var snapshotDocument = JsonDocument.Parse(snapshot))
        {
            var snapshotTelemetry = snapshotDocument.RootElement.GetProperty("data");
            Assert.Equal(0.0f, snapshotTelemetry.GetProperty("LapTime").GetSingle());
            Assert.Equal(0.0f, snapshotTelemetry.GetProperty("Speed").GetSingle());
            Assert.Equal(0.0f, snapshotTelemetry.GetProperty("TrackLength").GetSingle());
            var comparison = snapshotDocument.RootElement
                .GetProperty("context")
                .GetProperty("LocalReplayComparison");
            Assert.True(comparison.GetProperty("Valid").GetBoolean());
            Assert.InRange(comparison.GetProperty("CurrentCompletionRate").GetSingle(), 0.299f, 0.301f);
            Assert.InRange(comparison.GetProperty("DeltaAtCurrentProgress").GetSingle(), -0.501f, -0.499f);
            Assert.InRange(comparison.GetProperty("BestCompletionRateAtCurrentTime").GetSingle(), 0.309f, 0.311f);
        }

        var deltaTask = ReceiveTextAsync(socket);
        initialData.Speed = 92;
        await server.BroadcastBrowserLuaTelemetryAsync(scene, initialData, context);
        var delta = await deltaTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"type\":\"browserLuaTelemetry\"", delta);
        Assert.Contains("\"sceneRevision\":1", delta);
        Assert.Contains("\"Speed\":92", delta);
        Assert.Contains("\"TrackName\":\"Stage\"", delta);
        Assert.DoesNotContain("\"dashboards\"", delta);
        Assert.DoesNotContain("\"BestReplayTimeline\"", delta);

        var compatibilityTask = ReceiveTextAsync(socket);
        await server.BroadcastDrawFrameAsync(new WebDashboardDrawFrame(
            Array.Empty<WebDashboardDrawLayer>(),
            new DrawingCommand[] { new ClearCommand() },
            hasExplicitStaticLayers: false));
        var compatibilityFrame = await compatibilityTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("\"type\":\"drawCommands\"", compatibilityFrame);

        var resumedSnapshotTask = ReceiveTextAsync(socket);
        initialData.Speed = 94;
        await server.BroadcastBrowserLuaTelemetryAsync(scene, initialData, context);
        var resumedSnapshot = await resumedSnapshotTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("\"dashboards\"", resumedSnapshot);
        Assert.Contains("\"Speed\":94", resumedSnapshot);

        await SendClientControlMessage(socket, """{"type":"browserLuaFallback","reason":"test fallback"}""");
        await WaitForBrowserLuaCapability(server, expected: false);
    }

    [Fact]
    public async Task HybridLayeredFramesKeepTheBrowserLuaSceneUntilCompatibilityFallback()
    {
        var port = GetAvailablePort();
        using var server = new WebDashboardServer { Port = port };
        server.Initialize(new List<Dashboard>());
        server.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
        await WaitForClient(server);
        await SendClientControlMessage(socket, """{"type":"browserLuaCapabilities","supported":true}""");
        await WaitForBrowserLuaCapability(server, expected: true);

        var browserLuaScene = new WebDashboardBrowserLuaScene(
            new List<WebDashboardBrowserLuaDashboard>
            {
                new(
                    id: 4,
                    order: 4,
                    configurationRevision: 1,
                    settings: new Dictionary<string, object>())
            },
            new WebDashboardBrowserLuaConfig(
                hudChromaKeyMode: false,
                hudShiftGreenZone: 0.6f,
                hudShiftYellowZone: 0.75f,
                hudShiftRedZone: 0.9f));
        var data = new GameData { Speed = 85 };
        var context = new GameContext { GameState = 3 };

        var initialBrowserSnapshotTask = ReceiveTextAsync(socket);
        await server.BroadcastBrowserLuaTelemetryAsync(browserLuaScene, data, context);
        var initialBrowserSnapshot = await initialBrowserSnapshotTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("\"dashboards\"", initialBrowserSnapshot);
        Assert.Contains("\"hybrid\":false", initialBrowserSnapshot);

        var hybridCommandTask = ReceiveTextAsync(socket);
        await server.BroadcastDrawFrameAsync(
            CreateLayeredFrame(staticContentProvided: true, staticColor: 0xFF102030, dynamicValue: 10),
            new[] { 4 });
        var hybridCommand = await hybridCommandTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("\"type\":\"layeredDrawCommands\"", hybridCommand);
        Assert.Contains("\"browserLuaDashboardIds\":[4]", hybridCommand);

        var hybridBrowserDeltaTask = ReceiveTextAsync(socket);
        data.Speed = 90;
        await server.BroadcastBrowserLuaTelemetryAsync(browserLuaScene, data, context, isHybrid: true);
        var hybridBrowserDelta = await hybridBrowserDeltaTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("\"hybrid\":true", hybridBrowserDelta);
        Assert.Contains("\"Speed\":90", hybridBrowserDelta);
        Assert.DoesNotContain("\"dashboards\"", hybridBrowserDelta);

        var compatibilityCommandTask = ReceiveTextAsync(socket);
        await server.BroadcastDrawFrameAsync(
            CreateLayeredFrame(staticContentProvided: false, staticColor: 0, dynamicValue: 20));
        var compatibilityCommand = await compatibilityCommandTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("\"browserLuaDashboardIds\":[]", compatibilityCommand);

        var resumedBrowserSnapshotTask = ReceiveTextAsync(socket);
        data.Speed = 95;
        await server.BroadcastBrowserLuaTelemetryAsync(browserLuaScene, data, context);
        var resumedBrowserSnapshot = await resumedBrowserSnapshotTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("\"hybrid\":false", resumedBrowserSnapshot);
        Assert.Contains("\"dashboards\"", resumedBrowserSnapshot);
        Assert.Contains("\"Speed\":95", resumedBrowserSnapshot);
    }

    [Fact]
    public async Task UnsafeStaticLayerFallsBackToCompleteFrameProtocol()
    {
        var port = GetAvailablePort();
        using var server = new WebDashboardServer { Port = port };
        server.Initialize(new List<Dashboard>());
        server.Start();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
        await WaitForClient(server);

        var frame = CreateLayeredFrame(true, 0xFF102030, 10);
        var unsafeStaticLayer = new WebDashboardDrawLayer(
            "dashboard-0:static:1",
            WebDashboardDrawLayerKind.Static,
            new DrawingCommand[] { new RotateCommand { Angle = 1 } },
            staticContentProvided: true);
        var unsafeFrame = new WebDashboardDrawFrame(
            new[] { frame.Layers[0], unsafeStaticLayer, frame.Layers[2] },
            new DrawingCommand[]
            {
                new ClearCommand(),
                new RotateCommand { Angle = 1 },
                new DrawTextCommand { Text = "10", Color = 0xFFFFFFFF }
            },
            hasExplicitStaticLayers: true);

        var messageTask = ReceiveTextAsync(socket);
        await server.BroadcastDrawFrameAsync(unsafeFrame);
        var message = await messageTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"type\":\"drawCommands\"", message);
        Assert.DoesNotContain("\"type\":\"layeredDrawCommands\"", message);
        Assert.Contains("\"type\":\"rotate\"", message);
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
    public void ProxyCapturesFontStyleAndDirectWriteLayoutMetrics()
    {
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
        using var graphics = new GraphicsCommandProxy(capture);
        var brush = graphics.CreateSolidBrush(255, 255, 255);
        var font = graphics.CreateCustomFont("WRC Clean Roman", 32, bold: true, italic: true);

        graphics.DrawText(font, brush, 100, 200, "123");

        var command = Assert.IsType<DrawTextCommand>(Assert.Single(capture.GetCommands()));
        Assert.Equal(700, command.FontWeight);
        Assert.Equal("italic", command.FontStyle);
        Assert.True(command.TextWidth > 0);
        Assert.True(command.TextHeight > 0);
        Assert.True(command.Baseline > 0);
        Assert.True(command.LineHeight > 0);
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

    private static async Task WaitForClientCount(WebDashboardServer server, int expectedCount)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (server.ClientCount == expectedCount)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException($"WebDashboard did not reach {expectedCount} clients.");
    }

    private static WebDashboardDrawFrame CreateLayeredFrame(
        bool staticContentProvided,
        uint staticColor,
        int dynamicValue)
    {
        var staticCommands = staticContentProvided
            ? new DrawingCommand[]
            {
                new FillRectangleCommand
                {
                    X = 0,
                    Y = 0,
                    Width = 100,
                    Height = 100,
                    Color = staticColor
                }
            }
            : Array.Empty<DrawingCommand>();
        var dynamicCommands = new DrawingCommand[]
        {
            new DrawTextCommand
            {
                X = 10,
                Y = 20,
                Text = dynamicValue.ToString(),
                Color = 0xFFFFFFFF
            }
        };
        var layers = new WebDashboardDrawLayer[]
        {
            new(
                "scene:static:0",
                WebDashboardDrawLayerKind.Static,
                new DrawingCommand[] { new ClearCommand() },
                staticContentProvided: true),
            new(
                "dashboard-0:static:1",
                WebDashboardDrawLayerKind.Static,
                staticCommands,
                staticContentProvided),
            new(
                "dashboard-0:dynamic:2",
                WebDashboardDrawLayerKind.Dynamic,
                dynamicCommands)
        };

        return new WebDashboardDrawFrame(
            layers,
            layers.SelectMany(layer => layer.Commands).ToList(),
            hasExplicitStaticLayers: true);
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

    private static Task SendClientControlMessage(ClientWebSocket socket, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);
    }

    private static async Task WaitForBrowserLuaCapability(WebDashboardServer server, bool expected)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (server.CanExecuteBrowserLuaDashboards == expected)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException(
            $"WebDashboard browser Lua capability did not become {expected}.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
