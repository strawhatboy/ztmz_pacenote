using System.Net;
using System.Net.Sockets;
using NLog;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class CodemastersPluginTest
{
    [Fact]
    public void Loader_CreatesAllCodemastersGameProvidersFromPortableAssembly()
    {
        var games = GamePluginLoader.Load(
            [typeof(DirtRally2).Assembly.Location],
            LogManager.GetCurrentClassLogger());

        var expectedGameNames = new[]
        {
            DirtRally.GameName,
            DirtRally2.GameName,
            WRC.GameName
        }.Order().ToArray();

        Assert.Equal(
            expectedGameNames,
            games.Select(game => game.Name).Order().ToArray());
    }

    [Fact]
    public async Task DirtRally2Reader_ReceivesUdpPacketOnPortableRuntime()
    {
        var game = new DirtRally2
        {
            GameConfigurations = new Dictionary<string, IGameConfig>
            {
                [UdpGameConfig.Name] = new UdpGameConfig
                {
                    IPAddress = IPAddress.Loopback.ToString(),
                    Port = 0
                }
            }
        };
        var reader = Assert.IsType<DirtGameDataReader>(game.GameDataReader);
        var packetReceived = new TaskCompletionSource<GameData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.onNewGameData += (_, current) => packetReceived.TrySetResult(current);

        try
        {
            Assert.True(reader.Initialize(game));
            var listener = Assert.Single(reader.GetUdpListenerDiagnostics());
            Assert.True(listener.Status.IsListening);
            Assert.True(listener.Status.Port > 0);

            using var sender = new UdpClient(AddressFamily.InterNetwork);
            await sender.SendAsync(
                new byte[264],
                new IPEndPoint(IPAddress.Loopback, listener.Status.Port));

            await packetReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(reader.UdpStatus.PacketCount > 0);
        }
        finally
        {
            reader.Uninitialize(game);
        }
    }
}
