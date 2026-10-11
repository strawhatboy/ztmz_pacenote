using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using ZTMZ.PacenoteTool.AC;

const int sampleCount = 12;
const int sampleIntervalMilliseconds = 500;

try
{
    using var graphicsMap = MemoryMappedFile.OpenExisting("Local\\acpmf_graphics", MemoryMappedFileRights.Read);
    using var physicsMap = MemoryMappedFile.OpenExisting("Local\\acpmf_physics", MemoryMappedFileRights.Read);
    using var staticMap = MemoryMappedFile.OpenExisting("Local\\acpmf_static", MemoryMappedFileRights.Read);
    using var graphicsView = graphicsMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    using var physicsView = physicsMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    using var staticView = staticMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

    if (args.Contains("--watch-controls"))
    {
        using var supplement = new ACRVehicleMemoryReader();
        string? previous = null;
        for (var sample = 0; sample < 6000; sample++)
        {
            var vehicle = supplement.Read();
            var physics = Read<ACRPhysicsData>(physicsView);
            var line = $"available={vehicle is not null} handbrake={vehicle?.HandBrake:F2} steering={physics.SteeringAngle:F1} clutchRaw={physics.ClutchPosition:F1} throttle={physics.ThrottleInput:F1} brake={physics.BrakeInput:F1}";
            if (line != previous) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
            previous = line;
            await Task.Delay(100);
        }
        return;
    }
    if (args.Contains("--converted") || args.Contains("--damage"))
    {
        using var supplement = new ACRVehicleMemoryReader();
        ACRVehicleTelemetry? vehicle = null;
        for (var attempt = 0; attempt < 100 && vehicle is null; attempt++)
        {
            vehicle = supplement.Read();
            if (vehicle is null) await Task.Delay(100);
        }
        var raw = new ACRGameRawData
        {
            GraphicsData = Read<ACRGraphicsData>(graphicsView),
            PhysicsData = Read<ACRPhysicsData>(physicsView),
            StaticInfo = Read<ACRStaticInfo>(staticView)
        };
        if (args.Contains("--damage"))
        {
            var data = ACRGameDataConverter.Convert(raw, vehicle);
            Console.WriteLine($"Damage available={data.CarDamage.Dashboard.IsAvailable}, components={data.CarDamage.Components.Count}, raw={data.CarDamage.RawComponents.Count}");
            foreach (var part in data.CarDamage.RawComponents) Console.WriteLine($"{part.SourceName}: {part.RawValue}");
            double max = 0, total = 0;
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(220);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                _ = supplement.Read();
                timer.Stop();
                total += timer.Elapsed.TotalMilliseconds;
                max = Math.Max(max, timer.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"Supplement including damage reads: mean={total/20:F2} ms, max={max:F2} ms (20 samples)");
            return;
        }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(vehicle));
        Console.WriteLine(ACRGameDataConverter.Convert(raw, vehicle));
        return;
    }

    if (args.Contains("--snapshot"))
    {
        Dump("graphics", Read<ACRGraphicsData>(graphicsView));
        Dump("physics", Read<ACRPhysicsData>(physicsView));
        Dump("static", Read<ACRStaticInfo>(staticView));
        Console.WriteLine($"Map capacities: {graphicsView.Capacity}/{physicsView.Capacity}/{staticView.Capacity}");
        foreach (var (name, view, size) in new[] { ("physics", physicsView, Marshal.SizeOf<ACRPhysicsData>()), ("static", staticView, Marshal.SizeOf<ACRStaticInfo>()) })
            for (var offset = size; offset + 4 <= view.Capacity; offset += 4)
                if (view.ReadInt32(offset) != 0)
                    Console.WriteLine($"Tail {name}+{offset}: int={view.ReadInt32(offset)} float={view.ReadSingle(offset)}");
        return;
    }

    Console.WriteLine("sample,time_ms,session_clock,distance_m,progress,track_length_m,speed_kph,status,track,track_layout");

    for (var sample = 0; sample < sampleCount; sample++)
    {
        var graphics = Read<ACRGraphicsData>(graphicsView);
        var physics = Read<ACRPhysicsData>(physicsView);
        var staticInfo = Read<ACRStaticInfo>(staticView);

        Console.WriteLine(string.Join(',',
            sample,
            graphics.CurrentTimeMillis,
            graphics.SessionClock.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            graphics.TotalDistanceTraveled.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            graphics.NormalizedCarPos.ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
            staticInfo.TrackLengthMeters.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            physics.SpeedKilometersPerHour.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            graphics.CurrentStatus,
            Escape(staticInfo.TrackName),
            Escape(staticInfo.TrackLayoutConfiguration)));

        await Task.Delay(sampleIntervalMilliseconds);
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

static T Read<T>(MemoryMappedViewAccessor accessor) where T : struct
{
    var bytes = new byte[Marshal.SizeOf<T>()];
    accessor.ReadArray(0, bytes, 0, bytes.Length);
    var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
    try
    {
        return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
    }
    finally
    {
        handle.Free();
    }
}

static string Escape(string? value) => (value ?? string.Empty).Replace(',', '_');

static void Dump<T>(string name, T value) where T : struct
{
    Console.WriteLine($"{name}: size={Marshal.SizeOf<T>()}");
    foreach (var field in typeof(T).GetFields())
    {
        var item = field.GetValue(value);
        Console.WriteLine($"  {Marshal.OffsetOf<T>(field.Name)} {field.Name}: {System.Text.Json.JsonSerializer.Serialize(item, new System.Text.Json.JsonSerializerOptions { IncludeFields = true })}");
    }
}
