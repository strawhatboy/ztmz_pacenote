using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;

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
