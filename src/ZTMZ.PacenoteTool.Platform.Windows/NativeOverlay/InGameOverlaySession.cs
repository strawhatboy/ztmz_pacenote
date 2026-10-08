using System.Diagnostics;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WebDashboard;
using ZTMZ.PacenoteTool.UI.Windows;

namespace ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

/// <summary>Owns a loaded DLL's data connection. Call start/stop serially off the UI thread.</summary>
internal sealed class InGameOverlaySession
{
    private readonly GameOverlayManager overlay;
    private readonly Func<bool> stageActive;
    private readonly Action changed;
    private readonly Process process;
    private readonly InGameOverlayControlClient client;
    private readonly WebDashboardRuntime runtime = new();
    private readonly CancellationTokenSource stopping = new();
    private Task? pump;
    private InGameOverlayStatus? status;
    private Exception? failure;
    private long expectedWindow;

    public InGameOverlaySession(Process process, GameOverlayManager overlay, Func<bool> stageActive, Action changed)
    {
        this.process = process;
        this.overlay = overlay;
        this.stageActive = stageActive;
        this.changed = changed;
        client = new(process);
    }

    public bool IsActive => Volatile.Read(ref failure) == null && Volatile.Read(ref status)?.HasRecentSubmission == true
        && stageActive() && Config.Instance.UI_ShowHud && runtime.ClientCount > 0
        && Volatile.Read(ref expectedWindow) != 0
        && Volatile.Read(ref status)?.Window == (ulong)Volatile.Read(ref expectedWindow)
        && runtime.GetDiagnostics().RenderedFrameCount > 0
        && runtime.Dashboards.Any(d => d.SupportsWebDashboardClientLua && d.GetOutputTarget().Includes(DashboardOutputTarget.Overlay));
    public string? Error => Volatile.Read(ref failure)?.Message ?? Volatile.Read(ref status)?.RenderError;
    public void CancelPendingWork() => stopping.Cancel();

    public static bool IsLoaded(Process process) => process.Modules.Cast<ProcessModule>().Any(module =>
        string.Equals(module.ModuleName, "ztmz_dashboard_overlay.dll", StringComparison.OrdinalIgnoreCase));

    public async Task StartAsync()
    {
        try
        {
        await client.QueryAsync(stopping.Token).ConfigureAwait(false);
        await client.StopAsync().ConfigureAwait(false); // Reclaim a module left running after a host restart.
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            process.Refresh();
            if (process.HasExited) throw new InvalidOperationException("Game exited while waiting for its window.");
            expectedWindow = process.MainWindowHandle.ToInt64();
            if (expectedWindow != 0) break;
            if (waiting.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Game window is not ready for in-game rendering.");
            await Task.Delay(100, stopping.Token).ConfigureAwait(false);
        }
        runtime.Start(new WebDashboardRuntimeOptions(
            AppLevelVariables.Instance.GetPath(Constants.PATH_DASHBOARDS), AppContext.BaseDirectory,
            0, Config.Instance.WebDashboardFrameRate, SupportsNativeOverlay: true,
            CanvasResolution: Config.Instance.WebDashboardCanvasResolution,
            OutputTarget: DashboardOutputTarget.Overlay, LoopbackOnly: true),
            WebDashboardServerExtensions.CreateDefinitions(overlay.Dashboards, overlay.GetDashboardOutputTarget));
        // Only DX11 has controlled native/managed lifecycle validation so far.
        status = await client.StartAsync(runtime.Status.Port, "dx11", stopping.Token, (ulong)expectedWindow).ConfigureAwait(false);
        pump = PumpAsync();
        }
        catch (Exception error)
        {
            Volatile.Write(ref failure, error);
            try {await StopAsync().ConfigureAwait(false);}
            catch (Exception shutdownError) {Volatile.Write(ref failure, new AggregateException(error, shutdownError));}
            throw;
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            var nextStatus = Stopwatch.StartNew();
            bool wasVisible = false;
            while (!stopping.IsCancellationRequested)
            {
                bool visible = stageActive() && Config.Instance.UI_ShowHud;
                var args = overlay.DashboardScriptArguments;
                // Reuse the session controller's pacenote/replay context, including late startup.
                await runtime.PublishTelemetryAsync(args.GameData, args.GameContext, visible).ConfigureAwait(false);
                if (nextStatus.ElapsedMilliseconds >= 250)
                {
                    var current = await client.QueryAsync(stopping.Token).ConfigureAwait(false);
                    if (current.Port != runtime.Status.Port || !current.Started)
                        throw new InvalidOperationException("In-game overlay control ownership changed.");
                    Volatile.Write(ref status, current);
                    process.Refresh();
                    Volatile.Write(ref expectedWindow, process.MainWindowHandle.ToInt64());
                    nextStatus.Restart();
                    changed();
                }
                if (visible != wasVisible) {wasVisible = visible;changed();}
                await Task.Delay(1000 / Math.Clamp(Config.Instance.WebDashboardFrameRate, 10, 120), stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception error)
        {
            Volatile.Write(ref failure, error);
            try {await client.StopAsync().ConfigureAwait(false);runtime.Stop();}
            catch (Exception shutdownError) {Volatile.Write(ref failure, new AggregateException(error, shutdownError));}
            changed();
        }
    }

    public async Task StopAsync()
    {
        stopping.Cancel();
        if (pump != null) await pump.ConfigureAwait(false);
        // Do not enable the window replacement if we cannot confirm this renderer stopped.
        try { if (!process.HasExited) await client.StopAsync().ConfigureAwait(false); }
        catch (Exception) when (process.HasExited) { }
        runtime.Dispose();
        status = null;
    }
}
