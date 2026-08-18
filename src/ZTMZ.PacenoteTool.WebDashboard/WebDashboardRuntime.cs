#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.WebDashboard;

public sealed record WebDashboardRuntimeOptions(
    string DashboardPackagesPath,
    string StaticAssetsRoot,
    int Port,
    int FrameRate,
    bool BrowserLuaExecutionEnabled = true,
    bool SupportsNativeOverlay = false);

public sealed class WebDashboardRuntime : IDisposable
{
    private readonly NLog.Logger _logger;
    private readonly WebDashboardCatalog _catalog;
    private WebDashboardServer? _server;
    private IReadOnlyList<WebDashboardDefinition> _dashboards = [];
    private int _targetFrameRate = Config.WebDashboardFrameRateDefault;
    private long _frameCount;
    private DateTimeOffset? _lastFrameUtc;

    public WebDashboardRuntime(NLog.Logger? logger = null)
    {
        _logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
        _catalog = new WebDashboardCatalog(_logger);
    }

    public WebDashboardServerStatus Status { get; private set; } = new(
        WebDashboardServerState.Stopped,
        0,
        string.Empty);

    public IReadOnlyList<WebDashboardDefinition> Dashboards => _dashboards;

    public int ClientCount => _server?.ClientCount ?? 0;

    public void SetTargetFrameRate(int frameRate)
    {
        _targetFrameRate = Math.Clamp(
            frameRate,
            Config.WebDashboardFrameRateMinimum,
            Config.WebDashboardFrameRateMaximum);
    }

    public void Start(WebDashboardRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Stop();

        _dashboards = _catalog.Load(
            options.DashboardPackagesPath,
            options.SupportsNativeOverlay);
        var server = new WebDashboardServer
        {
            Port = options.Port,
            BrowserLuaExecutionEnabled = options.BrowserLuaExecutionEnabled
        };
        server.Initialize(_dashboards, options.DashboardPackagesPath, options.StaticAssetsRoot);
        server.Start();
        _server = server;
        SetTargetFrameRate(options.FrameRate);
        _frameCount = 0;
        _lastFrameUtc = null;
        Status = new WebDashboardServerStatus(
            WebDashboardServerState.Running,
            options.Port,
            WebDashboardServer.GetDashboardUrl(options.Port));
    }

    public async Task PublishTelemetryAsync(GameData data, GameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var server = _server;
        if (server == null || !server.IsRunning || server.ClientCount == 0)
        {
            return;
        }

        var enabledDashboards = _dashboards
            .Select((dashboard, index) => new { dashboard, index })
            .Where(item => item.dashboard.SupportsWebDashboardClientLua
                && item.dashboard.GetOutputTarget().Includes(DashboardOutputTarget.Web))
            .Select(item => new WebDashboardBrowserLuaDashboard(
                item.index,
                item.index,
                0,
                item.dashboard.GetSettings()))
            .ToList();
        if (enabledDashboards.Count == 0)
        {
            return;
        }

        var scene = new WebDashboardBrowserLuaScene(
            enabledDashboards,
            new WebDashboardBrowserLuaConfig(
                Config.Instance.HudChromaKeyMode,
                Config.Instance.HudShiftGreenZone,
                Config.Instance.HudShiftYellowZone,
                Config.Instance.HudShiftRedZone,
                _targetFrameRate));
        await server.BroadcastBrowserLuaTelemetryAsync(scene, data, context);
        System.Threading.Interlocked.Increment(ref _frameCount);
        _lastFrameUtc = DateTimeOffset.UtcNow;
    }

    public void PublishTelemetry(GameData data, GameContext context)
    {
        _ = PublishTelemetrySafelyAsync(data, context);
    }

    public WebDashboardRuntimeDiagnostics GetDiagnostics()
    {
        return new WebDashboardRuntimeDiagnostics(
            Status,
            ClientCount,
            _targetFrameRate,
            System.Threading.Interlocked.Read(ref _frameCount),
            _lastFrameUtc);
    }

    public void Stop()
    {
        var server = _server;
        _server = null;
        if (server != null)
        {
            server.Dispose();
        }

        if (Status.State != WebDashboardServerState.Stopped)
        {
            Status = new WebDashboardServerStatus(
                WebDashboardServerState.Stopped,
                Status.Port,
                Status.Url);
        }
    }

    public void Dispose() => Stop();

    private async Task PublishTelemetrySafelyAsync(GameData data, GameContext context)
    {
        try
        {
            await PublishTelemetryAsync(data, context);
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Failed to publish Web Dashboard telemetry");
        }
    }
}
