using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ZTMZ.PacenoteTool.KT;

try
{
if (args.Length == 3 && args[0] == "extract")
{
    var game = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
    if (output.Equals(game, StringComparison.OrdinalIgnoreCase) || output.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Output must be outside the game installation");
    var files = WrcGenerationsArchive.ReadResources(game, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "PACENOTES.CFG", "PACENOTEBLOCKS.CFG", "LOCALISATION_EN.PLOC", "LOCALISATION.LUA", "CARS.CFG" }, legacyWrc7: true);
    Directory.CreateDirectory(output);
    foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(output, name), bytes);
    var catalog = WrcGenerationsPacenoteCatalog.Parse(Encoding.UTF8.GetString(files["PACENOTES.CFG"]), Encoding.UTF8.GetString(files["PACENOTEBLOCKS.CFG"]));
    Console.WriteLine($"{catalog.AtomCount} atoms, {catalog.BlockCount} blocks");
    foreach (var name in new[] { "BLOCK_0321", "BLOCK_1605", "BLOCK_1855" })
        if (catalog.TryResolveIgnoringCase(name, out var atoms)) Console.WriteLine(name + " -> " + string.Join("/", atoms));
    return;
}
if (args.Length < 2 || !OperatingSystem.IsWindows()) throw new ArgumentException("Expected extract <game> <output>, session <PID>, listen <PID> <data.zdb>, or capture <PID> <seconds> <output.jsonl>");
using var process = Process.GetProcessById(int.Parse(args[1]));
using var memory = new Wrc9MemorySession(process);
if (args[0] == "listen" && args.Length == 3)
{
    using var database = new SqliteConnection(new SqliteConnectionStringBuilder
    { DataSource = Path.GetFullPath(args[2]), Mode = SqliteOpenMode.ReadOnly }.ToString());
    database.Open();
    using var command = database.CreateCommand();
    command.CommandText = "SELECT filename FROM pacenote_filenames";
    using var rows = command.ExecuteReader();
    var vocabulary = new HashSet<string>(StringComparer.Ordinal);
    while (rows.Read()) vocabulary.Add(rows.GetString(0));
    using var receiver = new Wrc9LiveSession(process.Id, memory.Pacenotes, vocabulary,
        evt => Console.WriteLine(JsonSerializer.Serialize(new { tick = Environment.TickCount64, evt.SourceResourceName,
            tokens = evt.Record?.RawText, evt.SystemSoundToken })));
    Console.WriteLine("Receiver starting; waiting for production bridge READY.");
    await receiver.Ready.Task.WaitAsync(TimeSpan.FromMinutes(2));
    Console.WriteLine("Production bridge READY; original game audio remains enabled.");
    var liveLifecycle = new Wrc9RaceSession();
    var previous = ZTMZ.PacenoteTool.Base.Game.GameState.Unknown;
    while (!process.HasExited)
    {
        var sample = memory.Read();
        var liveState = liveLifecycle.Update(sample, Environment.TickCount64);
        if (liveState != previous)
            Console.WriteLine(JsonSerializer.Serialize(new { tick = Environment.TickCount64, state = liveState.ToString(),
                raw = sample?.RawMilliseconds, finish = liveLifecycle.FinishState?.ToString(), native = sample?.NativeResult }));
        previous = liveState;
        await Task.Delay(50);
    }
    return;
}
if (args[0] == "session")
{
    Console.WriteLine(JsonSerializer.Serialize(memory.Read(), new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    return;
}
if (args[0] != "capture" || args.Length != 4) throw new ArgumentException("Expected capture <PID> <seconds> <output.jsonl>");
var seconds = int.Parse(args[2]);
if (seconds is < 1 or > 1800) throw new ArgumentOutOfRangeException("seconds");
using var outputFile = new StreamWriter(new FileStream(args[3], FileMode.CreateNew));
var watch = Stopwatch.StartNew(); var lifecycle = new Wrc9RaceSession();
var damageEvents = new Wrc9DamageEvents();
Console.WriteLine("READY: read-only WRC9 capture");
while (!process.HasExited && watch.Elapsed.TotalSeconds < seconds)
{
    var snapshot = memory.Read(); var state = lifecycle.Update(snapshot, Environment.TickCount64);
    var damage = damageEvents.Update(snapshot, state, lifecycle.IsNewSession);
    await outputFile.WriteLineAsync(JsonSerializer.Serialize(new { elapsedMs = watch.ElapsedMilliseconds, state = state.ToString(), snapshot, damage }, new JsonSerializerOptions { IncludeFields = true }));
    await outputFile.FlushAsync();
    await Task.Delay(100);
}
Console.WriteLine("Capture ended");
}
catch (Exception error)
{
    Console.Error.WriteLine($"WRC9 diagnostic stopped: {error.GetType().Name}: {error.Message}");
    Environment.ExitCode = 1;
}
