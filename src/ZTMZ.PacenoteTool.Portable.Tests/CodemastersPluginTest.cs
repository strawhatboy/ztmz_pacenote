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
            var packet = CreateDirtTelemetryPacket(
                speedMetersPerSecond: 34.5f,
                gear: 4,
                rpm: 6200,
                lapDistance: 1234.5f);
            await sender.SendAsync(
                packet,
                new IPEndPoint(IPAddress.Loopback, listener.Status.Port));

            var sample = await packetReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(reader.UdpStatus.PacketCount > 0);
            Assert.Equal(124.2f, sample.Speed, precision: 1);
            Assert.Equal(4f, sample.Gear);
            Assert.Equal(6200f, sample.RPM);
            Assert.Equal(1234.5f, sample.LapDistance);
        }
        finally
        {
            reader.Uninitialize(game);
        }
    }

    private static byte[] CreateDirtTelemetryPacket(
        float speedMetersPerSecond,
        float gear,
        float rpm,
        float lapDistance)
    {
        var packet = new byte[264];
        WriteSingle(packet, 0, 15.0f);
        WriteSingle(packet, 4, 12.5f);
        WriteSingle(packet, 8, lapDistance);
        WriteSingle(packet, 12, 0.25f);
        WriteSingle(packet, 28, speedMetersPerSecond);
        WriteSingle(packet, 33 << 2, gear);
        WriteSingle(packet, 37 << 2, rpm / 10f);
        WriteSingle(packet, 63 << 2, 800f);
        WriteSingle(packet, 64 << 2, 90f);
        WriteSingle(packet, 65 << 2, 6f);
        return packet;
    }

    private static void WriteSingle(byte[] packet, int offset, float value)
    {
        BitConverter.GetBytes(value).CopyTo(packet, offset);
    }
}
