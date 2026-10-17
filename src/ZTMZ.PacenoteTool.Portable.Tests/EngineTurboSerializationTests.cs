using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.WebDashboard;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class EngineTurboSerializationTests
{
    [Theory]
    [InlineData(false, "unknown")]
    [InlineData(true, "unknown")]
    [InlineData(false, "gauge")]
    [InlineData(true, "absolute")]
    public async Task BothWirePathsPreserveReferenceZeroFalseAndOmitUnavailableFields(bool browserLua, string reference)
    {
        using var server = new WebDashboardServer { Port = 0, LoopbackOnly = true };
        server.Initialize([], AppContext.BaseDirectory, AppContext.BaseDirectory);
        server.Start();
        var now = DateTimeOffset.UtcNow;
        var data = new GameData
        {
            Engine = new("test", now) { CoolantTemperatureCelsius = 0, OilPressureWarning = false },
            Turbo = new("test", now) { PressureBar = 0, PressureReference = reference }
        };
        using var scene = await Receive(server, data, browserLua);
        var wire = scene.RootElement.GetProperty("data");
        var engine = wire.GetProperty("Engine");
        Assert.Equal(0, engine.GetProperty("CoolantTemperatureCelsius").GetSingle());
        Assert.False(engine.GetProperty("OilPressureWarning").GetBoolean());
        Assert.Equal(now, engine.GetProperty("CapturedAtUtc").GetDateTimeOffset());
        Assert.False(engine.TryGetProperty("OilTemperatureCelsius", out _));
        Assert.False(engine.TryGetProperty("EngineWarning", out _));
        Assert.Equal(reference, wire.GetProperty("Turbo").GetProperty("PressureReference").GetString());
        Assert.Equal(0, wire.GetProperty("Turbo").GetProperty("PressureBar").GetSingle());
        using var empty = await Receive(server, default, browserLua);
        Assert.False(empty.RootElement.GetProperty("data").TryGetProperty("Engine", out _));
        Assert.False(empty.RootElement.GetProperty("data").TryGetProperty("Turbo", out _));
    }

    private static async Task<JsonDocument> Receive(WebDashboardServer server, GameData data, bool browserLua)
    {
        using var socket = new ClientWebSocket();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/ws"), deadline.Token);
        if (browserLua)
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"browserLuaCapabilities\",\"supported\":true}"),
                WebSocketMessageType.Text, true, deadline.Token);
        var publisher = Task.Run(async () =>
        {
            try
            {
                while (!deadline.IsCancellationRequested)
                {
                    if (browserLua)
                        await server.BroadcastBrowserLuaTelemetryAsync(new([], new(false, .7f, .8f, .9f)), data, new());
                    else await server.BroadcastGameData(data, new());
                    await Task.Delay(20, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
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
        finally { deadline.Cancel(); await publisher; socket.Abort(); }
    }
}
