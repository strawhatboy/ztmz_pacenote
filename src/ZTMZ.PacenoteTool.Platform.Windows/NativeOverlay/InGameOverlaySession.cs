using System.Diagnostics;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WebDashboard;
using ZTMZ.PacenoteTool.UI.Windows;

namespace ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

/// <summary>Owns a loaded DLL's data connection. Call start/stop serially off the UI thread.</summary>
internal sealed class InGameOverlaySession
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private string? lastDiagnostic;
    private string? waitingDiagnostic;
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
    public string? Error => Volatile.Read(ref failure)?.Message
        ?? (string.IsNullOrEmpty(Volatile.Read(ref status)?.RenderError) ? waitingDiagnostic : Volatile.Read(ref status)?.RenderError);
    public string FailureStatusKey => Error?.Contains("DX12 detected", StringComparison.Ordinal) == true
        ? "overlayBackend.dx12ContextMissing"
        : Error?.Contains("Vulkan", StringComparison.Ordinal) == true
            ? "overlayBackend.vulkanContextMissing"
            : waitingDiagnostic != null ? "overlayBackend.noFrames" : "settings.tooltip.overlayHudHomeStatusUnavailable";
    public void CancelPendingWork() => stopping.Cancel();

    public static bool IsLoaded(Process process) => process.Modules.Cast<ProcessModule>().Any(module =>
        string.Equals(module.ModuleName, "ztmz_dashboard_overlay.dll", StringComparison.OrdinalIgnoreCase));

    public async Task StartAsync()
    {
        try
        {
        var capabilities = await client.QueryAsync(stopping.Token).ConfigureAwait(false);
        Logger.Info("In-game overlay PID {0}: available APIs [{1}], DLL {2}.", process.Id, string.Join(",", capabilities.AvailableApis), InGameOverlayLoader.GetLibraryPath(process));
        if (capabilities.AvailableApis.Length == 0)
            throw new InvalidOperationException("The game has an older overlay DLL loaded. Exit and restart the game to load the multi-API renderer.");
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
        Logger.Info("Starting in-game overlay PID {0}: API auto, target HWND {1}, loopback port {2}.", process.Id, expectedWindow, runtime.Status.Port);
        status = await client.StartAsync(runtime.Status.Port, "auto", stopping.Token, (ulong)expectedWindow).ConfigureAwait(false);
        Logger.Info("In-game overlay PID {0}: installed candidates [{1}].", process.Id, string.Join(",", status.CandidateApis));
        foreach (var warning in status.HookWarnings) Logger.Warn("In-game overlay PID {0}: {1}", process.Id, warning);
        pump = PumpAsync();
        }
        catch (Exception error)
        {
            Logger.Error(error, "In-game overlay start failed for PID {0}.", process.Id);
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
            var firstFrameWait = Stopwatch.StartNew();
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
                    process.Refresh();
                    var currentWindow = process.MainWindowHandle.ToInt64();
                    if (currentWindow != 0 && currentWindow != Volatile.Read(ref expectedWindow))
                    {
                        Logger.Info("In-game overlay PID {0}: target HWND changed from {1} to {2}; updating renderer target.",
                            process.Id, expectedWindow, currentWindow);
                        current = await client.StartAsync(runtime.Status.Port, "auto", stopping.Token, (ulong)currentWindow).ConfigureAwait(false);
                        Volatile.Write(ref expectedWindow, currentWindow);
                        firstFrameWait.Restart();
                    }
                    Volatile.Write(ref status, current);
                    waitingDiagnostic = current.SubmittedFrames == 0 && firstFrameWait.Elapsed > TimeSpan.FromSeconds(15)
                        ? "No overlay frame submitted after 15 seconds. Check the selected API, hook warnings and render error in the log; the renderer may require attachment before device creation." : null;
                    var diagnostic = $"{current.SelectedApi}|{current.HasRecentSubmission}|{current.Window}|{current.RenderError}|{waitingDiagnostic}|{current.QueueSource}";
                    if (diagnostic != lastDiagnostic)
                    {
                        lastDiagnostic = diagnostic;
                        Logger.Info("In-game overlay PID {0}: requested={1}, selected={2}, recentSubmission={3}, frames={4}, HWND={5}, renderError={6}, wait={7}, queueSource={8}",
                            process.Id, current.RequestedApi, current.SelectedApi, current.HasRecentSubmission, current.SubmittedFrames,
                            current.Window, current.RenderError, waitingDiagnostic, current.QueueSource);
                        if (!string.IsNullOrEmpty(current.RenderError) || waitingDiagnostic != null)
                            Logger.Warn("In-game overlay PID {0}: {1}", process.Id, string.IsNullOrEmpty(current.RenderError) ? waitingDiagnostic : current.RenderError);
                    }
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
            Logger.Error(error, "In-game overlay session failed for PID {0}.", process.Id);
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
        Logger.Info("In-game overlay stopped for PID {0}; DLL remains loaded until game exit.", process.Id);
    }
}
