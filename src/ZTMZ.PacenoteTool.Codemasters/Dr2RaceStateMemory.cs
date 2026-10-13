using System;
using System.IO;

namespace ZTMZ.PacenoteTool.Codemasters;

// Names/order come from native state-name accessor RVA 0x4c6060.
public enum Dr2RaceState
{
    Finished, TimedOut, Eliminated, Racing, TerminalDamage, Retired,
    Disqualified, Griefing, Disconnected, Spectated, None
}

public readonly record struct Dr2RaceStateSnapshot(ulong RecordAddress, Dr2RaceState State);

/// <summary>Read-only counterpart of native local-player result lookup 0x42abc0.
/// The caller must gate this layout with Dr2VehicleMemory.SupportedHash.</summary>
public static class Dr2RaceStateMemory
{
    public static Dr2RaceStateSnapshot? Read(Func<ulong, int, byte[]> read, ulong imageBase)
    {
        ulong Ptr(ulong address)
        {
            var value = BitConverter.ToUInt64(read(address, 8));
            if (value < 0x10000 || value > 0x7fffffffffff || value % 8 != 0)
                throw new IOException("DR2 race-state pointer is unavailable.");
            return value;
        }
        int Int(ulong address) => BitConverter.ToInt32(read(address, 4));
        ushort Short(ulong address) => BitConverter.ToUInt16(read(address, 2));

        var players = Ptr(imageBase + 0x16951e8);
        var playerCount = Int(players + 0x40);
        if (playerCount is < 1 or > 64) return null;
        var playerList = Ptr(players + 0x30);
        var services = Ptr(imageBase + 0x1695150);
        var serviceIndex = Int(imageBase + 0x16940c4);
        if (serviceIndex is < 0 or > 4095) return null;
        var serviceTable = Ptr(services + 8);
        var localService = Ptr(serviceTable + (ulong)serviceIndex * 8);
        var localId = Short(localService + 0x2a);
        ulong player = 0;
        for (var i = 0; i < playerCount; i++)
        {
            var candidate = Ptr(playerList + (ulong)i * 8);
            if (Int(candidate + 0x2c) != 3 || Short(candidate + 0xbc) != localId) continue;
            if (player != 0) return null;
            player = candidate;
        }
        if (player == 0) return null;

        var race = Ptr(imageBase + 0x1695240);
        var count = BitConverter.ToUInt64(read(race + 0xacb0, 8));
        if (count is < 1 or > 64) return null;
        var records = Ptr(race + 0xaca0);
        ulong record = 0;
        for (ulong i = 0; i < count; i++)
        {
            var candidate = BitConverter.ToUInt64(read(records + i * 8, 8));
            if (candidate == 0) continue;
            if (Ptr(candidate + 0x40) != player) continue;
            if (record != 0) return null;
            record = candidate;
        }
        if (record == 0) return null;
        // 0x4c5110 decodes record+0x18 with the process key from 0xa0c00.
        var key = Int(imageBase + 0x15d8510);
        var state = Int(record + 0x18) ^ key;
        if (state is < 0 or > (int)Dr2RaceState.None) return null;
        // Reject ownership changes or teardown during the multi-read snapshot.
        if (Ptr(imageBase + 0x16951e8) != players || Ptr(imageBase + 0x1695240) != race ||
            Ptr(players + 0x30) != playerList || Int(players + 0x40) != playerCount ||
            Ptr(race + 0xaca0) != records || Short(localService + 0x2a) != localId ||
            Ptr(record + 0x40) != player || Int(imageBase + 0x15d8510) != key) return null;
        return new(record, (Dr2RaceState)state);
    }
}
