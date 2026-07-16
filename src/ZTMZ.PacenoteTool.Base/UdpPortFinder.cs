using System;
using System.Net;
using System.Net.Sockets;

namespace ZTMZ.PacenoteTool.Base;

public static class UdpPortFinder
{
    private const int FirstRecommendedPort = 30000;
    private const int LastRecommendedPort = 30999;

    public static int FindAvailablePort(IPAddress ipAddress)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        for (var port = FirstRecommendedPort; port <= LastRecommendedPort; port++)
        {
            using var client = new UdpClient(ipAddress.AddressFamily);
            client.Client.ExclusiveAddressUse = true;

            try
            {
                client.Client.Bind(new IPEndPoint(ipAddress, port));
                return port;
            }
            catch (SocketException)
            {
                // Try the next candidate. A port can be occupied or reserved by Windows.
            }
        }

        throw new InvalidOperationException(
            $"No available UDP port was found for {ipAddress} in the recommended range {FirstRecommendedPort}-{LastRecommendedPort}.");
    }

    public static bool CanBind(IPAddress ipAddress, int port, out SocketError socketError)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        using var client = new UdpClient(ipAddress.AddressFamily);
        client.Client.ExclusiveAddressUse = true;

        try
        {
            client.Client.Bind(new IPEndPoint(ipAddress, port));
            socketError = SocketError.Success;
            return true;
        }
        catch (SocketException exception)
        {
            socketError = exception.SocketErrorCode;
            return false;
        }
    }
}
