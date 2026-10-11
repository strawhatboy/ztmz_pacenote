using System.Buffers.Binary;
using System.IO;

namespace ZTMZ.PacenoteTool.AC;

// Optional fields not published in ACR's acpmf_* mappings. Keep conversion shared
// between hosts; an unavailable supplement must never suppress the standard feed.
internal sealed record ACRVehicleTelemetry(float HandBrake, float ShiftStart, float ShiftEnd)
{
    public ulong VehicleIdentity { get; init; }
    public float[]? WheelRadii { get; init; }
    public int MaxGears { get; init; }
    public float IdleRPM { get; init; }
    public ZTMZ.PacenoteTool.Base.Game.CarDamageSnapshot? Damage { get; init; }
}

internal static class ACRVehicleMemory
{
    internal const string SupportedHash = "42C359B0CD24774A86441710FDC7D7DB3BC06B57B3FF30CB9EA5D56FB71448FB";
    internal const ulong WorldRva = 0xb4ef3d8;

    // Verified against this executable's GetPlayerController, GetHandBrake,
    // GetRpmThresholds and physics shared-memory writer. No game calls/writes.
    internal static ACRVehicleTelemetry? Read(Func<ulong, int, byte[]> read, ulong imageBase)
    {
        ulong Pointer(ulong address)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(read(address, 8));
            if (value < 0x10000 || value > 0x7fffffffffff || (value & 7) != 0)
                throw new IOException("ACR vehicle pointer is unavailable.");
            return value;
        }
        var world = Pointer(imageBase + WorldRva);
        var instance = Pointer(world + 0x228);
        var players = Pointer(instance + 0x38);
        if (BinaryPrimitives.ReadInt32LittleEndian(read(instance + 0x40, 4)) != 1) return null;
        var player = Pointer(players);
        var controller = Pointer(player + 0x30);
        var pawn = Pointer(controller + 0x2e8);
        var simulation = Pointer(pawn + 0x580);
        if (Pointer(simulation) != imageBase + 0x96fb538) return null;
        var parameters = Pointer(pawn + 0x488);
        var controls = read(simulation + 0x131c, 0x18);
        // Native input serializer names +0x20 "handBrake" (controls start +0x1310).
        var handBrake = BitConverter.ToSingle(controls, 0x14);
        if (controls[1] > 1 || !float.IsFinite(handBrake) || handBrake < 0 || handBrake > 1) return null;
        if (controls[1] != 0) handBrake = 1; // forceHandBrake, e.g. held at the start.
        var thresholds = read(parameters + 0x2f0, 16);
        var start = BitConverter.ToSingle(thresholds, 0);
        var end = BitConverter.ToSingle(thresholds, 12);
        if (!float.IsFinite(start) || !float.IsFinite(end) || start <= 0 || end <= start || end > 30000)
            start = end = 0;
        var engineHolder = Pointer(simulation + 0x28c0);
        var engine = Pointer(engineHolder);
        if (Pointer(engine + 8) != simulation || Pointer(engine + 0xf0) != simulation) return null;
        // Engine::serialize -> EngineParams::serialize("minIdle").
        var idle = BitConverter.ToSingle(read(engine + 0x1c, 4));
        if (!float.IsFinite(idle) || idle < 0 || idle > 10000) idle = 0;
        var ratiosBegin = Pointer(engine + 0xf8);
        var ratiosEnd = BinaryPrimitives.ReadUInt64LittleEndian(read(engine + 0x100, 8));
        var gearCount = 0;
        if (ratiosEnd >= ratiosBegin && ratiosEnd - ratiosBegin is >= 12 and <= 56 && (ratiosEnd - ratiosBegin) % 4 == 0)
        {
            var ratios = read(ratiosBegin, (int)(ratiosEnd - ratiosBegin));
            // The shift routine indexes this array directly: reverse, neutral,
            // then forward gears. Do not count R/N as forward gears.
            if (BitConverter.ToSingle(ratios, 0) < 0 && BitConverter.ToSingle(ratios, 4) == 0)
            {
                gearCount = ratios.Length / 4 - 2;
                for (var i = 2; i < ratios.Length / 4; i++)
                    if (!float.IsFinite(BitConverter.ToSingle(ratios, i * 4)) || BitConverter.ToSingle(ratios, i * 4) <= 0)
                        gearCount = 0;
            }
        }
        var wheelsHolder = Pointer(simulation + 0x15e8);
        var wheels = Pointer(wheelsHolder);
        var radii = new float[4];
        for (var i = 0; i < 4; i++)
        {
            var wheel = Pointer(wheels + (ulong)i * 8);
            if (Pointer(wheel + 8) != simulation || BinaryPrimitives.ReadInt32LittleEndian(read(wheel + 0x10, 4)) != i)
                return null;
            // Same wheel radius passed to the wheel rigid body's cylindrical
            // mass/inertia calculation (RVA 0x4f34146); metres, not centimetres.
            var radius = BitConverter.ToSingle(read(wheel + 0x64, 4));
            if (float.IsFinite(radius) && radius is >= 0.1f and <= 1.5f) radii[i] = radius;
            if (Pointer(wheels + (ulong)i * 8) != wheel) return null;
        }
        // Reject a frame spanning a world/vehicle replacement. Resolve afresh on
        // every read, so returning to menus or changing cars cannot retain data.
        if (Pointer(imageBase + WorldRva) != world || Pointer(world + 0x228) != instance ||
            Pointer(instance + 0x38) != players || Pointer(players) != player ||
            Pointer(player + 0x30) != controller || Pointer(controller + 0x2e8) != pawn ||
            Pointer(pawn + 0x580) != simulation || Pointer(pawn + 0x488) != parameters ||
            Pointer(simulation + 0x28c0) != engineHolder || Pointer(engineHolder) != engine ||
            Pointer(simulation + 0x15e8) != wheelsHolder || Pointer(wheelsHolder) != wheels ||
            Pointer(engine + 0xf8) != ratiosBegin || BinaryPrimitives.ReadUInt64LittleEndian(read(engine + 0x100, 8)) != ratiosEnd) return null;
        return new(handBrake, start, end) { VehicleIdentity = pawn, MaxGears = gearCount, IdleRPM = idle, WheelRadii = radii };
    }
}
