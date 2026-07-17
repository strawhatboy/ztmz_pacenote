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
    private string listenAddress = string.Empty;
    private int listenPort;
    private string lastError = string.Empty;
    private long packetCount;
    private long lastPacketUtcTicks;
    private long lastErrorUtcTicks;
    private int lastPacketSize;

    public event Action<byte[], byte[]> onNewMessage;
    public event Action ListenStarted;

    public bool[] WheelAbnormalDetectedReported = { false, false, false, false };
    public int[] WheelAbnormalDetectedCounter = { 0, 0, 0, 0 };

    public GameData LastMessage { get; private set; }

    public UdpReceiverStatus Status
    {
        get
        {
            lock (lifecycleLock)
            {
                return new UdpReceiverStatus(
                    isInitialized,
                    listenAddress,
                    listenPort,
                    Interlocked.Read(ref packetCount),
                    ToUtcDateTimeOffset(Interlocked.Read(ref lastPacketUtcTicks)),
                    Volatile.Read(ref lastPacketSize),
                    ToUtcDateTimeOffset(Interlocked.Read(ref lastErrorUtcTicks)),
                    lastError);
            }
        }
    }

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
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
            {
                udpClient.Dispose();
                throw new PortAccessDeniedException(
                    $"Windows denied access to UDP port {port} on {ipAddress} when starting the UDP receiver.",
                    port,
                    ex.SocketErrorCode,
                    ex);
            }
            catch
            {
                udpClient.Dispose();
                throw;
            }

            var boundEndpoint = udpClient.Client.LocalEndPoint as IPEndPoint ?? endpoint;
            var cancellation = new CancellationTokenSource();
            client = udpClient;
            receiveCancellation = cancellation;
            lastMessage = null;
            listenAddress = boundEndpoint.Address.ToString();
            listenPort = boundEndpoint.Port;
            lastError = string.Empty;
            Interlocked.Exchange(ref packetCount, 0);
            Interlocked.Exchange(ref lastPacketUtcTicks, 0);
            Interlocked.Exchange(ref lastErrorUtcTicks, 0);
            Interlocked.Exchange(ref lastPacketSize, 0);
            isInitialized = true;
            _ = ReceiveLoopAsync(udpClient, boundEndpoint, cancellation.Token);
            logger.Info("UDP traffic listening started on {0}.", boundEndpoint);
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

                RecordError(ex);
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
                RecordError(ex);
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
            Interlocked.Increment(ref packetCount);
            Interlocked.Exchange(ref lastPacketUtcTicks, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref lastPacketSize, result.Buffer.Length);
            InvokeMessageHandlers(previousMessage, result.Buffer, result.RemoteEndPoint);
        }
    }

    private void RecordError(Exception exception)
    {
        lock (lifecycleLock)
        {
            lastError = $"{exception.GetType().Name}: {exception.Message}";
            Interlocked.Exchange(ref lastErrorUtcTicks, DateTime.UtcNow.Ticks);
        }
    }

    private static DateTimeOffset? ToUtcDateTimeOffset(long ticks)
    {
        return ticks == 0
            ? null
            : new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
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
