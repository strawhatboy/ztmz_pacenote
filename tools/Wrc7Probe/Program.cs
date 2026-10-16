using ZTMZ.PacenoteTool.KT;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Diagnostics;

try
{
if (args.Length == 4 && args[0] == "capture")
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var seconds = int.Parse(args[2]);
    if (seconds is < 1 or > 1800) throw new ArgumentOutOfRangeException("seconds");
    using var process = Process.GetProcessById(int.Parse(args[1]));
    using var memory = new Wrc7MemorySession(process);
    using var outputFile = new StreamWriter(new FileStream(args[3], FileMode.CreateNew));
    var watch = Stopwatch.StartNew(); var lifecycle = new Wrc7RaceSession();
    var damageEvents = new Wrc7DamageEvents();
    var json = new JsonSerializerOptions { IncludeFields = true };
    Console.WriteLine("READY: read-only WRC7 capture");
    while (!process.HasExited && watch.Elapsed.TotalSeconds < seconds)
    {
        var snapshot = memory.Read(); var state = lifecycle.Update(snapshot, Environment.TickCount64);
        var damage = damageEvents.Update(snapshot, state, lifecycle.IsNewSession);
        await outputFile.WriteLineAsync(JsonSerializer.Serialize(new { elapsedMs = watch.ElapsedMilliseconds, state = state.ToString(), snapshot, damage }, json));
        await outputFile.FlushAsync();
        await Task.Delay(100);
    }
    Console.WriteLine("Capture ended");
    return;
}


if (args.Length == 2 && args[0] == "session")
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    using var process = System.Diagnostics.Process.GetProcessById(int.Parse(args[1]));
    using var session = new Wrc7MemorySession(process);
    var snapshot = session.Read();
    Console.WriteLine(snapshot?.ToString() ?? "No verified active WRC7 session");
    if (snapshot is not null) Console.WriteLine(snapshot.Telemetry);
    Console.WriteLine(session.ReadPacenoteModes());
    return;
}

if (args.Length >= 4 && args[0] is "decode" or "decode-current")
{
    Wrc7PacenoteCatalog eventCatalog;
    var modes = Wrc7PacenoteModes.Unknown;
    if (args[0] == "decode-current")
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var process = System.Diagnostics.Process.GetProcessById(int.Parse(args[1]));
        using var session = new Wrc7MemorySession(process);
        eventCatalog = session.Pacenotes;
        modes = session.ReadPacenoteModes();
        Console.WriteLine(modes);
    }
    else eventCatalog = Wrc7PacenoteCatalog.Parse(File.ReadAllText(args[1]));
    using var database = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(args[2]), Mode = SqliteOpenMode.ReadOnly
    }.ToString());
    database.Open();
    using var command = database.CreateCommand();
    command.CommandText = "SELECT filename FROM pacenote_filenames";
    using var rows = command.ExecuteReader();
    var vocabulary = new HashSet<string>(StringComparer.Ordinal);
    while (rows.Read()) vocabulary.Add(rows.GetString(0));
    var decoder = new Wrc7LivePacenoteDecoder(eventCatalog, vocabulary);
    foreach (var resource in args.Skip(3))
    {
        var decoded = decoder.TryDecodeWithModes(0, 0, resource, modes, out var pacenote);
        Console.WriteLine($"{resource} -> {(decoded ? pacenote!.Record.RawText : "UNRESOLVED (mode, mapping or vocabulary)")}");
    }
    return;
}

if (args.Length == 3 && args[0] == "listen")
{
    await Wrc7BridgeListener.RunAsync(int.Parse(args[1]), args[2]);
    return;
}

if (args.Length >= 3 && args[0] == "resolve")
{
    var observedCatalog = Wrc7PacenoteCatalog.Parse(File.ReadAllText(args[1]));
    foreach (var resource in args.Skip(2))
    {
        Console.WriteLine(resource);
        foreach (var accessible in new[] { false, true })
        {
            var groups = observedCatalog.ResolveGroups(resource, accessible);
            Console.WriteLine($"  {(accessible ? "accessible" : "detailed")}: " +
                (groups.Count == 0 ? "UNRESOLVED" : string.Join(" -> ", groups.Select(g => "[" + string.Join(" | ", g) + "]"))));
        }
    }
    return;
}

if (args.Length != 2 && !(args.Length == 3 && args[2] == "--icons")) throw new ArgumentException("Expected installed WRC7 directory and separate inspection output directory [--icons]");
var game = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (output.Equals(game, StringComparison.OrdinalIgnoreCase) || output.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Inspection output must be outside the game directory");
var resources = WrcGenerationsArchive.ReadResources(game,
    args.Length == 3
        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PACENOTES_ICONS.PTX", "PACENOTES_ICONS.PSK", "ELEMENT_PACENOTE_ICONES.FLS" }
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PACENOTES.CFG", "CARS.CFG", "RALLIES.CFG", "LOCALISATION_EN.PLOC", "LOCALISATION_CH_S.PLOC", "LOCALISATION.LUA" }, legacyWrc7: true);
Directory.CreateDirectory(output);
foreach (var (name, bytes) in resources)
{
    File.WriteAllBytes(Path.Combine(output, name), bytes);
    Console.WriteLine($"{name}: {bytes.Length} bytes");
}
if (args.Length == 3) return;
var catalog = Wrc7PacenoteCatalog.Parse(System.Text.Encoding.UTF8.GetString(resources["PACENOTES.CFG"]));
Console.WriteLine($"WRC7 catalog: {catalog.AtomCount} atoms (sound variants kept separate)");

}
catch (Exception error)
{
    Console.Error.WriteLine($"WRC7 diagnostic stopped: {error.GetType().Name}: {error.Message}");
    Environment.ExitCode = 1;
}
