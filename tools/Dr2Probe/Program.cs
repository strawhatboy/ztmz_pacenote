using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;

// Full production lifecycle/countdown observer. The diagnostic UDP destination
// must already be configured; this tool never edits the game's configuration.
if (args.FirstOrDefault() == "--countdown-session")
{
    var observedGame = new DirtRally2();
    observedGame.GameConfigurations[UdpGameConfig.Name] = new UdpGameConfig { IPAddress = "127.0.0.1", Port = 59996 };
    var observedReader = observedGame.GameDataReader;
    using var output = args.Length > 2 ? new StreamWriter(new FileStream(args[2], FileMode.CreateNew)) { AutoFlush = true } : null;
    observedReader.onGameStateChanged += change =>
    {
        var line = System.Text.Json.JsonSerializer.Serialize(new
        {
            at = DateTimeOffset.Now,
            previous = change.LastGameState.ToString(), state = change.NewGameState.ToString(),
            parameters = change.Parameters
        });
        Console.WriteLine(line);
        output?.WriteLine(line);
    };
    try
    {
        observedReader.Initialize(observedGame);
        await Task.Delay((args.Length > 1 ? int.Parse(args[1]) : 300) * 1000);
    }
    finally { observedReader.Uninitialize(observedGame); }
    return;
}

// Observe the production start-light reader without changing game or UDP settings.
if (args.FirstOrDefault() == "--countdown")
{
    var type = typeof(DirtRally2).Assembly.GetType("ZTMZ.PacenoteTool.Codemasters.Dr2VehicleMemoryReader", true)!;
    using var memoryReader = (IDisposable)Activator.CreateInstance(type)!;
    var read = type.GetMethod("ReadStartLights")!;
    var countdown = new Dr2Countdown();
    var seconds = args.Length > 1 ? int.Parse(args[1]) : 300;
    using var output = args.Length > 2 ? new StreamWriter(new FileStream(args[2], FileMode.CreateNew)) { AutoFlush = true } : null;
    var end = Environment.TickCount64 + seconds * 1000L;
    Dr2StartLightsSnapshot? previous = null;
    while (Environment.TickCount64 < end)
    {
        var snapshot = (Dr2StartLightsSnapshot?)read.Invoke(memoryReader, null);
        var number = countdown.Update(snapshot);
        if (snapshot != previous)
        {
            var line = System.Text.Json.JsonSerializer.Serialize(new { at = DateTimeOffset.Now, snapshot, announced = number });
            Console.WriteLine(line);
            output?.WriteLine(line);
            previous = snapshot;
        }
        await Task.Delay(50);
    }
    return;
}

// Observe the same hash-gated reader used by the app without changing UDP settings.
// Arguments: --turbo <UDP idle radians/s> <UDP max radians/s> [seconds] [new log path]
if (args.FirstOrDefault() == "--turbo")
{
    var type = typeof(DirtRally2).Assembly.GetType("ZTMZ.PacenoteTool.Codemasters.Dr2VehicleMemoryReader", true)!;
    using var memoryReader = (IDisposable)Activator.CreateInstance(type)!;
    var read = type.GetMethod("Read")!;
    var culture = System.Globalization.CultureInfo.InvariantCulture;
    var idle = float.Parse(args[1], culture);
    var maximum = float.Parse(args[2], culture);
    var seconds = args.Length > 3 ? int.Parse(args[3], culture) : 10;
    using var output = args.Length > 4 ? new StreamWriter(new FileStream(args[4], FileMode.CreateNew)) { AutoFlush = true } : null;
    var end = Environment.TickCount64 + seconds * 1000L;
    long nextConsole = 0;
    while (Environment.TickCount64 < end)
    {
        var vehicle = (Dr2VehicleTelemetry?)read.Invoke(memoryReader, [idle, maximum]);
        var line = System.Text.Json.JsonSerializer.Serialize(new
        {
            at = DateTimeOffset.UtcNow,
            car = vehicle?.CarCode,
            physics = vehicle?.PhysicsAddress,
            turbo = vehicle?.Turbo
        });
        output?.WriteLine(line);
        if (Environment.TickCount64 >= nextConsole)
        {
            Console.WriteLine(line);
            nextConsole = Environment.TickCount64 + 1000;
        }
        await Task.Delay(50);
    }
    return;
}

// Same hash-gated production memory reader, independent of UDP (which can stop
// on result screens). Keep the capture local; never writes to the game.
if (args.FirstOrDefault() == "--race")
{
    var type = typeof(DirtRally2).Assembly.GetType("ZTMZ.PacenoteTool.Codemasters.Dr2VehicleMemoryReader", true)!;
    using var memoryReader = (IDisposable)Activator.CreateInstance(type)!;
    var read = type.GetMethod("ReadRaceState")!;
    var seconds = args.Length > 1 ? int.Parse(args[1]) : 300;
    using var output = args.Length > 2 ? new StreamWriter(new FileStream(args[2], FileMode.CreateNew)) { AutoFlush = true } : null;
    var end = Environment.TickCount64 + seconds * 1000L;
    string? previous = null;
    while (Environment.TickCount64 < end)
    {
        var snapshot = (Dr2RaceStateSnapshot?)read.Invoke(memoryReader, null);
        var value = snapshot is { } state ? $"record=0x{state.RecordAddress:x} state={(int)state.State} ({state.State})" : "unavailable";
        if (value != previous)
        {
            var line = $"{DateTimeOffset.Now:O} {value}";
            Console.WriteLine(line);
            output?.WriteLine(line);
            previous = value;
        }
        await Task.Delay(50);
    }
    return;
}

// Inspect the production reader while paused, without requiring UDP or driving.
if (args.FirstOrDefault() == "--damage")
{
    var type = typeof(DirtRally2).Assembly.GetType("ZTMZ.PacenoteTool.Codemasters.Dr2VehicleMemoryReader", true)!;
    using var memoryReader = (IDisposable)Activator.CreateInstance(type)!;
    var read = type.GetMethod("Read")!;
    var idle = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
    var maximum = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
    for (var attempt = 0; attempt < 50; attempt++)
    {
        var vehicle = (Dr2VehicleTelemetry?)read.Invoke(memoryReader, [idle, maximum]);
        if (vehicle?.Damage is { } damage)
        {
            foreach (var component in damage.Components)
                Console.WriteLine($"{component.Id}: severity={component.Severity}, magnitude={component.Magnitude}");
            foreach (var raw in damage.RawComponents.Where(r => r.SourceName == "tyre-state"))
                Console.WriteLine($"{raw.Id}: {raw.RawValue}");
            return;
        }
        await Task.Delay(100);
    }
    throw new InvalidOperationException("No verified player damage snapshot available.");
}

var game = new DirtRally2();
game.GameConfigurations[UdpGameConfig.Name] = new UdpGameConfig { IPAddress = "127.0.0.1", Port = 59997 };
var reader = game.GameDataReader;
long next = 0;
reader.onGameStateChanged += change => Console.WriteLine($"STATE {change.LastGameState} -> {change.NewGameState}");
reader.onNewGameData += (_, data) =>
{
    if (Environment.TickCount64 < next) return;
    next = Environment.TickCount64 + 1000;
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {reader.CarName} / {reader.CarClass} / {reader.TrackName} " +
        $"rpm={data.RPM:F1} max={data.MaxRPM:F1} shift={data.ShiftLightsRPMEnd:F1} " +
        $"handbrake={data.HandBrake:F2} valid={data.HandBrakeValid} time={data.LapTime:F3} " +
        $"damage={string.Join("; ", data.CarDamage?.RawComponents.Select(p => p.Id + ":" + p.RawValue) ?? [])}");
};
try
{
    reader.Initialize(game);
    await Task.Delay(args.Length > 0 ? int.Parse(args[0]) * 1000 : 10000);
}
finally { reader.Uninitialize(game); }
