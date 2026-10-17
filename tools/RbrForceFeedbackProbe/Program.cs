using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using ZTMZ.PacenoteTool.RBR;

// Uses the production reader. ReadProcessMemory only; no graphics hooks or injection.
if (args.Length is < 2 or > 3 || !int.TryParse(args[0], out var pid))
{
    Console.Error.WriteLine("RbrForceFeedbackProbe <pid> <new.jsonl> [seconds=120]");
    return 1;
}
var seconds = args.Length == 3 ? int.Parse(args[2]) : 120;
if (seconds is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(seconds));
using var process = Process.GetProcessById(pid);
using var handle = Native.OpenProcess(0x10, false, pid);
if (handle.IsInvalid) throw new InvalidOperationException("Cannot open process for reading.");
var reader = RBRForceFeedbackReader.Create(process, handle.DangerousGetHandle())
    ?? throw new InvalidOperationException("Unsupported RBR executable.");
using var output = new StreamWriter(new FileStream(args[1], FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
output.WriteLine(JsonSerializer.Serialize(new { kind = "header", pid, seconds, utc = DateTimeOffset.UtcNow, source = "production RBRForceFeedbackReader" }));
Console.WriteLine($"Recording production FFB to {Path.GetFullPath(args[1])}");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
var clock = Stopwatch.StartNew();
var samples = 0; var available = 0;
while (clock.Elapsed.TotalSeconds < seconds && !stop.IsCancellationRequested && !process.HasExited)
{
    var force = reader.Read(); samples++; if (force != null) available++;
    output.WriteLine(JsonSerializer.Serialize(new { kind = "sample", elapsedMs = clock.ElapsedMilliseconds, force }));
    try { await Task.Delay(20, stop.Token); } catch (OperationCanceledException) { break; }
}
output.WriteLine(JsonSerializer.Serialize(new { kind = "end", samples, available }));
Console.WriteLine($"Stopped: {samples} samples, {available} available.");
return 0;

static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
}
