using System.Diagnostics;
using System.Text.Json;
using ZTMZ.PacenoteTool.KT;
using ZTMZ.PacenoteTool.Base.Game;

try
{
if (args.Length == 4 && args[0] == "capture")
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var seconds = int.Parse(args[2]);
    if (seconds is < 1 or > 1800) throw new ArgumentOutOfRangeException("seconds");
    using var target = Process.GetProcessById(int.Parse(args[1]));
    using var memory = new WrcGenerationsMemorySession(target);
    using var file = new StreamWriter(new FileStream(args[3], FileMode.CreateNew));
    var captureClock = Stopwatch.StartNew();
    Console.WriteLine("READY: read-only WRCG production damage/hybrid capture");
    while (!target.HasExited && captureClock.Elapsed.TotalSeconds < seconds && !File.Exists(args[3] + ".stop"))
    {
        var sample = memory.Read();
        var damage = sample?.VehicleTimerAddress is { } timer ? memory.ReadDamage(timer) : null;
        var hybrid = sample?.VehicleTimerAddress is { } hybridTimer ? memory.ReadHybrid(hybridTimer) : null;
        await file.WriteLineAsync(JsonSerializer.Serialize(new { elapsedMs = captureClock.ElapsedMilliseconds, snapshot = sample, damage, hybrid }));
        await file.FlushAsync();
        await Task.Delay(100);
    }
    Console.WriteLine("Capture ended");
    return;
}

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This probe reads a Windows WRCG process");
if (args.Length is < 1 or > 2 || !int.TryParse(args[0], out var pid)) throw new ArgumentException("Expected WRCG PID [local UDP port]");
using var process = Process.GetProcessById(pid);
var watch = Stopwatch.StartNew();
using var source = new WrcGenerationsMemorySession(process);
Console.WriteLine($"Initialization: {watch.ElapsedMilliseconds} ms");
watch.Restart();
WrcGenerationsSessionSnapshot? snapshot = null;
for (var i = 0; i < 20; i++) snapshot = source.Read();
Console.WriteLine($"Mean snapshot read: {watch.Elapsed.TotalMilliseconds / 20:F3} ms");
Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
if (args.Length == 2 && args[1] == "--inputs")
{
    float? last = null;
    for (var i = 0; i < 3000; i++)
    {
        var value = source.ReadHandBrake();
        if (value != last) Console.WriteLine($"Handbrake {value?.ToString() ?? "unavailable"}");
        last = value;
        await Task.Delay(20);
    }
}
else if (args.Length == 2)
{
    using var udp = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, int.Parse(args[1])));
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    using var reader = new WrcGenerationsGameDataReader(source);
    reader.onGameStateChanged += e => Console.WriteLine($"Lifecycle {e.NewGameState}: {reader.TrackName} | {reader.CarName} | {reader.CarClass}");
    Console.WriteLine("Waiting for one UDP packet (paused games may send none)...");
    try
    {
        var packet = await udp.ReceiveAsync(timeout.Token);
        reader.onNewUdpMessage([], packet.Buffer);
        Console.WriteLine($"UDP: {packet.Buffer.Length} bytes, time={reader.CurrentGameData.LapTime}, distance={reader.CurrentGameData.LapDistance}");
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
        Console.WriteLine("No UDP packet received; memory snapshot succeeded, live lifecycle not verified.");
        Environment.ExitCode = 2;
    }
}

}
catch (Exception error)
{
    Console.Error.WriteLine($"WRCG diagnostic stopped: {error.GetType().Name}: {error.Message}");
    Environment.ExitCode = 1;
}
