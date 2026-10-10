using System.Diagnostics;
using ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

try
{
    if (args[0] == "--status-tests")
    {
        foreach (var api in new[] { "dx9", "dx10", "dx11", "dx12", "vulkan" })
        {
            var status = new InGameOverlayStatus
            {
                Version = 1, ModuleVersion = 1, Pid = 1, Instance = "test", Ok = true,
                Started = true, Port = 8080, RequestedApi = "auto", SelectedApi = api,
                SubmittedFrames = 1, Window = 1, LastFrameAgeMs = 0, RenderError = ""
            };
            Require(status.HasRecentSubmission, $"{api} submission was not acknowledged.");
            Require(!(status with { SubmittedFrames = 0 }).HasRecentSubmission, "Selected API alone claimed rendering.");
            Require(!(status with { RenderError = "device lost" }).HasRecentSubmission, "Render error claimed rendering.");
            Require(!(status with { SelectedApi = "auto" }).HasRecentSubmission, "Unselected API claimed rendering.");
            Require(!(status with { LastFrameAgeMs = 1500 }).HasRecentSubmission, "Stale frame claimed rendering.");
        }
        Console.WriteLine("Multi-API frame status checks passed.");
        return 0;
    }
    using var process = Process.GetProcessById(int.Parse(args[1]));
    var client = new InGameOverlayControlClient(process);
    if (args[0] is "--inject-probe" or "--load-probe")
    {
        await InGameOverlayLoader.LoadAsync(process, args[2]);
        var loaded = await client.QueryAsync();
        Require(!loaded.Started && loaded.SubmittedFrames == 0, "DLL started rendering before host configuration.");
        if (args[0] == "--load-probe")
        {
            process.Refresh();
            var module = process.Modules.Cast<ProcessModule>().Single(m => m.ModuleName == "ztmz_dashboard_overlay.dll");
            Require(string.Equals(module.FileName, Path.GetFullPath(args[2]), StringComparison.OrdinalIgnoreCase), "Unexpected module path.");
            Console.WriteLine("Remote DLL load/control probe passed: " + module.FileName);
            return 0;
        }
        process.Refresh();
        await client.StartAsync(9, "auto", targetWindow: ulong.Parse(args[3]));
        var frameDeadline = Stopwatch.StartNew();
        var rendered = await client.QueryAsync();
        while (!rendered.HasRecentSubmission && frameDeadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(100);
            rendered = await client.QueryAsync();
        }
        Require(rendered.HasRecentSubmission, "Injected module did not submit frames: " + System.Text.Json.JsonSerializer.Serialize(rendered));
        Require(rendered.SelectedApi == (args.Length > 4 ? args[4] : "dx11"), "Auto selected the wrong graphics API.");
        Require(!(await client.StopAsync()).Started, "Injected module did not stop.");
        Console.WriteLine("Remote DLL load/control/frame probe passed.");
        return 0;
    }
    var initial = await client.QueryAsync();
    Require(initial.Started && initial.SelectedApi == "dx11" && initial.SubmittedFrames > 0 && initial.Window != 0,
        "Managed client did not receive real frame acknowledgement.");
    Require((await client.StartAsync(8080)).SubmittedFrames == initial.SubmittedFrames, "Idempotent start reset progress.");
    var stopped = await client.StopAsync();
    Require(!stopped.Started && !stopped.HasRecentSubmission && stopped.SubmittedFrames == 0, "Stop retained frame acknowledgement.");
    var restarted = await client.StartAsync(8080);
    Require(restarted.Started && restarted.SelectedApi == "auto" && !restarted.HasRecentSubmission, "Restart claimed rendering before a frame.");
    using var cancelled = new CancellationTokenSource();cancelled.Cancel();
    try { await client.QueryAsync(cancelled.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { }
    using var ownProcess = Process.GetCurrentProcess();
    var absent = new InGameOverlayControlClient(ownProcess, TimeSpan.FromMilliseconds(100));
    try { await absent.QueryAsync(); throw new Exception("Absent server did not time out."); }
    catch (TimeoutException) { }
    // Old snapshots must age out without another query.
    await Task.Delay(1100);
    Require(!initial.HasRecentSubmission, "Old frame snapshot remained current.");
    Console.WriteLine("Managed control probe passed.");
    return 0;
}
catch (Exception error) {
    Console.Error.WriteLine(error);
    if (Environment.GetEnvironmentVariable("ZTMZ_OVERLAY_PROBE_LOG") is { Length: > 0 } log)
        File.WriteAllText(log, error.ToString());
    return 1;
}
