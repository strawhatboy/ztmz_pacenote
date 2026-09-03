using System;

namespace ZTMZ.PacenoteTool.AC;

internal sealed record ACRProtonEndpoint(int Port, string Token);

internal static class ACRProtonBridgeRuntime
{
    private static readonly object Sync = new();
    private static ACRProtonEndpoint? _endpoint;
    private static bool _connected;
    private static bool _injected;
    private static string _detail = string.Empty;

    public static void RegisterEndpoint(ACRProtonEndpoint endpoint)
    {
        lock (Sync)
        {
            _endpoint = endpoint;
            _connected = false;
            _injected = false;
            _detail = string.Empty;
        }
    }

    public static void ClearEndpoint(ACRProtonEndpoint endpoint)
    {
        lock (Sync)
        {
            if (_endpoint == endpoint)
            {
                _endpoint = null;
                _connected = false;
                _injected = false;
                _detail = string.Empty;
            }
        }
    }

    public static ACRProtonEndpoint? GetEndpoint()
    {
        lock (Sync)
        {
            return _endpoint;
        }
    }

    public static (bool Connected, bool Injected, string Detail) GetStatus()
    {
        lock (Sync)
        {
            return (_connected, _injected, _detail);
        }
    }

    public static void SetConnected(bool connected)
    {
        lock (Sync)
        {
            _connected = connected;
            if (!connected)
            {
                _injected = false;
            }
        }
    }

    public static void SetRelayStatus(string state, string? detail)
    {
        lock (Sync)
        {
            _detail = string.IsNullOrWhiteSpace(detail) ? state : $"{state}: {detail}";
            if (string.Equals(state, "injected", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "bridge-connected", StringComparison.OrdinalIgnoreCase))
            {
                _injected = true;
            }
        }
    }
}
