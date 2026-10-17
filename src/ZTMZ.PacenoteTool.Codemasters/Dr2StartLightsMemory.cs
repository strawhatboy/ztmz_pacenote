using System;
using System.IO;

namespace ZTMZ.PacenoteTool.Codemasters;

public readonly record struct Dr2StartLightsSnapshot(ulong Address, int Phase)
{
    public int Number => 6 - Phase;
}

/// <summary>Native rally start-light sequence, gated by Dr2VehicleMemory.SupportedHash.
/// HUD routine 0x5f008c displays sequence length - phase - 1. No UDP clock inference.</summary>
public static class Dr2StartLightsMemory
{
    public static Dr2StartLightsSnapshot? Read(Func<ulong, int, byte[]> read, ulong imageBase)
    {
        ulong Ptr(ulong address)
        {
            var value = BitConverter.ToUInt64(read(address, 8));
            if (value < 0x10000 || value > 0x7fffffffffff || value % 8 != 0)
                throw new IOException("DR2 start-light pointer is unavailable.");
            return value;
        }
        int Index() => BitConverter.ToInt32(read(imageBase + 0x16940e4, 4));
        // Same service lookup used by native start reset 0x5a0b59.
        var index = Index();
        if (index is < 0 or > 4095) return null;
        var services = Ptr(imageBase + 0x1695150);
        var table = Ptr(services + 8);
        var address = Ptr(table + (ulong)index * 8);
        var data = read(address, 0x4f0);
        if (BitConverter.ToUInt64(data, 0) != imageBase + 0x127ae38 ||
            BitConverter.ToUInt64(data, 0x10) != imageBase + 0x127ae50) return null;
        // Verified rally layout: waiting, five numbered lights, GO. Reject
        // different sequences (including rallycross), rather than guessing.
        if (data[0xa5] != 7 || data[0x20] > 6) return null;
        for (var phase = 0; phase < 7; phase++)
        {
            var kind = BitConverter.ToInt32(data, 0x128 + phase * 0x88);
            if (kind != (phase == 0 ? 0 : phase == 6 ? 2 : 1)) return null;
        }
        // Reject a reset, transition, or service replacement during the read.
        if (Index() != index || Ptr(imageBase + 0x1695150) != services ||
            Ptr(services + 8) != table || Ptr(table + (ulong)index * 8) != address ||
            read(address + 0x20, 1)[0] != data[0x20] || read(address + 0xa5, 1)[0] != 7)
            return null;
        return new(address, data[0x20]);
    }
}
