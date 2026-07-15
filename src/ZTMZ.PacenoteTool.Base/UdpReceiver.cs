using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base;

public class UdpReceiver : IDisposable
{
    private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly object lifecycleLock = new();

    private UdpClient client;
    private CancellationTokenSource receiveCancellation;
    private bool isInitialized;
    private byte[] lastMessage;

    public event Action<byte[], byte[]> onNewMessage;
    public event Action ListenStarted;

    public bool[] WheelAbnormalDetectedReported = { false, false, false, false };
    public int[] WheelAbnormalDetectedCounter = { 0, 0, 0, 0 };

    public GameData LastMessage { get; private set; }

    public void ResetWheelStatus()
    {
        WheelAbnormalDetectedReported = new[] { false, false, false, false };
        WheelAbnormalDetectedCounter = new[] { 0, 0, 0, 0 };
    }

    public void StartListening(int port)
    {
        StartListening(IPAddress.Loopback, port);
    }

    public void StartListening(IPAddress ipAddress, int port)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        lock (lifecycleLock)
        {
            if (isInitialized)
            {
                return;
            }

            var endpoint = new IPEndPoint(ipAddress, port);
            var udpClient = new UdpClient(ipAddress.AddressFamily);
            udpClient.Client.ExclusiveAddressUse = true;

            logger.Info("Trying to listen for UDP traffic on {0}", endpoint);
            try
            {
                udpClient.Client.Bind(endpoint);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                udpClient.Dispose();
                throw new PortAlreadyInUseException(
                    $"Port {port} on {ipAddress} is already in use when starting the UDP receiver.",
                    port);
            }
            catch
            {
                udpClient.Dispose();
                throw;
            }

            var cancellation = new CancellationTokenSource();
            client = udpClient;
            receiveCancellation = cancellation;
            lastMessage = null;
            isInitialized = true;
            _ = ReceiveLoopAsync(udpClient, endpoint, cancellation.Token);
            logger.Info("UDP traffic listening started on {0}.", endpoint);
        }

        InvokeListenStartedHandlers();
    }

    public void StopListening()
    {
        UdpClient udpClient;
        CancellationTokenSource cancellation;

        lock (lifecycleLock)
        {
            if (!isInitialized && client == null)
            {
                return;
            }

            isInitialized = false;
            udpClient = client;
            cancellation = receiveCancellation;
            client = null;
            receiveCancellation = null;
            lastMessage = null;
        }

        cancellation?.Cancel();
        udpClient?.Dispose();
        cancellation?.Dispose();
        logger.Info("UDP traffic listening stopped.");
    }

    private async Task ReceiveLoopAsync(UdpClient udpClient, IPEndPoint localEndpoint, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                logger.Error(ex,
                    "UDP receive failed on {0} with socket error {1}; the receiver will retry.",
                    localEndpoint,
                    ex.SocketErrorCode);

                try
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Unexpected UDP receive failure on {0}; the receiver will retry.", localEndpoint);

                try
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            if (result.Buffer.Length == 0)
            {
                continue;
            }

            var previousMessage = lastMessage;
            lastMessage = result.Buffer;
            InvokeMessageHandlers(previousMessage, result.Buffer, result.RemoteEndPoint);
        }
    }

    private void InvokeMessageHandlers(byte[] previousMessage, byte[] message, IPEndPoint remoteEndpoint)
    {
        var handlers = onNewMessage;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<byte[], byte[]> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(previousMessage, message);
            }
            catch (Exception ex)
            {
                logger.Error(ex,
                    "A UDP packet handler failed for a {0}-byte datagram from {1}; reception will continue.",
                    message.Length,
                    remoteEndpoint);
            }
        }
    }

    private void InvokeListenStartedHandlers()
    {
        var handlers = ListenStarted;
        if (handlers == null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "A UDP ListenStarted handler failed.");
            }
        }
    }

    public void Dispose()
    {
        StopListening();
        GC.SuppressFinalize(this);
    }
}
