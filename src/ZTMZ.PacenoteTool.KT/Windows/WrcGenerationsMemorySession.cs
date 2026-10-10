using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>
/// Optional Windows metadata reader. Only queries memory; never injects, writes,
/// suspends a thread or calls a game function. Unknown builds retain UDP/audio.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WrcGenerationsMemorySession : IWrcGenerationsSessionSource
{
    private const string SupportedHash = "C2D711BE210F4E1EA0E97882D070C359E66703CC6C40D39D9FC0E5FAF7824D2D";
    private readonly SafeProcessHandle handle;
    private readonly ulong imageBase;
    private readonly WrcGenerationsSessionCatalog catalog;

    internal WrcGenerationsMemorySession(Process process)
    {
        var module = process.MainModule ?? throw new InvalidDataException("WRCG module unavailable");
        if (!Path.GetFileName(module.FileName).Equals("WRCG.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Not a WRCG process");
        using (var file = File.OpenRead(module.FileName))
            if (Convert.ToHexString(SHA256.HashData(file)) != SupportedHash)
                throw new NotSupportedException("WRCG session metadata: executable build is not verified; UDP/audio remain available");
        imageBase = (ulong)module.BaseAddress.ToInt64();
        handle = OpenProcess(0x1010, false, process.Id); // QUERY_LIMITED_INFORMATION | VM_READ
        if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        try
        {
            // Verify the loaded image as well as the on-disk executable (ASLR supported).
            if (!ReadBytes(imageBase + 0x837130, 8).AsSpan().SequenceEqual(new byte[] { 0x48, 0x8b, 0x81, 0x50, 0x0d, 0, 0, 0xc3 }))
                throw new InvalidDataException("WRCG loaded session getter does not match the verified build");
            catalog = WrcGenerationsSessionCatalog.Load(Path.GetDirectoryName(module.FileName)!);
        }
        catch { handle.Dispose(); throw; }
    }

    public WrcGenerationsSessionSnapshot? Read()
    {
        try { return WrcGenerationsSessionMemory.Decode(ReadBytes, imageBase, catalog); }
        catch (IOException) { return null; } // Scene teardown/process exit is normal.
        catch (OverflowException) { return null; } // A torn/invalid pointer is never dereferenced.
    }

    private byte[] ReadBytes(ulong address, int length)
    {
        if (address < 0x10000 || address > 0x7fffffffffffUL - (ulong)length || length is < 1 or > 8192)
            throw new IOException("Invalid WRCG memory range");
        var bytes = new byte[length];
        if (!ReadProcessMemory(handle, (nint)address, bytes, (nuint)length, out var count) || count != (nuint)length)
            throw new IOException("WRCG session is unavailable");
        return bytes;
    }

    public void Dispose() => handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
}

/// <summary>Bounded portable decoder, separately testable without Win32 or a game.</summary>
internal static class WrcGenerationsSessionMemory
{
    internal static WrcGenerationsSessionSnapshot? Decode(Func<ulong, int, byte[]> read, ulong imageBase,
        WrcGenerationsSessionCatalog catalog)
    {
        static ulong Pointer(byte[] data, int offset = 0) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
        static int Integer(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        var manager = Pointer(read(imageBase + 0x1c61660, 8));
        if (manager == 0) return null;
        var race = read(manager, 0x1381);
        var stageAddress = Pointer(race, 0xd50);
        var rallyAddress = Pointer(race, 0xd58);
        var profiles = Pointer(race, 0x1378);
        var count = Integer(race, 0x1374);
        var index = race[0x1380]; // The game's player-0 -> profile-index mapping, not first loaded car.
        if (stageAddress == 0 || rallyAddress == 0 || profiles == 0 || count is < 1 or > 64 || index >= count) return null;
        var profileAddress = checked(profiles + (ulong)index * 0x210);
        var profile = read(profileAddress, 0x210);
        var carAddress = Pointer(profile, 0x30);
        if (carAddress == 0) return null;
        var stage = read(stageAddress, 0x20);
        var rally = read(rallyAddress, 0x18);
        var car = read(carAddress, 0x20);
        var stageIndex = Integer(stage, 8);
        var rallyIndex = Integer(rally, 8);
        var carNameId = Integer(car, 0x14);
        var trackName = catalog.Name(Integer(stage, 0xc));
        var rallyName = catalog.Name(Integer(rally, 0x10));
        var carName = catalog.Name(carNameId);
        if (stageIndex is < 0 or > 128 || rallyIndex is < 0 or > 128 ||
            trackName.Length == 0 || rallyName.Length == 0 || carName.Length == 0) return null;
        var timing = ReadTiming(read, imageBase, manager, race);
        // Reject mixed snapshots while the game replaces scene/profile objects.
        if (Pointer(read(imageBase + 0x1c61660, 8)) != manager ||
            Pointer(read(manager + 0xd50, 8)) != stageAddress || Pointer(read(manager + 0xd58, 8)) != rallyAddress ||
            Pointer(read(manager + 0x1378, 8)) != profiles || read(manager + 0x1380, 1)[0] != index ||
            Pointer(read(profileAddress + 0x30, 8)) != carAddress) return null;
        var raw = Integer(profile, 0x19c);
        var total = Integer(profile, 0x1a0);
        var ready = profile[0x1a4] == 1 && raw is > 0 and < 86_400_000 && total >= raw && total < 86_400_000;
        return new($"{rallyIndex}:{stageIndex}:{carNameId}:{profileAddress:x}",
            $"{rallyName} - {trackName}", carName, catalog.Class(carNameId), ready, raw, total, timing);
    }

    private static WrcGenerationsTimingSnapshot? ReadTiming(Func<ulong, int, byte[]> read, ulong imageBase,
        ulong manager, byte[] managerData)
    {
        // Optional: a torn timing read must not discard otherwise valid names.
        try
        {
            ulong Pointer(ulong p) => BinaryPrimitives.ReadUInt64LittleEndian(read(p, 8));
            int Integer(ulong p) => BinaryPrimitives.ReadInt32LittleEndian(read(p, 4));
            var race = BinaryPrimitives.ReadUInt64LittleEndian(managerData.AsSpan(0x48, 8));
            if (race == 0 || Pointer(imageBase + 0x1d61938) != race) return null;
            // Match the game's active-timer getter (0x8176d0), including its
            // player mapping in mode 7 (except submode 5).
            var mode = BinaryPrimitives.ReadInt32LittleEndian(managerData.AsSpan(0x12d0, 4));
            var submode = BinaryPrimitives.ReadInt32LittleEndian(managerData.AsSpan(0x12e0, 4));
            var index = mode == 7 && submode != 5 ? managerData[0x1380] : 0;
            if (index >= 50) return null;
            var slot = race + 0x4e8 + (ulong)index * 8;
            var timer = Pointer(slot);
            if (timer == 0) return null;
            var timerData = read(timer, 0xe0);
            if (timerData[0x9f] != 0) return null;
            var count = BinaryPrimitives.ReadInt32LittleEndian(timerData.AsSpan(0x3c, 4));
            var laps = BinaryPrimitives.ReadUInt64LittleEndian(timerData.AsSpan(0x40, 8));
            if (count is < 1 or > 128 || laps == 0) return null;
            var lap = checked(laps + (ulong)(count - 1) * 0x250);
            var start = ReadProtectedInteger(read, imageBase, lap + 0x10);
            var end = ReadProtectedInteger(read, imageBase, lap + 0x20);
            var clock = Pointer(race + 0x898);
            if (start is null || end is null || (end == 0 && clock == 0)) return null;
            var now = end != 0 ? end : ReadProtectedInteger(read, imageBase, clock + 0x2f0);
            var penalty = ReadProtectedInteger(read, imageBase, timer + 0xc8);
            if (now is null || penalty is null || penalty < 0 || penalty >= 86_400_000) return null;
            long raw = (long)now.Value - start.Value;
            if (raw is < 0 or >= 86_400_000) return null;
            // Validate ownership and lap identity again after the numeric reads.
            if (Pointer(manager + 0x48) != race || Pointer(imageBase + 0x1d61938) != race ||
                Pointer(slot) != timer || Integer(timer + 0x3c) != count || Pointer(timer + 0x40) != laps ||
                ReadProtectedInteger(read, imageBase, lap + 0x10) != start) return null;
            return new(timer, (int)raw, penalty.Value);
        }
        catch (IOException) { return null; }
        catch (OverflowException) { return null; }
    }

    internal static int? ReadProtectedInteger(Func<ulong, int, byte[]> read, ulong imageBase, ulong address)
    {
        // Read-only equivalent of the value extraction in 0x415310. The game
        // relocates slots even when reading them; never execute its getter or
        // mutate its table. Retry boundedly if the slot moves during our read.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var table = BinaryPrimitives.ReadUInt64LittleEndian(read(imageBase + 0x1bbf030, 8));
            var index = BinaryPrimitives.ReadUInt32LittleEndian(read(address + 8, 4));
            if (table == 0 || index >= 1_000_000) return null;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(read(checked(table + index * 4UL), 4));
            if (BinaryPrimitives.ReadUInt32LittleEndian(read(address + 8, 4)) != index ||
                BinaryPrimitives.ReadUInt64LittleEndian(read(imageBase + 0x1bbf030, 8)) != table ||
                BinaryPrimitives.ReadUInt32LittleEndian(read(checked(table + index * 4UL), 4)) != value) continue;
            var mask = unchecked(((uint)(address + 8) + index) * 0x159a55e5u + 0x159ae628u);
            return unchecked((int)(value ^ (mask == 0 ? 0xb10be924u : mask)));
        }
        return null;
    }
}
