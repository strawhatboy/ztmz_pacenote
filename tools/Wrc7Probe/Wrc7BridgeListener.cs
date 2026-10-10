using System.IO.Pipes;
using System.Text;
using ZTMZ.PacenoteTool.KT;

internal static class Wrc7BridgeListener
{
    public static async Task RunAsync(int pid, string catalogPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The WRC7 development bridge runs on Windows.");
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        if (!process.ProcessName.Equals("WRC7", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a running WRC7 process");
        var catalog = Wrc7PacenoteCatalog.Parse(await File.ReadAllTextAsync(catalogPath));
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => stop.Cancel();
        if (process.HasExited) stop.Cancel();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream($"ZTMZ.WRC7.Live.v1.{pid}", PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                Console.WriteLine($"Waiting for WRC7 bridge, PID {pid}; Ctrl+C stops the receiver.");
                try
                {
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    if (await ReadLineAsync(reader, stop.Token) != "READY")
                        throw new InvalidDataException("Missing bridge READY handshake");
                    Console.WriteLine("Bridge READY (original game audio remains enabled).");
                    while (await ReadLineAsync(reader, stop.Token) is { } line)
                    {
                        if (line.StartsWith("STATS ", StringComparison.Ordinal))
                        {
                            if (line != "STATS dropped=0") Console.WriteLine(line);
                            continue;
                        }
                        var fields = line.Split('\t', 2);
                        if (fields.Length != 2 || !long.TryParse(fields[0], out var tick))
                            throw new InvalidDataException("Invalid sound record");
                        var age = Environment.TickCount64 - tick;
                        if (age is < 0 or > 500) { Console.WriteLine($"Rejected stale sound ({age} ms): {fields[1]}"); continue; }
                        Console.WriteLine($"{DateTimeOffset.Now:O} ageMs={age} {fields[1]}");
                        foreach (var accessible in new[] { false, true })
                        {
                            var groups = catalog.ResolveGroups(fields[1], accessible);
                            Console.WriteLine($"  {(accessible ? "accessible" : "detailed")}: " +
                                (groups.Count == 0 ? "UNRESOLVED" : string.Join(" -> ", groups.Select(g => "[" + string.Join(" | ", g) + "]"))));
                        }
                    }
                }
                catch (IOException error) { Console.WriteLine($"Disconnected: {error.Message}"); }
                catch (InvalidDataException error) { Console.WriteLine($"Rejected connection: {error.Message}"); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { Console.CancelKeyPress -= cancel; }
    }

    // Bound the message while reading, rather than allocating an unbounded line first.
    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellation)
    {
        var text = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellation) != 0)
        {
            if (character[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length == 1024) throw new InvalidDataException("Oversized bridge message");
            text.Append(character[0]);
        }
        if (text.Length != 0) throw new InvalidDataException("Truncated bridge message");
        return null;
    }
}
