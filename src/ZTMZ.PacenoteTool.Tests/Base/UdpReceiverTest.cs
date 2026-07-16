using System.Net;
using System.Net.Sockets;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class UdpReceiverTest
{
    [Fact]
    public async Task ReceivesPacketsAfterAHandlerThrows()
    {
        var port = GetAvailablePort();
        using var receiver = new UdpReceiver();
        using var sender = new UdpClient();
        var messagesReceived = 0;
        var twoMessagesReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        receiver.onNewMessage += (_, _) => throw new InvalidOperationException("Test handler failure");
        receiver.onNewMessage += (_, _) =>
        {
            if (Interlocked.Increment(ref messagesReceived) == 2)
            {
                twoMessagesReceived.TrySetResult();
            }
        };
        receiver.StartListening(IPAddress.Loopback, port);

        await sender.SendAsync(new byte[] { 1 }, new IPEndPoint(IPAddress.Loopback, port));
        await sender.SendAsync(new byte[] { 2 }, new IPEndPoint(IPAddress.Loopback, port));

        await twoMessagesReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, messagesReceived);
    }

    [Fact]
    public void StopListeningReleasesThePort()
    {
        var port = GetAvailablePort();
        using var firstReceiver = new UdpReceiver();
        using var secondReceiver = new UdpReceiver();

        firstReceiver.StartListening(IPAddress.Loopback, port);
        firstReceiver.StopListening();

        secondReceiver.StartListening(IPAddress.Loopback, port);
    }

    [Fact]
    public void ReportsAddressAlreadyInUseAsPortConflict()
    {
        var port = GetAvailablePort();
        using var firstReceiver = new UdpReceiver();
        using var secondReceiver = new UdpReceiver();

        firstReceiver.StartListening(IPAddress.Loopback, port);

        var exception = Assert.Throws<PortAlreadyInUseException>(
            () => secondReceiver.StartListening(IPAddress.Loopback, port));
        Assert.Equal(port, exception.Port);
    }

    [Fact]
    public void FindsPortUsableByReceiver()
    {
        var port = UdpPortFinder.FindAvailablePort(IPAddress.Loopback);
        using var receiver = new UdpReceiver();

        receiver.StartListening(IPAddress.Loopback, port);
    }

    [Fact]
    public void ReportsAvailablePortAsBindable()
    {
        var port = GetAvailablePort();

        var canBind = UdpPortFinder.CanBind(IPAddress.Loopback, port, out var socketError);

        Assert.True(canBind);
        Assert.Equal(SocketError.Success, socketError);
    }

    private static int GetAvailablePort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
