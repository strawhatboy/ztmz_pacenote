using System.Diagnostics;
using ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

try
{
    using var process = Process.GetProcessById(int.Parse(args[1]));
    var client = new InGameOverlayControlClient(process);
    if (args[0] == "--inject-probe")
    {
        await InGameOverlayLoader.LoadAsync(process, args[2]);
        var loaded = await client.QueryAsync();
        Require(!loaded.Started && loaded.SubmittedFrames == 0, "DLL started rendering before host configuration.");
        process.Refresh();
        await client.StartAsync(9, "dx11", targetWindow: ulong.Parse(args[3]));
        await Task.Delay(400);
        Require((await client.QueryAsync()).HasRecentSubmission, "Injected module did not submit DX11 frames.");
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
catch (Exception error) { Console.Error.WriteLine(error);return 1; }
