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
