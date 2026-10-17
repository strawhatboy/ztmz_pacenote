using System;
using System.IO;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

public readonly record struct Dr2VehicleTelemetry(float HandBrake, float ShiftRPM, string CarCode)
{
    public ulong PhysicsAddress { get; init; }
    public CarDamageSnapshot? Damage { get; init; }
    public TurboTelemetrySnapshot? Turbo { get; init; }
}

/// <summary>Verified DR2 vehicle data absent from the legacy UDP packet.</summary>
public static class Dr2VehicleMemory
{
    public const string SupportedHash = "C119F509FC315D8168AA7AAA5D0A1EE8E3570C7220BD6CBB2D337D6725D73442";
    public const float AngularVelocityToRPM = 60f / (2f * MathF.PI);

    public static Dr2VehicleTelemetry? Read(Func<ulong, int, byte[]> read, ulong imageBase,
        float udpIdleAngularVelocity, float udpMaxAngularVelocity)
    {
        if (!float.IsFinite(udpIdleAngularVelocity) || !float.IsFinite(udpMaxAngularVelocity) ||
            udpIdleAngularVelocity <= 0 || udpMaxAngularVelocity <= udpIdleAngularVelocity) return null;
        ulong Ptr(ulong address) => BitConverter.ToUInt64(read(address, 8));
        var count = BitConverter.ToInt32(read(imageBase + 0x201b930, 4));
        if (count is < 1 or > 32) return null;
        ulong vehicle = 0;
        var entries = read(imageBase + 0x201b7b0, count * 8);
        for (var i = 0; i < count; i++)
        {
            var candidate = BitConverter.ToUInt64(entries, i * 8);
            if (candidate < 0x10000 || Ptr(candidate) != imageBase + 0x1400c30) continue;
            // Mode 1 includes ghost/prediction vehicles. Never take their inputs.
            if (BitConverter.ToInt32(read(candidate + 0xd48, 4)) != 0) continue;
            if (vehicle != 0) return null; // Ownership is ambiguous, e.g. multiple local vehicles.
            vehicle = candidate;
        }
        if (vehicle == 0) return null;
        var physics = Ptr(vehicle + 8);
        var limits = read(physics + 0x6d0, 8);
        var idle = Decode(limits, 0, 0xb05c6c43);
        var maximum = Decode(limits, 4, 0x6fa85a41);
        if (!float.IsFinite(idle) || !float.IsFinite(maximum) ||
            MathF.Abs(idle - udpIdleAngularVelocity) > 0.1f ||
            MathF.Abs(maximum - udpMaxAngularVelocity) > 0.1f) return null;
        // Accessor 0x7316e0, also used by the native HUD handbrake indicator.
        var handBrake = BitConverter.ToSingle(read(physics + 0x390, 4));
        // HUD update 0x1a4f50 decodes engine parameters +0x334 for its shift warning.
        var shift = Decode(read(physics + 0x804, 4), 0, 0xb0ef7c77);
        if (!float.IsFinite(handBrake) || handBrake is < 0 or > 1 ||
            !float.IsFinite(shift) || shift <= idle || shift > maximum) return null;
        // Reject a vehicle replacement during the multi-read snapshot.
        if (Ptr(vehicle + 8) != physics) return null;
        var codeBytes = read(physics + 0x4d0, 4);
        var code = System.Text.Encoding.ASCII.GetString(codeBytes, 0, 3);
        if (codeBytes[3] != 0 || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z0-9]{3}$")) return null;
        TurboTelemetrySnapshot? turbo = null;
        try { turbo = Dr2TurboTelemetry.Read(read, physics); }
        catch (IOException) { /* Optional turbo reads must not disable handbrake/RPM. */ }
        if (Ptr(vehicle + 8) != physics) return null;
        return new(handBrake, shift * AngularVelocityToRPM, code) { PhysicsAddress = physics, Turbo = turbo };
    }

    private static float Decode(byte[] bytes, int offset, uint multiplier) =>
        BitConverter.Int32BitsToSingle(unchecked((int)(BitConverter.ToUInt32(bytes, offset) * multiplier)));
}
