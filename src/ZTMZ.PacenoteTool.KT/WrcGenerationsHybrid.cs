using System.Buffers.Binary;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>Verified WRCG hybrid controller/HUD getters; called inside active-player validation.</summary>
internal static class WrcGenerationsHybrid
{
    internal static HybridTelemetrySnapshot? Read(Func<ulong, int, byte[]> read, ulong participant, ulong vehicle)
    {
        ulong Pointer(ulong a) => BinaryPrimitives.ReadUInt64LittleEndian(read(a, 8));
        int Enabled() => BinaryPrimitives.ReadInt32LittleEndian(read(vehicle + 0x1118, 4));
        // Ordinary cars retain default battery numbers in the physics object.
        // Never expose those as hybrid telemetry without the active controller.
        if (Enabled() != 1 || Pointer(vehicle + 0x2178) != participant) return null;
        var controller = Pointer(participant + 0xa08);
        var drive = Pointer(vehicle + 0xa0);
        if (controller == 0 || drive == 0) return null;
        var data = read(controller, 0x80);
        ulong P(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
        float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4));
        if (P(0) != participant || P(8) != vehicle) return null;
        var map = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x18, 4));
        var capacity = F(0x30);
        var recovered = F(0x3c);
        var boostStartEnergy = F(0x40);
        var boostBudget = F(0x5c);
        var energy = BinaryPrimitives.ReadDoubleLittleEndian(read(drive + 0x688, 8));
        var configuredCapacity = BinaryPrimitives.ReadDoubleLittleEndian(read(vehicle + 0x1120, 8));
        if (map is < 0 or > 2 || data[0x65] > 1 || data[0x66] > 1 || data[0x67] > 1 ||
            !float.IsFinite(capacity) || capacity is <= 0 or > 100 ||
            !double.IsFinite(configuredCapacity) || Math.Abs(configuredCapacity - capacity) > .0001 ||
            !double.IsFinite(energy) || energy < 0 || energy > capacity + .0001 ||
            !float.IsFinite(recovered) || recovered < 0 ||
            !float.IsFinite(boostStartEnergy) || boostStartEnergy < 0 || boostStartEnergy > capacity + .0001 ||
            !float.IsFinite(boostBudget) || boostBudget <= 0) return null;
        var active = data[0x65] == 1;
        // 0x8f5340: ready once 30 kJ have been recovered or the usable battery
        // ceiling is reached, provided boost is not already active.
        var ready = (data[0x66] == 1 || recovered >= 30) && !active;
        // 0x8f53e0: HUD reload gauge. During boost it displays remaining budget;
        // otherwise it displays recovered energy / 30 kJ. 1 kWh = 3600 kJ.
        var usedKJ = boostStartEnergy == 0 ? 0 : (boostStartEnergy - (float)energy) * 3600;
        var progress = active ? 1 - usedKJ / boostBudget : ready ? 1 : recovered / 30;
        if (!float.IsFinite(progress)) return null;
        if (Enabled() != 1 || Pointer(vehicle + 0x2178) != participant ||
            Pointer(participant + 0xa08) != controller || Pointer(vehicle + 0xa0) != drive ||
            Pointer(controller) != participant || Pointer(controller + 8) != vehicle) return null;
        return new("wrcg", DateTimeOffset.UtcNow)
        {
            BatteryCapacityKWh = capacity,
            BatteryEnergyKWh = (float)energy,
            BatteryChargeFraction = Math.Clamp((float)energy / capacity, 0, 1),
            BoostProgressFraction = Math.Clamp(progress, 0, 1),
            BoostReady = ready,
            BoostActive = active,
            RegenerationActive = data[0x67] == 1,
            BoostMap = map + 1
        };
    }
}
