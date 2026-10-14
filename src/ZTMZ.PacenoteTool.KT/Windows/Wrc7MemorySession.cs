using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace ZTMZ.PacenoteTool.KT;

internal sealed record Wrc7Snapshot(string Identity, string Track, string Car, string CarClass, int RawMilliseconds)
{
    public ZTMZ.PacenoteTool.Base.Game.GameData Telemetry { get; init; }
    public int? ResultMilliseconds { get; init; }
    public int PenaltyMilliseconds { get; init; }
    public Wrc7NativeResult? NativeResult { get; init; }
}

/// <summary>Read-only metadata for the verified single-player WRC7 build.</summary>
[SupportedOSPlatform("windows")]
internal sealed class Wrc7MemorySession : IDisposable
{
    internal const string SupportedHash = "0FEE0ED14AD05D76FD44B2BA1215BACAFC806DD9D7E8B313634A99C105903633";
    private readonly SafeProcessHandle handle;
    private readonly ulong imageBase;
    private readonly ulong physicsBase;
    private readonly WrcGenerationsSessionCatalog names;
    private readonly List<(string Key, int Mask)> stages = [];
    public Wrc7PacenoteCatalog Pacenotes { get; }

    internal static string Verify(Process process)
    {
        var path = process.MainModule?.FileName ?? throw new IOException("WRC7 executable unavailable");
        if (!Path.GetFileName(path).Equals("WRC7.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Not WRC7");
        using var file = File.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(file)) != SupportedHash) throw new NotSupportedException("This WRC7 executable build has not been verified.");
        return path;
    }

    public Wrc7MemorySession(Process process)
    {
        var executable = Verify(process);
        imageBase = (ulong)process.MainModule!.BaseAddress.ToInt64();
        foreach (ProcessModule module in process.Modules)
        {
            if (!module.ModuleName.Equals("physx64_s.dll", StringComparison.OrdinalIgnoreCase)) continue;
            using var binary = File.OpenRead(module.FileName);
            if (Convert.ToHexString(SHA256.HashData(binary)) == "3E72AF0BEB45BA0C186B1FE0E8DF1F8E103874B7D54E3D39BB937EA0CA4B46F3")
                physicsBase = (ulong)module.BaseAddress.ToInt64();
        }
        var resources = WrcGenerationsArchive.ReadResources(Path.GetDirectoryName(executable)!,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PACENOTES.CFG", "LOCALISATION_EN.PLOC", "LOCALISATION.LUA", "CARS.CFG", "RALLIES.CFG" }, legacyWrc7: true);
        names = new(resources["LOCALISATION_EN.PLOC"], Encoding.UTF8.GetString(resources["LOCALISATION.LUA"]), Encoding.UTF8.GetString(resources["CARS.CFG"]));
        Pacenotes = Wrc7PacenoteCatalog.Parse(Encoding.UTF8.GetString(resources["PACENOTES.CFG"]));
        foreach (Match match in Regex.Matches(Encoding.UTF8.GetString(resources["RALLIES.CFG"]),
            @"\bName\s*=\s*LocalesId\.(STAGENAME_\w+)\s*,[^{}]*?SelectiveLoadId\s*=\s*(\d+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
        {
            var bit = int.Parse(match.Groups[2].Value);
            if (bit is >= 0 and < 31) stages.Add((match.Groups[1].Value, 1 << bit));
        }
        handle = OpenProcess(0x1010, false, process.Id);
        if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    }

    public Wrc7Snapshot? Read()
    {
        try
        {
            var manager = Pointer(imageBase + 0x1091b80);
            if (Pointer(manager) != imageBase + 0xdb1b60) return null;
            var begin = Pointer(manager + 0x17798);
            // Multiplayer profile selection has not been verified: reject it.
            if (Pointer(manager + 0x177a0) - begin != 0x170) return null;
            var car = Pointer(begin + 0x20);
            var carId = Integer(car + 8);
            var participants = Pointer(manager + 0xb0);
            var countBytes = Pointer(manager + 0xb8) - participants;
            if (countBytes is < 8 or > 128 || countBytes % 8 != 0) return null;
            var vehicle = Pointer(participants);
            var stats = Pointer(vehicle + 0x38);
            if (Pointer(stats + 0x908) != vehicle) return null;
            var time = Integer(stats + 0x6c);
            if (time is < -1 or > 24 * 3600 * 1000) return null;
            var engine = Pointer(imageBase + 0x1091d20);
            var level = Pointer(Pointer(engine + 0x68) + 0x40);
            var levelName = String(level + 0x38);
            if (!levelName.StartsWith("DATA:/LEVELS/", StringComparison.OrdinalIgnoreCase)) return null;
            var leaf = Path.GetFileNameWithoutExtension(levelName);
            var country = leaf.Split('_')[0];
            var mask = Integer(Pointer(imageBase + 0x1091d78) + 0x108);
            var matches = stages.Where(stage => stage.Mask == mask && stage.Key.StartsWith("STAGENAME_" + country + "_", StringComparison.Ordinal))
                .Select(stage => stage.Key).Distinct().ToArray();
            if (matches.Length == 0) return null; // Main menu/non-rally level.
            // A loaded layer can contain multiple stages. Never pick the first.
            // Verified MC_SS2 has different English/Chinese names in the game
            // tables (ID 2041). Compare localization IDs, not translated text.
            var track = matches.Length == 1 ? names.Name(matches[0]) : string.Empty;
            var telemetry = ReadVehicleTelemetry(vehicle, Math.Max(0, time));
            var penalty = Integer(stats + 0x92c);
            var finish = Read(stats + 0x8c8, 3);
            var result = Read(begin + 0x154, 14);
            // Reject mixed owners or a result being updated during this read.
            if (Pointer(imageBase + 0x1091b80) != manager ||
                Pointer(manager + 0x17798) != begin || Pointer(manager + 0x177a0) != begin + 0x170 ||
                Pointer(manager + 0xb0) != participants || Pointer(participants) != vehicle ||
                Pointer(vehicle + 0x38) != stats || Pointer(stats + 0x908) != vehicle ||
                !finish.AsSpan().SequenceEqual(Read(stats + 0x8c8, 3)) ||
                !result.AsSpan().SequenceEqual(Read(begin + 0x154, 14))) return null;
            var finalTime = Wrc7RaceSession.VerifiedResult(time, penalty, finish, result);
            if (finalTime.HasValue) telemetry.LapTime = finalTime.Value / 1000f;
            return new($"{begin:x}:{vehicle:x}:{stats:x}:{levelName}:{mask}", track, names.Name(carId), names.Class(carId), time)
            {
                Telemetry = telemetry, ResultMilliseconds = finalTime, PenaltyMilliseconds = Math.Max(0, penalty),
                NativeResult = Wrc7NativeResult.Decode(finish, result)
            };
        }
        catch (IOException) { return null; }
        catch (OverflowException) { return null; }
    }

    private ZTMZ.PacenoteTool.Base.Game.GameData ReadVehicleTelemetry(ulong vehicle, int time)
    {
        var data = new ZTMZ.PacenoteTool.Base.Game.GameData { LapTime = time / 1000f, TimeStamp = DateTime.Now };
        try
        {
            // Same source objects used by the game's telemetry writer at 0x84df70.
            // The debug telemetry buffer itself is disabled in an unpatched game.
            var physics = Pointer(vehicle + 0x80);
            if (Pointer(physics) != imageBase + 0xdb8ea0 || Pointer(physics + 0x68) != vehicle) return data;
            var engine = Pointer(physics + 0x90);
            var values = Read(engine + 0x118, 0xb0);
            var limits = Read(physics + 0xaa4, 8);
            data = Wrc7VehicleTelemetry.Decode(values, limits, data);
            data = Wrc7VehicleTelemetry.DecodeControls(Read(engine + 0xd0, 24), Integer(physics + 0x90c), data);
            var stats = Pointer(vehicle + 0x38);
            // RaceStats update calls the route-relative spline distance function
            // 0x8322a0 and caches its result at +0x790; route length is +0x290.
            var route = Pointer(Pointer(imageBase + 0x10add50) + 0x260);
            data = Wrc7VehicleTelemetry.DecodeProgress(Read(route + 0x290, 4), Read(stats + 0x790, 4), data);
            data = Wrc7VehicleTelemetry.DecodeWheelSpeeds(Read(Pointer(engine + 0x30) + 0x1c0, 0xc0), Read(Pointer(engine + 0x60) + 0x10, 16), data);
            var body = Pointer(engine + 0xb0);
            if (physicsBase != 0 && Pointer(body) == physicsBase + 0x171880)
            {
                // Verified PhysX getters: linear velocity at +0x15c, global
                // body pose at +0x140, buffered or committed local COM pose.
                var localPose = (Integer(body + 0x17c) & 0x200) != 0 ? Pointer(body + 0x70) + 0xe0 : body + 0xb0;
                data = Wrc7VehicleTelemetry.DecodeMotion(Read(body + 0x140, 40), Read(localPose, 28), data);
            }
            return data;
        }
        catch (IOException) { return data; }
    }

    public Wrc7PacenoteModes ReadPacenoteModes()
    {
        try
        {
            // WRC7 0xe9d60 reads these three independent enum indices before
            // choosing DetailedSound (+8) or AccessibleSound (+0x50).
            // Category table 0x10af0a0: DEFAULT=0, HAIRPIN=1, DANGER=2.
            // Detail table 0x10aefc0: Detailed=0, Accessible=1.
            var manager = Pointer(imageBase + 0x1091b80);
            if (Pointer(manager) != imageBase + 0xdb1b60) return Wrc7PacenoteModes.Unknown;
            var values = Read(manager + 0xec0, 0x24);
            return new(Mode(0), Mode(0x10), Mode(0x20));
            bool? Mode(int offset) => BinaryPrimitives.ReadInt32LittleEndian(values.AsSpan(offset, 4)) switch
            {
                0 => false, 1 => true, _ => null
            };
        }
        catch (IOException) { return Wrc7PacenoteModes.Unknown; }
        catch (ObjectDisposedException) { return Wrc7PacenoteModes.Unknown; }
    }

    private byte[] Read(ulong address, int size)
    {
        if (size is < 1 or > 4096 || address < 65536 || address > 0x7fffffffffffUL - (ulong)size) throw new IOException("Invalid WRC7 memory range");
        var bytes = new byte[size];
        if (!ReadProcessMemory(handle, (nint)address, bytes, (nuint)size, out var count) || count != (nuint)size) throw new IOException("WRC7 session unavailable");
        return bytes;
    }
    private ulong Pointer(ulong address) => BinaryPrimitives.ReadUInt64LittleEndian(Read(address, 8));
    private int Integer(ulong address) => BinaryPrimitives.ReadInt32LittleEndian(Read(address, 4));
    private string String(ulong address)
    {
        var length = Pointer(address + 16); var capacity = Pointer(address + 24);
        if (length is 0 or > 511 || capacity < length) throw new IOException("Invalid WRC7 string");
        return Encoding.UTF8.GetString(Read(capacity < 16 ? address : Pointer(address), (int)length));
    }
    public void Dispose() => handle.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] bytes, nuint size, out nuint count);
}
