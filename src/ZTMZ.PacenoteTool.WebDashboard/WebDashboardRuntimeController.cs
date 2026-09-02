#nullable enable

using System;
using System.Net.Sockets;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WebDashboard;

public sealed record WebDashboardRuntimeControllerOptions(
    string DashboardPackagesPath,
    string StaticAssetsRoot,
    bool BrowserLuaExecutionEnabled = true,
    bool SupportsNativeOverlay = false);

public sealed class WebDashboardRuntimeController : IRuntimeDashboardController, IDisposable
{
    private readonly object _sync = new();
    private readonly ZTMZPacenoteTool _tool;
    private readonly WebDashboardRuntimeControllerOptions _options;
    private readonly WebDashboardRuntime _runtime;
    private WebDashboardGameSessionBridge? _bridge;
    private RuntimeDashboardStatus _status = new(
        WebDashboardServerState.Stopped.ToString(),
        0,
        string.Empty,
        string.Empty,
        false);
    private bool _disposed;

    public WebDashboardRuntimeController(
        ZTMZPacenoteTool tool,
        WebDashboardRuntimeControllerOptions options,
        WebDashboardRuntime? runtime = null)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runtime = runtime ?? new WebDashboardRuntime();
    }

    public event EventHandler? StatusChanged;

    public RuntimeDashboardStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public RuntimeDashboardDiagnostics Diagnostics
    {
        get
        {
            lock (_sync)
            {
                var diagnostics = _runtime.GetDiagnostics();
                return new RuntimeDashboardDiagnostics(
                    _status,
                    diagnostics.ConnectedClientCount,
                    diagnostics.TargetFrameRate,
                    diagnostics.RenderedFrameCount,
                    diagnostics.LastRenderedFrameUtc);
            }
        }
    }

    public void Configure(bool enabled, int port, int targetFrameRate)
    {
        RuntimeDashboardStatus status;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            status = enabled
                ? Start(port, targetFrameRate)
                : Stop(port);
            _status = status;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reloads package settings and output targets without changing the user's enabled switch.</summary>
    public void Reload()
    {
        RuntimeDashboardStatus status;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_status.IsRunning)
            {
                var port = _status.Port;
                var frameRate = _runtime.GetDiagnostics().TargetFrameRate;
                Stop(port);
                status = Start(port, frameRate);
            }
            else
            {
                status = _status;
            }
            _status = status;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop(_status.Port);
            _runtime.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private RuntimeDashboardStatus Start(int port, int targetFrameRate)
    {
        if (port is < 1024 or > 65535)
        {
            Stop(port);
            return CreateStatus(
                WebDashboardServerState.PortUnavailable,
                port,
                "Port must be between 1024 and 65535.");
        }

        if (_runtime.Status.IsRunning && _runtime.Status.Port == port)
        {
            _runtime.SetTargetFrameRate(targetFrameRate);
            _runtime.SetCanvasResolution(Config.Instance.WebDashboardCanvasResolution);
            return ConvertStatus(_runtime.Status);
        }

        Stop(port);
        try
        {
            _runtime.Start(new WebDashboardRuntimeOptions(
                _options.DashboardPackagesPath,
                _options.StaticAssetsRoot,
                port,
                targetFrameRate,
                _options.BrowserLuaExecutionEnabled,
                _options.SupportsNativeOverlay,
                Config.Instance.WebDashboardCanvasResolution));
            _bridge = new WebDashboardGameSessionBridge(_tool, _runtime);
            return ConvertStatus(_runtime.Status);
        }
        catch (Exception exception)
        {
            _bridge?.Dispose();
            _bridge = null;
            _runtime.Stop();
            return CreateStatus(GetStartFailureState(exception), port, exception.Message);
        }
    }

    private RuntimeDashboardStatus Stop(int port)
    {
        _bridge?.Dispose();
        _bridge = null;
        var statusPort = port == 0 ? _runtime.Status.Port : port;
        var statusUrl = _runtime.Status.Url;
        _runtime.Stop();
        return new RuntimeDashboardStatus(
            WebDashboardServerState.Stopped.ToString(),
            statusPort,
            statusUrl,
            string.Empty,
            false);
    }

    private static RuntimeDashboardStatus ConvertStatus(WebDashboardServerStatus status) =>
        new(
            status.State.ToString(),
            status.Port,
            status.Url,
            status.ErrorMessage,
            status.IsRunning);

    private static RuntimeDashboardStatus CreateStatus(
        WebDashboardServerState state,
        int port,
        string errorMessage) =>
        new(
            state.ToString(),
            port,
            WebDashboardServer.GetDashboardUrl(port),
            errorMessage,
            state == WebDashboardServerState.Running);

    private static WebDashboardServerState GetStartFailureState(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SocketException socketException &&
                socketException.SocketErrorCode is
                    SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                return WebDashboardServerState.PortUnavailable;
            }
        }

        return WebDashboardServerState.Error;
    }
}
