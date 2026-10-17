using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Portable.Tests;

[CollectionDefinition("Dashboard localization", DisableParallelization = true)]
public sealed class DashboardLocalizationCollection;

[Collection("Dashboard localization")]
public sealed class DashboardLocalizationTests
{
    [Fact]
    public async Task LanguageChangesPublishNewSceneAndDeltasRetainIt()
    {
        var loader = I18NLoader.Instance;
        var originalCulture = loader.CurrentCulture;
        var originalName = loader.CurrentCultureName;
        try
        {
            loader.CurrentCultureName = "zh-cn";
            loader.CurrentCulture = new Dictionary<string, string>
            {
                ["dashboards.liquidGlass.labels.coolant"] = "水温",
                ["dashboard.throttle"] = "油门",
                ["overlay.track"] = "赛道",
                ["ui.tb_gamestate"] = "比赛状态",
                ["ui.unrelated"] = "not needed by dashboards"
            };
            var chinese = CreateConfig();
            Assert.Same(chinese.Translations, CreateConfig().Translations);
            Assert.DoesNotContain("ui.unrelated", chinese.Translations.Keys);
            using var server = new WebDashboardServer { Port = 0, LoopbackOnly = true };
            server.Initialize([], AppContext.BaseDirectory, AppContext.BaseDirectory);
            server.Start();
            using var socket = new ClientWebSocket();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/ws"), deadline.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"browserLuaCapabilities\",\"supported\":true}"),
                WebSocketMessageType.Text, true, deadline.Token);

            async Task<JsonDocument> Receive(WebDashboardBrowserLuaConfig config)
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                var publisher = Task.Run(async () =>
                {
                    try
                    {
                        while (!stop.IsCancellationRequested)
                        {
                            await server.BroadcastBrowserLuaTelemetryAsync(new([], config), new(), new());
                            await Task.Delay(30, stop.Token);
                        }
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                });
                try
                {
                    using var bytes = new MemoryStream();
                    var buffer = new byte[4096];
                    WebSocketReceiveResult part;
                    do
                    {
                        part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
                        bytes.Write(buffer, 0, part.Count);
                    } while (!part.EndOfMessage);
                    return JsonDocument.Parse(bytes.ToArray());
                }
                finally { stop.Cancel(); await publisher; }
            }

            using var initial = await Receive(chinese);
            var revision = initial.RootElement.GetProperty("sceneRevision").GetInt64();
            var configWire = initial.RootElement.GetProperty("config");
            Assert.Equal("zh-cn", configWire.GetProperty("CultureName").GetString());
            Assert.Equal("水温", configWire.GetProperty("Translations").GetProperty("dashboards.liquidGlass.labels.coolant").GetString());
            using var delta = await Receive(CreateConfig());
            Assert.Equal(revision, delta.RootElement.GetProperty("sceneRevision").GetInt64());
            Assert.False(delta.RootElement.TryGetProperty("config", out _));

            loader.CurrentCultureName = "en-us";
            loader.CurrentCulture = new Dictionary<string, string> { ["dashboards.liquidGlass.labels.coolant"] = "COOLANT" };
            var english = CreateConfig();
            Assert.NotSame(chinese.Translations, english.Translations);
            Assert.Equal("水温", chinese.Translations["dashboards.liquidGlass.labels.coolant"]);
            // Drain any already queued frames until the new scene arrives.
            while (true)
            {
                using var changed = await Receive(english);
                if (changed.RootElement.GetProperty("sceneRevision").GetInt64() == revision) continue;
                var wire = changed.RootElement.GetProperty("config");
                Assert.Equal("en-us", wire.GetProperty("CultureName").GetString());
                Assert.Equal("COOLANT", wire.GetProperty("Translations").GetProperty("dashboards.liquidGlass.labels.coolant").GetString());
                break;
            }
            socket.Abort();
        }
        finally { loader.CurrentCulture = originalCulture; loader.CurrentCultureName = originalName; }
    }

    private static WebDashboardBrowserLuaConfig CreateConfig() => new(false, .7f, .8f, .9f);
}
