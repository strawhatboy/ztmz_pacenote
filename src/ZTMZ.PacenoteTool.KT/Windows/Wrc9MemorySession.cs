using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

internal sealed record Wrc9Snapshot(string Identity, string Track, string Car, string CarClass, int RawMilliseconds)
{
    public GameData Telemetry { get; init; }
    public int? ResultMilliseconds { get; init; }
    public int PenaltyMilliseconds { get; init; }
    public WrcGenerationsNativeResult? NativeResult { get; init; }
}

/// <summary>Read-only reader for Steam build 6335125. No game functions are called.</summary>
[SupportedOSPlatform("windows")]
internal sealed class Wrc9MemorySession : IDisposable
{
    internal const string SupportedHash = "2A671399D771284502443B28C0B932DEC9189E0A68D40FEEE4C8024B8F5220E6";
    private readonly SafeProcessHandle handle;
    private readonly ulong imageBase, physicsBase, mainThreadTeb;
    private readonly WrcGenerationsSessionCatalog names;
    public WrcGenerationsPacenoteCatalog Pacenotes { get; }

    internal static string Verify(Process process)
    {
        var path = process.MainModule?.FileName ?? throw new IOException("WRC9 executable unavailable");
        if (!Path.GetFileName(path).Equals("WRC9.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Not WRC9");
        using var file = File.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(file)) != SupportedHash)
            throw new NotSupportedException("This WRC9 executable build has not been verified.");
        return path;
    }

    public Wrc9MemorySession(Process process)
    {
        var executable = Verify(process);
        imageBase = (ulong)process.MainModule!.BaseAddress.ToInt64();
        var resources = WrcGenerationsArchive.ReadResources(Path.GetDirectoryName(executable)!,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PACENOTES.CFG", "PACENOTEBLOCKS.CFG", "LOCALISATION_EN.PLOC", "LOCALISATION.LUA", "CARS.CFG" }, legacyWrc7: true);
        names = new(resources["LOCALISATION_EN.PLOC"], Encoding.UTF8.GetString(resources["LOCALISATION.LUA"]), Encoding.UTF8.GetString(resources["CARS.CFG"]));
        Pacenotes = WrcGenerationsPacenoteCatalog.Parse(Encoding.UTF8.GetString(resources["PACENOTES.CFG"]), Encoding.UTF8.GetString(resources["PACENOTEBLOCKS.CFG"]));
        foreach (ProcessModule module in process.Modules)
        {
            if (!module.ModuleName.Equals("PhysX64_s.dll", StringComparison.OrdinalIgnoreCase)) continue;
            using var binary = File.OpenRead(module.FileName);
            if (Convert.ToHexString(SHA256.HashData(binary)) == "3775BC47A4BEE80CB752544FEF5C40FD22E8A734CB930FE33F33CCC21C24E1F1")
                physicsBase = (ulong)module.BaseAddress.ToInt64();
        }
        handle = OpenProcess(0x1010, false, process.Id);
        if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        try
        {
            if (!Read(imageBase + 0x64e710, 11).AsSpan().SequenceEqual(new byte[] { 0x0f, 0xb6, 0xc2, 0x0f, 0xb6, 0x94, 0x08, 0x30, 0x11, 0, 0 }))
                throw new InvalidDataException("WRC9 loaded player-profile getter differs from the verified build");
            // The verified game's initial thread owns its race clock's TLS
            // table. Keep the TEB, not its movable numeric slot/table pointers.
            var threads = process.Threads.Cast<ProcessThread>().ToArray();
            try
            {
                var initial = threads.OrderBy(t => t.StartTime).First();
                using var thread = OpenThread(0x40, false, (uint)initial.Id);
                if (thread.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var info = new byte[48];
                if (NtQueryInformationThread(thread, 0, info, info.Length, IntPtr.Zero) != 0) throw new IOException("WRC9 main-thread metadata unavailable");
                mainThreadTeb = BinaryPrimitives.ReadUInt64LittleEndian(info.AsSpan(8));
            }
            finally { foreach (var thread in threads) thread.Dispose(); }
        }
        catch { handle.Dispose(); throw; }
    }

    public Wrc9Snapshot? Read()
    {
        try { return Wrc9SessionMemory.Decode(Read, imageBase, mainThreadTeb, physicsBase, names); }
        catch (IOException) { return null; }
        catch (OverflowException) { return null; }
    }
    private byte[] Read(ulong address, int size)
    {
        if (size is < 1 or > 8192 || address < 65536 || address > 0x7fffffffffffUL - (ulong)size) throw new IOException("Invalid WRC9 memory range");
        var bytes = new byte[size];
        if (!ReadProcessMemory(handle, (nint)address, bytes, (nuint)size, out var count) || count != (nuint)size) throw new IOException("WRC9 session unavailable");
        return bytes;
    }
    public void Dispose() => handle.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeWaitHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint tid);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationThread(SafeWaitHandle thread, int kind, [Out] byte[] information, int length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] bytes, nuint size, out nuint count);
}

internal static class Wrc9SessionMemory
{
    internal static Wrc9Snapshot? Decode(Func<ulong, int, byte[]> read, ulong imageBase, ulong teb, ulong physicsBase, WrcGenerationsSessionCatalog names)
    {
        ulong Pointer(ulong p) => BinaryPrimitives.ReadUInt64LittleEndian(read(p, 8));
        int Integer(ulong p) => BinaryPrimitives.ReadInt32LittleEndian(read(p, 4));
        var manager = Pointer(imageBase + 0x1a4e820);
        if (manager == 0 || Pointer(manager) != imageBase + 0x13f3398) return null;
        var race = Pointer(manager + 0x48);
        if (race == 0 || Pointer(imageBase + 0x1b024e8) != race || Pointer(race) != imageBase + 0x13f2510) return null;
        // Only the inspected single-player profile mapping is supported.
        if (Integer(manager + 0x1124) != 1 || read(manager + 0x1130, 1)[0] != 0) return null;
        var profile = Pointer(manager + 0x1128);
        var stage = Pointer(manager + 0xbc8); var rally = Pointer(manager + 0xbd0);
        if (profile == 0 || stage == 0 || rally == 0) return null;
        var car = Pointer(profile + 0x30);
        if (car == 0) return null;
        var trackName = names.Name(Integer(stage + 12)); var rallyName = names.Name(Integer(rally + 16));
        var carId = Integer(car + 8); var carName = names.Name(carId);
        if (trackName.Length == 0 || rallyName.Length == 0 || carName.Length == 0) return null;
        var timer = Pointer(race + 0x4e8);
        if (timer == 0) return null;
        var participant = Pointer(timer + 0xa0);
        if (participant == 0 || Pointer(participant + 0x38) != timer || read(participant + 0x44, 1)[0] != 0) return null;
        var flags = read(timer + 0x9c, 4); var result = read(profile + 0x194, 14);
        if (flags[0] > 1 || flags[1] > 1 || flags[3] > 1 || result[12] > 1 || result[13] > 1) return null;
        var native = new WrcGenerationsNativeResult(race, timer, flags[0] == 1, flags[1] == 1, flags[3] == 1, result[12] == 1, result[13] == 1);
        int raw = 0, penalty = 0; ulong laps = 0; int count = 0; int? start = null;
        if (native.TimerStarted && !native.TimerFailed)
        {
            count = Integer(timer + 0x3c); laps = Pointer(timer + 0x40);
            // Live restart capture: TimerStarted becomes true before the first
            // lap is allocated (1.4 seconds in the recorded sequence). This is
            // still preparation, not missing telemetry or a completed attempt.
            if (count == 0 && !native.Pending) return null;
            if (count is < 0 or > 1 || (count == 1 && laps == 0)) return null;
            if (count == 1)
            {
                ulong GlobalTable() => Pointer(imageBase + 0x19e9140);
                start = ReadProtectedInteger(read, GlobalTable, laps + 0x10);
                var end = ReadProtectedInteger(read, GlobalTable, laps + 0x20);
                var clock = Pointer(race + 0x898);
                ulong ClockTable()
                {
                    var index = Integer(imageBase + 0x1b0eb38);
                    if (index is < 0 or >= 64) throw new IOException("Unsupported WRC9 TLS index");
                    return Pointer(Pointer(Pointer(teb + 0x58) + (ulong)index * 8) + 0x40);
                }
                var now = end != 0 ? end : clock == 0 ? null : ReadProtectedInteger(read, ClockTable, clock + 0x2e0);
                var added = ReadProtectedInteger(read, GlobalTable, timer + 0xc8);
                if (start is null || now is null || added is null || added is < 0 or >= 86_400_000 ||
                    (long)now - start is < 0 or >= 86_400_000) return null;
                raw = now.Value - start.Value; penalty = added.Value;
            }
        }
        int? final = null;
        var recordedRaw = BinaryPrimitives.ReadInt32LittleEndian(result);
        var recordedTotal = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(4));
        if (native.TimerEnded && !native.TimerFailed && native.ProfileReady && !native.ProfileFailed && raw > 0 &&
            recordedRaw == raw && (long)raw + penalty == recordedTotal) final = recordedTotal;
        var data = ReadVehicle(read, imageBase, physicsBase, participant, (final ?? raw) / 1000f);
        data.Time = raw / 1000f;
        data = ReadProgress(read, imageBase, race, timer, data);
        // HUD setup 0x3f966d passes HudRedRpm to 0x40ea00; its red-zone
        // threshold is compared at 0x411a64. The shared helper adds ZTMZ's
        // progressive display lead-in; this is not a native LED start value.
        try { if (data.MaxRPM > 0) data = KtShiftLights.ApplyWarning(data, Integer(car + 0x15c)); }
        catch (IOException) { } // Optional HUD metadata must not discard a valid clock.
        if (Pointer(imageBase + 0x1a4e820) != manager || Pointer(manager + 0x48) != race || Pointer(imageBase + 0x1b024e8) != race ||
            Integer(manager + 0x1124) != 1 || read(manager + 0x1130, 1)[0] != 0 || Pointer(manager + 0x1128) != profile ||
            Pointer(manager + 0xbc8) != stage || Pointer(manager + 0xbd0) != rally || Pointer(profile + 0x30) != car ||
            Pointer(race + 0x4e8) != timer || Pointer(timer + 0xa0) != participant || Pointer(participant + 0x38) != timer ||
            !read(timer + 0x9c, 4).AsSpan().SequenceEqual(flags) || !read(profile + 0x194, 14).AsSpan().SequenceEqual(result) ||
            (native.TimerStarted && !native.TimerFailed && (Integer(timer + 0x3c) != count || Pointer(timer + 0x40) != laps ||
                (count == 1 && ReadProtectedInteger(read, () => Pointer(imageBase + 0x19e9140), laps + 0x10) != start)))) return null;
        return new($"{profile:x}:{timer:x}:{stage:x}:{car:x}", $"{rallyName} - {trackName}", carName, names.Class(carId), raw)
        { Telemetry = data, ResultMilliseconds = final, PenaltyMilliseconds = penalty, NativeResult = native };
    }

    internal static int? ReadProtectedInteger(Func<ulong, int, byte[]> read, Func<ulong> getTable, ulong address)
    {
        uint Number(ulong p) => BinaryPrimitives.ReadUInt32LittleEndian(read(p, 4));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var table = getTable(); var index = Number(address + 8);
            if (table == 0 || index >= 1_000_000) return null;
            var value = Number(checked(table + index * 4UL));
            if (Number(address + 8) != index || getTable() != table || Number(checked(table + index * 4UL)) != value) continue;
            var mask = unchecked(((uint)(address + 8) + index) * 0x159a55e5u + 0x159ae628u);
            return unchecked((int)(value ^ (mask == 0 ? 0xb10be924u : mask)));
        }
        return null;
    }

    internal static GameData ReadProgress(Func<ulong, int, byte[]> read, ulong image, ulong race, ulong timer, GameData data)
    {
        try
        {
            ulong Pointer(ulong p) => BinaryPrimitives.ReadUInt64LittleEndian(read(p, 8));
            int Integer(ulong p) => BinaryPrimitives.ReadInt32LittleEndian(read(p, 4));
            float Single(ulong p) => BinaryPrimitives.ReadSingleLittleEndian(read(p, 4));
            // Native route-length getter 0xf5cc30 and progress getter 0x67f470.
            // Only the verified single-lap case is supported.
            if (Pointer(timer) != image + 0x13f6f98 || Integer(race + 0x808) != 1) return data;
            var count = Integer(race + 0x844); var index = Integer(timer + 0x70);
            if (count is < 1 or > 128 || index < 0 || index >= count) return data;
            var routes = Pointer(race + 0x848);
            var reference = Pointer(routes + (ulong)index * 16 + 8);
            var route = Pointer(reference + 0x38); var shape = Pointer(route + 0x178);
            var length = Single(shape + 0x8c); var distance = Single(timer + 0x88);
            if (Pointer(race + 0x848) != routes || Integer(race + 0x844) != count || Integer(timer + 0x70) != index ||
                Pointer(routes + (ulong)index * 16 + 8) != reference || Pointer(reference + 0x38) != route || Pointer(route + 0x178) != shape ||
                Integer(race + 0x808) != 1) return data;
            return Wrc9VehicleTelemetry.DecodeProgress(length, distance, data);
        }
        catch (IOException) { return data; }
    }

    internal static GameData ReadVehicle(Func<ulong, int, byte[]> read, ulong imageBase, ulong physicsBase, ulong participant, float time)
    {
        var data = new GameData { LapTime = time, TimeStamp = DateTime.Now };
        try
        {
            ulong Pointer(ulong p) => BinaryPrimitives.ReadUInt64LittleEndian(read(p, 8));
            int Integer(ulong p) => BinaryPrimitives.ReadInt32LittleEndian(read(p, 4));
            var vehicle = Pointer(participant + 0x70);
            if (vehicle == 0 || Pointer(vehicle) != imageBase + 0x13feda8 || Pointer(vehicle + 0x60) != participant) return data;
            var drive = Pointer(vehicle + 0x80);
            if (drive == 0) return data;
            var driveBytes = read(drive, 0x538);
            data = Wrc9VehicleTelemetry.DecodeDrive(driveBytes, data);
            try
            {
                var query = Pointer(vehicle + 0x88); var config = Pointer(drive + 0x30); var dynamics = Pointer(drive + 0x60);
                var wheels = Wrc9VehicleTelemetry.DecodeWheels(read(vehicle, 0x1208), driveBytes,
                    read(query, 0x280), read(config + 0x1c0, 0xc0), read(dynamics, 0x20), data);
                if (Pointer(vehicle + 0x88) == query && Pointer(drive + 0x30) == config && Pointer(drive + 0x60) == dynamics) data = wheels;
            }
            catch (IOException) { }
            data.CarDamage = Wrc9Damage.Read(read, imageBase, participant, vehicle)!;
            try
            {
                var body = Pointer(drive + 0xc0);
                if (physicsBase != 0 && body != 0 && Pointer(body) == physicsBase + 0x176820)
                {
                    // Verified PhysX getters 0xe1d0, 0xda30 and 0x2cfd0.
                    var local = (Integer(body + 0x17c) & 0x200) != 0 ? Pointer(body + 0x70) + 0xe0 : body + 0xb0;
                    var motion = Wrc7VehicleTelemetry.DecodeMotion(read(body + 0x140, 40), read(local, 28), data);
                    if (Pointer(drive + 0xc0) == body) data = motion;
                }
            }
            catch (IOException) { }
            if (Pointer(participant + 0x70) != vehicle || Pointer(vehicle + 0x60) != participant || Pointer(vehicle + 0x80) != drive)
                return new() { LapTime = time, TimeStamp = DateTime.Now };
        }
        catch (IOException) { return new() { LapTime = time, TimeStamp = DateTime.Now }; }
        return data;
    }
}
